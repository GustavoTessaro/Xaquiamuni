using System.Net;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xaquiamuni.Api.Catalogo;
using Xaquiamuni.Aplicacao.Estoque;
using Xaquiamuni.Aplicacao.Imagens;
using Xaquiamuni.Aplicacao.Pedidos;
using Xaquiamuni.Dominio.Entidades;
using Xaquiamuni.Dominio.Estoque;
using Xaquiamuni.Infraestrutura.Identidade;
using Xaquiamuni.Infraestrutura.Imagens;
using Xaquiamuni.Infraestrutura.Persistencia;

namespace Xaquiamuni.Tests.Integration;

public sealed partial class CadeiaFabricacaoSqlServerIntegrationTests
{
    [SqlServerIntegrationFact]
    public async Task ExclusaoProduto_ContratoReplayPreservaCategoriaEPermiteRecadastro()
    {
        await using var ambiente = await Ambiente.CriarAsync();
        using var admin = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Administrador);
        using var cliente = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Cliente);
        await using var db = ambiente.Banco.CreateVerificationContext();
        var (produto, padrao, extra) = await ReservarProdutoAcabadoSqlServerIntegrationTests.CriarProdutoCompravelAsync(db);
        var rota = $"/api/admin/produtos/{produto.Id:D}";
        var versao = await VersaoProdutoAsync(admin, rota);
        var chave = Guid.NewGuid();
        using (var negado = await ExcluirProdutoAsync(cliente, rota, versao, chave))
            Assert.Equal(HttpStatusCode.Forbidden, negado.StatusCode);
        using (var logout = await cliente.EnviarAsync("/api/auth/logout", new { }))
            Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        using (var anonimo = await cliente.Http.DeleteAsync(rota))
            Assert.Equal(HttpStatusCode.Unauthorized, anonimo.StatusCode);
        using (var semCsrf = await ExcluirProdutoAsync(admin, rota, versao, chave, csrf: false))
            Assert.Equal(HttpStatusCode.BadRequest, semCsrf.StatusCode);
        using (var semVersao = await ExcluirProdutoAsync(admin, rota, null, chave))
            await ConferirErroPostalAsync(semVersao, 400, "versao_invalida");
        using (var semChave = await ExcluirProdutoAsync(admin, rota, versao, null))
            await ConferirErroPostalAsync(semChave, 400, "chave_invalida");
        using (var obsoleta = await ExcluirProdutoAsync(admin, rota, $"\"{new string('0', 64)}\"", chave))
            await ConferirErroPostalAsync(obsoleta, 409, "versao_desatualizada");
        using (var excluido = await ExcluirProdutoAsync(admin, rota, versao, chave))
            Assert.Equal(HttpStatusCode.NoContent, excluido.StatusCode);
        using (var replay = await ExcluirProdutoAsync(admin, rota, versao, chave))
            Assert.Equal(HttpStatusCode.NoContent, replay.StatusCode);
        using (var ausente = await ExcluirProdutoAsync(admin, rota, versao, Guid.NewGuid()))
            Assert.Equal(HttpStatusCode.NotFound, ausente.StatusCode);
        using (var catalogo = await cliente.Http.GetAsync($"/api/catalogo/produtos/{produto.Id:D}"))
            Assert.Equal(HttpStatusCode.NotFound, catalogo.StatusCode);
        Assert.True(await db.Categorias.AnyAsync(c => c.Id == produto.CategoriaId));
        Assert.False(await db.ProdutoVariantes.AnyAsync(v => v.Id == padrao.Id || v.Id == extra.Id));
        Assert.False(await db.ProdutoImagens.AnyAsync(i => i.ProdutoId == produto.Id));
        Assert.Equal(1, await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM OperacoesExclusaoProduto WHERE ProdutoId = {produto.Id}").SingleAsync());

        var dados = new { nome = produto.Nome, categoriaId = produto.CategoriaId, simples = true,
            variantePadrao = new { unidadeVenda = "UN", precoVenda = 10m } };
        var novo = await admin.PostAsync<ProdutoResponse>("/api/admin/produtos", dados, HttpStatusCode.Created);
        Assert.NotEqual(produto.Id, novo.Id);
        Assert.NotEqual(padrao.Id, Assert.Single(novo.Variantes).Id);
        var rotaNova = $"/api/admin/produtos/{novo.Id:D}";
        using (var divergente = await ExcluirProdutoAsync(admin, rotaNova, await VersaoProdutoAsync(admin, rotaNova), chave))
            await ConferirErroPostalAsync(divergente, 409, "chave_em_uso");
        using var naoPublicado = await ExcluirProdutoAsync(admin, rotaNova, await VersaoProdutoAsync(admin, rotaNova), Guid.NewGuid());
        Assert.Equal(HttpStatusCode.NoContent, naoPublicado.StatusCode);
    }

    [SqlServerIntegrationFact]
    public async Task ExclusaoProduto_ReverteTudoERecuperaLimpezaAposFalhaPreservandoCompartilhadas()
    {
        await using var ambiente = await Ambiente.CriarAsync(migration: "20261124012758_AuditarExclusaoVariante");
        await using var db = ambiente.Banco.CreateVerificationContext();
        var (produto, _, _) = await ReservarProdutoAcabadoSqlServerIntegrationTests.CriarProdutoCompravelAsync(db);
        var categoria = await db.Categorias.SingleAsync(c => c.Id == produto.CategoriaId);
        var outro = Produto.CriarSimples("Outro produto", null, categoria, "UN", 19m);
        db.Produtos.Add(outro);
        await db.SaveChangesAsync();
        var repositorio = new EfProdutoRepository(db);
        var agregado = (await repositorio.ConsultarAsync(produto.Id))!;
        var imagem = Assert.Single(agregado.Imagens);
        var chaveCompartilhada = ChaveArmazenamentoImagemProduto.Gerar();
        var chaveAlias = chaveCompartilhada[..15] + chaveCompartilhada[15..^5].ToUpperInvariant() + chaveCompartilhada[^5..];
        Assert.NotEqual(chaveCompartilhada, chaveAlias);
        var editavel = await db.Produtos.Include(p => p.Imagens).SingleAsync(p => p.Id == produto.Id);
        var vizinho = await db.Produtos.Include(p => p.Imagens).SingleAsync(p => p.Id == outro.Id);
        db.ProdutoImagens.Add(editavel.AdicionarImagem(chaveCompartilhada, "compartilhada.webp", "image/webp", 7, 4, 4));
        db.ProdutoImagens.Add(vizinho.AdicionarImagem(chaveAlias, "vizinha.webp", "image/webp", 7, 4, 4));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        await db.Database.MigrateAsync();

        var armazenamento = ambiente.Host.Services.GetRequiredService<IArmazenamentoImagemProduto>();
        foreach (var chave in new[] { imagem.ChaveArmazenamento, chaveCompartilhada })
            await armazenamento.ArmazenarAsync(chave, new MemoryStream([1, 2, 3]));
        var relogio = new RelogioLimpezaProduto();
        var versao = ProdutoResponse.From((await repositorio.ConsultarAsync(produto.Id))!).Versao;
        var opcoes = new DbContextOptionsBuilder<XaquiamuniDbContext>().UseSqlServer(ambiente.Banco.ConexaoIsolada)
            .AddInterceptors(new FalhaCommitVariante()).Options;
        await using var falha = new XaquiamuniDbContext(opcoes);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new EfExcluirProduto(falha, relogio).ExecutarAsync(produto.Id, versao, Guid.NewGuid(), Guid.NewGuid(),
                p => ProdutoResponse.From(p).Versao, default));
        Assert.Equal(2, await db.ProdutoImagens.CountAsync(i => i.ProdutoId == produto.Id));
        Assert.Equal(2, await db.ProdutoVariantes.CountAsync(v => v.ProdutoId == produto.Id));
        Assert.Equal(0, await ContarLimpezasAsync(db));
        Assert.True(await armazenamento.ExisteAsync(imagem.ChaveArmazenamento));
        Assert.True(await armazenamento.ExisteAsync(chaveCompartilhada));

        var exclusao = await new EfExcluirProduto(db, relogio).ExecutarAsync(produto.Id, versao, Guid.NewGuid(), Guid.NewGuid(),
            p => ProdutoResponse.From(p).Versao, default);
        Assert.Equal(204, exclusao.Status);
        Assert.Equal(2, await ContarLimpezasAsync(db));
        Assert.True(await armazenamento.ExisteAsync(imagem.ChaveArmazenamento));

        var intermitente = new ArmazenamentoIntermitente(armazenamento);
        await using var primeiroProcesso = ambiente.Banco.CreateVerificationContext();
        await new LimpezaImagensProduto(primeiroProcesso, intermitente, relogio, NullLogger<LimpezaImagensProduto>.Instance)
            .ProcessarAsync(default);
        Assert.Equal(1, await ContarLimpezasAsync(db));
        Assert.True(await armazenamento.ExisteAsync(imagem.ChaveArmazenamento));
        Assert.True(await armazenamento.ExisteAsync(chaveCompartilhada));
        Assert.False(await db.Produtos.AnyAsync(p => p.Id == produto.Id));

        relogio.Agora = relogio.Agora.AddMinutes(2);
        await using var segundoProcesso = ambiente.Banco.CreateVerificationContext();
        var limpeza = new LimpezaImagensProduto(segundoProcesso, armazenamento, relogio, NullLogger<LimpezaImagensProduto>.Instance);
        await limpeza.ProcessarAsync(default);
        await limpeza.ProcessarAsync(default);
        Assert.False(await armazenamento.ExisteAsync(imagem.ChaveArmazenamento));
        Assert.True(await armazenamento.ExisteAsync(chaveCompartilhada));
        Assert.Equal(0, await ContarLimpezasAsync(db));
        Assert.True(await db.Produtos.AnyAsync(p => p.Id == outro.Id));
        Assert.True(await db.ProdutoImagens.AnyAsync(i => i.ProdutoId == outro.Id && i.ChaveArmazenamento == chaveAlias));
    }

    [SqlServerIntegrationFact]
    public Task ExclusaoProduto_CheckoutPrimeiro() =>
        DisputarVarianteCheckoutAsync(checkoutPrimeiro: true, excluirProduto: true);

    [SqlServerIntegrationFact]
    public Task ExclusaoProduto_ExclusaoAntesDoCheckout() =>
        DisputarVarianteCheckoutAsync(checkoutPrimeiro: false, excluirProduto: true);

    [SqlServerIntegrationFact]
    public Task ExclusaoProduto_EntradaEstoquePrimeiro() =>
        DisputarVarianteEstoqueAsync(entradaPrimeiro: true, excluirProduto: true);

    [SqlServerIntegrationFact]
    public Task ExclusaoProduto_ExclusaoAntesDaEntradaEstoque() =>
        DisputarVarianteEstoqueAsync(entradaPrimeiro: false, excluirProduto: true);

    [SqlServerIntegrationFact]
    public Task ExclusaoProduto_CotacaoPostalPrimeiro() =>
        DisputarVariantePostalAsync(postalPrimeiro: true, excluirProduto: true);

    [SqlServerIntegrationFact]
    public Task ExclusaoProduto_ExclusaoAntesDaCotacaoPostal() =>
        DisputarVariantePostalAsync(postalPrimeiro: false, excluirProduto: true);

    [SqlServerIntegrationFact]
    public Task ExclusaoProduto_PromocaoDiretaPrimeiro() => DisputarAlteracaoCatalogoAsync("promocao", escritorPrimeiro: true);

    [SqlServerIntegrationFact]
    public Task ExclusaoProduto_AntesDaPromocao() => DisputarAlteracaoCatalogoAsync("promocao", escritorPrimeiro: false);

    [SqlServerIntegrationFact]
    public Task ExclusaoProduto_ExclusaoIsoladaPrimeiro() => DisputarAlteracaoCatalogoAsync("variante", escritorPrimeiro: true);

    [SqlServerIntegrationFact]
    public Task ExclusaoProduto_AntesDaExclusaoIsolada() => DisputarAlteracaoCatalogoAsync("variante", escritorPrimeiro: false);

    [SqlServerIntegrationFact]
    public Task ExclusaoProduto_DuasExclusoesProduzemUmaAuditoria() => DisputarAlteracaoCatalogoAsync("produto", escritorPrimeiro: false);

    [SqlServerIntegrationFact]
    public Task ExclusaoProduto_CriacaoVariantePrimeiro() => DisputarEdicaoCatalogoAsync(imagem: false, edicaoPrimeiro: true);

    [SqlServerIntegrationFact]
    public Task ExclusaoProduto_AntesDaCriacaoVariante() => DisputarEdicaoCatalogoAsync(imagem: false, edicaoPrimeiro: false);

    [SqlServerIntegrationFact]
    public Task ExclusaoProduto_AssociacaoImagemPrimeiro() => DisputarEdicaoCatalogoAsync(imagem: true, edicaoPrimeiro: true);

    [SqlServerIntegrationFact]
    public Task ExclusaoProduto_AntesDaAssociacaoImagem() => DisputarEdicaoCatalogoAsync(imagem: true, edicaoPrimeiro: false);

    [SqlServerIntegrationFact]
    public Task ExclusaoProduto_RemocaoImagemPrimeiro() =>
        DisputarEdicaoCatalogoAsync(imagem: true, edicaoPrimeiro: true, removerImagem: true);

    [SqlServerIntegrationFact]
    public Task ExclusaoProduto_AntesDaRemocaoImagem() =>
        DisputarEdicaoCatalogoAsync(imagem: true, edicaoPrimeiro: false, removerImagem: true);

    private static async Task DisputarEdicaoCatalogoAsync(bool imagem, bool edicaoPrimeiro, bool removerImagem = false)
    {
        var barreira = new BarreiraCommitCatalogo();
        await using var ambiente = await Ambiente.CriarAsync(interceptor: barreira);
        using var admin = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Administrador);
        using var outroAdmin = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Administrador);
        await using var db = ambiente.Banco.CreateVerificationContext();
        var (produto, _, extra) = await ReservarProdutoAcabadoSqlServerIntegrationTests.CriarProdutoCompravelAsync(db);
        var foto = Assert.Single(produto.Imagens);
        if (removerImagem)
        {
            var agregado = await db.Produtos.Include(p => p.Imagens).SingleAsync(p => p.Id == produto.Id);
            db.ProdutoImagens.Add(agregado.AdicionarImagem(ChaveArmazenamentoImagemProduto.Gerar(),
                "preservada.webp", "image/webp", 3, 32, 32));
            await db.SaveChangesAsync();
        }
        var rota = $"/api/admin/produtos/{produto.Id:D}";
        var versao = await VersaoProdutoAsync(admin, rota);
        // Obter os tokens antes de armar a barreira isola as transações dos comandos testados.
        var tokenAdmin = await TokenComprovanteAsync(admin);
        var tokenEdicao = await TokenComprovanteAsync(outroAdmin);
        using var excluir = new HttpRequestMessage(HttpMethod.Delete, rota);
        excluir.Headers.IfMatch.ParseAdd(versao);
        excluir.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("D"));
        excluir.Headers.Add("X-XSRF-TOKEN", tokenAdmin);
        using var editar = removerImagem
            ? new HttpRequestMessage(HttpMethod.Delete, $"{rota}/imagens/{foto.Id:D}")
            : imagem
            ? new HttpRequestMessage(HttpMethod.Put, $"{rota}/imagens/{foto.Id:D}/variantes/{extra.Id:D}")
            : new HttpRequestMessage(HttpMethod.Post, $"{rota}/variantes")
            {
                Content = System.Net.Http.Json.JsonContent.Create(new { identificacao = "Nova", unidadeVenda = "UN", precoVenda = 17 })
            };
        editar.Headers.Add("X-XSRF-TOKEN", tokenEdicao);
        Task<HttpResponseMessage> Excluir() => admin.Http.SendAsync(excluir);
        Task<HttpResponseMessage> Editar() => outroAdmin.Http.SendAsync(editar);

        barreira.Armar();
        var primeira = edicaoPrimeiro ? Editar() : Excluir();
        Task<HttpResponseMessage> segunda;
        try
        {
            await barreira.PrimeiraPronta.Task.WaitAsync(TimeSpan.FromSeconds(8));
            segunda = edicaoPrimeiro ? Excluir() : Editar();
            await barreira.SegundaIniciada.Task.WaitAsync(TimeSpan.FromSeconds(8));
            Assert.False(segunda.IsCompleted);
        }
        finally
        {
            barreira.Prosseguir.TrySetResult();
        }
        using var sucesso = await primeira;
        using var recusa = await segunda;
        if (edicaoPrimeiro)
        {
            Assert.True(sucesso.IsSuccessStatusCode, await sucesso.Content.ReadAsStringAsync());
            await ConferirErroPostalAsync(recusa, 409, "versao_desatualizada");
        }
        else
        {
            Assert.Equal(HttpStatusCode.NoContent, sucesso.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, recusa.StatusCode);
        }
        Assert.Equal(edicaoPrimeiro, await db.Produtos.AnyAsync(p => p.Id == produto.Id));
        Assert.Equal(edicaoPrimeiro ? imagem ? 2 : 3 : 0, await db.ProdutoVariantes.CountAsync(v => v.ProdutoId == produto.Id));
        Assert.Equal(edicaoPrimeiro && imagem && !removerImagem ? 1 : 0,
            await db.Set<ProdutoImagemVariante>().CountAsync(a => a.ProdutoId == produto.Id));
        if (removerImagem)
        {
            Assert.False(await db.ProdutoImagens.AnyAsync(i => i.Id == foto.Id));
            Assert.Equal(edicaoPrimeiro ? 1 : 0, await db.ProdutoImagens.CountAsync(i => i.ProdutoId == produto.Id));
        }
    }

    private sealed class BarreiraCommitCatalogo : DbTransactionInterceptor
    {
        private int _iniciadas = -1;
        public TaskCompletionSource PrimeiraPronta { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SegundaIniciada { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Prosseguir { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Armar() => Volatile.Write(ref _iniciadas, 0);

        public override ValueTask<DbTransaction> TransactionStartedAsync(DbConnection connection,
            TransactionEndEventData eventData, DbTransaction result, CancellationToken cancellationToken = default)
        {
            if (Volatile.Read(ref _iniciadas) >= 0 && Interlocked.Increment(ref _iniciadas) == 2)
                SegundaIniciada.TrySetResult();
            return ValueTask.FromResult(result);
        }

        public override async ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (Volatile.Read(ref _iniciadas) > 0 && PrimeiraPronta.TrySetResult())
                await Prosseguir.Task.WaitAsync(TimeSpan.FromSeconds(18), cancellationToken);
            return result;
        }
    }

    private static async Task DisputarAlteracaoCatalogoAsync(string escritor, bool escritorPrimeiro)
    {
        var barreira = new BarreiraAtendimento();
        await using var ambiente = await Ambiente.CriarAsync(interceptor: barreira, promocoesComerciais: true);
        using var admin = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Administrador);
        using var outroAdmin = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Administrador);
        await using var db = ambiente.Banco.CreateVerificationContext();
        var (produto, _, extra) = await ReservarProdutoAcabadoSqlServerIntegrationTests.CriarProdutoCompravelAsync(db);
        var rota = $"/api/admin/produtos/{produto.Id:D}";
        var versao = await VersaoProdutoAsync(admin, rota);
        Task<HttpResponseMessage> Excluir() => ExcluirProdutoAsync(admin, rota, versao, Guid.NewGuid());
        Task<HttpResponseMessage> Escrever() => escritor switch
        {
            "promocao" => outroAdmin.EnviarAsync("/api/admin/promocoes", new
            {
                configuracao = new { produtoId = produto.Id, tipo = "PrecoFixo", escopo = "TodasVariantes", valor = "900" },
                ativa = false
            }, Guid.NewGuid()),
            "variante" => ExcluirVarianteAsync(outroAdmin, rota, extra.Id, versao, Guid.NewGuid()),
            _ => ExcluirProdutoAsync(outroAdmin, rota, versao, Guid.NewGuid())
        };

        barreira.Armar("catalog-promotions");
        var primeira = escritorPrimeiro ? Escrever() : Excluir();
        Task<HttpResponseMessage> segunda;
        try
        {
            await barreira.PrimeiroAdquiriu.Task.WaitAsync(TimeSpan.FromSeconds(8));
            segunda = escritorPrimeiro ? Excluir() : Escrever();
            await barreira.SegundoSolicitou.Task.WaitAsync(TimeSpan.FromSeconds(8));
            Assert.False(segunda.IsCompleted);
        }
        finally
        {
            barreira.Prosseguir.TrySetResult();
        }
        using var sucesso = await primeira;
        using var recusa = await segunda;
        Assert.Equal(escritorPrimeiro && escritor == "promocao" ? HttpStatusCode.Created : HttpStatusCode.NoContent,
            sucesso.StatusCode);
        if (escritorPrimeiro)
            await ConferirErroPostalAsync(recusa, 409, escritor == "promocao" ? "produto_com_historico_comercial" : "versao_desatualizada");
        else
            Assert.Equal(HttpStatusCode.NotFound, recusa.StatusCode);
        Assert.Equal(escritorPrimeiro, await db.Produtos.AnyAsync(p => p.Id == produto.Id));
        Assert.Equal(escritorPrimeiro ? 0 : 1, await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM OperacoesExclusaoProduto").SingleAsync());
    }

    private static Task<int> ContarLimpezasAsync(XaquiamuniDbContext db) =>
        db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM LimpezasImagensProdutos").SingleAsync();

    [SqlServerIntegrationFact]
    public async Task ExclusaoProduto_DetectaHistoricoDiretoSemVarianteEFormatoPostalInconclusivo()
    {
        await using var ambiente = await Ambiente.CriarAsync(municipiosPostais: MunicipiosPostaisTeste);
        using var admin = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Administrador);
        using var cliente = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Cliente);
        await using var db = ambiente.Banco.CreateVerificationContext();
        var (produto, _, extra) = await ReservarProdutoAcabadoSqlServerIntegrationTests.CriarProdutoCompravelAsync(db);
        await EscreverAtendimentoAsync(admin, HttpMethod.Put, "/correios-manual", new SalvarCorreiosManual(true, 48, 0), HttpStatusCode.OK);
        using var postal = await EscreverPostalAsync(cliente, Postal, new
        {
            itens = new[] { new { produtoId = produto.Id, produtoVarianteId = extra.Id, quantidade = 1 } },
            endereco = DestinoPostalTeste
        });
        Assert.Equal(HttpStatusCode.Created, postal.StatusCode);
        var rota = $"/api/admin/produtos/{produto.Id:D}";
        var versao = await VersaoProdutoAsync(admin, rota);
        var somenteProduto = System.Text.Json.JsonSerializer.Serialize(new[] { new { ProdutoId = produto.Id } });
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE SolicitacoesCorreios SET ItensJson = {somenteProduto}");
        using (var bloqueado = await ExcluirProdutoAsync(admin, rota, versao, Guid.NewGuid()))
            await ConferirErroPostalAsync(bloqueado, 409, "produto_com_historico_comercial");
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE SolicitacoesCorreios SET ItensJson = {"{}"}");
        using (var inconclusivo = await ExcluirProdutoAsync(admin, rota, versao, Guid.NewGuid()))
            await ConferirErroPostalAsync(inconclusivo, 409, "produto_historico_inconclusivo");
        Assert.Equal(2, await db.ProdutoVariantes.CountAsync(v => v.ProdutoId == produto.Id));
        Assert.Single(await db.ProdutoImagens.Where(i => i.ProdutoId == produto.Id).ToArrayAsync());
    }

    [SqlServerIntegrationFact]
    public async Task ExclusaoProduto_PreservaOperacaoPromocionalLegadaEPoliticaSemSaldo()
    {
        await using var ambiente = await Ambiente.CriarAsync(promocoesComerciais: true);
        using var admin = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Administrador);
        await using var db = ambiente.Banco.CreateVerificationContext();
        var (produto, _, _) = await ReservarProdutoAcabadoSqlServerIntegrationTests.CriarProdutoCompravelAsync(db);
        var configuracao = new ConfigurarPromocao(produto.Id, "PrecoFixo", "TodasVariantes", "900", null, null, []);
        var promocao = await new EfPromocoes(db, TimeProvider.System).SalvarAsync(null, configuracao, null, false,
            Guid.NewGuid(), Guid.NewGuid(), default);
        Assert.Equal(201, promocao.Status);
        // Simula uma base legada em que sobrou apenas o resultado comercial da operação.
        db.Remove(await db.Set<Promocao>().SingleAsync());
        await db.SaveChangesAsync();
        var rota = $"/api/admin/produtos/{produto.Id:D}";
        using (var recusado = await ExcluirProdutoAsync(admin, rota, await VersaoProdutoAsync(admin, rota), Guid.NewGuid()))
            await ConferirErroPostalAsync(recusado, 409, "produto_com_historico_comercial");

        var categoria = await db.Categorias.SingleAsync(c => c.Id == produto.CategoriaId);
        var outro = Produto.CriarSimples("Produto com política", null, categoria, "UN", 12m);
        db.Produtos.Add(outro);
        await db.SaveChangesAsync();
        db.PoliticasEstoqueProdutoVariante.Add(PoliticaEstoqueProdutoVariante.Criar(
            Assert.Single(outro.Variantes), prazoValidadePadraoDias: 30));
        await db.SaveChangesAsync();
        var rotaOutro = $"/api/admin/produtos/{outro.Id:D}";
        using var politica = await ExcluirProdutoAsync(admin, rotaOutro, await VersaoProdutoAsync(admin, rotaOutro), Guid.NewGuid());
        await ConferirErroPostalAsync(politica, 409, "variante_com_historico_estoque");
        Assert.Equal(2, await db.Produtos.CountAsync());
        Assert.Empty(await db.SaldosProdutoAcabado.ToArrayAsync());
    }

    [SqlServerIntegrationFact]
    public async Task ExclusaoProduto_CriacaoReceitaRejeitadaQuandoExclusaoVenceAposALeitura()
    {
        await using var banco = await SqlServerTestDatabase.CreateAsync();
        await using var db = banco.CreateVerificationContext();
        var (produto, _, extra) = await ReservarProdutoAcabadoSqlServerIntegrationTests.CriarProdutoCompravelAsync(db);
        var insumo = InsumoEstoque.Criar("Base de teste", TipoInsumoEstoque.MateriaPrima, UnidadeFisicaEstoque.Grama);
        db.InsumosEstoque.Add(insumo);
        await db.SaveChangesAsync();
        var versao = ProdutoResponse.From((await new EfProdutoRepository(db).ConsultarAsync(produto.Id))!).Versao;
        var opcoes = new DbContextOptionsBuilder<XaquiamuniDbContext>().UseSqlServer(banco.ConexaoIsolada)
            .AddInterceptors(new AntesDeGravarReceita(async () =>
            {
                await using var exclusao = banco.CreateVerificationContext();
                var resultado = await new EfExcluirProduto(exclusao, TimeProvider.System).ExecutarAsync(
                    produto.Id, versao, Guid.NewGuid(), Guid.NewGuid(), p => ProdutoResponse.From(p).Versao, default);
                Assert.Equal(204, resultado.Status);
            })).Options;
        await using var escrita = new XaquiamuniDbContext(opcoes);
        var receita = await new EfReceitaProducaoRepository(escrita, TimeProvider.System).SalvarAsync(
            null, new ReceitaInput(extra.Id, "Receita concorrente", 1, [new(insumo.Id, "10", "Grama")], null),
            Guid.NewGuid(), default);
        Assert.Equal(ReceitaStatus.Conflito, receita.Status);
        Assert.False(await db.ReceitasProducao.AnyAsync());
        Assert.False(await db.Produtos.AnyAsync(p => p.Id == produto.Id));
    }

    private sealed class RelogioLimpezaProduto : TimeProvider
    {
        public DateTimeOffset Agora { get; set; } = new(2090, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Agora;
    }

    private sealed class ArmazenamentoIntermitente(IArmazenamentoImagemProduto interno) : IArmazenamentoImagemProduto
    {
        public Task ExcluirAsync(string chave, CancellationToken cancellationToken = default) => throw new IOException("Falha de teste.");
        public Task<bool> ExisteAsync(string chave, CancellationToken cancellationToken = default) =>
            interno.ExisteAsync(chave, cancellationToken);
        public Task<Stream?> AbrirLeituraAsync(string chave, CancellationToken cancellationToken = default) =>
            interno.AbrirLeituraAsync(chave, cancellationToken);
        public Task ArmazenarAsync(string chave, Stream conteudo, CancellationToken cancellationToken = default) =>
            interno.ArmazenarAsync(chave, conteudo, cancellationToken);
    }

    private static async Task<HttpResponseMessage> ExcluirProdutoAsync(Sessao sessao, string rota,
        string? versao, Guid? chave, bool csrf = true)
    {
        using var requisicao = new HttpRequestMessage(HttpMethod.Delete, rota);
        if (versao is not null) requisicao.Headers.IfMatch.ParseAdd(versao);
        if (csrf) requisicao.Headers.Add("X-XSRF-TOKEN", await TokenComprovanteAsync(sessao));
        if (chave.HasValue) requisicao.Headers.Add("Idempotency-Key", chave.Value.ToString("D"));
        return await sessao.Http.SendAsync(requisicao);
    }
}
