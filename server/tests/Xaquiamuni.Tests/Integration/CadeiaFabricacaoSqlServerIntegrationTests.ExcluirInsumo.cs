using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xaquiamuni.Aplicacao.Estoque;
using Xaquiamuni.Dominio.Entidades;
using Xaquiamuni.Dominio.Estoque;
using Xaquiamuni.Infraestrutura.Identidade;
using Xaquiamuni.Infraestrutura.Persistencia;

namespace Xaquiamuni.Tests.Integration;

public sealed partial class CadeiaFabricacaoSqlServerIntegrationTests
{
    [SqlServerIntegrationFact]
    public async Task ExclusaoInsumo_ContratoVersaoAuditoriaERecadastro()
    {
        await using var ambiente = await Ambiente.CriarAsync();
        using var admin = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Administrador);
        using var cliente = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Cliente);
        await using var db = ambiente.Banco.CreateVerificationContext();
        var (primeiro, forma, outro, outraForma, _) = await SemearExclusaoInsumoAsync(db);
        var adicional = primeiro.AdicionarFormaCompra("Saco", 5, UnidadeFisicaEstoque.Grama);
        primeiro.DefinirFormaCompraAtiva(adicional.Id, false);
        db.FormasCompraInsumo.Add(adicional);
        await db.SaveChangesAsync();
        var rota = $"/api/admin/insumos/{primeiro.Id:D}";
        var subrota = $"{rota}/formas-compra/{forma.Id:D}";
        var versao = await VersaoProdutoAsync(admin, rota);

        using (var proibido = await RemoverInsumoAsync(cliente, subrota, versao, Guid.NewGuid()))
            Assert.Equal(HttpStatusCode.Forbidden, proibido.StatusCode);
        using (var logout = await cliente.EnviarAsync("/api/auth/logout", new { }))
            Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        using (var anonimo = await cliente.Http.DeleteAsync(subrota))
            Assert.Equal(HttpStatusCode.Unauthorized, anonimo.StatusCode);
        using (var semCsrf = await RemoverInsumoAsync(admin, subrota, versao, Guid.NewGuid(), csrf: false))
            Assert.Equal(HttpStatusCode.BadRequest, semCsrf.StatusCode);
        using (var semVersao = await RemoverInsumoAsync(admin, subrota, null, Guid.NewGuid()))
            await ConferirErroPostalAsync(semVersao, 400, "versao_invalida");
        using (var chaveInvalida = await RemoverInsumoAsync(admin, subrota, versao, Guid.Empty))
            await ConferirErroPostalAsync(chaveInvalida, 400, "chave_invalida");
        using (var fora = await RemoverInsumoAsync(admin, $"{rota}/formas-compra/{outraForma.Id:D}", versao, Guid.NewGuid()))
            Assert.Equal(HttpStatusCode.NotFound, fora.StatusCode);

        var chave = Guid.NewGuid();
        using (var removida = await RemoverInsumoAsync(admin, subrota, versao, chave))
            Assert.Equal(HttpStatusCode.NoContent, removida.StatusCode);
        using (var replay = await RemoverInsumoAsync(admin, subrota, versao, chave))
            Assert.Equal(HttpStatusCode.NoContent, replay.StatusCode);
        using (var antiga = await RemoverInsumoAsync(admin, rota, versao, Guid.NewGuid()))
            await ConferirErroPostalAsync(antiga, 409, "versao_desatualizada");
        Assert.True(await db.FormasCompraInsumo.AnyAsync(f => f.Id == adicional.Id));

        var atual = await VersaoProdutoAsync(admin, rota);
        using (var ultima = await RemoverInsumoAsync(admin, $"{rota}/formas-compra/{adicional.Id:D}", atual, Guid.NewGuid()))
            Assert.Equal(HttpStatusCode.NoContent, ultima.StatusCode);
        var chaveAgregado = Guid.NewGuid();
        var versaoAgregado = await VersaoProdutoAsync(admin, rota);
        using (var excluido = await RemoverInsumoAsync(admin, rota, versaoAgregado, chaveAgregado))
            Assert.Equal(HttpStatusCode.NoContent, excluido.StatusCode);
        Assert.True(await db.InsumosEstoque.AnyAsync(i => i.Id == outro.Id));
        Assert.Equal(3, await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM OperacoesExclusaoInsumo").SingleAsync());
        using (var ausente = await RemoverInsumoAsync(admin, rota, versaoAgregado, Guid.NewGuid()))
            Assert.Equal(HttpStatusCode.NotFound, ausente.StatusCode);

        db.ChangeTracker.Clear();
        var novo = InsumoEstoque.Criar(primeiro.Nome, TipoInsumoEstoque.MateriaPrima, UnidadeFisicaEstoque.Grama);
        var novaForma = novo.AdicionarFormaCompra(forma.Nome, 20, UnidadeFisicaEstoque.Grama);
        novo.DefinirAtivo(false);
        db.InsumosEstoque.Add(novo);
        await db.SaveChangesAsync();
        Assert.NotEqual(primeiro.Id, novo.Id);
        Assert.NotEqual(forma.Id, novaForma.Id);
        var novaRota = $"/api/admin/insumos/{novo.Id:D}";
        var novaVersao = await VersaoProdutoAsync(admin, novaRota);
        using (var chaveAntiga = await RemoverInsumoAsync(admin, novaRota, novaVersao, chaveAgregado))
            await ConferirErroPostalAsync(chaveAntiga, 409, "chave_em_uso");
        using var integral = await RemoverInsumoAsync(admin, novaRota, novaVersao, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.NoContent, integral.StatusCode);
        Assert.False(await db.FormasCompraInsumo.AnyAsync(f => f.Id == novaForma.Id));
    }

    [SqlServerIntegrationFact]
    public async Task ExclusaoInsumo_PreservaHistoricoDeComponenteSubstituidoECancelado()
    {
        await using var ambiente = await Ambiente.CriarAsync();
        await using var db = ambiente.Banco.CreateVerificationContext();
        var (insumo, forma, outro, outraForma, fornecedor) = await SemearExclusaoInsumoAsync(db);
        var autor = Guid.NewGuid();
        var compras = new EfCompraMateriaPrimaRepository(db, TimeProvider.System);
        var criada = await compras.CriarAsync(CompraParaExcluir(fornecedor.Id, insumo.Id, forma.Id), autor, Guid.NewGuid(), default);
        Assert.Equal(CompraMateriaPrimaStatus.Sucesso, criada.Status);
        db.ChangeTracker.Clear();
        var editada = await compras.EditarAsync(criada.Compra!.Id,
            CompraParaExcluir(fornecedor.Id, outro.Id, outraForma.Id) with { Versao = criada.Compra.Versao }, autor, default);
        Assert.Equal(CompraMateriaPrimaStatus.Sucesso, editada.Status);
        db.ChangeTracker.Clear();
        var cancelada = await compras.CancelarAsync(criada.Compra.Id, editada.Compra!.Versao, autor, default);
        Assert.Equal(CompraMateriaPrimaStatus.Sucesso, cancelada.Status);
        Assert.False(await db.Set<ComponenteCompraMateriaPrima>().AnyAsync(c => c.InsumoId == insumo.Id));

        using var admin = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Administrador);
        var rota = $"/api/admin/insumos/{insumo.Id:D}";
        var versao = await VersaoProdutoAsync(admin, rota);
        using var bloqueioForma = await RemoverInsumoAsync(admin, $"{rota}/formas-compra/{forma.Id:D}", versao, Guid.NewGuid());
        await ConferirErroPostalAsync(bloqueioForma, 409, "forma_compra_com_historico");
        using var bloqueioInsumo = await RemoverInsumoAsync(admin, rota, versao, Guid.NewGuid());
        await ConferirErroPostalAsync(bloqueioInsumo, 409, "insumo_com_compras");
        var rotaOutro = $"/api/admin/insumos/{outro.Id:D}";
        using var bloqueioAtual = await RemoverInsumoAsync(admin, rotaOutro, await VersaoProdutoAsync(admin, rotaOutro), Guid.NewGuid());
        await ConferirErroPostalAsync(bloqueioAtual, 409, "insumo_com_compras");
        Assert.Equal(2, await db.InsumosEstoque.CountAsync());
        Assert.Equal(2, await db.FormasCompraInsumo.CountAsync());
        Assert.Equal(SituacaoCompraMateriaPrima.Cancelada, await db.ComprasMateriasPrimas.Select(c => c.Situacao).SingleAsync());
    }

    [SqlServerIntegrationFact]
    public async Task ExclusaoInsumo_MigrationDistingueHistoricoComprovadoInconclusivoENovo()
    {
        await using var banco = await SqlServerTestDatabase.CreateAsync(
            targetMigration: "20261124060713_AuditarExclusaoProdutoELimpezaImagens");
        await using var db = banco.CreateVerificationContext();
        var (insumo, forma, outro, outraForma, fornecedor) = await SemearExclusaoInsumoAsync(db);
        var autor = Guid.NewGuid();
        var compra = await new EfCompraMateriaPrimaRepository(db, TimeProvider.System).CriarAsync(
            CompraParaExcluir(fornecedor.Id, insumo.Id, forma.Id), autor, Guid.NewGuid(), default);
        Assert.Equal(CompraMateriaPrimaStatus.Sucesso, compra.Status);
        await db.Database.MigrateAsync();
        db.ChangeTracker.Clear();
        var exclusao = new EfExcluirInsumo(db, TimeProvider.System);
        Assert.Equal((409, "forma_compra_com_historico"), await exclusao.ExecutarAsync(insumo.Id,
            forma.Id, VersaoCadastroInsumo.Calcular(insumo), autor, Guid.NewGuid(), default));
        Assert.Equal((409, "forma_compra_historico_inconclusivo"), await exclusao.ExecutarAsync(outro.Id,
            outraForma.Id, VersaoCadastroInsumo.Calcular(outro), autor, Guid.NewGuid(), default));
        Assert.Equal((409, "insumo_historico_inconclusivo"), await exclusao.ExecutarAsync(outro.Id,
            null, VersaoCadastroInsumo.Calcular(outro), autor, Guid.NewGuid(), default));
        var novo = InsumoEstoque.Criar("Novo após migration", TipoInsumoEstoque.MateriaPrima, UnidadeFisicaEstoque.Grama);
        db.InsumosEstoque.Add(novo);
        await db.SaveChangesAsync();
        Assert.Equal(204, (await exclusao.ExecutarAsync(novo.Id, null, VersaoCadastroInsumo.Calcular(novo),
            autor, Guid.NewGuid(), default)).Status);
    }

    [SqlServerIntegrationFact]
    public Task ExclusaoInsumo_CompraPrimeiro() => DisputarCompraEExclusaoAsync(false, true);

    [SqlServerIntegrationFact]
    public Task ExclusaoInsumo_AntesDaCompra() => DisputarCompraEExclusaoAsync(false, false);

    [SqlServerIntegrationFact]
    public Task ExclusaoInsumo_CompraPrimeiroParaForma() => DisputarCompraEExclusaoAsync(true, true);

    [SqlServerIntegrationFact]
    public Task ExclusaoInsumo_FormaAntesDaCompra() => DisputarCompraEExclusaoAsync(true, false);

    [SqlServerIntegrationFact]
    public Task ExclusaoInsumo_EntradaPrimeiro() => DisputarCompraEExclusaoAsync(false, true, "entrada");

    [SqlServerIntegrationFact]
    public Task ExclusaoInsumo_AntesDaEntrada() => DisputarCompraEExclusaoAsync(false, false, "entrada");

    [SqlServerIntegrationFact]
    public Task ExclusaoInsumo_NovaFormaPrimeiro() => DisputarCompraEExclusaoAsync(false, true, "forma");

    [SqlServerIntegrationFact]
    public Task ExclusaoInsumo_AntesDaNovaForma() => DisputarCompraEExclusaoAsync(false, false, "forma");

    [SqlServerIntegrationFact]
    public Task ExclusaoInsumo_DuasExclusoesUmaAuditoria() => DisputarCompraEExclusaoAsync(false, false, "exclusao");

    [SqlServerIntegrationFact]
    public Task ExclusaoInsumo_ContagemPrimeiro() => DisputarCompraEExclusaoAsync(false, true, "contagem");

    [SqlServerIntegrationFact]
    public Task ExclusaoInsumo_AntesDaContagem() => DisputarCompraEExclusaoAsync(false, false, "contagem");

    [SqlServerIntegrationFact]
    public Task ExclusaoInsumo_ReceitaPrimeiro() => DisputarCompraEExclusaoAsync(false, true, "receita");

    [SqlServerIntegrationFact]
    public Task ExclusaoInsumo_AntesDaReceita() => DisputarCompraEExclusaoAsync(false, false, "receita");

    [SqlServerIntegrationFact]
    public Task ExclusaoInsumo_SubstituicaoPrimeiro() => DisputarCompraEExclusaoAsync(false, true, "substituicao");

    [SqlServerIntegrationFact]
    public Task ExclusaoInsumo_AntesDaSubstituicao() => DisputarCompraEExclusaoAsync(false, false, "substituicao");

    private static async Task DisputarCompraEExclusaoAsync(bool excluirForma, bool compraPrimeiro, string operacao = "compra")
    {
        var barreira = new BarreiraAtendimento();
        await using var ambiente = await Ambiente.CriarAsync(interceptor: barreira);
        using var admin = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Administrador);
        using var comprador = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Administrador);
        await using var db = ambiente.Banco.CreateVerificationContext();
        var (insumo, forma, outro, outraForma, fornecedor) = await SemearExclusaoInsumoAsync(db);
        var rota = $"/api/admin/insumos/{insumo.Id:D}";
        var versao = await VersaoProdutoAsync(admin, rota);
        var varianteId = operacao == "receita"
            ? (await ReservarProdutoAcabadoSqlServerIntegrationTests.CriarProdutoCompravelAsync(db)).Padrao.Id : Guid.Empty;
        CompraMateriaPrima? rascunho = null;
        if (operacao == "substituicao")
        {
            var resultado = await new EfCompraMateriaPrimaRepository(db, TimeProvider.System).CriarAsync(
                CompraParaExcluir(fornecedor.Id, outro.Id, outraForma.Id), Guid.NewGuid(), Guid.NewGuid(), default);
            Assert.Equal(CompraMateriaPrimaStatus.Sucesso, resultado.Status);
            rascunho = resultado.Compra!;
        }
        async Task<HttpResponseMessage> SubstituirAsync()
        {
            using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/compras-materias-primas/{rascunho!.Id:D}")
            {
                Content = System.Net.Http.Json.JsonContent.Create(
                    CompraParaExcluir(fornecedor.Id, insumo.Id, forma.Id) with { Versao = rascunho.Versao })
            };
            request.Headers.Add("X-XSRF-TOKEN", await TokenComprovanteAsync(comprador));
            return await comprador.Http.SendAsync(request);
        }
        Task<HttpResponseMessage> Excluir() => RemoverInsumoAsync(admin,
            excluirForma ? $"{rota}/formas-compra/{forma.Id:D}" : rota, versao, Guid.NewGuid());
        Task<HttpResponseMessage> Escrever() => operacao switch
        {
            "entrada" => comprador.EnviarAsync(rota + "/entradas",
                new { tipo = "Aquisicao", quantidadeOriginal = 1, formaCompraId = forma.Id, custoTotal = 10 }, Guid.NewGuid()),
            "forma" => comprador.EnviarAsync(rota + "/formas-compra",
                new { nome = "Embalagem nova", quantidadeEquivalentePorEmbalagem = 21, unidade = "Grama" }),
            "contagem" => comprador.EnviarAsync(rota + "/contagens",
                new { quantidadeEncontrada = 0, unidadeOriginal = "Grama", motivo = "Conferência" }, Guid.NewGuid()),
            "receita" => comprador.EnviarAsync("/api/admin/receitas",
                new ReceitaInput(varianteId, "Receita de concorrência", 1, [new(insumo.Id, "1", "Grama")], null)),
            "substituicao" => SubstituirAsync(),
            "exclusao" => RemoverInsumoAsync(comprador, rota, versao, Guid.NewGuid()),
            _ => comprador.EnviarAsync("/api/admin/compras-materias-primas",
                CompraParaExcluir(fornecedor.Id, insumo.Id, forma.Id), Guid.NewGuid())
        };

        barreira.Armar($"stock-supply:{insumo.Id:D}");
        var primeira = compraPrimeiro ? Escrever() : Excluir();
        Task<HttpResponseMessage> segunda;
        try
        {
            await barreira.PrimeiroAdquiriu.Task.WaitAsync(TimeSpan.FromSeconds(8));
            segunda = compraPrimeiro ? Excluir() : Escrever();
            await barreira.SegundoSolicitou.Task.WaitAsync(TimeSpan.FromSeconds(8));
            Assert.False(segunda.IsCompleted);
        }
        finally
        {
            barreira.Prosseguir.TrySetResult();
        }
        using var vencedora = await primeira;
        using var recusada = await segunda;
        Assert.Equal(!compraPrimeiro ? HttpStatusCode.NoContent : operacao is "substituicao" or "contagem"
            ? HttpStatusCode.OK : HttpStatusCode.Created, vencedora.StatusCode);
        if (compraPrimeiro)
            await ConferirErroPostalAsync(recusada, 409, operacao switch
            {
                "entrada" or "contagem" => "insumo_com_historico_estoque",
                "receita" => "insumo_com_receitas",
                "forma" => "versao_desatualizada",
                _ => excluirForma ? "forma_compra_com_historico" : "insumo_com_compras"
            });
        else
            Assert.Equal(operacao is "compra" or "receita" or "substituicao"
                ? HttpStatusCode.BadRequest : HttpStatusCode.NotFound, recusada.StatusCode);
        Assert.Equal(compraPrimeiro, await db.FormasCompraInsumo.AnyAsync(f => f.Id == forma.Id));
        Assert.Equal(compraPrimeiro && operacao is ("compra" or "substituicao") ? 1 : 0, await db.Set<ComponenteCompraMateriaPrima>()
            .CountAsync(c => c.InsumoId == insumo.Id));
        if (operacao == "substituicao")
            Assert.Equal(compraPrimeiro ? insumo.Id : outro.Id,
                (await db.Set<ComponenteCompraMateriaPrima>().AsNoTracking().SingleAsync()).InsumoId);
        Assert.Equal(compraPrimeiro && operacao == "entrada" ? 1 : 0, await db.MovimentosInsumo.CountAsync());
        Assert.Equal(compraPrimeiro ? 0 : 1,
            await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM OperacoesExclusaoInsumo").SingleAsync());
    }

    [SqlServerIntegrationFact]
    public async Task ExclusaoInsumo_ContagemSemDiferencaProtegeMesmoSemSaldo()
    {
        await using var banco = await SqlServerTestDatabase.CreateAsync();
        await using var db = banco.CreateVerificationContext();
        var (insumo, _, _, _, _) = await SemearExclusaoInsumoAsync(db);
        var autor = Guid.NewGuid();
        var estoque = banco.Services.GetRequiredService<IEstoqueInsumoRepository>();
        var contagem = await estoque.AjustarContagemAsync(new(insumo.Id, autor, 0, UnidadeFisicaEstoque.Grama,
            null, null, "Conferência", "exclusao-contagem"));
        Assert.Equal(OperacaoEstoqueInsumoStatus.SemDiferenca, contagem.Status);
        Assert.False(await db.SaldosInsumo.AnyAsync(s => s.InsumoId == insumo.Id));
        Assert.Equal((409, "insumo_com_historico_estoque"),
            await new EfExcluirInsumo(db, TimeProvider.System).ExecutarAsync(insumo.Id, null,
                VersaoCadastroInsumo.Calcular(insumo), autor, Guid.NewGuid(), default));
        Assert.Single(await db.AjustesSemDiferencaInsumo.ToArrayAsync());
    }

    [SqlServerIntegrationFact]
    public async Task ExclusaoInsumo_SnapshotDeEntradaProtegeSomenteAFormaUtilizada()
    {
        await using var banco = await SqlServerTestDatabase.CreateAsync();
        await using var db = banco.CreateVerificationContext();
        var (insumo, forma, _, _, _) = await SemearExclusaoInsumoAsync(db);
        var extra = insumo.AdicionarFormaCompra("Forma livre", 30, UnidadeFisicaEstoque.Grama);
        db.FormasCompraInsumo.Add(extra);
        await db.SaveChangesAsync();
        var autor = Guid.NewGuid();
        var entrada = await banco.Services.GetRequiredService<IEstoqueInsumoRepository>().RegistrarEntradaAsync(
            new(insumo.Id, autor, TipoMovimentoInsumo.Aquisicao, 1, null, forma.Id,
                null, null, null, null, null, false, 10, "entrada-com-snapshot"));
        Assert.True(entrada.Sucesso, entrada.Erro);
        var exclusao = new EfExcluirInsumo(db, TimeProvider.System);
        var versao = VersaoCadastroInsumo.Calcular(insumo);
        Assert.Equal((409, "forma_compra_com_historico"),
            await exclusao.ExecutarAsync(insumo.Id, forma.Id, versao, autor, Guid.NewGuid(), default));
        Assert.Equal((204, null), await exclusao.ExecutarAsync(insumo.Id, extra.Id, versao, autor, Guid.NewGuid(), default));
        var movimento = await db.MovimentosInsumo.SingleAsync();
        Assert.Equal(forma.Id, movimento.FormaCompraId);
        Assert.Equal(20m, movimento.QuantidadeBase.ValorBase);
    }

    [SqlServerIntegrationFact]
    public async Task ExclusaoInsumo_PreservaMaterialUsadoSomenteNaVersaoAnteriorDaReceita()
    {
        await using var banco = await SqlServerTestDatabase.CreateAsync();
        await using var db = banco.CreateVerificationContext();
        var (insumo, _, outro, _, _) = await SemearExclusaoInsumoAsync(db);
        var (_, variante, _) = await ReservarProdutoAcabadoSqlServerIntegrationTests.CriarProdutoCompravelAsync(db);
        var autor = Guid.NewGuid();
        var receita = new EfReceitaProducaoRepository(db, TimeProvider.System);
        var dados = new ReceitaInput(variante.Id, "Receita de teste", 1, [new(insumo.Id, "1", "Grama")], null);
        var criada = await receita.SalvarAsync(null, dados, autor, default);
        Assert.Equal(ReceitaStatus.Sucesso, criada.Status);
        db.ChangeTracker.Clear();
        var editada = await receita.SalvarAsync(criada.Receita!.Id, dados with
        {
            Itens = [new(outro.Id, "1", "Grama")], Token = criada.Receita.Token
        }, autor, default);
        Assert.Equal(ReceitaStatus.Sucesso, editada.Status);
        Assert.Equal(outro.Id, Assert.Single(editada.Receita!.Atual.Itens).InsumoId);
        Assert.Equal((409, "insumo_com_receitas"), await new EfExcluirInsumo(db, TimeProvider.System).ExecutarAsync(
            insumo.Id, null, VersaoCadastroInsumo.Calcular(insumo), autor, Guid.NewGuid(), default));
    }

    [SqlServerIntegrationFact]
    public async Task ExclusaoInsumo_RollbackPreservaCadastroFormasEAuditoria()
    {
        await using var banco = await SqlServerTestDatabase.CreateAsync();
        await using var db = banco.CreateVerificationContext();
        var (insumo, forma, _, _, _) = await SemearExclusaoInsumoAsync(db);
        var opcoes = new DbContextOptionsBuilder<XaquiamuniDbContext>().UseSqlServer(banco.ConexaoIsolada)
            .AddInterceptors(new FalhaCommitVariante()).Options;
        await using var falha = new XaquiamuniDbContext(opcoes);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new EfExcluirInsumo(falha, TimeProvider.System)
            .ExecutarAsync(insumo.Id, null, VersaoCadastroInsumo.Calcular(insumo), Guid.NewGuid(), Guid.NewGuid(), default));
        Assert.True(await db.InsumosEstoque.AnyAsync(i => i.Id == insumo.Id));
        Assert.True(await db.FormasCompraInsumo.AnyAsync(f => f.Id == forma.Id));
        Assert.Equal(0, await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM OperacoesExclusaoInsumo").SingleAsync());
    }

    [SqlServerIntegrationFact]
    public async Task ExclusaoInsumo_PreservaRecebimentoPagamentoEOrdemCancelada()
    {
        await using var ambiente = await Ambiente.CriarAsync();
        using var admin = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Administrador);
        await using var db = ambiente.Banco.CreateVerificationContext();
        var dados = await PrepararCompraAsync(admin, db);
        var rota = $"/api/admin/insumos/{dados.Extrato:D}";
        var versao = await VersaoProdutoAsync(admin, rota);
        using (var antesDoRecebimento = await RemoverInsumoAsync(admin, rota, versao, Guid.NewGuid()))
            await ConferirErroPostalAsync(antesDoRecebimento, 409, "insumo_com_compras");

        var (_, ordem) = await ReceberEIniciarAsync(admin, dados);
        var pagamento = await PagarCompraDaCadeiaAsync(admin, db, dados);
        var quitacao = await admin.PostAsync<Xaquiamuni.Aplicacao.Financeiro.PagamentoFornecedorDto>(
            $"{Financeiro}/compras/{dados.Compra.Resumo.Id:D}/pagamentos",
            new Xaquiamuni.Aplicacao.Financeiro.PagarFornecedorInput(dados.Hoje, "Pix", "Quitação de teste", 2650),
            HttpStatusCode.OK, Guid.NewGuid());
        await admin.PostAsync<System.Text.Json.JsonElement>($"{Producao}/{ordem.Resumo.Id:D}/cancelar",
            new { }, HttpStatusCode.OK, Guid.NewGuid());
        using var bloqueado = await RemoverInsumoAsync(admin, rota, versao, Guid.NewGuid());
        await ConferirErroPostalAsync(bloqueado, 409, "insumo_com_compras");
        Assert.Equal(SituacaoCompraMateriaPrima.Recebida, await db.ComprasMateriasPrimas.Select(c => c.Situacao).SingleAsync());
        Assert.Equal(new[] { pagamento.Id, quitacao.Id }.Order(),
            (await db.Set<Xaquiamuni.Dominio.Financeiro.PagamentoFornecedor>().Select(p => p.Id).ToArrayAsync()).Order());
        Assert.Equal(SituacaoOrdemProducao.Cancelada, await db.OrdensProducao.Select(o => o.Situacao).SingleAsync());
        Assert.Equal(4, await db.MovimentosInsumo.CountAsync());
    }

    private static async Task<(InsumoEstoque Insumo, FormaCompraInsumo Forma, InsumoEstoque Outro,
        FormaCompraInsumo OutraForma, Fornecedor Fornecedor)> SemearExclusaoInsumoAsync(XaquiamuniDbContext db)
    {
        var insumo = InsumoEstoque.Criar("Material de teste", TipoInsumoEstoque.MateriaPrima, UnidadeFisicaEstoque.Grama);
        var outro = InsumoEstoque.Criar("Outro material", TipoInsumoEstoque.MateriaPrima, UnidadeFisicaEstoque.Grama);
        var forma = insumo.AdicionarFormaCompra("Caixa", 20, UnidadeFisicaEstoque.Grama);
        var outraForma = outro.AdicionarFormaCompra("Pacote", 35, UnidadeFisicaEstoque.Grama);
        var fornecedor = Fornecedor.Criar("Fornecedor para exclusão", null, null, null, DateTimeOffset.UtcNow);
        db.InsumosEstoque.AddRange(insumo, outro);
        db.Fornecedores.Add(fornecedor);
        await db.SaveChangesAsync();
        return (insumo, forma, outro, outraForma, fornecedor);
    }

    private static CompraMateriaPrimaInput CompraParaExcluir(Guid fornecedor, Guid insumo, Guid forma) =>
        new(fornecedor, new DateOnly(2026, 11, 17), [new(null, 1, 25, [new(insumo, forma, 1, null)])], null);

    private static async Task<HttpResponseMessage> RemoverInsumoAsync(Sessao sessao, string rota, string? versao,
        Guid chave, bool csrf = true)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, rota);
        if (versao is not null) request.Headers.IfMatch.ParseAdd(versao);
        if (csrf) request.Headers.Add("X-XSRF-TOKEN", await TokenComprovanteAsync(sessao));
        request.Headers.Add("Idempotency-Key", chave.ToString("D"));
        return await sessao.Http.SendAsync(request);
    }
}
