using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xaquiamuni.Aplicacao.Estoque;
using Xaquiamuni.Aplicacao.Pedidos;
using Xaquiamuni.Dominio.Entidades;
using Xaquiamuni.Dominio.Estoque;
using Xaquiamuni.Infraestrutura.Identidade;
using Xaquiamuni.Infraestrutura.Persistencia;

namespace Xaquiamuni.Tests.Integration;

public sealed partial class CadeiaFabricacaoSqlServerIntegrationTests
{
    [SqlServerIntegrationFact]
    public async Task EntregaPostal_PostaUmaVezAuditaRastreamentoEConciliaFinanceiro()
    {
        await using var ambiente = await Ambiente.CriarAsync(municipiosPostais: MunicipiosPostaisTeste);
        using var admin = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Administrador);
        using var cliente = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Cliente);
        using var outro = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Cliente);
        await using var db = ambiente.Banco.CreateVerificationContext();
        var (produto, variante, _) = await ReservarProdutoAcabadoSqlServerIntegrationTests.CriarProdutoCompravelAsync(db);
        await admin.PostAsync<JsonElement>($"/api/admin/estoque/produtos-acabados/variantes/{variante.Id:D}/entradas",
            new { tipo = "EntradaManual", quantidade = 3, condicaoDestino = "Vendavel", semControleValidade = true, motivo = "Teste" },
            HttpStatusCode.Created, Guid.NewGuid());
        await EscreverAtendimentoAsync(admin, HttpMethod.Put, "/correios-manual",
            new SalvarCorreiosManual(true, 48, 0), HttpStatusCode.OK);
        var cotacao = await PrepararCotacaoConversaoAsync(admin, cliente, produto.Id, variante.Id);
        var revisao = await cliente.PostAsync<JsonElement>(RevisarPostal(cotacao), new { }, HttpStatusCode.OK);
        var pedido = await cliente.PostAsync<JsonElement>("/api/pedidos",
            new { conversaoCorreiosId = revisao.GetProperty("conversaoCorreiosId").GetString() },
            HttpStatusCode.Created, Guid.NewGuid());
        var id = pedido.GetProperty("id").GetGuid();
        var rota = $"/api/admin/pedidos/{id:D}";
        var rotaHistorico = rota + "/historico-rastreamento";
        var rotaCliente = $"/api/pedidos/{id:D}";
        var corpo = new { confirmado = true, servicoPostalUtilizado = "SEDEX" };
        var receber = new { confirmado = true };

        async Task<string> VersaoAsync(string? caminho = null)
        {
            using var detalhe = await admin.Http.GetAsync(caminho ?? rota);
            detalhe.EnsureSuccessStatusCode();
            return detalhe.Headers.ETag!.Tag;
        }
        async Task VerificarFinanceiroAsync()
        {
            var evento = Assert.Single((await admin.Http.GetFromJsonAsync<JsonElement>(Movimentacoes))
                .GetProperty("itens").EnumerateArray());
            Assert.Equal("3698", evento.GetProperty("valorCentavos").GetString());
            var resumo = await admin.Http.GetFromJsonAsync<JsonElement>("/api/admin/financeiro/resumo");
            Assert.Equal("3698", resumo.GetProperty("fluxoPeriodo").GetProperty("entradasRecebidasCentavos").GetString());
        }

        var aguardando = await VersaoAsync();
        using var proibidoHistorico = await cliente.Http.GetAsync(rotaHistorico);
        Assert.Equal(HttpStatusCode.Forbidden, proibidoHistorico.StatusCode);
        using var pedidoInexistente = await admin.Http.GetAsync($"/api/admin/pedidos/{Guid.NewGuid():D}/historico-rastreamento");
        Assert.Equal(HttpStatusCode.NotFound, pedidoInexistente.StatusCode);
        Assert.Empty((await admin.Http.GetFromJsonAsync<JsonElement>(rotaHistorico)).EnumerateArray());
        Assert.Equal(aguardando, await VersaoAsync());
        using (var semPagamento = await EscreverPostalAsync(admin, rota + "/registrar-postagem", corpo, aguardando))
            await ConferirErroPostalAsync(semPagamento, 409, "estado_incompativel");
        using (var semCsrf = await EscreverPostalAsync(admin, rota + "/registrar-postagem", corpo, aguardando, csrf: false))
            Assert.Equal(HttpStatusCode.BadRequest, semCsrf.StatusCode);
        using (var proibido = await EscreverPostalAsync(cliente, rota + "/registrar-postagem", corpo, aguardando))
            Assert.Equal(HttpStatusCode.Forbidden, proibido.StatusCode);
        using (var alheio = await outro.Http.GetAsync(rotaCliente))
            Assert.Equal(HttpStatusCode.NotFound, alheio.StatusCode);
        using (var sair = await outro.EnviarAsync("/api/auth/logout", new { }))
            Assert.Equal(HttpStatusCode.NoContent, sair.StatusCode);
        using (var anonimo = await outro.Http.GetAsync(rotaCliente))
            Assert.Equal(HttpStatusCode.Unauthorized, anonimo.StatusCode);
        using var historicoAnonimo = await outro.Http.GetAsync(rotaHistorico);
        Assert.Equal(HttpStatusCode.Unauthorized, historicoAnonimo.StatusCode);
        var bytes = PdfComprovante();
        using (var comprovante = await EnviarComprovanteAsync(cliente, rotaCliente + "/comprovante-pix",
            bytes, "teste.pdf", "application/pdf", await TokenComprovanteAsync(cliente)))
            Assert.Equal(HttpStatusCode.Created, comprovante.StatusCode);
        await admin.PostAsync<JsonElement>(rota + "/confirmar-pagamento", new { recebimentoConferido = true }, HttpStatusCode.OK);
        using (var semPreparacao = await EscreverPostalAsync(admin, rota + "/registrar-postagem", corpo, await VersaoAsync()))
            await ConferirErroPostalAsync(semPreparacao, 409, "estado_incompativel");
        await admin.PostAsync<JsonElement>(rota + "/iniciar-preparacao", new { }, HttpStatusCode.OK);
        var preparada = await VersaoAsync();

        var local = await cliente.PostAsync<JsonElement>("/api/pedidos",
            new { itens = new[] { new { produtoId = produto.Id, produtoVarianteId = variante.Id, quantidade = 1 } } },
            HttpStatusCode.Created, Guid.NewGuid());
        var rotaLocal = $"/api/admin/pedidos/{local.GetProperty("id").GetGuid():D}";
        using (var outraModalidade = await EscreverPostalAsync(admin, rotaLocal + "/registrar-postagem", corpo,
            await VersaoAsync(rotaLocal)))
            await ConferirErroPostalAsync(outraModalidade, 409, "estado_incompativel");
        using (var antiga = await EscreverPostalAsync(admin, rota + "/registrar-postagem", corpo, aguardando))
            await ConferirErroPostalAsync(antiga, 409, "versao_desatualizada");
        using (var semConferencia = await EscreverPostalAsync(admin, rota + "/registrar-postagem",
            corpo with { confirmado = false }, preparada))
            Assert.Equal(HttpStatusCode.BadRequest, semConferencia.StatusCode);

        var antes = await db.Pedidos.AsNoTracking().SingleAsync(x => x.Id == id);
        var falha = new FalharAposBaixaPostal();
        var options = new DbContextOptionsBuilder<XaquiamuniDbContext>().UseSqlServer(db.Database.GetConnectionString())
            .AddInterceptors(falha).Options;
        await using (var dbFalha = new XaquiamuniDbContext(options))
        {
            IEntregaPostal repo = new EfEstoqueProdutoAcabadoRepository(dbFalha, TimeProvider.System,
                ambiente.Host.Services.GetRequiredService<ICalendarioOperacionalEstoque>());
            await Assert.ThrowsAsync<InvalidOperationException>(() => repo.ExecutarAsync(
                new(id, antes.PagamentoConfirmadoPorId!.Value, Guid.NewGuid(), antes.Versao, EtapaEntregaPostal.Postar, "SEDEX"), default));
        }
        Assert.True(falha.Executada);
        Assert.Equal(SituacaoPedido.EmPreparacao, await db.Pedidos.Where(x => x.Id == id).Select(x => x.Situacao).SingleAsync());
        Assert.Equal(EstadoReservaProdutoAcabado.Confirmada, await db.ReservasProdutoAcabado.Where(x => x.PedidoId == id)
            .Select(x => x.Estado).SingleAsync());
        Assert.Equal(3, (await db.SaldosProdutoAcabado.AsNoTracking().SingleAsync()).QuantidadeFisica);
        Assert.Empty(await db.Set<OperacaoEntregaPostal>().ToListAsync());
        Assert.Single(await db.MovimentosProdutoAcabado.ToListAsync());

        var chave = Guid.NewGuid();
        var concorrentes = await Task.WhenAll(
            EscreverPostalAsync(admin, rota + "/registrar-postagem", corpo, preparada, chave),
            EscreverPostalAsync(admin, rota + "/confirmar-entrega-postal", receber, preparada));
        using var postada = concorrentes[0];
        using var entregaAdiantada = concorrentes[1];
        Assert.Equal(HttpStatusCode.OK, postada.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, entregaAdiantada.StatusCode);
        var versaoPostada = await VersaoAsync();
        using (var duplicada = await EscreverPostalAsync(admin, rota + "/registrar-postagem", corpo, versaoPostada))
            await ConferirErroPostalAsync(duplicada, 409, "estado_incompativel");
        using (var divergente = await EscreverPostalAsync(admin, rota + "/registrar-postagem",
            corpo with { servicoPostalUtilizado = "PAC" }, preparada, chave))
            await ConferirErroPostalAsync(divergente, 409, "chave_em_uso");
        await VerificarFinanceiroAsync();
        await admin.PostAsync<JsonElement>(rota + "/cancelar", new { motivo = "Após postagem" }, HttpStatusCode.Conflict);
        var detalhe = await cliente.Http.GetFromJsonAsync<JsonElement>(rotaCliente);
        Assert.Equal("Postado", detalhe.GetProperty("situacao").GetString());
        Assert.Equal("SEDEX", detalhe.GetProperty("postagem").GetProperty("servicoPostalUtilizado").GetString());
        Assert.Equal("PAC", detalhe.GetProperty("entrega").GetProperty("propostaPostal").GetProperty("servico").GetString());
        Assert.Equal("1300", detalhe.GetProperty("freteCentavos").GetString());
        Assert.Equal(JsonValueKind.Null, detalhe.GetProperty("postagem").GetProperty("codigoRastreamento").ValueKind);

        Assert.Empty((await admin.Http.GetFromJsonAsync<JsonElement>(rotaHistorico)).EnumerateArray());
        async Task<HttpResponseMessage> CorrigirAsync(string codigo, string versao, Guid? chaveCorrecao = null)
        {
            using var request = new HttpRequestMessage(HttpMethod.Put, rota + "/rastreamento-postal")
            {
                Content = JsonContent.Create(new { codigoRastreamento = codigo }),
            };
            request.Headers.Add("X-XSRF-TOKEN", await TokenComprovanteAsync(admin));
            request.Headers.Add("If-Match", versao);
            request.Headers.Add("Idempotency-Key", (chaveCorrecao ?? Guid.NewGuid()).ToString("D"));
            return await admin.Http.SendAsync(request);
        }
        using (var r = await CorrigirAsync("inválido", versaoPostada)) Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        var chaveCorrecao = Guid.NewGuid();
        using (var r = await CorrigirAsync("  aa 123 456 789 br ", versaoPostada, chaveCorrecao))
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        using (var r = await CorrigirAsync("  aa 123 456 789 br ", versaoPostada, chaveCorrecao))
            Assert.True((await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("repetida").GetBoolean());
        var primeira = Assert.Single((await admin.Http.GetFromJsonAsync<JsonElement>(rotaHistorico)).EnumerateArray());
        Assert.Equal(JsonValueKind.Null, primeira.GetProperty("codigoAnterior").ValueKind);
        using (var r = await CorrigirAsync("AB123456789BR", await VersaoAsync())) Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var historico = await db.Set<OperacaoEntregaPostal>().AsNoTracking()
            .Where(x => x.Etapa == EtapaEntregaPostal.CorrigirRastreamento).ToListAsync();
        Assert.Equal(2, historico.Count);
        Assert.Contains(historico, x => x.CodigoAnterior == null && x.CodigoNovo == "AA123456789BR");
        Assert.Contains(historico, x => x.CodigoAnterior == "AA123456789BR" && x.CodigoNovo == "AB123456789BR");

        var chaveReceber = Guid.NewGuid();
        var versaoReceber = await VersaoAsync();
        using var entregue = await EscreverPostalAsync(admin, rota + "/confirmar-entrega-postal", receber, versaoReceber, chaveReceber);
        Assert.Equal(HttpStatusCode.OK, entregue.StatusCode);
        using var replay = await EscreverPostalAsync(admin, rota + "/confirmar-entrega-postal", receber, versaoReceber, chaveReceber);
        Assert.True((await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("repetida").GetBoolean());
        using var replayPostagem = await EscreverPostalAsync(admin, rota + "/registrar-postagem", corpo, preparada, chave);
        Assert.Equal("Postado", (await replayPostagem.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("situacao").GetString());
        using (var nova = await EscreverPostalAsync(admin, rota + "/confirmar-entrega-postal", receber, await VersaoAsync()))
            await ConferirErroPostalAsync(nova, 409, "estado_incompativel");
        detalhe = await cliente.Http.GetFromJsonAsync<JsonElement>(rotaCliente);
        Assert.Equal("Entregue", detalhe.GetProperty("situacao").GetString());
        Assert.NotEqual(JsonValueKind.Null, detalhe.GetProperty("postagem").GetProperty("entregueEmUtc").ValueKind);
        Assert.Equal("AB123456789BR", detalhe.GetProperty("postagem").GetProperty("codigoRastreamento").GetString());
        Assert.Equal("https://rastreamento.correios.com.br/app/index.php",
            detalhe.GetProperty("postagem").GetProperty("urlConsultaRastreamento").GetString());
        Assert.Equal(1, (await db.SaldosProdutoAcabado.AsNoTracking().SingleAsync()).QuantidadeFisica);
        var movimento = await db.MovimentosProdutoAcabado.SingleAsync(x => x.ReservaProdutoAcabadoId != null);
        Assert.Equal("Saída para postagem postal", movimento.Motivo);
        Assert.Equal(bytes, await cliente.Http.GetByteArrayAsync(rotaCliente + "/comprovante-pix/conteudo"));
        await VerificarFinanceiroAsync();

        var versaoFinal = await VersaoAsync();
        using var respostaHistorico = await admin.Http.GetAsync(rotaHistorico);
        respostaHistorico.EnsureSuccessStatusCode();
        Assert.True(respostaHistorico.Headers.CacheControl?.Private);
        Assert.True(respostaHistorico.Headers.CacheControl?.NoStore);
        Assert.Null(respostaHistorico.Headers.ETag);
        var eventos = (await respostaHistorico.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().ToArray();
        Assert.Equal(new[] { "AB123456789BR", "AA123456789BR" }, eventos.Select(x => x.GetProperty("codigoNovo").GetString()));
        Assert.All(eventos, evento =>
        {
            Assert.Equal(new[] { "administradorId", "codigoAnterior", "codigoNovo", "realizadaEmUtc" },
                evento.EnumerateObject().Select(x => x.Name).Order(StringComparer.Ordinal));
            Assert.Equal(TimeSpan.Zero, evento.GetProperty("realizadaEmUtc").GetDateTimeOffset().Offset);
            Assert.NotEqual(Guid.Empty, evento.GetProperty("administradorId").GetGuid());
        });
        Assert.Equal(versaoFinal, await VersaoAsync());
        Assert.False(detalhe.TryGetProperty("historicoRastreamento", out _));

        var autorIndisponivel = Guid.NewGuid();
        var instante = eventos[0].GetProperty("realizadaEmUtc").GetDateTimeOffset().AddMinutes(1);
        db.Set<OperacaoEntregaPostal>().AddRange(
            new OperacaoEntregaPostal(autorIndisponivel, new Guid("00000000-0000-0000-0000-000000000002"),
                id, new string('b', 64), EtapaEntregaPostal.CorrigirRastreamento, instante,
                SituacaoPedido.Entregue, "AB123456789BR", "AD123456789BR"),
            new OperacaoEntregaPostal(autorIndisponivel, new Guid("00000000-0000-0000-0000-000000000001"),
                id, new string('a', 64), EtapaEntregaPostal.CorrigirRastreamento, instante,
                SituacaoPedido.Entregue, "AD123456789BR", "AC123456789BR"));
        await db.SaveChangesAsync();
        var comEmpate = (await admin.Http.GetFromJsonAsync<JsonElement>(rotaHistorico)).EnumerateArray().Take(2).ToArray();
        Assert.Equal(new[] { "AC123456789BR", "AD123456789BR" }, comEmpate.Select(x => x.GetProperty("codigoNovo").GetString()));
        Assert.All(comEmpate, evento => Assert.Equal(autorIndisponivel, evento.GetProperty("administradorId").GetGuid()));
    }

    private sealed class FalharAposBaixaPostal : SaveChangesInterceptor
    {
        public bool Executada { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var db = eventData.Context!;
            if (db.ChangeTracker.Entries<Pedido>().Any(x => x.Entity.Situacao == SituacaoPedido.Postado))
            {
                Assert.NotNull(db.Database.CurrentTransaction);
                Assert.Equal(EntityState.Unchanged, Assert.Single(db.ChangeTracker.Entries<MovimentoProdutoAcabado>()).State);
                Executada = true;
                throw new InvalidOperationException("Falha de teste após a baixa postal.");
            }
            return ValueTask.FromResult(result);
        }
    }
}
