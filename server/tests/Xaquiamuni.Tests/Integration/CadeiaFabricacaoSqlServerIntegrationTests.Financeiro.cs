using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Api.Estoque;
using Xaquiamuni.Aplicacao.Estoque;
using Xaquiamuni.Aplicacao.Financeiro;
using Xaquiamuni.Dominio.Financeiro;
using Xaquiamuni.Infraestrutura.Identidade;

namespace Xaquiamuni.Tests.Integration;

public sealed partial class CadeiaFabricacaoSqlServerIntegrationTests
{
    private const string Financeiro = "/api/admin/financeiro";

    [SqlServerIntegrationFact]
    public async Task Financeiro_ProtegeRotasERegistraLancamentosComCancelamentoAuditavel()
    {
        await using var ambiente = await Ambiente.CriarAsync();
        using var admin = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Administrador);
        using var cliente = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Cliente);
        var url = Financeiro + "/lancamentos";
        var entrada = new CriarLancamentoInput("Entrada", "Outros", "Aporte", new(2026, 1, 1), "Pix", long.MaxValue);
        var chave = Guid.NewGuid();

        using var negada = await cliente.Http.GetAsync(url);
        Assert.Equal(HttpStatusCode.Forbidden, negada.StatusCode);
        using var semCsrf = await admin.Http.PostAsJsonAsync(url, entrada);
        Assert.Equal(HttpStatusCode.BadRequest, semCsrf.StatusCode);
        using var semChave = await admin.EnviarAsync(url, entrada);
        Assert.Equal(HttpStatusCode.BadRequest, semChave.StatusCode);
        using var invalida = await admin.EnviarAsync(url, entrada with { ValorCentavos = 0 }, chave);
        Assert.Equal(HttpStatusCode.BadRequest, invalida.StatusCode);

        var criada = await admin.PostAsync<LancamentoDto>(url, entrada, HttpStatusCode.Created, chave);
        using var outro = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Administrador);
        var tentativaUrl = Financeiro + $"/tentativas/{chave:D}";
        using var proibida = await cliente.Http.GetAsync(tentativaUrl);
        Assert.Equal(HttpStatusCode.Forbidden, proibida.StatusCode);
        using var alheia = await outro.Http.GetAsync(tentativaUrl);
        Assert.Equal(HttpStatusCode.NotFound, alheia.StatusCode);
        using var ausente = await admin.Http.GetAsync(Financeiro + $"/tentativas/{Guid.NewGuid():D}");
        Assert.Equal(HttpStatusCode.NotFound, ausente.StatusCode);
        await ConferirTentativaAsync(admin, chave, "criar", criada.Id, criada);
        Assert.Equal("9223372036854775807", criada.ValorCentavos);
        Assert.Equal(TimeSpan.Zero, criada.CriadoEmUtc.Offset);
        Assert.Equal(criada, await admin.PostAsync<LancamentoDto>(url, entrada, HttpStatusCode.OK, chave));
        var saida = entrada with { Tipo = "Saida", ValorCentavos = 600 };
        using var conflito = await admin.EnviarAsync(url, saida, chave);
        Assert.Equal(HttpStatusCode.Conflict, conflito.StatusCode);
        await admin.PostAsync<LancamentoDto>(url, saida, HttpStatusCode.Created, Guid.NewGuid());

        var cancelar = url + $"/{criada.Id:D}/cancelar";
        var motivo = new CancelarLancamentoInput("Lançamento corrigido");
        var chaveCancelar = Guid.NewGuid();
        var cancelada = await admin.PostAsync<LancamentoDto>(cancelar, motivo, HttpStatusCode.OK, chaveCancelar);
        await ConferirTentativaAsync(admin, chaveCancelar, "cancelar", criada.Id, cancelada);
        await ConferirTentativaAsync(admin, chave, "criar", criada.Id, criada);
        Assert.Equal(criada.ResponsavelId, cancelada.CanceladoPorId);
        Assert.NotNull(cancelada.CanceladoEmUtc);
        Assert.Equal(motivo.Motivo, cancelada.MotivoCancelamento);
        Assert.Equal(cancelada, await admin.PostAsync<LancamentoDto>(cancelar, motivo, HttpStatusCode.OK, chaveCancelar));
        Assert.Equal(cancelada, await admin.Http.GetFromJsonAsync<LancamentoDto>(url + $"/{criada.Id:D}"));
        using var jaCancelada = await admin.EnviarAsync(cancelar, motivo, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Conflict, jaCancelada.StatusCode);
        Assert.Equal(2, (await admin.Http.GetFromJsonAsync<LancamentoDto[]>(url))!.Length);
    }

    [SqlServerIntegrationFact]
    public async Task Financeiro_PagamentoAntecipadoKitParcialEConcorrenciaSemEfeitoNoEstoque()
    {
        await using var ambiente = await Ambiente.CriarAsync();
        using var admin = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Administrador);
        using var outroAdmin = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Administrador);
        await using var db = ambiente.Banco.CreateVerificationContext();
        var dados = await PrepararCompraAsync(admin, db);
        var id = dados.Compra.Resumo.Id;
        var url = Financeiro + $"/compras/{id:D}";
        var pagar = url + "/pagamentos";
        async Task VerificarFornecedorPreservadoAsync()
        {
            var rota = $"/api/admin/fornecedores/{dados.FornecedorId:D}";
            using var detalhe = await admin.Http.GetAsync(rota);
            using var pedidoExclusao = new HttpRequestMessage(HttpMethod.Delete, rota);
            pedidoExclusao.Headers.IfMatch.Add(detalhe.Headers.ETag!);
            pedidoExclusao.Headers.Add("X-XSRF-TOKEN", await TokenComprovanteAsync(admin));
            pedidoExclusao.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("D"));
            using var excl = await admin.Http.SendAsync(pedidoExclusao);
            Assert.Equal(HttpStatusCode.Conflict, excl.StatusCode);
            Assert.Equal("fornecedor_com_compras", (await excl.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("codigo").GetString());
        }
        var original = (await admin.Http.GetFromJsonAsync<CompraFinanceiraDto>(url))!;
        Assert.Equal(("Rascunho", "Pendente", "4650", "0", "4650"),
            (original.SituacaoCompra, original.SituacaoFinanceira, original.TotalCentavos, original.PagoCentavos, original.SaldoCentavos));
        var input = new PagarFornecedorInput(dados.Hoje, "Pix", "Adiantamento", 650);
        var chave = Guid.NewGuid();
        var parcial = await admin.PostAsync<PagamentoFornecedorDto>(pagar, input, HttpStatusCode.OK, chave);
        await ConferirTentativaAsync(admin, chave, "pagar", id, parcial);
        Assert.Equal(id, parcial.CompraId);
        Assert.False(await db.MovimentosInsumo.AnyAsync());
        Assert.False(await db.Set<LancamentoFinanceiro>().AnyAsync());
        Assert.Equal("Parcial", (await admin.Http.GetFromJsonAsync<CompraFinanceiraDto>(url))!.SituacaoFinanceira);

        using var cancelamento = await admin.EnviarAsync(Compras + $"/{id:D}/cancelar", new { dados.Compra.Versao });
        Assert.Equal(HttpStatusCode.Conflict, cancelamento.StatusCode);
        var csrf = await admin.Http.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        using var editar = new HttpRequestMessage(HttpMethod.Put, Compras + $"/{id:D}")
        {
            Content = JsonContent.Create(new CompraMateriaPrimaInput(dados.FornecedorId, dados.Hoje,
                [new(null, 1, 1m, [new(dados.Extrato, null, 1, null)])], dados.Compra.Versao))
        };
        editar.Headers.Add("X-XSRF-TOKEN", csrf.GetProperty("requestToken").GetString());
        using var edicaoBloqueada = await admin.Http.SendAsync(editar);
        Assert.Equal(HttpStatusCode.Conflict, edicaoBloqueada.StatusCode);
        await admin.PostAsync<System.Text.Json.JsonElement>(dados.Receber, dados.Recebimento, HttpStatusCode.OK, Guid.NewGuid());
        Assert.Equal("650", (await admin.Http.GetFromJsonAsync<CompraFinanceiraDto>(url))!.PagoCentavos);
        var movimentos = await db.MovimentosInsumo.CountAsync();
        Assert.Equal(4, movimentos);
        await VerificarFornecedorPreservadoAsync();

        var quitar = input with { ValorCentavos = 4000, Referencia = null };
        var respostas = await Task.WhenAll(admin.EnviarAsync(pagar, quitar, Guid.NewGuid()),
            outroAdmin.EnviarAsync(pagar, quitar, Guid.NewGuid()));
        using var vencedora = Assert.Single(respostas, r => r.StatusCode == HttpStatusCode.OK);
        using var perdedora = Assert.Single(respostas, r => r.StatusCode == HttpStatusCode.Conflict);
        var quitada = (await admin.Http.GetFromJsonAsync<CompraFinanceiraDto>(url))!;
        Assert.Equal(("Recebida", "Quitada", "4650", "0"),
            (quitada.SituacaoCompra, quitada.SituacaoFinanceira, quitada.PagoCentavos, quitada.SaldoCentavos));
        await VerificarFornecedorPreservadoAsync();
        Assert.Equal(parcial, await admin.PostAsync<PagamentoFornecedorDto>(pagar, input, HttpStatusCode.OK, chave));
        using var divergente = await admin.EnviarAsync(pagar, input with { ValorCentavos = 651 }, chave);
        Assert.Equal(HttpStatusCode.Conflict, divergente.StatusCode);
        Assert.Equal(2, (await admin.Http.GetFromJsonAsync<PagamentoFornecedorDto[]>(pagar))!.Length);
        Assert.Equal(movimentos, await db.MovimentosInsumo.CountAsync());
        Assert.False(await db.Set<LancamentoFinanceiro>().AnyAsync());
    }

    private static async Task ConferirTentativaAsync<T>(Sessao admin, Guid chave, string operacao, Guid id, T resultado)
    {
        using var resposta = await admin.Http.GetAsync(Financeiro + $"/tentativas/{chave:D}");
        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.True(resposta.Headers.CacheControl?.NoStore);
        var dados = await resposta.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(operacao, dados.GetProperty("operacao").GetString());
        Assert.Equal(id, dados.GetProperty("recursoId").GetGuid());
        Assert.Equal(resultado, dados.GetProperty("resultado").Deserialize<T>(new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    [SqlServerIntegrationFact]
    public async Task Financeiro_CompraRecebidaComCustoParcialContinuaSemTotalConfiavel()
    {
        await using var ambiente = await Ambiente.CriarAsync();
        using var admin = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Administrador);
        await using var db = ambiente.Banco.CreateVerificationContext();
        var dados = await PrepararCompraAsync(admin, db, custoParcial: true);
        await admin.PostAsync<System.Text.Json.JsonElement>(dados.Receber, dados.Recebimento, HttpStatusCode.OK, Guid.NewGuid());
        var url = Financeiro + $"/compras/{dados.Compra.Resumo.Id:D}";
        var compra = (await admin.Http.GetFromJsonAsync<CompraFinanceiraDto>(url))!;
        Assert.Equal("Recebida", compra.SituacaoCompra);
        Assert.Equal("ValorAConferir", compra.SituacaoFinanceira);
        Assert.Equal("ParcialmenteConhecido", compra.ComposicaoValor);
        Assert.Null(compra.TotalCentavos);
        Assert.Null(compra.SaldoCentavos);
        Assert.Equal("0", compra.PagoCentavos);
        using var negado = await admin.EnviarAsync(url + "/pagamentos",
            new PagarFornecedorInput(dados.Hoje, "Pix", null, 1), Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Conflict, negado.StatusCode);
        Assert.False(await db.Set<PagamentoFornecedor>().AnyAsync());

        var outra = await admin.PostAsync<AdminComprasMateriasPrimasEndpoints.DetalheCompra>(Compras,
            new CompraMateriaPrimaInput(dados.FornecedorId, dados.Hoje,
                [new(null, 1, null, [new(dados.Extrato, null, 1, null)])], null),
            HttpStatusCode.Created, Guid.NewGuid());
        var desconhecida = (await admin.Http.GetFromJsonAsync<CompraFinanceiraDto>(
            Financeiro + $"/compras/{outra.Resumo.Id:D}"))!;
        Assert.Equal("Desconhecido", desconhecida.ComposicaoValor);
        Assert.Equal("ValorAConferir", desconhecida.SituacaoFinanceira);
        Assert.Null(desconhecida.TotalCentavos);
    }

    [SqlServerIntegrationFact]
    public async Task Financeiro_FalhaNaGravacaoDaChaveRevertePagamentoEPermiteRepetir()
    {
        await using var ambiente = await Ambiente.CriarAsync();
        using var admin = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Administrador);
        await using var db = ambiente.Banco.CreateVerificationContext();
        var dados = await PrepararCompraAsync(admin, db);
        var url = Financeiro + $"/compras/{dados.Compra.Resumo.Id:D}";
        await admin.PostAsync<System.Text.Json.JsonElement>(dados.Receber, dados.Recebimento, HttpStatusCode.OK, Guid.NewGuid());
        Assert.Equal("Pendente", (await admin.Http.GetFromJsonAsync<CompraFinanceiraDto>(url))!.SituacaoFinanceira);
        await db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE [OperacoesFinanceiras] ADD CONSTRAINT [CK_FalhaFinanceiroTeste] CHECK (1 = 0);
            """);
        var input = new PagarFornecedorInput(dados.Hoje, "Boleto", null, 4650);
        var chave = Guid.NewGuid();
        using var falha = await admin.EnviarAsync(url + "/pagamentos", input, chave);
        Assert.Equal(HttpStatusCode.InternalServerError, falha.StatusCode);
        Assert.False(await db.Set<PagamentoFornecedor>().AnyAsync());
        Assert.Equal("4650", (await admin.Http.GetFromJsonAsync<CompraFinanceiraDto>(url))!.SaldoCentavos);
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE [OperacoesFinanceiras] DROP CONSTRAINT [CK_FalhaFinanceiroTeste]");
        await admin.PostAsync<PagamentoFornecedorDto>(url + "/pagamentos", input, HttpStatusCode.OK, chave);
        Assert.Equal(1, await db.Set<PagamentoFornecedor>().CountAsync());
    }
}
