using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xaquiamuni.Api.Estoque;
using Xaquiamuni.Aplicacao.Estoque;
using Xaquiamuni.Aplicacao.Pedidos;
using Xaquiamuni.Dominio.Estoque;
using Xaquiamuni.Infraestrutura.Identidade;

namespace Xaquiamuni.Tests.Integration;

public sealed partial class ReservarProdutoAcabadoSqlServerIntegrationTests
{
    [SqlServerIntegrationFact]
    public async Task SaidaAutomaticaAlocaFefoDeVariosLotesProtegeReservasERepeteTodosOsMovimentos()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var db = database.CreateVerificationContext();
        var variante = await CriarVarianteAsync(db);
        await EntrarAsync(db, variante.Id, 10, validade: Hoje.AddDays(-1));
        var posterior = (await EntrarAsync(db, variante.Id, 2, validade: Hoje.AddDays(2))).Lote!;
        var primeiros = new[]
        {
            (await EntrarAsync(db, variante.Id, 2, validade: Hoje)).Lote!,
            (await EntrarAsync(db, variante.Id, 2, validade: Hoje)).Lote!,
        }.OrderBy(l => l.Codigo, StringComparer.Ordinal).ToArray();
        var reserva = Assert.Single((await CasoDeUso(db).ExecuteAsync(new(Guid.NewGuid(), variante.Id, 1))).Reservas);
        Assert.Equal(primeiros[0].Id, reserva.LoteId);
        var repositorio = Repositorio(db);
        var input = new SaidaAutomaticaProdutoAcabadoInput(variante.Id, Responsavel, 6, "Amostras", Guid.NewGuid().ToString());

        Assert.Equal(OperacaoProdutoAcabadoStatus.SaldoInsuficiente, (await repositorio.RegistrarSaidaAutomaticaAsync(input)).Status);
        Assert.Equal(16, await db.SaldosProdutoAcabado.SumAsync(s => s.QuantidadeVendavel));
        Assert.Equal(4, await db.MovimentosProdutoAcabado.CountAsync());

        input = input with { Quantidade = 4 };
        var resultado = await repositorio.RegistrarSaidaAutomaticaAsync(input);
        Assert.Equal(OperacaoProdutoAcabadoStatus.Sucesso, resultado.Status);
        Assert.Equal([primeiros[0].Id, primeiros[1].Id, posterior.Id], resultado.Movimentos.Select(m => m.LoteId));
        Assert.Equal([1, 2, 1], resultado.Movimentos.Select(m => m.Quantidade));
        Assert.All(resultado.Movimentos, m =>
        {
            Assert.Equal(TipoMovimentoProdutoAcabado.SaidaManual, m.Tipo);
            Assert.Null(m.ReservaProdutoAcabadoId);
            Assert.Equal(Responsavel, m.ResponsavelId);
        });
        Assert.Single(resultado.Movimentos.Select(m => m.RegistradoEmUtc).Distinct());
        Assert.Equal(1, await db.SaldosProdutoAcabado.Where(s => s.LoteId == reserva.LoteId).Select(s => s.QuantidadeVendavel).SingleAsync());
        Assert.Equal(EstadoReservaProdutoAcabado.Ativa, (await db.ReservasProdutoAcabado.SingleAsync()).Estado);

        var replay = await repositorio.RegistrarSaidaAutomaticaAsync(input);
        Assert.True(replay.Repetida);
        Assert.Equal(resultado.Movimentos.Select(m => m.Id), replay.Movimentos.Select(m => m.Id));
        Assert.Equal(OperacaoProdutoAcabadoStatus.ConflitoIdempotencia,
            (await repositorio.RegistrarSaidaAutomaticaAsync(input with { Motivo = "Outro motivo" })).Status);
        Assert.Equal(12, await db.SaldosProdutoAcabado.SumAsync(s => s.QuantidadeVendavel));
        Assert.Equal(7, await db.MovimentosProdutoAcabado.CountAsync());
        Assert.Empty(await db.Pedidos.ToListAsync());
    }

    [SqlServerIntegrationFact]
    public async Task SaidaAutomaticaDisputaCheckoutPelaUltimaUnidade()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var seed = database.CreateVerificationContext();
        var cliente = await CriarClienteCompraAsync(seed);
        var (produto, variante, _) = await CriarProdutoCompravelAsync(seed);
        await EntrarAsync(seed, variante.Id, 1);
        await using var dbSaida = database.CreateVerificationContext();
        await using var dbCompra = database.CreateVerificationContext();
        var inicio = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<SaidaAutomaticaProdutoAcabadoResult> SairAsync()
        {
            await inicio.Task;
            return await Repositorio(dbSaida).RegistrarSaidaAutomaticaAsync(
                new(variante.Id, Responsavel, 1, "Amostra", Guid.NewGuid().ToString()));
        }
        async Task<CriarPedidoCompraResult> ComprarAsync()
        {
            await inicio.Task;
            return await Compra(dbCompra).ExecuteAsync(new(cliente, Guid.NewGuid(), [new(produto.Id, variante.Id, 1)]));
        }
        var saida = SairAsync();
        var compra = ComprarAsync();
        inicio.SetResult();
        await Task.WhenAll(saida, compra);
        Assert.Contains(saida.Result.Status, new[] { OperacaoProdutoAcabadoStatus.Sucesso, OperacaoProdutoAcabadoStatus.SaldoInsuficiente });
        Assert.Contains(compra.Result.Status, new[] { CriarPedidoCompraStatus.Sucesso, CriarPedidoCompraStatus.SaldoInsuficiente });
        Assert.NotEqual(saida.Result.Status == OperacaoProdutoAcabadoStatus.Sucesso, compra.Result.Status == CriarPedidoCompraStatus.Sucesso);
        Assert.Equal(saida.Result.Movimentos.Count, await seed.MovimentosProdutoAcabado.CountAsync(m => m.Tipo == TipoMovimentoProdutoAcabado.SaidaManual));
        Assert.Equal(compra.Result.Status == CriarPedidoCompraStatus.Sucesso ? 1 : 0, await seed.ReservasProdutoAcabado.CountAsync());
        Assert.Equal(1 - saida.Result.Movimentos.Count, await seed.SaldosProdutoAcabado.SumAsync(s => s.QuantidadeVendavel));
    }

    [SqlServerIntegrationFact]
    public async Task SaidaAutomaticaUsaInstanteSqlParaPrazoDaReservaEDiaDeValidade()
    {
        var relogio = new RelogioSqlControlado();
        await using var database = await SqlServerTestDatabase.CreateAsync(dbContextInterceptor: relogio);
        await using var db = database.CreateVerificationContext();
        var variante = await CriarVarianteAsync(db);
        var lote = (await EntrarAsync(db, variante.Id, 2, validade: Hoje)).Lote!;
        var expira = new DateTimeOffset(2026, 11, 19, 2, 59, 59, TimeSpan.Zero);
        var ativa = ReservaProdutoAcabado.Criar(Guid.NewGuid(), variante.Id, lote.Id, 1, expira.AddHours(-24), expira);
        var confirmada = ReservaProdutoAcabado.Criar(Guid.NewGuid(), variante.Id, lote.Id, 1, expira.AddHours(-24), expira);
        confirmada.ConfirmarCompromisso(expira.AddHours(-1));
        db.ReservasProdutoAcabado.AddRange(ativa, confirmada);
        await db.SaveChangesAsync();
        await using var scope = database.Services.CreateAsyncScope();
        var saida = scope.ServiceProvider.GetRequiredService<IRegistrarSaidaAutomaticaProdutoAcabado>();
        var input = new SaidaAutomaticaProdutoAcabadoInput(variante.Id, Responsavel, 1, "Amostra", Guid.NewGuid().ToString());
        relogio.Agora = expira.AddTicks(-1);
        Assert.Equal(OperacaoProdutoAcabadoStatus.SaldoInsuficiente, (await saida.RegistrarSaidaAutomaticaAsync(input)).Status);
        relogio.Agora = expira;
        var movimento = Assert.Single((await saida.RegistrarSaidaAutomaticaAsync(input)).Movimentos);
        Assert.Equal(expira, movimento.RegistradoEmUtc);
        Assert.Equal(1, await db.SaldosProdutoAcabado.SumAsync(s => s.QuantidadeVendavel));

        var outra = await CriarVarianteAsync(db, "Outro produto");
        await EntrarAsync(db, outra.Id, 1, validade: Hoje);
        relogio.Agora = expira.AddSeconds(1);
        var vencido = await saida.RegistrarSaidaAutomaticaAsync(input with { VarianteId = outra.Id, ChaveIdempotencia = Guid.NewGuid().ToString() });
        Assert.Equal(OperacaoProdutoAcabadoStatus.SaldoInsuficiente, vencido.Status);
        Assert.Empty(vencido.Movimentos);
        Assert.Equal(2, await db.SaldosProdutoAcabado.SumAsync(s => s.QuantidadeVendavel));
    }

    [SqlServerIntegrationFact]
    public async Task SaidaAutomaticaApiExigeAdminCsrfERecusaLoteNoCorpo()
    {
        var relogio = new RelogioSqlControlado { Agora = Agora };
        await using var database = await SqlServerTestDatabase.CreateAsync(dbContextInterceptor: relogio);
        await using var db = database.CreateVerificationContext();
        var variante = await CriarVarianteAsync(db);
        var lote = (await EntrarAsync(db, variante.Id, 1)).Lote!;
        using var factory = CriarApiPedidos(database, relogio);
        using var anonimo = ClienteHttpPedidos(factory);
        using var cliente = ClienteHttpPedidos(factory, Guid.NewGuid());
        using var admin = ClienteHttpPedidos(factory, Responsavel, XaquiamuniRoles.Administrador);
        var url = $"{AdminEstoqueProdutoAcabadoEndpoints.BasePath}/variantes/{variante.Id:D}/saidas-automaticas";
        var chave = Guid.NewGuid().ToString();
        var dados = new { quantidade = 1, motivo = "Amostra" };
        async Task<HttpResponseMessage> PostAsync(HttpClient navegador, object corpo, string? csrf = null)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(corpo) };
            request.Headers.Add("Idempotency-Key", chave);
            if (csrf is not null) request.Headers.Add("X-XSRF-TOKEN", csrf);
            return await navegador.SendAsync(request);
        }

        using var semSessao = await PostAsync(anonimo, dados);
        using var semPermissao = await PostAsync(cliente, dados);
        using var semToken = await PostAsync(admin, dados);
        Assert.Equal(HttpStatusCode.Unauthorized, semSessao.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, semPermissao.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, semToken.StatusCode);
        var csrf = await CsrfPedidosAsync(admin);
        using var loteIndevido = await PostAsync(admin, new { quantidade = 1, motivo = "Amostra", loteId = lote.Id }, csrf);
        Assert.Equal(HttpStatusCode.BadRequest, loteIndevido.StatusCode);
        using var criado = await PostAsync(admin, dados, csrf);
        using var replay = await PostAsync(admin, dados, csrf);
        Assert.Equal(HttpStatusCode.Created, criado.StatusCode);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        var resposta = (await replay.Content.ReadFromJsonAsync<SaidaAutomaticaProdutoAcabadoAdminResponse>())!;
        Assert.True(resposta.Repetida);
        var movimento = await db.MovimentosProdutoAcabado.SingleAsync(m => m.Tipo == TipoMovimentoProdutoAcabado.SaidaManual);
        Assert.Equal(movimento.Id, Assert.Single(resposta.MovimentoIds));
        Assert.Equal(lote.Id, movimento.LoteId);
        Assert.Equal(0, await db.SaldosProdutoAcabado.SumAsync(s => s.QuantidadeVendavel));
    }
}
