using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xaquiamuni.Aplicacao.Pedidos;
using Xaquiamuni.Infraestrutura.Identidade;
using Xaquiamuni.Infraestrutura.Persistencia;

namespace Xaquiamuni.Tests.Integration;

public sealed partial class CadeiaFabricacaoSqlServerIntegrationTests
{
    [SqlServerIntegrationFact]
    public Task CheckoutEntrega_ExclusaoEntraDepoisDoCheckout() => DisputarExclusaoAsync(checkoutPrimeiro: true);

    [SqlServerIntegrationFact]
    public Task CheckoutEntrega_CheckoutEntraDepoisDaExclusao() => DisputarExclusaoAsync(checkoutPrimeiro: false);

    [SqlServerIntegrationFact]
    public Task Atendimento_TarifaConcorrenteInvalidaRevisaoDaExclusao() => DisputarAlteracaoAsync(editar: true);

    [SqlServerIntegrationFact]
    public Task Atendimento_DuasExclusoesRegistramApenasUmaOperacao() => DisputarAlteracaoAsync(editar: false);

    private static async Task DisputarAlteracaoAsync(bool editar)
    {
        var barreira = new BarreiraAtendimento();
        await using var ambiente = await Ambiente.CriarAsync(interceptor: barreira);
        using var admin = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Administrador);
        using var outraSessao = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Administrador);
        var dados = new SalvarCidade("4322509", "Vacaria", "RS", 1300, true);
        var cidade = await EscreverAtendimentoAsync(admin, HttpMethod.Post, "/cidades", dados, HttpStatusCode.Created);
        var rota = $"/cidades/{cidade.GetProperty("id").GetGuid():D}";
        var csrf = await TokenComprovanteAsync(admin);
        var outroCsrf = await TokenComprovanteAsync(outraSessao);
        barreira.Armar();
        var primeiro = editar
            ? EnviarAtendimentoAsync(admin, HttpMethod.Put, rota, dados with { TarifaCentavos = 1400, Revisao = 1 }, Guid.NewGuid(), csrf)
            : EnviarAtendimentoAsync(admin, HttpMethod.Delete, rota + "?revisao=1", null, Guid.NewGuid(), csrf);
        Task<HttpResponseMessage> segundo;
        try
        {
            await barreira.PrimeiroAdquiriu.Task.WaitAsync(TimeSpan.FromSeconds(8));
            segundo = EnviarAtendimentoAsync(outraSessao, HttpMethod.Delete, rota + "?revisao=1", null, Guid.NewGuid(), outroCsrf);
            await barreira.SegundoSolicitou.Task.WaitAsync(TimeSpan.FromSeconds(8));
            Assert.False(segundo.IsCompleted);
        }
        finally
        {
            barreira.Prosseguir.TrySetResult();
        }
        using var vencedor = await primeiro;
        using var rejeitado = await segundo;
        Assert.Equal(HttpStatusCode.OK, vencedor.StatusCode);
        Assert.Equal(editar ? HttpStatusCode.Conflict : HttpStatusCode.NotFound, rejeitado.StatusCode);
        await using var db = ambiente.Banco.CreateVerificationContext();
        Assert.Equal(editar ? 0 : 1, await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM OperacoesAtendimento WHERE Operacao = 'excluir-cidade'").SingleAsync());
    }

    [SqlServerIntegrationFact]
    public async Task Atendimento_FalhaAntesDoCommitReverteCidadeEAuditoria()
    {
        await using var banco = await SqlServerTestDatabase.CreateAsync();
        await using var db = banco.CreateVerificationContext();
        var autor = Guid.NewGuid();
        var criada = await new EfConfiguracoesAtendimento(db, TimeProvider.System).SalvarCidadeAsync(null,
            new SalvarCidade("4322509", "Vacaria", "RS", 1300, true), autor, Guid.NewGuid(), default);
        var cidade = Assert.IsType<CidadeEntregaDto>(criada.Dados);
        var options = new DbContextOptionsBuilder<XaquiamuniDbContext>().UseSqlServer(banco.ConexaoIsolada)
            .AddInterceptors(new FalhaAntesDoCommit()).Options;
        await using var outra = new XaquiamuniDbContext(options);
        var operacao = new EfConfiguracoesAtendimento(outra, TimeProvider.System);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            operacao.ExcluirCidadeAsync(cidade.Id, cidade.Revisao, autor, Guid.NewGuid(), default));
        Assert.NotNull(await new EfConfiguracoesAtendimento(db, TimeProvider.System).CidadeAsync(cidade.Id, default));
        Assert.Equal(0, await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM OperacoesAtendimento WHERE Operacao = 'excluir-cidade'").SingleAsync());
    }

    private sealed class FalhaAntesDoCommit : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("Falha de teste antes do commit.");
    }

    private static async Task DisputarExclusaoAsync(bool checkoutPrimeiro)
    {
        var barreira = new BarreiraAtendimento();
        await using var ambiente = await Ambiente.CriarAsync(interceptor: barreira,
            municipiosPorCep: new Dictionary<string, string> { ["95200000"] = "4322509" });
        using var admin = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Administrador);
        using var cliente = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Cliente);
        await using var db = ambiente.Banco.CreateVerificationContext();
        var (produto, variante, _) = await ReservarProdutoAcabadoSqlServerIntegrationTests.CriarProdutoCompravelAsync(db);
        await admin.PostAsync<JsonElement>($"/api/admin/estoque/produtos-acabados/variantes/{variante.Id:D}/entradas",
            new { tipo = "EntradaManual", quantidade = 2, condicaoDestino = "Vendavel", semControleValidade = true, motivo = "Teste" },
            HttpStatusCode.Created, Guid.NewGuid());
        var cidade = await EscreverAtendimentoAsync(admin, HttpMethod.Post, "/cidades",
            new SalvarCidade("4322509", "Vacaria", "RS", 1300, true), HttpStatusCode.Created);
        await EscreverAtendimentoAsync(admin, HttpMethod.Put, "/modalidades",
            new SalvarModalidades(true, true, false, null, null, 0), HttpStatusCode.OK);
        var itens = new[] { new { produtoId = produto.Id, produtoVarianteId = variante.Id, quantidade = 1 } };
        var endereco = new EnderecoCotacaoEntrega("95200000", "RS", "Vacaria", "Centro", "Rua de teste", "12", null);
        var cotacao = await cliente.PostAsync<JsonElement>(CotacoesEntrega, new { itens, endereco }, HttpStatusCode.OK);
        var ofertaEntregaId = Assert.Single(cotacao.GetProperty("ofertas").EnumerateArray(),
            x => x.GetProperty("modalidade").GetString() == "EntregaPropria").GetProperty("id").GetString();
        var rota = $"/cidades/{cidade.GetProperty("id").GetGuid():D}?revisao=1";
        var csrf = await TokenComprovanteAsync(admin);
        Task<HttpResponseMessage> Checkout() => cliente.EnviarAsync("/api/pedidos", new { itens, ofertaEntregaId }, Guid.NewGuid());
        Task<HttpResponseMessage> Excluir() => EnviarAtendimentoAsync(admin, HttpMethod.Delete, rota, null, Guid.NewGuid(), csrf);

        barreira.Armar();
        var primeiro = checkoutPrimeiro ? Checkout() : Excluir();
        Task<HttpResponseMessage> segundo;
        try
        {
            await barreira.PrimeiroAdquiriu.Task.WaitAsync(TimeSpan.FromSeconds(8));
            segundo = checkoutPrimeiro ? Excluir() : Checkout();
            await barreira.SegundoSolicitou.Task.WaitAsync(TimeSpan.FromSeconds(8));
            Assert.False(segundo.IsCompleted);
        }
        finally
        {
            barreira.Prosseguir.TrySetResult();
        }
        using var vencedor = await primeiro;
        using var rejeitado = await segundo;
        Assert.Equal(checkoutPrimeiro ? HttpStatusCode.Created : HttpStatusCode.OK, vencedor.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, rejeitado.StatusCode);
        Assert.Equal(checkoutPrimeiro ? "cidade_com_pedidos" : "oferta_entrega_invalida",
            (await rejeitado.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("codigo").GetString());
        Assert.Equal(checkoutPrimeiro ? 1 : 0, await db.Pedidos.CountAsync());
        Assert.Equal(checkoutPrimeiro ? 1 : 0, await db.ReservasProdutoAcabado.CountAsync());

        if (!checkoutPrimeiro) return;
        await EscreverAtendimentoAsync(cliente, HttpMethod.Delete, rota, null, HttpStatusCode.Forbidden);
        await EscreverAtendimentoAsync(admin, HttpMethod.Delete, rota, null, HttpStatusCode.BadRequest, csrf: false);
        var outra = await EscreverAtendimentoAsync(admin, HttpMethod.Post, "/cidades",
            new SalvarCidade("4314902", "Porto Alegre", "RS", 1200, false), HttpStatusCode.Created);
        var outraId = outra.GetProperty("id").GetGuid();
        // Um GUID em outro campo do JSON não é uma referência de entrega.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE [Pedidos] SET [Entrega] = JSON_MODIFY([Entrega], '$.Observacao', {outraId.ToString("D")})
            """);
        await EscreverAtendimentoAsync(admin, HttpMethod.Delete, $"/cidades/{outraId:D}?revisao=1",
            null, HttpStatusCode.OK);

        // JSON válido com CidadeId inválido impede verificar a ausência de referências.
        await db.Database.ExecuteSqlRawAsync("""
            UPDATE [Pedidos] SET [Entrega] = JSON_MODIFY([Entrega], '$.CidadeId', 'identificador inválido')
            """);
        using var inconclusivo = await Excluir();
        Assert.Equal(HttpStatusCode.Conflict, inconclusivo.StatusCode);
        Assert.Equal("historico_entrega_invalido",
            (await inconclusivo.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("codigo").GetString());
        var cidadeId = cidade.GetProperty("id").GetGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE [Pedidos] SET [Entrega] = JSON_MODIFY(JSON_MODIFY([Entrega], '$.CidadeId', NULL),
                '$.cidadeId', {cidadeId.ToString("D")})
            """);
        await VerificarCidadeComPedidosAsync(admin, $"/cidades/{cidadeId:D}", 1);
    }

    private sealed class BarreiraAtendimento : DbCommandInterceptor
    {
        private int _solicitacoes = -1;
        public TaskCompletionSource PrimeiroAdquiriu { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SegundoSolicitou { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Prosseguir { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Armar() => Volatile.Write(ref _solicitacoes, 0);

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Volatile.Read(ref _solicitacoes) >= 0 && EhLockAtendimento(command) &&
                Interlocked.Increment(ref _solicitacoes) == 2)
                SegundoSolicitou.TrySetResult();
            return ValueTask.FromResult(result);
        }

        public override async ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            int result, CancellationToken cancellationToken = default)
        {
            if (Volatile.Read(ref _solicitacoes) > 0 && EhLockAtendimento(command) && PrimeiroAdquiriu.TrySetResult())
                await Prosseguir.Task.WaitAsync(TimeSpan.FromSeconds(18), cancellationToken);
            return result;
        }

        private static bool EhLockAtendimento(DbCommand command) =>
            command.CommandText.Contains("sp_getapplock", StringComparison.Ordinal) &&
            command.Parameters.Cast<DbParameter>().Any(p => Equals(p.Value, "store-fulfillment-config"));
    }
}
