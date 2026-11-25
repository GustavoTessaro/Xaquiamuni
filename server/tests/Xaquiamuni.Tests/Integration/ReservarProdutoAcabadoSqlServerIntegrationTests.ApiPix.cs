using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xaquiamuni.Api.Autenticacao;
using Xaquiamuni.Api.Pedidos;
using Xaquiamuni.Aplicacao.Pedidos;
using Xaquiamuni.Dominio.Entidades;
using Xaquiamuni.Dominio.Estoque;
using Xaquiamuni.Infraestrutura.Identidade;
using Xaquiamuni.Infraestrutura.Persistencia;

namespace Xaquiamuni.Tests.Integration;

public sealed partial class ReservarProdutoAcabadoSqlServerIntegrationTests
{
    [SqlServerIntegrationFact]
    public Task ApiPixAutorizaConfiguracaoEExplicaAusenciaOuEncerramentoSemAlterarPedido() =>
        VerificarApiPixAsync(new("Email", "recebimento@example.test", "Loja de teste", null),
            "loja@example.test", "loja@example.test");

    [SqlServerIntegrationFact]
    public Task ApiPixCnpjNormalizaEPreservaAutorizacaoCsrfETitularidade() =>
        VerificarApiPixAsync(new("Cnpj", "99.999.999/9999-62", "Loja de teste", null),
            "12.abc.345/01de-35", "12ABC34501DE35");

    private static async Task VerificarApiPixAsync(SalvarPixManualRequest dto, string novaChave, string chaveNormalizada)
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateVerificationContext();
        var (pedido, variante) = await CriarPedidoParaPagamentoAsync(context);
        var adminId = await CriarAdministradorPagamentoAsync(context);
        var outroId = await CriarClienteCompraAsync(context);
        var agora = await LerUtcSqlAsync(context);
        var item = new DadosItemPedido(variante.ProdutoId, variante.Id, "Sabonete", "Padrao", 1, 1390);
        var pago = Pedido.Criar(pedido.ClienteId, agora, [item]);
        pago.ConfirmarPagamento(adminId, agora);
        var cancelado = Pedido.Criar(pedido.ClienteId, agora, [item]);
        cancelado.Cancelar(adminId, agora, "teste");
        var vencido = Pedido.Criar(pedido.ClienteId, agora.AddDays(-2), [item]);
        var expirado = Pedido.Criar(pedido.ClienteId, agora.AddDays(-2), [item]);
        expirado.Expirar(agora);
        var entregue = Pedido.Criar(pedido.ClienteId, agora, [item]);
        entregue.ConfirmarPagamento(adminId, agora);
        entregue.IniciarPreparacao(adminId, agora);
        entregue.AutorizarRetirada(adminId, agora);
        entregue.ConfirmarEntrega(adminId, agora);
        context.Pedidos.AddRange(pago, cancelado, vencido, expirado, entregue);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        // Retorna ao esquema da versão anterior para exercitar o pedido sem snapshot ao migrar.
        await context.Database.MigrateAsync("20261125044134_AdicionarEmailComercial");
        await context.Database.MigrateAsync();

        using var factory = CriarApiPedidos(database);
        using var anonimo = ClienteHttpPedidos(factory);
        using var cliente = ClienteHttpPedidos(factory, pedido.ClienteId);
        using var outro = ClienteHttpPedidos(factory, outroId);
        using var admin = ClienteHttpPedidos(factory, adminId, XaquiamuniRoles.Administrador);
        var url = $"/api/pedidos/{pedido.Id:D}/pagamento";
        using var semSessao = await anonimo.GetAsync(url);
        Assert.Equal(HttpStatusCode.Unauthorized, semSessao.StatusCode);
        using var alheio = await outro.GetAsync(url);
        Assert.Equal(HttpStatusCode.NotFound, alheio.StatusCode);
        using var ausente = await cliente.GetAsync(url);
        using var semConfig = await LerJsonPedidosAsync(ausente);
        Assert.Equal(HttpStatusCode.OK, ausente.StatusCode);
        Assert.Equal("configuracao_ausente", semConfig.RootElement.GetProperty("situacao").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, semConfig.RootElement.GetProperty("pix").ValueKind);

        using var anonimoPut = await SalvarPixAsync(anonimo, dto);
        using var clientePut = await SalvarPixAsync(cliente, dto);
        using var semCsrf = await SalvarPixAsync(admin, dto);
        using var csrfInvalido = await SalvarPixAsync(admin, dto, "invalido");
        Assert.Equal(HttpStatusCode.Unauthorized, anonimoPut.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, clientePut.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, semCsrf.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, csrfInvalido.StatusCode);

        var token = await CsrfPedidosAsync(admin);
        dto = dto with { Revisao = 0, ConfirmarPendentes = true };
        using var configVazia = await admin.GetAsync(PagamentoPixManualEndpoints.AdminPath);
        Assert.Equal(HttpStatusCode.OK, configVazia.StatusCode);
        foreach (var invalido in new[]
        {
            dto with { ChavePix = "invalida" }, dto with { TipoChave = "1" },
            dto with { NomeRecebedor = " " }, dto with { InstrucoesAdicionais = new string('x', 501) },
        })
        {
            using var recusado = await SalvarPixAsync(admin, invalido, token);
            Assert.Equal(HttpStatusCode.BadRequest, recusado.StatusCode);
        }
        Assert.Empty(await context.ConfiguracoesPixManual.AsNoTracking().ToListAsync());

        using var semConfirmacao = await SalvarPixAsync(admin, dto with { ConfirmarPendentes = false }, token);
        Assert.Equal(HttpStatusCode.Conflict, semConfirmacao.StatusCode);
        var chaveOriginal = Guid.NewGuid();
        using var criado = await SalvarPixAsync(admin, dto, token, chaveOriginal);
        Assert.Equal(HttpStatusCode.OK, criado.StatusCode);
        var antes = await cliente.GetFromJsonAsync<System.Text.Json.JsonElement>(url);
        Assert.Equal(dto.NomeRecebedor, antes.GetProperty("pix").GetProperty("nomeRecebedor").GetString());
        if (dto.TipoChave == "Cnpj")
        {
            using var criadoJson = await LerJsonPedidosAsync(criado);
            Assert.Equal("99999999999962", criadoJson.RootElement.GetProperty("pix").GetProperty("chavePix").GetString());
        }
        var versaoAtual = dto with { Revisao = 1, ChavePix = novaChave, NomeRecebedor = "Loja de teste atualizada", InstrucoesAdicionais = "Confira o nome antes de pagar." };
        using var atualizado = await SalvarPixAsync(admin, versaoAtual, token);
        Assert.Equal(HttpStatusCode.OK, atualizado.StatusCode);
        Assert.True(atualizado.Headers.CacheControl?.NoStore);
        Assert.Equal(chaveNormalizada, (await context.ConfiguracoesPixManual.AsNoTracking().SingleAsync()).ChavePix);

        using var replay = await SalvarPixAsync(admin, dto, token, chaveOriginal);
        Assert.Equal(await criado.Content.ReadAsStringAsync(), await replay.Content.ReadAsStringAsync());
        using var chaveReutilizada = await SalvarPixAsync(admin, versaoAtual, token, chaveOriginal);
        Assert.Equal(HttpStatusCode.Conflict, chaveReutilizada.StatusCode);
        using var revisaoAntiga = await SalvarPixAsync(admin, dto, token);
        Assert.Equal(HttpStatusCode.Conflict, revisaoAntiga.StatusCode);
        var corridas = await Task.WhenAll(
            SalvarPixAsync(admin, versaoAtual with { Revisao = 2 }, token),
            SalvarPixAsync(admin, versaoAtual with { Revisao = 2 }, token));
        using var primeira = corridas[0];
        using var segunda = corridas[1];
        Assert.Contains(corridas, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Contains(corridas, r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal(3, await context.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM [AlteracoesConfiguracaoPix]").SingleAsync());

        using var consultaAdmin = await admin.GetAsync(PagamentoPixManualEndpoints.AdminPath);
        using var consultaAdminJson = await LerJsonPedidosAsync(consultaAdmin);
        Assert.Equal(chaveNormalizada, consultaAdminJson.RootElement.GetProperty("pix").GetProperty("chavePix").GetString());
        Assert.Equal(dto.TipoChave, consultaAdminJson.RootElement.GetProperty("pix").GetProperty("tipoChave").GetString());
        using var disponivel = await cliente.GetAsync(url);
        using var json = await LerJsonPedidosAsync(disponivel);
        Assert.Equal(HttpStatusCode.OK, disponivel.StatusCode);
        Assert.True(disponivel.Headers.CacheControl?.NoStore);
        Assert.Equal("disponivel", json.RootElement.GetProperty("situacao").GetString());
        Assert.Equal(pedido.TotalCentavos.ToString(), json.RootElement.GetProperty("valorCentavos").GetString());
        Assert.Equal(pedido.PrazoPagamentoEmUtc, json.RootElement.GetProperty("prazoPagamentoEmUtc").GetDateTimeOffset());
        Assert.True(json.RootElement.GetProperty("legado").GetBoolean());
        var pix = json.RootElement.GetProperty("pix");
        Assert.Equal(chaveNormalizada, pix.GetProperty("chavePix").GetString());
        Assert.Equal(dto.TipoChave, pix.GetProperty("tipoChave").GetString());
        Assert.Equal(versaoAtual.NomeRecebedor, pix.GetProperty("nomeRecebedor").GetString());
        Assert.Equal(versaoAtual.InstrucoesAdicionais, pix.GetProperty("instrucoesAdicionais").GetString());
        Assert.False(pix.TryGetProperty("id", out _));

        var novo = Pedido.Criar(pedido.ClienteId, agora, [item]);
        context.Pedidos.Add(novo);
        await context.SaveChangesAsync();
        var novoPix = await cliente.GetFromJsonAsync<System.Text.Json.JsonElement>($"/api/pedidos/{novo.Id:D}/pagamento");
        Assert.Equal(chaveNormalizada, novoPix.GetProperty("pix").GetProperty("chavePix").GetString());
        Assert.Equal(novo.TotalCentavos.ToString(), novoPix.GetProperty("valorCentavos").GetString());
        Assert.Equal(novo.PrazoPagamentoEmUtc, novoPix.GetProperty("prazoPagamentoEmUtc").GetDateTimeOffset());

        foreach (var fechado in new[] { pago, cancelado, expirado, vencido, entregue })
        {
            using var response = await cliente.GetAsync($"/api/pedidos/{fechado.Id:D}/pagamento");
            using var estado = await LerJsonPedidosAsync(response);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("pagamento_indisponivel", estado.RootElement.GetProperty("situacao").GetString());
            Assert.Equal(System.Text.Json.JsonValueKind.Null, estado.RootElement.GetProperty("pix").ValueKind);
        }

        Assert.Equal(SituacaoPedido.AguardandoPagamento, await context.Pedidos.Where(p => p.Id == pedido.Id)
            .Select(p => p.Situacao).SingleAsync());
        Assert.Equal(EstadoReservaProdutoAcabado.Ativa, await context.ReservasProdutoAcabado.Select(r => r.Estado).SingleAsync());
        Assert.Equal(1, (await context.SaldosProdutoAcabado.AsNoTracking().SingleAsync()).QuantidadeFisica);
        Assert.Equal(1, await context.MovimentosProdutoAcabado.CountAsync());
    }

    [SqlServerIntegrationFact]
    public async Task ApiPix_PedidoConcorrenteConservaRevisaoERollbackRemoveSnapshot()
    {
        await using var banco = await SqlServerTestDatabase.CreateAsync();
        await using var verificacao = banco.CreateVerificationContext();
        var (anterior, variante) = await CriarPedidoParaPagamentoAsync(verificacao);
        var data = DateOnly.FromDateTime((await LerUtcSqlAsync(verificacao)).UtcDateTime);
        await EntrarAsync(verificacao, variante.Id, 3, validade: data.AddDays(10));
        var pix = new EfPagamentoPixManual(verificacao);
        var autor = await CriarAdministradorPagamentoAsync(verificacao);
        var a = ConfiguracaoPixManual.Criar(TipoChavePixManual.Email, "primeira@example.test", "Loja", null);
        var b = ConfiguracaoPixManual.Criar(TipoChavePixManual.Email, "segunda@example.test", "Loja", null);
        Assert.Equal(StatusSalvarPix.Sucesso, (await pix.SalvarAsync(a, 0, autor, Guid.NewGuid(), true, default)).Status);

        var itens = new[] { new ItemCompraInput(variante.ProdutoId, variante.Id, 1) };
        var chave = Guid.NewGuid();
        var barreira = new PausaSnapshotPix();
        await using var checkout = AbrirContexto(barreira);
        var pedidoEmCurso = Compra(checkout).ExecuteAsync(new(anterior.ClienteId, chave, itens));
        await barreira.Gravado.Task.WaitAsync(TimeSpan.FromSeconds(30));
        Task<ResultadoSalvarPix> atualizar;
        try
        {
            atualizar = pix.SalvarAsync(b, 1, autor, Guid.NewGuid(), true, default);
            // Outra conexão confirma que a alteração não pode tomar o lock exclusivo enquanto o
            // pedido e o snapshot estão gravados, mas a transação de checkout ainda não terminou.
            await using var sondagem = banco.CreateVerificationContext();
            Assert.Equal(0, await sondagem.Database.SqlQueryRaw<int>("""
                SELECT APPLOCK_TEST(N'public', N'configuracao-pix', N'Exclusive', N'Session') AS [Value]
                """).SingleAsync());
        }
        finally
        {
            barreira.Continuar.TrySetResult();
        }

        var criado = Assert.IsType<Pedido>((await pedidoEmCurso).Pedido);
        Assert.Equal(StatusSalvarPix.Sucesso, (await atualizar).Status);
        var consulta = await pix.ConsultarPedidoAsync(criado.Id, anterior.ClienteId, default);
        Assert.Equal("primeira@example.test", consulta!.Dados!.ChavePix);
        Assert.False(consulta.Legado);
        Assert.Equal(1L, await verificacao.Set<SnapshotPixPedido>().Where(s => s.PedidoId == criado.Id)
            .Select(s => s.Revisao).SingleAsync());
        var noMomentoSemPix = await pix.ConsultarPedidoAsync(anterior.Id, anterior.ClienteId, default);
        Assert.Null(noMomentoSemPix!.Dados);
        Assert.False(noMomentoSemPix.Legado);

        await using var falho = AbrirContexto(new PausaSnapshotPix(falhar: true));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Compra(falho).ExecuteAsync(new(anterior.ClienteId, Guid.NewGuid(), itens)));
        Assert.Equal(2, await verificacao.Pedidos.CountAsync());
        Assert.Equal(2, await verificacao.Set<SnapshotPixPedido>().CountAsync());
        Assert.Equal(2, await verificacao.ReservasProdutoAcabado.CountAsync());

        XaquiamuniDbContext AbrirContexto(SaveChangesInterceptor interceptor) =>
            new(new DbContextOptionsBuilder<XaquiamuniDbContext>()
                .UseSqlServer(verificacao.Database.GetConnectionString())
                .AddInterceptors(interceptor).Options);
    }

    [SqlServerIntegrationFact]
    public async Task ApiPix_MigracaoPreservaConfiguracaoExistenteSemPreencherSnapshotLegado()
    {
        await using var banco = await SqlServerTestDatabase.CreateAsync();
        await using var db = banco.CreateVerificationContext();
        var (pedido, _) = await CriarPedidoParaPagamentoAsync(db);
        await db.Database.MigrateAsync("20261125044134_AdicionarEmailComercial");
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO [ConfiguracoesPixManual] ([Id], [TipoChave], [ChavePix], [NomeRecebedor], [InstrucoesAdicionais])
            VALUES (1, 2, N'legado@example.test', N'Loja anterior', N'Instruções preservadas')
            """);
        await db.Database.MigrateAsync();

        var pix = new EfPagamentoPixManual(db);
        var config = await pix.ConsultarConfiguracaoAsync(default);
        Assert.Equal(1, config.Revisao);
        Assert.Equal("legado@example.test", config.Dados!.ChavePix);
        Assert.Equal("Instruções preservadas", config.Dados.InstrucoesAdicionais);
        Assert.Empty(await db.Set<SnapshotPixPedido>().ToListAsync());
        var legado = await pix.ConsultarPedidoAsync(pedido.Id, pedido.ClienteId, default);
        Assert.True(legado!.Legado);
        Assert.Equal(pedido.TotalCentavos, legado.ValorCentavos);
        Assert.Equal(pedido.PrazoPagamentoEmUtc, legado.PrazoPagamentoEmUtc);
        Assert.Single(await db.ReservasProdutoAcabado.ToListAsync());
    }

    private sealed class PausaSnapshotPix(bool falhar = false) : SaveChangesInterceptor
    {
        public TaskCompletionSource Gravado { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Continuar { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            if (!eventData.Context!.ChangeTracker.Entries<SnapshotPixPedido>().Any() || !Gravado.TrySetResult())
                return result;
            if (falhar) throw new InvalidOperationException("Falha sintética depois de persistir o snapshot.");
            await Continuar.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            return result;
        }
    }

    private static async Task<HttpResponseMessage> SalvarPixAsync(HttpClient client, object dados, string? token = null, Guid? chave = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, PagamentoPixManualEndpoints.AdminPath)
        {
            Content = JsonContent.Create(dados, dados.GetType()),
        };
        request.Headers.Add("Idempotency-Key", (chave ?? Guid.NewGuid()).ToString("D"));
        if (token is not null) request.Headers.Add(AntiforgeryConfiguration.HeaderName, token);
        return await client.SendAsync(request);
    }
}
