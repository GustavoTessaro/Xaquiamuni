using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Api.Estoque;
using Xaquiamuni.Aplicacao.Estoque;
using Xaquiamuni.Aplicacao.Financeiro;
using Xaquiamuni.Dominio.Estoque;
using Xaquiamuni.Dominio.Financeiro;
using Xaquiamuni.Infraestrutura.Identidade;

namespace Xaquiamuni.Tests.Integration;

public sealed partial class CadeiaFabricacaoSqlServerIntegrationTests
{
    [SqlServerIntegrationFact]
    public async Task ResumoFinanceiro_DistinguePosicaoAtualDeFluxoNegativoEValoresDesconhecidos()
    {
        await using var ambiente = await Ambiente.CriarAsync();
        using var admin = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Administrador);
        await using var db = ambiente.Banco.CreateVerificationContext();
        var dados = await PrepararCompraAsync(admin, db, custoParcial: true);
        var data = dados.Hoje.AddDays(-5);
        var agora = DateTimeOffset.UtcNow;
        var autor = Guid.NewGuid();

        async Task<AdminComprasMateriasPrimasEndpoints.DetalheCompra> CriarCompraAsync() =>
            await admin.PostAsync<AdminComprasMateriasPrimasEndpoints.DetalheCompra>(Compras,
                new CompraMateriaPrimaInput(dados.FornecedorId, dados.Hoje,
                    [new(null, 1, 10.03m, [new(dados.Extrato, null, 1, null)])], null),
                HttpStatusCode.Created, Guid.NewGuid());

        var parcial = await CriarCompraAsync();
        await admin.PostAsync<PagamentoFornecedorDto>(
            Financeiro + $"/compras/{parcial.Resumo.Id:D}/pagamentos",
            new PagarFornecedorInput(data, "Pix", null, 501), HttpStatusCode.OK, Guid.NewGuid());
        var cancelada = await CriarCompraAsync();
        using var cancelamento = await admin.EnviarAsync(
            Compras + $"/{cancelada.Resumo.Id:D}/cancelar", new { cancelada.Versao });
        Assert.Equal(HttpStatusCode.OK, cancelamento.StatusCode);

        var variante = await db.ProdutoVariantes.Include(v => v.Produto).SingleAsync(v => v.Id == dados.VarianteId);
        var venda = VendaExterna.Criar(CanalVendaExterna.Presencial, null, data, "Cliente teste",
            null, [new ItemVendaExterna(variante, 1, 1000, 0)], autor, agora);
        venda.Receber(101, data, MeioRecebimentoVendaExterna.Dinheiro, autor, Guid.NewGuid(), agora);
        var vendaCancelada = VendaExterna.Criar(CanalVendaExterna.Presencial, null, data, "Cliente teste",
            null, [new ItemVendaExterna(variante, 1, 50, 0)], autor, agora);
        vendaCancelada.Cancelar("Teste", autor, agora);
        db.AddRange(venda, vendaCancelada, LancamentoFinanceiro.Criar(TipoLancamento.Saida,
            "Teste", "Saída ativa", 333, data, MeioFinanceiro.Pix, autor, agora));
        await db.SaveChangesAsync();

        var filtrado = await ObterResumoAsync(admin, $"?dataInicial={data:yyyy-MM-dd}&dataFinal={data:yyyy-MM-dd}");
        Assert.Equal(new FluxoFinanceiroDto(data, data, "101", "834", "-733"), filtrado.FluxoPeriodo);
        Assert.Equal(("899", "502", 1L), (filtrado.PosicaoAtual.AReceberCentavos, filtrado.PosicaoAtual.APagarCentavos,
            filtrado.PosicaoAtual.ComprasValorAConferir));
        var vazio = await ObterResumoAsync(admin, $"?dataInicial={dados.Hoje:yyyy-MM-dd}");
        Assert.Equal(new FluxoFinanceiroDto(dados.Hoje, null, "0", "0", "0"), vazio.FluxoPeriodo);
        Assert.Equal(filtrado.PosicaoAtual, vazio.PosicaoAtual);

        using var invalido = await admin.Http.GetAsync(Financeiro + "/resumo?dataInicial=2026-12-01&dataFinal=2026-11-30");
        Assert.Equal(HttpStatusCode.BadRequest, invalido.StatusCode);
    }

    private static async Task<ResumoFinanceiroDto> ObterResumoAsync(Sessao admin, string filtro)
    {
        using var resposta = await admin.Http.GetAsync(Financeiro + "/resumo" + filtro);
        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.True(resposta.Headers.CacheControl?.NoStore);
        return (await resposta.Content.ReadFromJsonAsync<ResumoFinanceiroDto>())!;
    }
}
