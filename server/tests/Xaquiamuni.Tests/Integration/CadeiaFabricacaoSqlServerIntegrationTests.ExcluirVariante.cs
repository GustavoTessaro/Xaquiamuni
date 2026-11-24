using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xaquiamuni.Api.Catalogo;
using Xaquiamuni.Aplicacao.Estoque;
using Xaquiamuni.Aplicacao.Pedidos;
using Xaquiamuni.Dominio.Estoque;
using Xaquiamuni.Infraestrutura.Identidade;
using Xaquiamuni.Infraestrutura.Persistencia;

namespace Xaquiamuni.Tests.Integration;

public sealed partial class CadeiaFabricacaoSqlServerIntegrationTests
{
    [SqlServerIntegrationFact]
    public async Task ExclusaoVariante_RollbackPreservaVarianteEAuditoriaAtomica()
    {
        await using var banco = await SqlServerTestDatabase.CreateAsync(
            targetMigration: "20261124011336_RegistrarUsoEExclusaoFornecedor");
        await using var db = banco.CreateVerificationContext();
        var (produto, _, extra) = await ReservarProdutoAcabadoSqlServerIntegrationTests.CriarProdutoCompravelAsync(db);
        await db.Database.MigrateAsync();
        var agregado = await new EfProdutoRepository(db).ConsultarAsync(produto.Id);
        var versao = ProdutoResponse.From(agregado!).Versao;
        var options = new DbContextOptionsBuilder<XaquiamuniDbContext>().UseSqlServer(banco.ConexaoIsolada)
            .AddInterceptors(new FalhaCommitVariante()).Options;
        await using var erro = new XaquiamuniDbContext(options);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new EfExcluirVariante(erro, TimeProvider.System).ExecutarAsync(produto.Id, extra.Id, versao,
                Guid.NewGuid(), Guid.NewGuid(), p => ProdutoResponse.From(p).Versao, default));
        Assert.True(await db.ProdutoVariantes.AnyAsync(v => v.Id == extra.Id));
        Assert.Equal(0, await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM OperacoesExclusaoVariante").SingleAsync());
    }

    private sealed class FalhaCommitVariante : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("Falha de teste antes do commit.");
    }

    [SqlServerIntegrationFact]
    public async Task ExclusaoVariante_ReceitaRejeitaVarianteExcluidaEntreLeituraEGravacao()
    {
        await using var banco = await SqlServerTestDatabase.CreateAsync();
        await using var db = banco.CreateVerificationContext();
        var (produto, _, extra) = await ReservarProdutoAcabadoSqlServerIntegrationTests.CriarProdutoCompravelAsync(db);
        var insumo = InsumoEstoque.Criar("Base de teste", TipoInsumoEstoque.MateriaPrima, UnidadeFisicaEstoque.Grama);
        db.InsumosEstoque.Add(insumo);
        await db.SaveChangesAsync();
        var agregado = await new EfProdutoRepository(db).ConsultarAsync(produto.Id);
        var versao = ProdutoResponse.From(agregado!).Versao;
        var options = new DbContextOptionsBuilder<XaquiamuniDbContext>().UseSqlServer(banco.ConexaoIsolada)
            .AddInterceptors(new AntesDeGravarReceita(async () =>
            {
                await using var exclusaoDb = banco.CreateVerificationContext();
                var resultado = await new EfExcluirVariante(exclusaoDb, TimeProvider.System).ExecutarAsync(
                    produto.Id, extra.Id, versao, Guid.NewGuid(), Guid.NewGuid(), p => ProdutoResponse.From(p).Versao, default);
                Assert.Equal(204, resultado.Status);
            })).Options;
        await using var receitaDb = new XaquiamuniDbContext(options);
        var receita = await new EfReceitaProducaoRepository(receitaDb, TimeProvider.System).SalvarAsync(
            null, new ReceitaInput(extra.Id, "Receita concorrente", 1, [new(insumo.Id, "10", "Grama")], null),
            Guid.NewGuid(), default);
        Assert.Equal(ReceitaStatus.Conflito, receita.Status);
        Assert.False(await db.ReceitasProducao.AnyAsync());
        Assert.False(await db.ProdutoVariantes.AnyAsync(v => v.Id == extra.Id));
    }

    private sealed class AntesDeGravarReceita(Func<Task> executar) : SaveChangesInterceptor
    {
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            await executar();
            return result;
        }
    }

    [SqlServerIntegrationFact]
    public async Task ExclusaoVariante_PreservaContagemSemDiferencaComSaldoZero()
    {
        await using var banco = await SqlServerTestDatabase.CreateAsync();
        await using var db = banco.CreateVerificationContext();
        var (produto, _, extra) = await ReservarProdutoAcabadoSqlServerIntegrationTests.CriarProdutoCompravelAsync(db);
        var estoque = banco.Services.GetRequiredService<IEstoqueProdutoAcabadoRepository>();
        var autor = Guid.NewGuid();
        var entrada = await estoque.RegistrarEntradaAsync(new RegistrarEntradaProdutoAcabadoInput(
            extra.Id, autor, TipoMovimentoProdutoAcabado.EntradaManual, 1, CondicaoEstoqueProdutoAcabado.Vendavel,
            "Entrada de teste", "entrada-teste", SemControleValidade: true));
        Assert.True(entrada.Sucesso, entrada.Erro);
        var estorno = await estoque.EstornarAsync(new EstornarMovimentoProdutoAcabadoInput(
            entrada.Movimento!.Id, autor, "Estorno de teste", "estorno-teste"));
        Assert.True(estorno.Sucesso, estorno.Erro);
        var versaoSaldo = await db.SaldosProdutoAcabado.Where(s => s.ProdutoVarianteId == extra.Id)
            .Select(s => EF.Property<byte[]>(s, "Versao")).SingleAsync();
        var contagem = await estoque.ContarAsync(new ContarProdutoAcabadoInput(
            extra.Id, entrada.Lote!.Id, autor, 0, 0, 0, "Contagem de teste", "contagem-teste", versaoSaldo));
        Assert.True(contagem.Sucesso, contagem.Erro);
        Assert.True(contagem.SemDiferenca);
        var agregado = await new EfProdutoRepository(db).ConsultarAsync(produto.Id);
        var resultado = await new EfExcluirVariante(db, TimeProvider.System).ExecutarAsync(
            produto.Id, extra.Id, ProdutoResponse.From(agregado!).Versao, autor, Guid.NewGuid(),
            p => ProdutoResponse.From(p).Versao, default);
        Assert.Equal((409, "variante_com_historico_estoque"), resultado);
        Assert.Equal(1, await db.ContagensSemDiferencaProdutoAcabado.CountAsync());
        Assert.Equal(2, await db.MovimentosProdutoAcabado.CountAsync(m => m.ProdutoVarianteId == extra.Id));
        Assert.True(await db.ProdutoVariantes.AnyAsync(v => v.Id == extra.Id));
    }

    [SqlServerIntegrationFact]
    public async Task ExclusaoVariante_DuasExclusoesConcorrentesProduzemUmEvento()
    {
        var barreira = new BarreiraAtendimento();
        await using var ambiente = await Ambiente.CriarAsync(interceptor: barreira);
        using var admin = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Administrador);
        using var outroAdmin = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Administrador);
        await using var db = ambiente.Banco.CreateVerificationContext();
        var (produto, _, extra) = await ReservarProdutoAcabadoSqlServerIntegrationTests.CriarProdutoCompravelAsync(db);
        var rota = $"/api/admin/produtos/{produto.Id:D}";
        var versao = await VersaoProdutoAsync(admin, rota);
        barreira.Armar("catalog-promotions");
        var primeira = ExcluirVarianteAsync(admin, rota, extra.Id, versao, Guid.NewGuid());
        Task<HttpResponseMessage> segunda;
        try
        {
            await barreira.PrimeiroAdquiriu.Task.WaitAsync(TimeSpan.FromSeconds(8));
            segunda = ExcluirVarianteAsync(outroAdmin, rota, extra.Id, versao, Guid.NewGuid());
            await barreira.SegundoSolicitou.Task.WaitAsync(TimeSpan.FromSeconds(8));
            Assert.False(segunda.IsCompleted);
        }
        finally
        {
            barreira.Prosseguir.TrySetResult();
        }
        using var sucesso = await primeira;
        using var ausente = await segunda;
        Assert.Equal(HttpStatusCode.NoContent, sucesso.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, ausente.StatusCode);
        Assert.Equal(1, await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM OperacoesExclusaoVariante").SingleAsync());
    }

    [SqlServerIntegrationFact]
    public async Task ExclusaoVariante_EdicaoNoIntervaloInvalidaVersao()
    {
        var barreira = new BarreiraAtendimento();
        await using var ambiente = await Ambiente.CriarAsync(interceptor: barreira);
        using var admin = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Administrador);
        await using var db = ambiente.Banco.CreateVerificationContext();
        var (produto, _, extra) = await ReservarProdutoAcabadoSqlServerIntegrationTests.CriarProdutoCompravelAsync(db);
        var rota = $"/api/admin/produtos/{produto.Id:D}";
        var versao = await VersaoProdutoAsync(admin, rota);
        barreira.Armar($"finished-variant:{extra.Id:D}");
        var exclusao = ExcluirVarianteAsync(admin, rota, extra.Id, versao, Guid.NewGuid());
        try
        {
            await barreira.PrimeiroAdquiriu.Task.WaitAsync(TimeSpan.FromSeconds(8));
            await using var edicao = ambiente.Banco.CreateVerificationContext();
            var atual = await edicao.Produtos.SingleAsync(p => p.Id == produto.Id);
            atual.AtualizarDados("Nome atualizado", atual.Descricao);
            await edicao.SaveChangesAsync();
        }
        finally
        {
            barreira.Prosseguir.TrySetResult();
        }
        using var conflito = await exclusao;
        await ConferirErroPostalAsync(conflito, 409, "versao_desatualizada");
        Assert.True(await db.ProdutoVariantes.AnyAsync(v => v.Id == extra.Id));
    }

    [SqlServerIntegrationFact]
    public async Task ExclusaoVariante_ValidaVersaoReplayImagensEUltimaVariante()
    {
        await using var ambiente = await Ambiente.CriarAsync();
        using var admin = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Administrador);
        using var cliente = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Cliente);
        await using var db = ambiente.Banco.CreateVerificationContext();
        var (produto, padrao, extra) = await ReservarProdutoAcabadoSqlServerIntegrationTests.CriarProdutoCompravelAsync(db);
        var agregado = await db.Produtos.Include(p => p.Variantes).Include(p => p.Imagens).ThenInclude(i => i.Variantes)
            .SingleAsync(p => p.Id == produto.Id);
        var imagem = Assert.Single(agregado.Imagens);
        agregado.AssociarImagemAVariante(imagem.Id, padrao.Id);
        agregado.AssociarImagemAVariante(imagem.Id, extra.Id);
        await db.SaveChangesAsync();
        await db.Database.MigrateAsync("20261124011336_RegistrarUsoEExclusaoFornecedor");
        await db.Database.MigrateAsync();
        var rota = $"/api/admin/produtos/{produto.Id:D}";
        var chave = Guid.NewGuid();
        var versao = await VersaoProdutoAsync(admin, rota);
        using (var negado = await ExcluirVarianteAsync(cliente, rota, extra.Id, versao, chave))
            Assert.Equal(HttpStatusCode.Forbidden, negado.StatusCode);
        using (var logout = await cliente.EnviarAsync("/api/auth/logout", new { }))
            Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        using (var anonimo = await cliente.Http.DeleteAsync($"{rota}/variantes/{extra.Id:D}"))
            Assert.Equal(HttpStatusCode.Unauthorized, anonimo.StatusCode);
        using (var semCsrf = await ExcluirVarianteAsync(admin, rota, extra.Id, versao, chave, csrf: false))
            Assert.Equal(HttpStatusCode.BadRequest, semCsrf.StatusCode);
        using (var semVersao = await ExcluirVarianteAsync(admin, rota, extra.Id, null, chave))
            Assert.Equal(HttpStatusCode.BadRequest, semVersao.StatusCode);
        using (var versaoInvalida = await ExcluirVarianteAsync(admin, rota, extra.Id, "*", chave))
            await ConferirErroPostalAsync(versaoInvalida, 400, "versao_invalida");
        var outroProduto = await admin.PostAsync<ProdutoResponse>("/api/admin/produtos",
            new { nome = "Outro produto", categoriaId = produto.CategoriaId, simples = true,
                variantePadrao = new { unidadeVenda = "UN", precoVenda = 10 } }, HttpStatusCode.Created);
        using (var alheia = await ExcluirVarianteAsync(admin, rota, Assert.Single(outroProduto.Variantes).Id, versao, chave))
            Assert.Equal(HttpStatusCode.NotFound, alheia.StatusCode);
        using (var principal = await ExcluirVarianteAsync(admin, rota, padrao.Id, versao, chave))
            await ConferirErroPostalAsync(principal, 409, "variante_padrao");
        using (var obsoleta = await ExcluirVarianteAsync(admin, rota, extra.Id, $"\"{new string('0', 64)}\"", chave))
            await ConferirErroPostalAsync(obsoleta, 409, "versao_desatualizada");
        using (var excluida = await ExcluirVarianteAsync(admin, rota, extra.Id, versao, chave))
            Assert.Equal(HttpStatusCode.NoContent, excluida.StatusCode);
        using (var replay = await ExcluirVarianteAsync(admin, rota, extra.Id, versao, chave))
            Assert.Equal(HttpStatusCode.NoContent, replay.StatusCode);
        var atual = await VersaoProdutoAsync(admin, rota);
        Assert.NotEqual(versao, atual);
        using (var divergente = await ExcluirVarianteAsync(admin, rota, padrao.Id, atual, chave))
            await ConferirErroPostalAsync(divergente, 409, "chave_em_uso");
        using (var ultima = await ExcluirVarianteAsync(admin, rota, padrao.Id, atual, Guid.NewGuid()))
            await ConferirErroPostalAsync(ultima, 409, "ultima_variante");
        db.ChangeTracker.Clear();
        var persistido = await db.Produtos.Include(p => p.Variantes).Include(p => p.Imagens).ThenInclude(i => i.Variantes)
            .SingleAsync(p => p.Id == produto.Id);
        Assert.True(persistido.Publicado);
        Assert.Equal(padrao.Id, Assert.Single(persistido.Variantes).Id);
        var imagemPreservada = Assert.Single(persistido.Imagens);
        Assert.Equal(imagem.Id, imagemPreservada.Id);
        Assert.Equal(padrao.Id, Assert.Single(imagemPreservada.Variantes).ProdutoVarianteId);
        Assert.Equal(1, await db.Database.SqlQuery<int>(
            $"SELECT COUNT(*) AS [Value] FROM OperacoesExclusaoVariante WHERE VarianteId = {extra.Id}").SingleAsync());
        using (var ausente = await ExcluirVarianteAsync(admin, rota, extra.Id, atual, Guid.NewGuid()))
            Assert.Equal(HttpStatusCode.NotFound, ausente.StatusCode);
        var recriada = await admin.PostAsync<ProdutoResponse>(rota + "/variantes",
            new { identificacao = extra.Identificacao, unidadeVenda = "UN", precoVenda = 10 }, HttpStatusCode.OK);
        var nova = Assert.Single(recriada.Variantes, v => !v.Padrao);
        Assert.NotEqual(extra.Id, nova.Id);
        db.ChangeTracker.Clear();
        var editavel = await db.Produtos.Include(p => p.Variantes).SingleAsync(p => p.Id == produto.Id);
        editavel.DesativarVariante(nova.Id);
        await db.SaveChangesAsync();
        using (var inativa = await ExcluirVarianteAsync(admin, rota, nova.Id, await VersaoProdutoAsync(admin, rota), Guid.NewGuid()))
            Assert.Equal(HttpStatusCode.NoContent, inativa.StatusCode);
    }

    [SqlServerIntegrationFact]
    public async Task ExclusaoVariante_ProtegeSnapshotPostalERejeitaSolicitacaoAposExclusao()
    {
        await using var ambiente = await Ambiente.CriarAsync(municipiosPostais: MunicipiosPostaisTeste);
        using var admin = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Administrador);
        using var cliente = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Cliente);
        await using var db = ambiente.Banco.CreateVerificationContext();
        var (produto, _, extra) = await ReservarProdutoAcabadoSqlServerIntegrationTests.CriarProdutoCompravelAsync(db);
        await EscreverAtendimentoAsync(admin, HttpMethod.Put, "/correios-manual",
            new SalvarCorreiosManual(true, 48, 0), HttpStatusCode.OK);
        var corpo = new { itens = new[] { new { produtoId = produto.Id, produtoVarianteId = extra.Id, quantidade = 1 } },
            endereco = DestinoPostalTeste };
        using var criada = await EscreverPostalAsync(cliente, Postal, corpo);
        Assert.Equal(HttpStatusCode.Created, criada.StatusCode);
        var rota = $"/api/admin/produtos/{produto.Id:D}";
        using var bloqueada = await ExcluirVarianteAsync(admin, rota, extra.Id, await VersaoProdutoAsync(admin, rota), Guid.NewGuid());
        await ConferirErroPostalAsync(bloqueada, 409, "variante_com_historico_comercial");

        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE SolicitacoesCorreios SET ItensJson = {"{}"}");
        using var inconclusiva = await ExcluirVarianteAsync(admin, rota, extra.Id, await VersaoProdutoAsync(admin, rota), Guid.NewGuid());
        await ConferirErroPostalAsync(inconclusiva, 409, "variante_historico_inconclusivo");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE SolicitacoesCorreios SET ItensJson = {JsonSerializer.Serialize(new[] { new { ProdutoId = produto.Id, ProdutoVarianteId = Guid.NewGuid(), Nota = extra.Id } })}");
        using var excluida = await ExcluirVarianteAsync(admin, rota, extra.Id, await VersaoProdutoAsync(admin, rota), Guid.NewGuid());
        Assert.Equal(HttpStatusCode.NoContent, excluida.StatusCode);
        using var recusada = await EscreverPostalAsync(cliente, Postal, corpo with { endereco = DestinoPostalTeste with { Numero = "16" } });
        await ConferirErroPostalAsync(recusada, 400, "itens_invalidos");
        Assert.False(await db.Pedidos.AnyAsync());
        Assert.False(await db.ReservasProdutoAcabado.AnyAsync());
    }

    [SqlServerIntegrationFact]
    public async Task ExclusaoVariante_BloqueiaAssociacaoPromocionalRemovida()
    {
        await using var ambiente = await Ambiente.CriarAsync(promocoesComerciais: true);
        using var admin = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Administrador);
        await using var db = ambiente.Banco.CreateVerificationContext();
        var (produto, padrao, extra) = await ReservarProdutoAcabadoSqlServerIntegrationTests.CriarProdutoCompravelAsync(db);
        var promocoes = new EfPromocoes(db, TimeProvider.System);
        var dados = new ConfigurarPromocao(produto.Id, "PrecoFixo", "VariantesSelecionadas", "900", null, null, [extra.Id]);
        var autor = Guid.NewGuid();
        var chaveCriacao = Guid.NewGuid();
        var criada = await promocoes.SalvarAsync(null, dados, null, false, autor, chaveCriacao, default);
        Assert.Equal(201, criada.Status);
        db.ChangeTracker.Clear();
        var editada = await promocoes.SalvarAsync(criada.Detalhe!.Id, dados with { VariantesIds = [padrao.Id] },
            criada.Detalhe.Versao, false, autor, Guid.NewGuid(), default);
        Assert.Equal(200, editada.Status);
        Assert.DoesNotContain(extra.Id, editada.Detalhe!.VariantesIds);
        var rota = $"/api/admin/produtos/{produto.Id:D}";
        using var bloqueada = await ExcluirVarianteAsync(admin, rota, extra.Id, await VersaoProdutoAsync(admin, rota), Guid.NewGuid());
        await ConferirErroPostalAsync(bloqueada, 409, "variante_com_historico_comercial");
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE OperacoesPromocao
            SET Resultado = JSON_MODIFY(Resultado, '$.Detalhe.VariantesIds', JSON_QUERY('[]'))
            WHERE AdministradorId = {autor} AND Chave = {chaveCriacao}
            """);
        using var inconclusiva = await ExcluirVarianteAsync(admin, rota, extra.Id, await VersaoProdutoAsync(admin, rota), Guid.NewGuid());
        await ConferirErroPostalAsync(inconclusiva, 409, "variante_historico_inconclusivo");
        Assert.True(await db.ProdutoVariantes.AnyAsync(v => v.Id == extra.Id));
    }

    [SqlServerIntegrationFact]
    public async Task ExclusaoVariante_BloqueiaReceitaMesmoSemEstoque()
    {
        await using var ambiente = await Ambiente.CriarAsync();
        using var admin = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Administrador);
        await using var db = ambiente.Banco.CreateVerificationContext();
        var (produto, _, extra) = await ReservarProdutoAcabadoSqlServerIntegrationTests.CriarProdutoCompravelAsync(db);
        var insumo = InsumoEstoque.Criar("Base da receita", TipoInsumoEstoque.MateriaPrima, UnidadeFisicaEstoque.Grama);
        db.InsumosEstoque.Add(insumo);
        await db.SaveChangesAsync();
        await admin.PostAsync<JsonElement>("/api/admin/receitas",
            new ReceitaInput(extra.Id, "Receita da variante", 1, [new(insumo.Id, "10", "Grama")], null), HttpStatusCode.Created);
        var rota = $"/api/admin/produtos/{produto.Id:D}";
        using var bloqueada = await ExcluirVarianteAsync(admin, rota, extra.Id, await VersaoProdutoAsync(admin, rota), Guid.NewGuid());
        await ConferirErroPostalAsync(bloqueada, 409, "variante_com_historico_producao");
        Assert.False(await db.LotesProdutoAcabado.AnyAsync());
        Assert.True(await db.ProdutoVariantes.AnyAsync(v => v.Id == extra.Id));
    }

    [SqlServerIntegrationFact]
    public async Task ExclusaoVariante_PreservaOrdemCancelada()
    {
        await using var ambiente = await Ambiente.CriarAsync();
        using var admin = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Administrador);
        await using var db = ambiente.Banco.CreateVerificationContext();
        var dados = await PrepararCompraAsync(admin, db);
        var (_, ordem) = await ReceberEIniciarAsync(admin, dados);
        await admin.PostAsync<JsonElement>($"{Producao}/{ordem.Resumo.Id:D}/cancelar", new { },
            HttpStatusCode.OK, Guid.NewGuid());
        var agregado = await db.Produtos.Include(p => p.Variantes).SingleAsync(p => p.Id == dados.ProdutoId);
        var alternativa = Assert.Single(agregado.Variantes, v => !v.Padrao);
        var rota = $"/api/admin/produtos/{dados.ProdutoId:D}";
        using var mudarPadrao = new HttpRequestMessage(HttpMethod.Put, $"{rota}/variantes/{alternativa.Id:D}/padrao");
        mudarPadrao.Headers.Add("X-XSRF-TOKEN", await TokenComprovanteAsync(admin));
        using var padraoAlterado = await admin.Http.SendAsync(mudarPadrao);
        padraoAlterado.EnsureSuccessStatusCode();
        using var bloqueada = await ExcluirVarianteAsync(admin, rota, dados.VarianteId,
            await VersaoProdutoAsync(admin, rota), Guid.NewGuid());
        await ConferirErroPostalAsync(bloqueada, 409, "variante_com_historico_producao");
        Assert.Equal(SituacaoOrdemProducao.Cancelada, (await db.OrdensProducao.AsNoTracking().SingleAsync()).Situacao);
        Assert.True(await db.ProdutoVariantes.AnyAsync(v => v.Id == dados.VarianteId));
    }

    [SqlServerIntegrationFact]
    public async Task ExclusaoVariante_PreservaVendaExternaMesmoComSaldoZerado()
    {
        await using var ambiente = await Ambiente.CriarAsync();
        using var admin = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Administrador);
        await using var db = ambiente.Banco.CreateVerificationContext();
        var (produto, _, extra) = await ReservarProdutoAcabadoSqlServerIntegrationTests.CriarProdutoCompravelAsync(db);
        await admin.PostAsync<JsonElement>($"/api/admin/estoque/produtos-acabados/variantes/{extra.Id:D}/entradas",
            new { tipo = "EntradaManual", quantidade = 1, condicaoDestino = "Vendavel", semControleValidade = true, motivo = "Teste" },
            HttpStatusCode.Created, Guid.NewGuid());
        var venda = new CriarVendaExternaInput("Presencial", null, new DateOnly(2026, 11, 17), "Cliente de teste", null,
            [new(produto.Id, extra.Id, 1, 900)], null);
        await admin.PostAsync<JsonElement>("/api/admin/vendas-externas", venda, HttpStatusCode.Created, Guid.NewGuid());
        var rota = $"/api/admin/produtos/{produto.Id:D}";
        using var bloqueada = await ExcluirVarianteAsync(admin, rota, extra.Id, await VersaoProdutoAsync(admin, rota), Guid.NewGuid());
        await ConferirErroPostalAsync(bloqueada, 409, "variante_com_historico_comercial");
        Assert.Equal(0, (await db.SaldosProdutoAcabado.SingleAsync()).QuantidadeFisica);
        Assert.Equal(1, await db.Set<VendaExterna>().CountAsync());
        Assert.True(await db.ProdutoVariantes.AnyAsync(v => v.Id == extra.Id));
    }

    private static async Task<string> VersaoProdutoAsync(Sessao admin, string rota)
    {
        using var resposta = await admin.Http.GetAsync(rota);
        resposta.EnsureSuccessStatusCode();
        return resposta.Headers.ETag!.Tag;
    }

    [SqlServerIntegrationFact]
    public Task ExclusaoVariante_CotacaoPostalPrimeiro() => DisputarVariantePostalAsync(postalPrimeiro: true);

    [SqlServerIntegrationFact]
    public Task ExclusaoVariante_ExclusaoAntesDaCotacaoPostal() => DisputarVariantePostalAsync(postalPrimeiro: false);

    [SqlServerIntegrationFact]
    public Task ExclusaoVariante_EntradaEstoquePrimeiro() => DisputarVarianteEstoqueAsync(entradaPrimeiro: true);

    [SqlServerIntegrationFact]
    public Task ExclusaoVariante_ExclusaoAntesDaEntradaEstoque() => DisputarVarianteEstoqueAsync(entradaPrimeiro: false);

    [SqlServerIntegrationFact]
    public Task ExclusaoVariante_CheckoutPrimeiro() => DisputarVarianteCheckoutAsync(checkoutPrimeiro: true);

    [SqlServerIntegrationFact]
    public Task ExclusaoVariante_ExclusaoAntesDoCheckout() => DisputarVarianteCheckoutAsync(checkoutPrimeiro: false);

    private static async Task DisputarVarianteCheckoutAsync(bool checkoutPrimeiro)
    {
        var barreira = new BarreiraAtendimento();
        await using var ambiente = await Ambiente.CriarAsync(interceptor: barreira);
        using var admin = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Administrador);
        using var cliente = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Cliente);
        await using var db = ambiente.Banco.CreateVerificationContext();
        var (produto, _, extra) = await ReservarProdutoAcabadoSqlServerIntegrationTests.CriarProdutoCompravelAsync(db);
        if (checkoutPrimeiro)
            await admin.PostAsync<JsonElement>($"/api/admin/estoque/produtos-acabados/variantes/{extra.Id:D}/entradas",
                new { tipo = "EntradaManual", quantidade = 1, condicaoDestino = "Vendavel", semControleValidade = true, motivo = "Teste" },
                HttpStatusCode.Created, Guid.NewGuid());
        var rota = $"/api/admin/produtos/{produto.Id:D}";
        var versao = await VersaoProdutoAsync(admin, rota);
        var itens = new[] { new { produtoId = produto.Id, produtoVarianteId = extra.Id, quantidade = 1 } };
        string? ofertaEntregaId = null;
        if (!checkoutPrimeiro)
        {
            var cotacao = await cliente.PostAsync<JsonElement>(CotacoesEntrega, new { itens }, HttpStatusCode.OK);
            ofertaEntregaId = Assert.Single(cotacao.GetProperty("ofertas").EnumerateArray()).GetProperty("id").GetString();
            Assert.False(string.IsNullOrWhiteSpace(ofertaEntregaId));
        }
        Task<HttpResponseMessage> Checkout() => cliente.EnviarAsync("/api/pedidos", new { itens, ofertaEntregaId }, Guid.NewGuid());
        Task<HttpResponseMessage> Excluir() => ExcluirVarianteAsync(admin, rota, extra.Id, versao, Guid.NewGuid());
        barreira.Armar($"finished-variant:{extra.Id:D}");
        var primeira = checkoutPrimeiro ? Checkout() : Excluir();
        Task<HttpResponseMessage> segunda;
        try
        {
            await barreira.PrimeiroAdquiriu.Task.WaitAsync(TimeSpan.FromSeconds(8));
            segunda = checkoutPrimeiro ? Excluir() : Checkout();
            await barreira.SegundoSolicitou.Task.WaitAsync(TimeSpan.FromSeconds(8));
            Assert.False(segunda.IsCompleted);
        }
        finally
        {
            barreira.Prosseguir.TrySetResult();
        }
        using var sucesso = await primeira;
        using var recusa = await segunda;
        Assert.Equal(checkoutPrimeiro ? HttpStatusCode.Created : HttpStatusCode.NoContent, sucesso.StatusCode);
        if (checkoutPrimeiro)
        {
            await ConferirErroPostalAsync(recusa, 409, "variante_com_historico_comercial");
            var pedido = (await sucesso.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
            await admin.PostAsync<JsonElement>($"/api/admin/pedidos/{pedido:D}/cancelar",
                new { motivo = "Cancelamento de teste" }, HttpStatusCode.OK, Guid.NewGuid());
            using var aindaBloqueada = await Excluir();
            await ConferirErroPostalAsync(aindaBloqueada, 409, "variante_com_historico_comercial");
        }
        else
            Assert.Equal(HttpStatusCode.Conflict, recusa.StatusCode);
        Assert.Equal(checkoutPrimeiro ? 1 : 0, await db.Pedidos.CountAsync());
        Assert.Equal(checkoutPrimeiro ? 1 : 0, await db.ReservasProdutoAcabado.CountAsync());
    }

    private static async Task DisputarVarianteEstoqueAsync(bool entradaPrimeiro)
    {
        var barreira = new BarreiraAtendimento();
        await using var ambiente = await Ambiente.CriarAsync(interceptor: barreira);
        using var admin = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Administrador);
        using var outroAdmin = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Administrador);
        await using var db = ambiente.Banco.CreateVerificationContext();
        var (produto, _, extra) = await ReservarProdutoAcabadoSqlServerIntegrationTests.CriarProdutoCompravelAsync(db);
        var rota = $"/api/admin/produtos/{produto.Id:D}";
        var versao = await VersaoProdutoAsync(admin, rota);
        var entrada = new { tipo = "EntradaManual", quantidade = 1, condicaoDestino = "Vendavel",
            semControleValidade = true, motivo = "Teste de concorrência" };
        Task<HttpResponseMessage> Registrar() => outroAdmin.EnviarAsync(
            $"/api/admin/estoque/produtos-acabados/variantes/{extra.Id:D}/entradas", entrada, Guid.NewGuid());
        Task<HttpResponseMessage> Excluir() => ExcluirVarianteAsync(admin, rota, extra.Id, versao, Guid.NewGuid());
        barreira.Armar($"finished-variant:{extra.Id:D}");
        var primeira = entradaPrimeiro ? Registrar() : Excluir();
        Task<HttpResponseMessage> segunda;
        try
        {
            await barreira.PrimeiroAdquiriu.Task.WaitAsync(TimeSpan.FromSeconds(8));
            segunda = entradaPrimeiro ? Excluir() : Registrar();
            await barreira.SegundoSolicitou.Task.WaitAsync(TimeSpan.FromSeconds(8));
            Assert.False(segunda.IsCompleted);
        }
        finally
        {
            barreira.Prosseguir.TrySetResult();
        }
        using var sucesso = await primeira;
        using var recusa = await segunda;
        Assert.Equal(entradaPrimeiro ? HttpStatusCode.Created : HttpStatusCode.NoContent, sucesso.StatusCode);
        if (entradaPrimeiro)
            await ConferirErroPostalAsync(recusa, 409, "variante_com_historico_estoque");
        else
            Assert.Equal(HttpStatusCode.NotFound, recusa.StatusCode);
        Assert.Equal(entradaPrimeiro ? 1 : 0, await db.MovimentosProdutoAcabado.CountAsync());
        Assert.Equal(entradaPrimeiro, await db.ProdutoVariantes.AnyAsync(v => v.Id == extra.Id));
    }

    private static async Task DisputarVariantePostalAsync(bool postalPrimeiro)
    {
        var barreira = new BarreiraAtendimento();
        await using var ambiente = await Ambiente.CriarAsync(interceptor: barreira, municipiosPostais: MunicipiosPostaisTeste);
        using var admin = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Administrador);
        using var cliente = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Cliente);
        await using var db = ambiente.Banco.CreateVerificationContext();
        var (produto, _, extra) = await ReservarProdutoAcabadoSqlServerIntegrationTests.CriarProdutoCompravelAsync(db);
        await EscreverAtendimentoAsync(admin, HttpMethod.Put, "/correios-manual",
            new SalvarCorreiosManual(true, 48, 0), HttpStatusCode.OK);
        var rota = $"/api/admin/produtos/{produto.Id:D}";
        var versao = await VersaoProdutoAsync(admin, rota);
        var corpo = new { itens = new[] { new { produtoId = produto.Id, produtoVarianteId = extra.Id, quantidade = 1 } },
            endereco = DestinoPostalTeste };
        Task<HttpResponseMessage> Cotar() => EscreverPostalAsync(cliente, Postal, corpo);
        Task<HttpResponseMessage> Excluir() => ExcluirVarianteAsync(admin, rota, extra.Id, versao, Guid.NewGuid());
        barreira.Armar($"finished-variant:{extra.Id:D}");
        var primeira = postalPrimeiro ? Cotar() : Excluir();
        Task<HttpResponseMessage> segunda;
        try
        {
            await barreira.PrimeiroAdquiriu.Task.WaitAsync(TimeSpan.FromSeconds(8));
            segunda = postalPrimeiro ? Excluir() : Cotar();
            await barreira.SegundoSolicitou.Task.WaitAsync(TimeSpan.FromSeconds(8));
            Assert.False(segunda.IsCompleted);
        }
        finally
        {
            barreira.Prosseguir.TrySetResult();
        }
        using var sucesso = await primeira;
        using var recusa = await segunda;
        Assert.Equal(postalPrimeiro ? HttpStatusCode.Created : HttpStatusCode.NoContent, sucesso.StatusCode);
        await ConferirErroPostalAsync(recusa, postalPrimeiro ? 409 : 400,
            postalPrimeiro ? "variante_com_historico_comercial" : "itens_invalidos");
        Assert.Equal(postalPrimeiro, await db.ProdutoVariantes.AnyAsync(v => v.Id == extra.Id));
    }

    private static async Task<HttpResponseMessage> ExcluirVarianteAsync(Sessao sessao, string rota, Guid variante,
        string? versao, Guid chave, bool csrf = true)
    {
        using var requisicao = new HttpRequestMessage(HttpMethod.Delete, $"{rota}/variantes/{variante:D}");
        if (versao is not null) requisicao.Headers.IfMatch.ParseAdd(versao);
        if (csrf) requisicao.Headers.Add("X-XSRF-TOKEN", await TokenComprovanteAsync(sessao));
        requisicao.Headers.Add("Idempotency-Key", chave.ToString("D"));
        return await sessao.Http.SendAsync(requisicao);
    }
}
