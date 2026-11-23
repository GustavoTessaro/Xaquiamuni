using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xaquiamuni.Api.Pedidos;
using Xaquiamuni.Aplicacao.Estoque;
using Xaquiamuni.Aplicacao.Pedidos;
using Xaquiamuni.Dominio.Entidades;
using Xaquiamuni.Dominio.Estoque;
using Xaquiamuni.Infraestrutura.Identidade;
using Xaquiamuni.Infraestrutura.Persistencia;

namespace Xaquiamuni.Tests.Integration;

public sealed partial class CadeiaFabricacaoSqlServerIntegrationTests
{
    private const string CotacoesEntrega = "/api/checkout/cotacoes-entrega";

    [SqlServerIntegrationFact]
    public async Task CheckoutEntrega_AlteracaoConcorrentePromocaoCancelamentoEExpiracao()
    {
        var relogio = new RelogioJornadaCompra();
        var agora = DateTimeOffset.UtcNow;
        relogio.Definir(agora);
        await using var ambiente = await Ambiente.CriarAsync(relogioSql: relogio, promocoesComerciais: true,
            municipiosPorCep: new Dictionary<string, string> { ["95200000"] = "4322509" });
        using var admin = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Administrador);
        using var cliente = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Cliente);
        await using var db = ambiente.Banco.CreateVerificationContext();
        var (produto, variante, _) = await ReservarProdutoAcabadoSqlServerIntegrationTests.CriarProdutoCompravelAsync(db);
        await admin.PostAsync<JsonElement>($"/api/admin/estoque/produtos-acabados/variantes/{variante.Id:D}/entradas",
            new { tipo = "EntradaManual", quantidade = 8, condicaoDestino = "Vendavel", semControleValidade = true, motivo = "Teste" },
            HttpStatusCode.Created, Guid.NewGuid());
        var cidade = new SalvarCidade("4322509", "Vacaria", "RS", 1300, true);
        var criada = await EscreverAtendimentoAsync(admin, HttpMethod.Post, "/cidades", cidade, HttpStatusCode.Created);
        await EscreverAtendimentoAsync(admin, HttpMethod.Put, "/modalidades",
            new SalvarModalidades(true, true, false, null, null, 0), HttpStatusCode.OK);

        var itens = new[] { new { produtoId = produto.Id, produtoVarianteId = variante.Id, quantidade = 1 } };
        var clienteId = (await db.ClientePerfis.AsNoTracking().SingleAsync()).UserId;
        var endereco = ClienteEndereco.Criar(Guid.NewGuid(), clienteId, "95200000", "RS", "Vacaria",
            "Centro", "Rua de teste", "12", null, true);
        db.ClienteEnderecos.Add(endereco);
        await db.SaveChangesAsync();

        async Task<string> CotarAsync()
        {
            var cotacao = await cliente.PostAsync<JsonElement>(CotacoesEntrega, new { itens, usarEnderecoPadrao = true }, HttpStatusCode.OK);
            return Assert.Single(cotacao.GetProperty("ofertas").EnumerateArray(),
                item => item.GetProperty("modalidade").GetString() == "EntregaPropria").GetProperty("id").GetString()!;
        }
        async Task RecusarAsync(string oferta)
        {
            var conflito = await cliente.PostAsync<JsonElement>("/api/pedidos", new { itens, ofertaEntregaId = oferta },
                HttpStatusCode.Conflict, Guid.NewGuid());
            Assert.Equal("oferta_entrega_invalida", conflito.GetProperty("codigo").GetString());
        }

        var regular = await CotarAsync();
        var fim = agora.AddMinutes(4);
        await admin.PostAsync<JsonElement>("/api/admin/promocoes", new
        {
            configuracao = new { produtoId = produto.Id, tipo = "PrecoFixo", escopo = "TodasVariantes", valor = "900", fimUtc = fim },
            ativa = true,
        }, HttpStatusCode.Created, Guid.NewGuid());
        await RecusarAsync(regular);
        var promocional = await CotarAsync();
        relogio.Definir(fim);
        await RecusarAsync(promocional);

        var original = await CotarAsync();
        endereco.AtualizarDados(endereco.Cep, endereco.Uf, endereco.Cidade, endereco.Bairro,
            endereco.Logradouro, "16", null);
        await db.SaveChangesAsync();
        await RecusarAsync(original);
        var ofertaEntregaId = await CotarAsync();
        var rotaCidade = $"/cidades/{criada.GetProperty("id").GetGuid():D}";
        var chave = Guid.NewGuid();
        var tokenAdmin = await TokenComprovanteAsync(admin);
        var checkout = cliente.EnviarAsync("/api/pedidos", new { itens, ofertaEntregaId }, chave);
        var tarifa = EnviarAtendimentoAsync(admin, HttpMethod.Put, rotaCidade,
            cidade with { Revisao = 1, TarifaCentavos = 1400 }, Guid.NewGuid(), tokenAdmin);
        await Task.WhenAll(checkout, tarifa);
        using var alterada = await tarifa;
        using var resultado = await checkout;
        Assert.Equal(HttpStatusCode.OK, alterada.StatusCode);
        Assert.Contains(resultado.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.Conflict });
        if (resultado.StatusCode == HttpStatusCode.Created)
        {
            var congelado = await resultado.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("1300", congelado.GetProperty("freteCentavos").GetString());
            var id = congelado.GetProperty("id").GetGuid();
            await admin.PostAsync<JsonElement>($"/api/admin/pedidos/{id:D}/cancelar",
                new { motivo = "Cancelamento de teste" }, HttpStatusCode.OK, Guid.NewGuid());
        }
        else
        {
            Assert.Equal("oferta_entrega_invalida",
                (await resultado.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("codigo").GetString());
            Assert.Empty(await db.Pedidos.AsNoTracking().ToListAsync());
        }

        var nova = await CotarAsync();
        var cancelar = await cliente.PostAsync<JsonElement>("/api/pedidos", new { itens, ofertaEntregaId = nova },
            HttpStatusCode.Created, Guid.NewGuid());
        var cancelarId = cancelar.GetProperty("id").GetGuid();
        await admin.PostAsync<JsonElement>($"/api/admin/pedidos/{cancelarId:D}/cancelar",
            new { motivo = "Cancelamento de teste" }, HttpStatusCode.OK, Guid.NewGuid());
        var pedido = await cliente.PostAsync<JsonElement>("/api/pedidos", new { itens, ofertaEntregaId = nova },
            HttpStatusCode.Created, Guid.NewGuid());
        Assert.Equal("1400", pedido.GetProperty("freteCentavos").GetString());
        Assert.Equal("16", pedido.GetProperty("entrega").GetProperty("destino").GetProperty("numero").GetString());
        var prazo = pedido.GetProperty("prazoPagamentoEmUtc").GetDateTimeOffset();
        relogio.Definir(prazo);
        await using var scope = ambiente.Host.Services.CreateAsyncScope();
        var expirar = scope.ServiceProvider.GetRequiredService<EncerrarPedidoCompra>();
        var pedidoId = pedido.GetProperty("id").GetGuid();
        Assert.Equal(EncerrarPedidoStatus.Expirado, await expirar.ExpirarAsync(pedidoId));
        var estados = await db.Pedidos.AsNoTracking().Select(x => x.Situacao).ToListAsync();
        Assert.All(estados, estado => Assert.Contains(estado, new[] { SituacaoPedido.Expirado, SituacaoPedido.Cancelado }));
        Assert.All(await db.ReservasProdutoAcabado.AsNoTracking().ToListAsync(), reserva =>
            Assert.Contains(reserva.Estado, new[] { EstadoReservaProdutoAcabado.Expirada, EstadoReservaProdutoAcabado.Liberada }));
        Assert.Equal(8, (await db.SaldosProdutoAcabado.AsNoTracking().SingleAsync()).QuantidadeFisica);
        Assert.Single(await db.MovimentosProdutoAcabado.AsNoTracking().ToListAsync());
    }

    [SqlServerIntegrationFact]
    public async Task CheckoutEntrega_ValidaOfertaReservaPixFinanceiroEReplay()
    {
        var relogio = new RelogioJornadaCompra();
        var agora = DateTimeOffset.UtcNow;
        relogio.Definir(agora);
        await using var ambiente = await Ambiente.CriarAsync(relogioSql: relogio,
            municipiosPorCep: new Dictionary<string, string> { ["95200000"] = "4322509", ["90000000"] = "4314902" });
        using var admin = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Administrador);
        using var cliente = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Cliente);
        using var outro = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Cliente);
        await using var db = ambiente.Banco.CreateVerificationContext();
        var (produto, variante, _) = await ReservarProdutoAcabadoSqlServerIntegrationTests.CriarProdutoCompravelAsync(db);
        await admin.PostAsync<JsonElement>($"/api/admin/estoque/produtos-acabados/variantes/{variante.Id:D}/entradas",
            new { tipo = "EntradaManual", quantidade = 12, condicaoDestino = "Vendavel", semControleValidade = true, motivo = "Teste" },
            HttpStatusCode.Created, Guid.NewGuid());

        var itens = new[] { new { produtoId = produto.Id, produtoVarianteId = variante.Id, quantidade = 2 } };
        var destino = new EnderecoCotacaoEntrega("95200-000", "rs", "Vacaria", "Centro", "Rua de teste", "12", null);
        async Task<JsonElement[]> CotarAsync(EnderecoCotacaoEntrega endereco) =>
            (await cliente.PostAsync<JsonElement>(CotacoesEntrega, new { itens, endereco }, HttpStatusCode.OK))
                .GetProperty("ofertas").EnumerateArray().ToArray();
        async Task RecusarAsync(Sessao sessao, string oferta, string codigo = "oferta_entrega_invalida", Guid? chave = null)
        {
            var erro = await sessao.PostAsync<JsonElement>("/api/pedidos", new { itens, ofertaEntregaId = oferta },
                HttpStatusCode.Conflict, chave ?? Guid.NewGuid());
            Assert.Equal(codigo, erro.GetProperty("codigo").GetString());
        }

        using (var negado = await admin.EnviarAsync(CotacoesEntrega, new { itens, endereco = destino }))
            Assert.Equal(HttpStatusCode.Forbidden, negado.StatusCode);
        using (var semCsrf = await cliente.Http.PostAsJsonAsync(CotacoesEntrega, new { itens, endereco = destino }))
            Assert.Equal(HttpStatusCode.BadRequest, semCsrf.StatusCode);

        var retirada = Assert.Single(await CotarAsync(destino));
        Assert.Equal("RetiradaLocal", retirada.GetProperty("modalidade").GetString());
        Assert.Equal("0", retirada.GetProperty("freteCentavos").GetString());
        var cidade = new SalvarCidade("4322509", "Vacaria", "RS", 1250, false);
        var criada = await EscreverAtendimentoAsync(admin, HttpMethod.Post, "/cidades", cidade, HttpStatusCode.Created);
        var rotaCidade = $"/cidades/{criada.GetProperty("id").GetGuid():D}";
        var config = new SalvarModalidades(true, true, false, "Rua da loja", "Balcão", 0);
        await EscreverAtendimentoAsync(admin, HttpMethod.Put, "/modalidades", config, HttpStatusCode.OK);
        Assert.Single(await CotarAsync(destino));
        cidade = cidade with { Ativa = true, Revisao = 1 };
        await EscreverAtendimentoAsync(admin, HttpMethod.Put, rotaCidade, cidade, HttpStatusCode.OK);
        Assert.Single(await CotarAsync(destino with { Cidade = "Outro município" }));
        Assert.Single(await CotarAsync(destino with { Cep = "inválido" }));
        Assert.Single(await CotarAsync(destino with { Cep = "12345678" }));
        Assert.Single(await CotarAsync(destino with { Cep = "90000000", Cidade = "Porto Alegre" }));
        Assert.Single(await CotarAsync(destino with { Numero = "" }));
        var antiga = Assert.Single(await CotarAsync(destino), item => item.GetProperty("modalidade").GetString() == "EntregaPropria");
        var tokenAntigo = antiga.GetProperty("id").GetString()!;
        await RecusarAsync(outro, tokenAntigo);
        await RecusarAsync(cliente, "Correios");
        await RecusarAsync(cliente, "x" + tokenAntigo);
        cidade = cidade with { Ativa = false, Revisao = 2 };
        await EscreverAtendimentoAsync(admin, HttpMethod.Put, rotaCidade, cidade, HttpStatusCode.OK);
        await RecusarAsync(cliente, tokenAntigo);
        Assert.Single(await CotarAsync(destino));
        cidade = cidade with { Ativa = true, Revisao = 3 };
        await EscreverAtendimentoAsync(admin, HttpMethod.Put, rotaCidade, cidade, HttpStatusCode.OK);
        var novaOferta = Assert.Single(await CotarAsync(destino), item =>
            item.GetProperty("modalidade").GetString() == "EntregaPropria").GetProperty("id").GetString()!;
        cidade = cidade with { TarifaCentavos = 1300, Revisao = 4 };
        await EscreverAtendimentoAsync(admin, HttpMethod.Put, rotaCidade, cidade, HttpStatusCode.OK);
        await RecusarAsync(cliente, novaOferta);
        await EscreverAtendimentoAsync(admin, HttpMethod.Put, "/modalidades",
            config with { RetiradaHabilitada = false, Revisao = 1 }, HttpStatusCode.OK);
        await cliente.PostAsync<JsonElement>("/api/pedidos", new { itens }, HttpStatusCode.Conflict, Guid.NewGuid());

        var oferta = Assert.Single(await CotarAsync(destino));
        Assert.Equal("1300", oferta.GetProperty("freteCentavos").GetString());
        Assert.Equal("3698", oferta.GetProperty("totalCentavos").GetString());
        var ofertaId = oferta.GetProperty("id").GetString()!;
        var vencimento = oferta.GetProperty("validaAteUtc").GetDateTimeOffset();
        await cliente.PostAsync<JsonElement>("/api/pedidos",
            new { itens = new[] { itens[0] with { quantidade = 1 } }, ofertaEntregaId = ofertaId },
            HttpStatusCode.Conflict, Guid.NewGuid());
        relogio.Definir(vencimento);
        await RecusarAsync(cliente, ofertaId);
        Assert.Empty(await db.Pedidos.ToListAsync());
        Assert.Empty(await db.ReservasProdutoAcabado.ToListAsync());

        ofertaId = Assert.Single(await CotarAsync(destino)).GetProperty("id").GetString()!;
        var chave = Guid.NewGuid();
        var payload = new { itens, ofertaEntregaId = ofertaId };
        var pedido = await cliente.PostAsync<JsonElement>("/api/pedidos", payload, HttpStatusCode.Created, chave);
        var pedidoId = pedido.GetProperty("id").GetGuid();
        Assert.Equal("EntregaPropria", pedido.GetProperty("modalidadeEntrega").GetString());
        Assert.Equal("2398", pedido.GetProperty("subtotalProdutosCentavos").GetString());
        Assert.Equal("1300", pedido.GetProperty("freteCentavos").GetString());
        Assert.Equal("3698", pedido.GetProperty("totalCentavos").GetString());
        Assert.Equal("4322509", pedido.GetProperty("entrega").GetProperty("destino").GetProperty("codigoIbge").GetString());
        Assert.Equal(2, (await db.ReservasProdutoAcabado.AsNoTracking().SingleAsync()).Quantidade);
        Assert.Equal(12, (await db.SaldosProdutoAcabado.AsNoTracking().SingleAsync()).QuantidadeFisica);

        relogio.Definir(vencimento.AddMinutes(6));
        var replay = await cliente.PostAsync<JsonElement>("/api/pedidos", payload, HttpStatusCode.OK, chave);
        Assert.True(JsonElement.DeepEquals(pedido, replay));
        await RecusarAsync(cliente, tokenAntigo, "chave_em_uso", chave);

        var caminho = $"/api/pedidos/{pedidoId:D}";
        var rotaAdmin = $"/api/admin/pedidos/{pedidoId:D}";
        async Task<string> VersaoAsync()
        {
            using var detalhe = await admin.Http.GetAsync(rotaAdmin);
            detalhe.EnsureSuccessStatusCode();
            return detalhe.Headers.ETag!.Tag;
        }
        async Task<HttpResponseMessage> TransicaoAsync(Sessao sessao, string etapa, string versao, Guid chaveOperacao,
            bool csrf = true, bool confirmado = true)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{rotaAdmin}/{etapa}")
            {
                Content = JsonContent.Create(new { confirmado }),
            };
            request.Headers.Add("If-Match", versao);
            request.Headers.Add("Idempotency-Key", chaveOperacao.ToString("D"));
            if (csrf)
                request.Headers.Add("X-XSRF-TOKEN", await TokenComprovanteAsync(sessao));
            return await sessao.Http.SendAsync(request);
        }
        const string iniciar = "iniciar-entrega-propria";
        const string receber = "confirmar-recebimento";
        var pendente = await VersaoAsync();
        using (var antesDoPix = await TransicaoAsync(admin, iniciar, pendente, Guid.NewGuid()))
            Assert.Equal(HttpStatusCode.Conflict, antesDoPix.StatusCode);
        using var comprovante = await EnviarComprovanteAsync(cliente, caminho + "/comprovante-pix",
            PdfComprovante(), "comprovante.pdf", "application/pdf", await TokenComprovanteAsync(cliente));
        Assert.True(comprovante.IsSuccessStatusCode, await comprovante.Content.ReadAsStringAsync());
        Assert.Equal(SituacaoPedido.AguardandoPagamento, (await db.Pedidos.AsNoTracking().SingleAsync()).Situacao);
        Assert.Empty((await admin.Http.GetFromJsonAsync<JsonElement>(Movimentacoes)).GetProperty("itens").EnumerateArray());

        using var configurarPix = new HttpRequestMessage(HttpMethod.Put, "/api/admin/configuracoes/pix-manual")
        {
            Content = JsonContent.Create(new SalvarPixManualRequest("Email", "loja@example.com", "Loja teste", null)),
        };
        configurarPix.Headers.Add("X-XSRF-TOKEN", await TokenComprovanteAsync(admin));
        using var pixConfigurado = await admin.Http.SendAsync(configurarPix);
        Assert.Equal(HttpStatusCode.OK, pixConfigurado.StatusCode);
        Assert.Equal("3698", (await cliente.Http.GetFromJsonAsync<JsonElement>(caminho + "/pagamento"))
            .GetProperty("valorCentavos").GetString());
        var confirmar = $"/api/admin/pedidos/{pedidoId:D}/confirmar-pagamento";
        var chavePix = Guid.NewGuid();
        for (var i = 0; i < 2; i++)
            await admin.PostAsync<JsonElement>(confirmar, new { recebimentoConferido = true }, HttpStatusCode.OK, chavePix);
        using (var antesDaPreparacao = await TransicaoAsync(admin, iniciar, await VersaoAsync(), Guid.NewGuid()))
            Assert.Equal(HttpStatusCode.Conflict, antesDaPreparacao.StatusCode);
        var caixa = Assert.Single((await admin.Http.GetFromJsonAsync<JsonElement>(Movimentacoes))
            .GetProperty("itens").EnumerateArray());
        Assert.Equal("3698", caixa.GetProperty("valorCentavos").GetString());
        Assert.Equal(pedidoId, caixa.GetProperty("recursoId").GetGuid());
        await admin.PostAsync<JsonElement>($"/api/admin/pedidos/{pedidoId:D}/iniciar-preparacao",
            new { }, HttpStatusCode.OK, Guid.NewGuid());
        await admin.PostAsync<JsonElement>($"/api/admin/pedidos/{pedidoId:D}/liberar-retirada",
            new { }, HttpStatusCode.Conflict, Guid.NewGuid());
        Assert.Equal(12, (await db.SaldosProdutoAcabado.AsNoTracking().SingleAsync()).QuantidadeFisica);
        Assert.Single(await db.MovimentosProdutoAcabado.ToListAsync());

        var preparada = await VersaoAsync();
        var chaveSaida = Guid.NewGuid();
        using (var proibido = await TransicaoAsync(cliente, iniciar, preparada, chaveSaida))
            Assert.Equal(HttpStatusCode.Forbidden, proibido.StatusCode);
        using (var semCsrf = await TransicaoAsync(admin, iniciar, preparada, chaveSaida, csrf: false))
            Assert.Equal(HttpStatusCode.BadRequest, semCsrf.StatusCode);
        using (var semConferencia = await TransicaoAsync(admin, iniciar, preparada, chaveSaida, confirmado: false))
            Assert.Equal(HttpStatusCode.BadRequest, semConferencia.StatusCode);
        using (var semSaida = await TransicaoAsync(admin, receber, preparada, Guid.NewGuid()))
            Assert.Equal(HttpStatusCode.Conflict, semSaida.StatusCode);
        using (var versaoAntiga = await TransicaoAsync(admin, iniciar, pendente, Guid.NewGuid()))
        {
            Assert.Equal(HttpStatusCode.Conflict, versaoAntiga.StatusCode);
            Assert.Equal("versao_desatualizada",
                (await versaoAntiga.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("codigo").GetString());
        }

        var antes = await db.Pedidos.AsNoTracking().SingleAsync();
        var falha = new FalharAoGravarSaida();
        var options = new DbContextOptionsBuilder<XaquiamuniDbContext>()
            .UseSqlServer(db.Database.GetConnectionString()).AddInterceptors(falha, relogio).Options;
        await using (var dbComFalha = new XaquiamuniDbContext(options))
        {
            IEntregaPropriaPedido repositorio = new EfEstoqueProdutoAcabadoRepository(dbComFalha,
                TimeProvider.System, ambiente.Host.Services.GetRequiredService<ICalendarioOperacionalEstoque>());
            await Assert.ThrowsAsync<InvalidOperationException>(() => repositorio.ExecutarAsync(
                new(pedidoId, antes.PagamentoConfirmadoPorId!.Value, chaveSaida, antes.Versao, EtapaEntregaPropria.Iniciar), default));
        }
        Assert.True(falha.Executada);
        Assert.Equal(SituacaoPedido.EmPreparacao, await db.Pedidos.Select(x => x.Situacao).SingleAsync());
        Assert.Equal(12, await db.SaldosProdutoAcabado.AsNoTracking().Select(x => x.QuantidadeFisica).SingleAsync());
        Assert.Equal(EstadoReservaProdutoAcabado.Confirmada,
            await db.ReservasProdutoAcabado.Select(x => x.Estado).SingleAsync());
        Assert.Equal(1, await db.MovimentosProdutoAcabado.CountAsync());

        var csrfAdmin = await TokenComprovanteAsync(admin);
        async Task<HttpResponseMessage> ConcorrerAsync(string etapa, Guid chaveOperacao)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, rotaAdmin + "/" + etapa)
            {
                Content = JsonContent.Create(new { confirmado = true }),
            };
            request.Headers.Add("X-XSRF-TOKEN", csrfAdmin);
            request.Headers.Add("If-Match", preparada);
            request.Headers.Add("Idempotency-Key", chaveOperacao.ToString("D"));
            return await admin.Http.SendAsync(request);
        }
        var concorrente = Guid.NewGuid();
        var respostas = await Task.WhenAll(ConcorrerAsync(iniciar, chaveSaida), ConcorrerAsync(iniciar, concorrente),
            ConcorrerAsync(receber, Guid.NewGuid()));
        using var primeira = respostas[0];
        using var segunda = respostas[1];
        using var recebimentoConcorrente = respostas[2];
        Assert.Single(respostas.Take(2), resposta => resposta.StatusCode == HttpStatusCode.OK);
        Assert.Single(respostas.Take(2), resposta => resposta.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal(HttpStatusCode.Conflict, recebimentoConcorrente.StatusCode);
        var vencedora = primeira.IsSuccessStatusCode ? chaveSaida : concorrente;
        Assert.Equal(SituacaoPedido.EmEntrega, await db.Pedidos.Select(x => x.Situacao).SingleAsync());
        Assert.Equal(10, await db.SaldosProdutoAcabado.AsNoTracking().Select(x => x.QuantidadeFisica).SingleAsync());
        Assert.Equal(EstadoReservaProdutoAcabado.Consumida,
            await db.ReservasProdutoAcabado.Select(x => x.Estado).SingleAsync());
        var baixa = await db.MovimentosProdutoAcabado.AsNoTracking().SingleAsync(x => x.ReservaProdutoAcabadoId != null);
        Assert.Equal("Saída para entrega própria", baixa.Motivo);
        Assert.Equal(baixa.RegistradoEmUtc, await db.Pedidos.Select(x => x.SaidaEntregaEmUtc).SingleAsync());
        Assert.Equal("3698", Assert.Single((await admin.Http.GetFromJsonAsync<JsonElement>(Movimentacoes))
            .GetProperty("itens").EnumerateArray()).GetProperty("valorCentavos").GetString());
        await admin.PostAsync<JsonElement>(rotaAdmin + "/cancelar", new { motivo = "Após saída" },
            HttpStatusCode.Conflict, Guid.NewGuid());

        var emEntrega = await VersaoAsync();
        using (var chaveEmUso = await TransicaoAsync(admin, receber, emEntrega, vencedora))
            Assert.Equal(HttpStatusCode.Conflict, chaveEmUso.StatusCode);
        var chaveReceber = Guid.NewGuid();
        using (var recebido = await TransicaoAsync(admin, receber, emEntrega, chaveReceber))
            Assert.Equal(HttpStatusCode.OK, recebido.StatusCode);
        using (var replaySaida = await TransicaoAsync(admin, iniciar, preparada, vencedora))
        {
            Assert.Equal(HttpStatusCode.OK, replaySaida.StatusCode);
            var gravado = await replaySaida.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(gravado.GetProperty("repetida").GetBoolean());
            Assert.Equal("EmEntrega", gravado.GetProperty("situacao").GetString());
        }
        using (var replayRecebimento = await TransicaoAsync(admin, receber, emEntrega, chaveReceber))
            Assert.True((await replayRecebimento.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("repetida").GetBoolean());
        using (var duplicado = await TransicaoAsync(admin, receber, await VersaoAsync(), Guid.NewGuid()))
            Assert.Equal(HttpStatusCode.Conflict, duplicado.StatusCode);
        Assert.Equal(SituacaoPedido.Entregue, await db.Pedidos.Select(x => x.Situacao).SingleAsync());
        Assert.Equal(10, await db.SaldosProdutoAcabado.AsNoTracking().Select(x => x.QuantidadeFisica).SingleAsync());
        Assert.Equal(2, await db.MovimentosProdutoAcabado.CountAsync());
        Assert.Equal(PdfComprovante(), await cliente.Http.GetByteArrayAsync(caminho + "/comprovante-pix/conteudo"));
        Assert.Single((await admin.Http.GetFromJsonAsync<JsonElement>(Movimentacoes)).GetProperty("itens").EnumerateArray());
    }

    private sealed class FalharAoGravarSaida : SaveChangesInterceptor
    {
        public bool Executada { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData dados,
            InterceptionResult<int> resultado, CancellationToken cancellationToken = default)
        {
            var contexto = dados.Context!;
            if (contexto.ChangeTracker.Entries<Pedido>().Any(x => x.State == EntityState.Modified &&
                x.Entity.Situacao == SituacaoPedido.EmEntrega))
            {
                Assert.NotNull(contexto.Database.CurrentTransaction);
                Assert.Equal(EntityState.Unchanged, Assert.Single(contexto.ChangeTracker.Entries<MovimentoProdutoAcabado>()).State);
                Executada = true;
                throw new InvalidOperationException("Falha de teste após a baixa.");
            }
            return ValueTask.FromResult(resultado);
        }
    }
}
