using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xaquiamuni.Api.Pedidos;
using Xaquiamuni.Aplicacao.Estoque;
using Xaquiamuni.Aplicacao.Pedidos;
using Xaquiamuni.Dominio.Entidades;
using Xaquiamuni.Infraestrutura.Identidade;
using Xaquiamuni.Infraestrutura.Persistencia;

namespace Xaquiamuni.Tests.Integration;

public sealed partial class CadeiaFabricacaoSqlServerIntegrationTests
{
    private static string RevisarPostal(Guid id) => $"/api/checkout/cotacoes-correios/{id:D}/revisar";

    [SqlServerIntegrationFact]
    public async Task ConversaoCorreios_RespeitaEstadoValidadeECicloPrePagamento()
    {
        var relogio = new RelogioPostalTeste();
        var relogioSql = new RelogioJornadaCompra();
        relogioSql.Definir(relogio.Agora);
        await using var ambiente = await Ambiente.CriarAsync(relogio: relogio, relogioSql: relogioSql,
            municipiosPostais: MunicipiosPostaisTeste);
        using var admin = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Administrador);
        using var cliente = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Cliente);
        await using var db = ambiente.Banco.CreateVerificationContext();
        var (produto, variante, _) = await ReservarProdutoAcabadoSqlServerIntegrationTests.CriarProdutoCompravelAsync(db);
        await admin.PostAsync<JsonElement>($"/api/admin/estoque/produtos-acabados/variantes/{variante.Id:D}/entradas",
            new { tipo = "EntradaManual", quantidade = 2, condicaoDestino = "Vendavel", semControleValidade = true, motivo = "Teste" },
            HttpStatusCode.Created, Guid.NewGuid());
        await EscreverAtendimentoAsync(admin, HttpMethod.Put, "/correios-manual",
            new SalvarCorreiosManual(true, 1, 0), HttpStatusCode.OK);

        async Task<string> RevisarAsync(Guid id) => (await cliente.PostAsync<JsonElement>(RevisarPostal(id), new { }, HttpStatusCode.OK))
            .GetProperty("conversaoCorreiosId").GetString()!;
        async Task RejeitarAsync(string token, string codigo)
        {
            using var rejeitada = await EscreverPostalAsync(cliente, "/api/pedidos", new { conversaoCorreiosId = token });
            await ConferirErroPostalAsync(rejeitada, 409, codigo);
        }

        var cancelada = await PrepararCotacaoConversaoAsync(admin, cliente, produto.Id, variante.Id, 1);
        var tokenCancelada = await RevisarAsync(cancelada);
        using (var cancelamento = await EscreverPostalAsync(cliente, $"{Postal}/{cancelada:D}/cancelar", new { }, "\"3\""))
            Assert.Equal(HttpStatusCode.OK, cancelamento.StatusCode);
        await RejeitarAsync(tokenCancelada, "cotacao_correios_nao_aceita");

        using var solicitada = await EscreverPostalAsync(cliente, Postal, new
        {
            itens = new[] { new { produtoId = produto.Id, produtoVarianteId = variante.Id, quantidade = 1 } },
            endereco = DestinoPostalTeste,
        });
        var recusada = (await solicitada.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        using (var recusa = await EscreverPostalAsync(admin, $"{PostalAdmin}/{recusada:D}/recusar", new { }, "\"1\""))
            Assert.Equal(HttpStatusCode.OK, recusa.StatusCode);
        using (var revisao = await EscreverPostalAsync(cliente, RevisarPostal(recusada), new { }))
            await ConferirErroPostalAsync(revisao, 409, "cotacao_correios_nao_aceita");

        var limite = relogio.Agora.AddMinutes(1);
        var vence = await PrepararCotacaoConversaoAsync(admin, cliente, produto.Id, variante.Id, 1, limite);
        var tokenVence = await RevisarAsync(vence);
        relogio.Agora = limite;
        await RejeitarAsync(tokenVence, "cotacao_correios_expirada");
        Assert.False(await db.Pedidos.AnyAsync());
        Assert.False(await db.ReservasProdutoAcabado.AnyAsync());

        async Task<Guid> ComprarAsync()
        {
            var id = await PrepararCotacaoConversaoAsync(admin, cliente, produto.Id, variante.Id, 1);
            var token = await RevisarAsync(id);
            var pedido = await cliente.PostAsync<JsonElement>("/api/pedidos", new { conversaoCorreiosId = token },
                HttpStatusCode.Created, Guid.NewGuid());
            return pedido.GetProperty("id").GetGuid();
        }
        var pedidoCancelado = await ComprarAsync();
        await admin.PostAsync<JsonElement>($"/api/admin/pedidos/{pedidoCancelado:D}/cancelar", new { motivo = "Teste" },
            HttpStatusCode.OK, Guid.NewGuid());
        var pedidoVencido = await ComprarAsync();
        var prazo = await db.Pedidos.Where(x => x.Id == pedidoVencido).Select(x => x.PrazoPagamentoEmUtc).SingleAsync();
        relogioSql.Definir(prazo);
        await using var scope = ambiente.Host.Services.CreateAsyncScope();
        Assert.Equal(EncerrarPedidoStatus.Expirado,
            await scope.ServiceProvider.GetRequiredService<EncerrarPedidoCompra>().ExpirarAsync(pedidoVencido));
        var reservas = await db.ReservasProdutoAcabado.AsNoTracking().ToDictionaryAsync(x => x.PedidoId);
        Assert.Equal(Xaquiamuni.Dominio.Estoque.EstadoReservaProdutoAcabado.Liberada, reservas[pedidoCancelado].Estado);
        Assert.Equal(Xaquiamuni.Dominio.Estoque.EstadoReservaProdutoAcabado.Expirada, reservas[pedidoVencido].Estado);
        Assert.Single(await db.MovimentosProdutoAcabado.ToListAsync());
    }

    [SqlServerIntegrationFact]
    public async Task ConversaoCorreios_ReconfirmaPrecosFazRollbackConciliaPixEReproduzAposVencimento()
    {
        var relogio = new RelogioPostalTeste();
        var relogioSql = new RelogioJornadaCompra();
        relogioSql.Definir(relogio.Agora);
        await using var ambiente = await Ambiente.CriarAsync(relogio: relogio, relogioSql: relogioSql,
            promocoesComerciais: true, municipiosPostais: MunicipiosPostaisTeste);
        using var admin = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Administrador);
        using var cliente = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Cliente);
        using var outro = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Cliente);
        await using var db = ambiente.Banco.CreateVerificationContext();
        var (produto, variante, _) = await ReservarProdutoAcabadoSqlServerIntegrationTests.CriarProdutoCompravelAsync(db);
        await admin.PostAsync<JsonElement>($"/api/admin/estoque/produtos-acabados/variantes/{variante.Id:D}/entradas",
            new { tipo = "EntradaManual", quantidade = 2, condicaoDestino = "Vendavel", semControleValidade = true, motivo = "Teste" },
            HttpStatusCode.Created, Guid.NewGuid());
        await EscreverAtendimentoAsync(admin, HttpMethod.Put, "/correios-manual",
            new SalvarCorreiosManual(true, 1, 0), HttpStatusCode.OK);

        var id = await PrepararCotacaoConversaoAsync(admin, cliente, produto.Id, variante.Id);
        var rota = RevisarPostal(id);
        using (var alheia = await EscreverPostalAsync(outro, rota, new { }))
            Assert.Equal(HttpStatusCode.NotFound, alheia.StatusCode);
        using (var semCsrf = await EscreverPostalAsync(cliente, rota, new { }, csrf: false))
            Assert.Equal(HttpStatusCode.BadRequest, semCsrf.StatusCode);
        var primeira = await cliente.PostAsync<JsonElement>(rota, new { }, HttpStatusCode.OK);
        Assert.Equal("2398", primeira.GetProperty("subtotalProdutosCentavos").GetString());
        Assert.Equal("1300", primeira.GetProperty("freteCentavos").GetString());
        Assert.Equal("3698", primeira.GetProperty("totalCentavos").GetString());
        var anterior = primeira.GetProperty("conversaoCorreiosId").GetString()!;
        using (var alheia = await EscreverPostalAsync(outro, "/api/pedidos", new { conversaoCorreiosId = anterior }))
            Assert.Equal(HttpStatusCode.NotFound, alheia.StatusCode);
        using (var misturada = await EscreverPostalAsync(cliente, "/api/pedidos",
            new { conversaoCorreiosId = anterior, ofertaEntregaId = "local" }))
            Assert.Equal(HttpStatusCode.BadRequest, misturada.StatusCode);

        var alterada = await db.ProdutoVariantes.SingleAsync(x => x.Id == variante.Id);
        alterada.AtualizarDados(alterada.Identificacao, alterada.UnidadeVenda, 12.49m);
        await db.SaveChangesAsync();
        async Task<string> ReconfirmarAsync(string token, string total)
        {
            var conflito = await cliente.PostAsync<JsonElement>("/api/pedidos", new { conversaoCorreiosId = token },
                HttpStatusCode.Conflict, Guid.NewGuid());
            Assert.Equal("preco_alterado", conflito.GetProperty("codigo").GetString());
            var revisao = conflito.GetProperty("revisaoPostal");
            Assert.Equal(total, revisao.GetProperty("totalCentavos").GetString());
            Assert.Equal("1300", revisao.GetProperty("freteCentavos").GetString());
            Assert.False(await db.Pedidos.AnyAsync());
            Assert.False(await db.ReservasProdutoAcabado.AnyAsync());
            return revisao.GetProperty("conversaoCorreiosId").GetString()!;
        }
        var regular = await ReconfirmarAsync(anterior, "3798");
        await admin.PostAsync<JsonElement>("/api/admin/promocoes", new
        {
            configuracao = new { produtoId = produto.Id, tipo = "PrecoFixo", escopo = "TodasVariantes", valor = "900" },
            ativa = true,
        }, HttpStatusCode.Created, Guid.NewGuid());
        var token = await ReconfirmarAsync(regular, "3100");

        var carrinhoDiferente = new[] { new { produtoId = produto.Id, produtoVarianteId = variante.Id,
            quantidade = 1, precoAceitoCentavos = "900" } };
        using (var comItens = await EscreverPostalAsync(cliente, "/api/pedidos",
            new { conversaoCorreiosId = token, itens = carrinhoDiferente }))
            Assert.Equal(HttpStatusCode.BadRequest, comItens.StatusCode);

        var pedidoLocal = await outro.PostAsync<JsonElement>("/api/pedidos", new { itens = carrinhoDiferente },
            HttpStatusCode.Created, Guid.NewGuid());
        var localId = pedidoLocal.GetProperty("id").GetGuid();
        var semSaldo = await cliente.PostAsync<JsonElement>("/api/pedidos", new { conversaoCorreiosId = token },
            HttpStatusCode.Conflict, Guid.NewGuid());
        Assert.Equal("itens_indisponiveis", semSaldo.GetProperty("codigo").GetString());
        Assert.Equal(variante.Id, Assert.Single(semSaldo.GetProperty("variantesIndisponiveis").EnumerateArray()).GetGuid());
        var indisponivel = await cliente.PostAsync<JsonElement>(rota, new { }, HttpStatusCode.OK);
        Assert.Equal(JsonValueKind.Null, indisponivel.GetProperty("conversaoCorreiosId").ValueKind);
        Assert.False(Assert.Single(indisponivel.GetProperty("itens").EnumerateArray()).GetProperty("disponivel").GetBoolean());
        await admin.PostAsync<JsonElement>($"/api/admin/pedidos/{localId:D}/cancelar", new { motivo = "Liberar teste" },
            HttpStatusCode.OK, Guid.NewGuid());

        var sessao = await cliente.Http.GetFromJsonAsync<JsonElement>("/api/auth/me");
        var clienteId = sessao.GetProperty("userId").GetGuid();
        foreach (var aposReserva in new[] { false, true })
        {
            var falha = new FalharConversaoPostal(aposReserva);
            var options = new DbContextOptionsBuilder<XaquiamuniDbContext>().UseSqlServer(db.Database.GetConnectionString())
                .AddInterceptors(falha, relogioSql).Options;
            await using var contexto = new XaquiamuniDbContext(options);
            var services = ambiente.Host.Services;
            ICriarPedidoCompraRepository repositorio = new EfEstoqueProdutoAcabadoRepository(
                contexto, relogio, services.GetRequiredService<ICalendarioOperacionalEstoque>(),
                services.GetRequiredService<IOptions<PromocoesComerciaisOptions>>(),
                services.GetRequiredService<IDataProtectionProvider>());
            await Assert.ThrowsAsync<InvalidOperationException>(() => repositorio.CriarAsync(
                new(clienteId, Guid.NewGuid(), [], ConversaoCorreiosId: token), default));
            Assert.True(falha.Executada);
            Assert.Equal(1, await db.Pedidos.CountAsync());
            Assert.Equal(1, await db.ReservasProdutoAcabado.CountAsync());
            Assert.Equal(SituacaoCotacaoCorreios.Aceita,
                await db.Set<SolicitacaoCorreios>().Where(x => x.Id == id).Select(x => x.Situacao).SingleAsync());
        }

        var chaveA = Guid.NewGuid();
        var chaveB = Guid.NewGuid();
        var payload = new { conversaoCorreiosId = token };
        var csrf = await TokenComprovanteAsync(cliente);
        async Task<HttpResponseMessage> ConverterAsync(Guid chave)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/pedidos") { Content = JsonContent.Create(payload) };
            request.Headers.Add("X-XSRF-TOKEN", csrf);
            request.Headers.Add("Idempotency-Key", chave.ToString("D"));
            return await cliente.Http.SendAsync(request);
        }
        var corrida = await Task.WhenAll(ConverterAsync(chaveA), ConverterAsync(chaveB));
        using var a = corrida[0];
        using var b = corrida[1];
        var criada = Assert.Single(corrida, r => r.StatusCode == HttpStatusCode.Created);
        await ConferirErroPostalAsync(Assert.Single(corrida, r => r.StatusCode == HttpStatusCode.Conflict), 409, "cotacao_correios_convertida");
        var chaveOriginal = criada == a ? chaveA : chaveB;
        var pedido = await criada.Content.ReadFromJsonAsync<JsonElement>();
        var pedidoId = pedido.GetProperty("id").GetGuid();
        Assert.Equal("Correios", pedido.GetProperty("modalidadeEntrega").GetString());
        Assert.Equal("3100", pedido.GetProperty("totalCentavos").GetString());
        Assert.Equal("1800", pedido.GetProperty("subtotalProdutosCentavos").GetString());
        Assert.Equal(2, Assert.Single(pedido.GetProperty("itens").EnumerateArray()).GetProperty("quantidade").GetInt32());
        Assert.Equal("Rua de teste", pedido.GetProperty("entrega").GetProperty("destino").GetProperty("logradouro").GetString());
        var snapshot = pedido.GetProperty("entrega").GetProperty("propostaPostal");
        Assert.Equal("PAC", snapshot.GetProperty("servico").GetString());
        Assert.Equal(5, snapshot.GetProperty("prazoDias").GetInt32());
        Assert.Equal(id, snapshot.GetProperty("solicitacaoId").GetGuid());
        Assert.Equal(1, snapshot.GetProperty("revisao").GetInt32());
        Assert.Single(await db.Pedidos.AsNoTracking().Where(x => x.CotacaoCorreiosId == id).ToListAsync());
        Assert.Equal(2, await db.ReservasProdutoAcabado.Where(x => x.PedidoId == pedidoId).SumAsync(x => x.Quantidade));

        relogio.Agora = snapshot.GetProperty("validaAteUtc").GetDateTimeOffset();
        var replay = await cliente.PostAsync<JsonElement>("/api/pedidos", payload, HttpStatusCode.OK, chaveOriginal);
        Assert.True(JsonElement.DeepEquals(pedido, replay));
        var rotaCotacao = $"/api/cotacoes-correios/{id:D}";
        using var leitura = await cliente.Http.GetAsync(rotaCotacao);
        Assert.Equal(HttpStatusCode.OK, leitura.StatusCode);
        Assert.True(leitura.Headers.CacheControl?.NoStore);
        var convertida = await leitura.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ConvertidaEmPedido", convertida.GetProperty("resumo").GetProperty("situacao").GetString());
        Assert.Equal(replay.GetProperty("id").GetGuid(), convertida.GetProperty("pedidoConvertidoId").GetGuid());
        using var alheio = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Cliente);
        using var cotacaoAlheia = await alheio.Http.GetAsync(rotaCotacao);
        using var pedidoAlheio = await alheio.Http.GetAsync($"/api/pedidos/{pedidoId:D}");
        Assert.Equal(HttpStatusCode.NotFound, cotacaoAlheia.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, pedidoAlheio.StatusCode);
        using (var divergente = await EscreverPostalAsync(cliente, "/api/pedidos",
            new { conversaoCorreiosId = anterior }, chave: chaveOriginal))
            await ConferirErroPostalAsync(divergente, 409, "chave_em_uso");
        var caminho = $"/api/pedidos/{pedidoId:D}";
        using var configPix = new HttpRequestMessage(HttpMethod.Put, "/api/admin/configuracoes/pix-manual")
        {
            Content = JsonContent.Create(new SalvarPixManualRequest("Email", "loja@example.test", "Loja teste", null)),
        };
        configPix.Headers.Add("X-XSRF-TOKEN", await TokenComprovanteAsync(admin));
        using var pixSalvo = await admin.Http.SendAsync(configPix);
        Assert.Equal(HttpStatusCode.OK, pixSalvo.StatusCode);
        Assert.Equal("3100", (await cliente.Http.GetFromJsonAsync<JsonElement>(caminho + "/pagamento")).GetProperty("valorCentavos").GetString());
        using var comprovante = await EnviarComprovanteAsync(cliente, caminho + "/comprovante-pix",
            PdfComprovante(), "teste.pdf", "application/pdf", await TokenComprovanteAsync(cliente));
        Assert.Equal(HttpStatusCode.Created, comprovante.StatusCode);
        Assert.Equal(SituacaoPedido.AguardandoPagamento, await db.Pedidos.Where(x => x.Id == pedidoId).Select(x => x.Situacao).SingleAsync());
        Assert.Empty((await admin.Http.GetFromJsonAsync<JsonElement>(Movimentacoes)).GetProperty("itens").EnumerateArray());
        var adminPedido = $"/api/admin/pedidos/{pedidoId:D}";
        for (var i = 0; i < 2; i++)
            await admin.PostAsync<JsonElement>(adminPedido + "/confirmar-pagamento", new { recebimentoConferido = true },
                HttpStatusCode.OK, Guid.NewGuid());
        var evento = Assert.Single((await admin.Http.GetFromJsonAsync<JsonElement>(Movimentacoes)).GetProperty("itens").EnumerateArray());
        Assert.Equal("3100", evento.GetProperty("valorCentavos").GetString());
        await admin.PostAsync<JsonElement>(adminPedido + "/iniciar-preparacao", new { }, HttpStatusCode.OK, Guid.NewGuid());
        await admin.PostAsync<JsonElement>(adminPedido + "/liberar-retirada", new { }, HttpStatusCode.Conflict, Guid.NewGuid());
        await admin.PostAsync<JsonElement>(adminPedido + "/confirmar-entrega", new { entregaPresencialConfirmada = true },
            HttpStatusCode.Conflict, Guid.NewGuid());
        using var detalhe = await admin.Http.GetAsync(adminPedido);
        using var propria = await EscreverPostalAsync(admin, adminPedido + "/iniciar-entrega-propria",
            new { confirmado = true }, detalhe.Headers.ETag!.Tag);
        Assert.Equal(HttpStatusCode.Conflict, propria.StatusCode);
        Assert.Single(await db.MovimentosProdutoAcabado.ToListAsync());
    }

    private static async Task<Guid> PrepararCotacaoConversaoAsync(Sessao admin, Sessao cliente, Guid produto,
        Guid variante, int quantidade = 2, DateTimeOffset? validade = null)
    {
        using var solicitada = await EscreverPostalAsync(cliente, Postal,
            new { itens = new[] { new { produtoId = produto, produtoVarianteId = variante, quantidade } }, endereco = DestinoPostalTeste });
        Assert.Equal(HttpStatusCode.Created, solicitada.StatusCode);
        var id = (await solicitada.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        using (var naoAceita = await EscreverPostalAsync(cliente, RevisarPostal(id), new { }))
            await ConferirErroPostalAsync(naoAceita, 409, "cotacao_correios_nao_aceita");
        using var proposta = await EscreverPostalAsync(admin, $"{PostalAdmin}/{id:D}/propostas",
            new PublicarPropostaCorreios("PAC", 1300, 5, validade, 400, null), "\"1\"");
        Assert.Equal(HttpStatusCode.OK, proposta.StatusCode);
        using var aceita = await EscreverPostalAsync(cliente, $"{Postal}/{id:D}/aceitar", new { revisao = 1 }, "\"2\"");
        Assert.Equal(HttpStatusCode.OK, aceita.StatusCode);
        return id;
    }

    private sealed class FalharConversaoPostal(bool aposReserva) : SaveChangesInterceptor
    {
        public bool Executada { get; private set; }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            var db = eventData.Context!;
            if (db.ChangeTracker.Entries<Pedido>().Any(x => x.Entity.CotacaoCorreiosId != null)
                && db.ChangeTracker.Entries<Xaquiamuni.Dominio.Estoque.ReservaProdutoAcabado>().Any() == aposReserva)
            {
                Assert.NotNull(db.Database.CurrentTransaction);
                Executada = true;
                throw new InvalidOperationException("Falha de teste durante conversão postal.");
            }
            return ValueTask.FromResult(result);
        }
    }
}
