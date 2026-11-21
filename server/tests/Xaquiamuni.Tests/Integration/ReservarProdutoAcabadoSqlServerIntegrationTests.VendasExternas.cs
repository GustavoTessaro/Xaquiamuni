using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xaquiamuni.Aplicacao.Estoque;
using Xaquiamuni.Aplicacao.Pedidos;
using Xaquiamuni.Dominio.Entidades;
using Xaquiamuni.Dominio.Estoque;
using Xaquiamuni.Infraestrutura.Identidade;

namespace Xaquiamuni.Tests.Integration;

public sealed partial class ReservarProdutoAcabadoSqlServerIntegrationTests
{
    private const string VendasUrl = "/api/admin/vendas-externas";

    [SqlServerIntegrationFact]
    public async Task VendasExternas_ApiValidaAcessoFefoAtomicidadeEReplay()
    {
        var relogio = new RelogioSqlControlado { Agora = Agora };
        await using var banco = await SqlServerTestDatabase.CreateAsync(dbContextInterceptor: relogio);
        await using var db = banco.CreateVerificationContext();
        var (produto, variante, alternativa) = await CriarProdutoCompravelAsync(db);
        var outraVariante = ProdutoVariante.Criar("Unidade", "UN", 8m, padrao: true);
        var outroProduto = Produto.Criar("Vela", null, Categoria.Criar("Velas", null, 1), [outraVariante]);
        db.Produtos.Add(outroProduto);
        await db.SaveChangesAsync();
        var maisTarde = (await EntrarAsync(db, variante.Id, 2, Hoje.AddDays(5))).Lote!;
        var maisCedo = (await EntrarAsync(db, variante.Id, 1, Hoje.AddDays(2))).Lote!;
        await EntrarAsync(db, outraVariante.Id, 1);
        await EntrarAsync(db, alternativa.Id, 1, Hoje.AddDays(-1));
        await EntrarAsync(db, alternativa.Id, 1, condicao: CondicaoEstoqueProdutoAcabado.Quarentena);
        using var factory = CriarApiPedidos(banco, relogio);
        using var admin = ClienteHttpPedidos(factory, Responsavel, XaquiamuniRoles.Administrador);
        using var outro = ClienteHttpPedidos(factory, Guid.NewGuid(), XaquiamuniRoles.Administrador);
        using var anonimo = ClienteHttpPedidos(factory);
        using var cliente = ClienteHttpPedidos(factory, Guid.NewGuid());
        var token = await CsrfPedidosAsync(admin);
        var chave = Guid.NewGuid();
        var itens = new[] { new ItemVendaExternaInput(produto.Id, variante.Id, 2, 900, 100),
            new ItemVendaExternaInput(outroProduto.Id, outraVariante.Id, 1, 800) };
        var comando = NovaVenda(itens);
        using var anonim = await EnviarVendaAsync(anonimo, VendasUrl, comando, chave);
        using var proibida = await EnviarVendaAsync(cliente, VendasUrl, comando, chave);
        using var semCsrf = await EnviarVendaAsync(admin, VendasUrl, comando, chave);
        Assert.Equal(HttpStatusCode.Unauthorized, anonim.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, proibida.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, semCsrf.StatusCode);

        using var insuficiente = await EnviarVendaAsync(admin, VendasUrl, comando with
        {
            Itens = itens.OrderBy(i => i.VarianteId).Select((i, n) => n == 1 ? i with { Quantidade = 20 } : i).ToArray(),
        }, chave, token);
        Assert.Equal(HttpStatusCode.Conflict, insuficiente.StatusCode);
        using var inelegivel = await EnviarVendaAsync(admin, VendasUrl, NovaVenda([new(produto.Id, alternativa.Id, 1, 800)]), chave, token);
        Assert.Equal(HttpStatusCode.Conflict, inelegivel.StatusCode);
        Assert.Empty(await db.Set<VendaExterna>().ToListAsync());
        Assert.Empty(await db.Set<BaixaVendaExterna>().ToListAsync());
        Assert.Equal(5, await db.MovimentosProdutoAcabado.CountAsync());

        using var criada = await EnviarVendaAsync(admin, VendasUrl, comando, chave, token);
        Assert.Equal(HttpStatusCode.Created, criada.StatusCode);
        var resumo = (await criada.Content.ReadFromJsonAsync<ResumoVendaExterna>())!;
        Assert.Equal("2500", resumo.TotalCentavos);
        Assert.Equal("Pendente", resumo.SituacaoPagamento);
        var url = $"{VendasUrl}/{resumo.Id:D}";
        var detalhe = await admin.GetFromJsonAsync<JsonElement>(url);
        Assert.Equal(2, detalhe.GetProperty("itens").GetArrayLength());
        Assert.Equal(3, detalhe.GetProperty("baixas").GetArrayLength());
        var movimentos = await db.MovimentosProdutoAcabado.Where(m => m.Tipo == TipoMovimentoProdutoAcabado.SaidaManual)
            .OrderBy(m => m.ChaveIdempotencia).ToListAsync();
        Assert.Equal(3, movimentos.Count);
        var fefo = movimentos.Where(m => m.ProdutoVarianteId == variante.Id).ToArray();
        Assert.Equal(new[] { maisCedo.Id, maisTarde.Id }, fefo.Select(m => m.LoteId));
        using var replay = await EnviarVendaAsync(admin, VendasUrl, comando, chave, token);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(resumo, await replay.Content.ReadFromJsonAsync<ResumoVendaExterna>());
        using var divergente = await EnviarVendaAsync(admin, VendasUrl, comando with { Comprador = "Outro nome" }, chave, token);
        Assert.Equal(HttpStatusCode.Conflict, divergente.StatusCode);
        var tentativa = $"{VendasUrl}/tentativas/{chave:D}";
        Assert.Equal(resumo, await admin.GetFromJsonAsync<ResumoVendaExterna>(tentativa));
        using var alheia = await outro.GetAsync(tentativa);
        Assert.Equal(HttpStatusCode.NotFound, alheia.StatusCode);
        var lista = (await admin.GetFromJsonAsync<ResumoVendaExterna[]>(VendasUrl + "?canal=Feira&situacao=Registrada"))!;
        Assert.Equal(resumo with { Comprador = comando.Comprador }, Assert.Single(lista));
        var estorno = await Repositorio(db).EstornarAsync(new(fefo[0].Id, Responsavel, "Teste", Guid.NewGuid().ToString("N")));
        Assert.Equal(OperacaoProdutoAcabadoStatus.ConflitoConcorrencia, estorno.Status);
        Assert.Single(await db.Set<VendaExterna>().ToListAsync());
        Assert.Equal(8, await db.MovimentosProdutoAcabado.CountAsync());
    }

    [SqlServerIntegrationFact]
    public async Task VendasExternas_RecebimentosConcorrentesEReplayOriginal()
    {
        var relogio = new RelogioSqlControlado { Agora = Agora };
        await using var banco = await SqlServerTestDatabase.CreateAsync(dbContextInterceptor: relogio);
        await using var db = banco.CreateVerificationContext();
        var (produto, variante, _) = await CriarProdutoCompravelAsync(db);
        await EntrarAsync(db, variante.Id, 2);
        using var factory = CriarApiPedidos(banco, relogio);
        using var admin = ClienteHttpPedidos(factory, Responsavel, XaquiamuniRoles.Administrador);
        var token = await CsrfPedidosAsync(admin);
        var dados = NovaVenda([new(produto.Id, variante.Id, 1, 1000)]) with { RecebimentoInicial = new(400, Hoje, "Pix") };
        var chaveCriar = Guid.NewGuid();
        using var criada = await EnviarVendaAsync(admin, VendasUrl, dados, chaveCriar, token);
        Assert.Equal(HttpStatusCode.Created, criada.StatusCode);
        var parcial = (await criada.Content.ReadFromJsonAsync<ResumoVendaExterna>())!;
        Assert.Equal("Parcial", parcial.SituacaoPagamento);
        Assert.Equal("600", parcial.SaldoCentavos);
        var filtradas = (await admin.GetFromJsonAsync<ResumoVendaExterna[]>(VendasUrl + "?pagamento=Parcial"))!;
        Assert.Equal(parcial.Id, Assert.Single(filtradas).Id);
        var url = $"{VendasUrl}/{parcial.Id:D}";
        using var pagaInvalida = await EnviarVendaAsync(admin, url + "/recebimentos", new ReceberVendaExternaInput(601, Hoje, "Pix"), Guid.NewGuid(), token);
        Assert.Equal(HttpStatusCode.Conflict, pagaInvalida.StatusCode);
        using var cancelamentoParcial = await EnviarVendaAsync(admin, url + "/cancelar", new { motivo = "Teste" }, Guid.NewGuid(), token);
        Assert.Equal(HttpStatusCode.Conflict, cancelamentoParcial.StatusCode);
        var chaves = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var pagamento = new ReceberVendaExternaInput(600, Hoje, "Dinheiro");
        var respostas = await Task.WhenAll(chaves.Select(c => EnviarVendaAsync(admin, url + "/recebimentos", pagamento, c, token)));
        using var aceita = Assert.Single(respostas, r => r.StatusCode == HttpStatusCode.OK);
        using var recusada = Assert.Single(respostas, r => r.StatusCode == HttpStatusCode.Conflict);
        var paga = (await aceita.Content.ReadFromJsonAsync<ResumoVendaExterna>())!;
        Assert.Equal("Paga", paga.SituacaoPagamento);
        Assert.Equal("0", paga.SaldoCentavos);
        Assert.NotEqual(parcial.Versao, paga.Versao);
        var chaveAceita = ReferenceEquals(aceita, respostas[0]) ? chaves[0] : chaves[1];
        using var replayPagamento = await EnviarVendaAsync(admin, url + "/recebimentos", pagamento, chaveAceita, token);
        Assert.Equal(paga, await replayPagamento.Content.ReadFromJsonAsync<ResumoVendaExterna>());
        Assert.Equal(paga, await admin.GetFromJsonAsync<ResumoVendaExterna>(
            $"{VendasUrl}/tentativas/{chaveAceita:D}?operacao=receber"));
        Assert.Empty((await admin.GetFromJsonAsync<ResumoVendaExterna[]>(VendasUrl + "?pagamento=Parcial"))!);
        using var replayCriacao = await EnviarVendaAsync(admin, VendasUrl, dados, chaveCriar, token);
        Assert.Equal(parcial, await replayCriacao.Content.ReadFromJsonAsync<ResumoVendaExterna>());
        using var cancelarPaga = await EnviarVendaAsync(admin, url + "/cancelar", new { motivo = "Teste" }, Guid.NewGuid(), token);
        Assert.Equal(HttpStatusCode.Conflict, cancelarPaga.StatusCode);
        var pagaNaHora = await Repositorio(db).CriarAsync(dados with { Comprador = null,
            RecebimentoInicial = new(1000, Hoje, "Pix") }, Responsavel, Guid.NewGuid(), default);
        Assert.Equal("Paga", pagaNaHora.Venda?.SituacaoPagamento);
        Assert.Equal(3, await db.Set<RecebimentoVendaExterna>().CountAsync());
        Assert.Equal(2, await db.Set<BaixaVendaExterna>().CountAsync());
    }

    [SqlServerIntegrationFact]
    public async Task VendasExternas_CancelaUmaVezERecusaLoteVencido()
    {
        var relogio = new RelogioSqlControlado { Agora = Agora };
        await using var banco = await SqlServerTestDatabase.CreateAsync(dbContextInterceptor: relogio);
        await using var db = banco.CreateVerificationContext();
        var (produto, variante, _) = await CriarProdutoCompravelAsync(db);
        await EntrarAsync(db, variante.Id, 2, Hoje);
        relogio.Agora = Agora.AddSeconds(1);
        await using var escopo = banco.Services.CreateAsyncScope();
        var repo = escopo.ServiceProvider.GetRequiredService<IGerenciarVendasExternas>();
        var comando = NovaVenda([new(produto.Id, variante.Id, 1, 1000)]);
        Assert.Equal(VendaExternaStatus.Invalida, (await repo.CriarAsync(comando with { Comprador = null }, Responsavel, Guid.NewGuid(), default)).Status);
        var venda = await repo.CriarAsync(comando, Responsavel, Guid.NewGuid(), default);
        Assert.Equal(VendaExternaStatus.Sucesso, venda.Status);
        var cancelamento = new CancelarVendaExternaInput("Registro incorreto");
        var chave = Guid.NewGuid();
        Assert.Equal(VendaExternaStatus.Sucesso, (await repo.CancelarAsync(venda.Venda!.Id, cancelamento, Responsavel, chave, default)).Status);
        Assert.Equal(VendaExternaStatus.Repetida, (await repo.CancelarAsync(venda.Venda.Id, cancelamento, Responsavel, chave, default)).Status);
        Assert.Equal(VendaExternaStatus.Conflito, (await repo.CancelarAsync(venda.Venda.Id, cancelamento, Responsavel, Guid.NewGuid(), default)).Status);
        var compensado = await db.Set<BaixaVendaExterna>().SingleAsync();
        var compensacao = await db.MovimentosProdutoAcabado.SingleAsync(m => m.Id == compensado.MovimentoCancelamentoId);
        Assert.Equal(compensado.MovimentoId, compensacao.MovimentoOriginalId);
        Assert.Equal(2, await db.SaldosProdutoAcabado.SumAsync(s => s.QuantidadeVendavel));
        relogio.Agora = Agora.AddMinutes(5);
        var segunda = await repo.CriarAsync(comando, Responsavel, Guid.NewGuid(), default);
        Assert.Equal(VendaExternaStatus.Sucesso, segunda.Status);
        relogio.Agora = Agora.AddDays(1);
        Assert.Equal(VendaExternaStatus.Conflito, (await repo.CancelarAsync(segunda.Venda!.Id, cancelamento, Responsavel, Guid.NewGuid(), default)).Status);
        Assert.Equal(1, await db.SaldosProdutoAcabado.SumAsync(s => s.QuantidadeVendavel));
        Assert.Equal(1, await db.MovimentosProdutoAcabado.CountAsync(m => m.Tipo == TipoMovimentoProdutoAcabado.Estorno));
    }

    [SqlServerIntegrationFact]
    public async Task VendasExternas_DisputaCheckoutPelaUltimaUnidade()
    {
        await using var banco = await SqlServerTestDatabase.CreateAsync(dbContextInterceptor: new RelogioSqlControlado { Agora = Agora });
        await using var seed = banco.CreateVerificationContext();
        var cliente = await CriarClienteCompraAsync(seed);
        var (produto, variante, _) = await CriarProdutoCompravelAsync(seed);
        await EntrarAsync(seed, variante.Id, 1);
        await using var vendaDb = banco.CreateVerificationContext();
        await using var compraDb = banco.CreateVerificationContext();
        var largada = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<T> Executar<T>(Func<Task<T>> func) { await largada.Task; return await func(); }
        var venda = Executar(() => Repositorio(vendaDb).CriarAsync(NovaVenda([new(produto.Id, variante.Id, 1, 1000)]),
            Responsavel, Guid.NewGuid(), default));
        var compra = Executar(() => Compra(compraDb).ExecuteAsync(new(cliente, Guid.NewGuid(), [new(produto.Id, variante.Id, 1)])));
        largada.SetResult();
        await Task.WhenAll(venda, compra);
        var vendas = await seed.Set<VendaExterna>().CountAsync();
        var reservas = await seed.ReservasProdutoAcabado.CountAsync();
        Assert.Equal(1, vendas + reservas);
        Assert.Equal(vendas == 1 ? VendaExternaStatus.Sucesso : VendaExternaStatus.Conflito, venda.Result.Status);
        Assert.Equal(reservas == 1 ? CriarPedidoCompraStatus.Sucesso : CriarPedidoCompraStatus.SaldoInsuficiente, compra.Result.Status);
        Assert.Equal(vendas, await seed.Set<BaixaVendaExterna>().CountAsync());
        Assert.Equal(reservas, await seed.SaldosProdutoAcabado.SumAsync(s => s.QuantidadeVendavel));
    }

    private static CriarVendaExternaInput NovaVenda(IReadOnlyList<ItemVendaExternaInput> itens) =>
        new("Feira", null, Hoje, "Comprador de teste", null, itens, null);

    private static async Task<HttpResponseMessage> EnviarVendaAsync(HttpClient client, string url, object corpo, Guid chave, string? csrf = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(corpo) };
        request.Headers.Add("Idempotency-Key", chave.ToString("D"));
        if (csrf is not null) request.Headers.Add("X-XSRF-TOKEN", csrf);
        return await client.SendAsync(request);
    }
}
