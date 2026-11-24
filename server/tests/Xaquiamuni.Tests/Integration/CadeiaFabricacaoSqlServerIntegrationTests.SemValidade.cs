using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Api.Catalogo;
using Xaquiamuni.Api.Estoque;
using Xaquiamuni.Aplicacao.Estoque;
using Xaquiamuni.Dominio.Estoque;
using Xaquiamuni.Infraestrutura.Identidade;
using static Xaquiamuni.Tests.Integration.ReservarProdutoAcabadoSqlServerIntegrationTests;

namespace Xaquiamuni.Tests.Integration;

public sealed partial class CadeiaFabricacaoSqlServerIntegrationTests
{
    [SqlServerIntegrationFact]
    public async Task EstoqueSemValidade_RecebeProduzReservaVendeECancelaSemLiberarEstoqueInelegivel()
    {
        await using var ambiente = await Ambiente.CriarAsync();
        using var admin = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Administrador);
        using var cliente = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Cliente);
        await using var db = ambiente.Banco.CreateVerificationContext();
        var dados = await PrepararCompraAsync(admin, db);
        var recebimento = dados.Recebimento with { Validades = null, SemControleValidade = true };
        var chave = Guid.NewGuid();
        var recebido = await admin.PostAsync<JsonElement>(dados.Receber, recebimento, HttpStatusCode.OK, chave);
        var replay = await admin.PostAsync<JsonElement>(dados.Receber, recebimento, HttpStatusCode.OK, chave);
        Assert.Equal(recebido.GetRawText(), replay.GetRawText());
        using var divergente = await admin.EnviarAsync(dados.Receber, dados.Recebimento, chave);
        Assert.Equal(HttpStatusCode.Conflict, divergente.StatusCode);
        Assert.True((await db.InsumosEstoque.AsNoTracking().SingleAsync(i => i.Id == dados.Erva)).ValidadeObrigatoria);
        var lotesErva = await db.LotesInsumo.AsNoTracking().Where(l => l.InsumoId == dados.Erva).ToArrayAsync();
        Assert.Equal(2, lotesErva.Length);
        Assert.All(lotesErva, l => Assert.Null(l.DataValidade));
        await ConferirFisicoAsync(db, dados, 200, 18, 6);

        var receita = (await CriarReceitaAsync(admin, dados)).Resumo.Id;
        var previa = await PreviaAsync(admin, receita);
        var ordem = await IniciarAsync(admin, new(receita, previa.ReceitaVersaoId, 4), Guid.NewGuid());
        var concluida = await admin.PostAsync<ConclusaoProducao>(
            $"{Producao}/{ordem.Resumo.Id:D}/concluir",
            Conclusao(ordem, dados) with { DataValidade = null, SemControleValidade = true }, HttpStatusCode.OK, Guid.NewGuid());
        Assert.Null(concluida.DataValidade);
        Assert.Equal(1m, Assert.Single(concluida.Consumos, c => c.InsumoId == dados.Erva).Liberada);
        await ConferirFisicoAsync(db, dados, 125, 7, 2);
        var catalogo = $"/api/catalogo/produtos/{dados.ProdutoId:D}";
        Assert.True((await cliente.Http.GetFromJsonAsync<ProdutoCatalogoDetalheResponse>(catalogo))!.Disponivel);

        var estoque = $"/api/admin/estoque/produtos-acabados/variantes/{dados.VarianteId:D}";
        async Task<Guid> EntrarAsync(DateOnly? validade, CondicaoEstoqueProdutoAcabado condicao)
        {
            var entrada = await admin.PostAsync<JsonElement>(estoque + "/entradas", new EntradaProdutoAcabadoAdminRequest
            {
                Tipo = TipoMovimentoProdutoAcabado.EntradaManual,
                Quantidade = 1,
                CondicaoDestino = condicao,
                DataFabricacao = validade is null ? null : dados.Hoje.AddDays(-5),
                DataValidade = validade,
                SemControleValidade = validade is null,
                Motivo = "Regressao de estoque misto"
            }, HttpStatusCode.Created, Guid.NewGuid());
            return entrada.GetProperty("loteId").GetGuid();
        }

        var datado = await EntrarAsync(dados.Hoje.AddDays(10), CondicaoEstoqueProdutoAcabado.Vendavel);
        var vencido = await EntrarAsync(dados.Hoje.AddDays(-3), CondicaoEstoqueProdutoAcabado.Vendavel);
        var novo = await EntrarAsync(null, CondicaoEstoqueProdutoAcabado.Vendavel);
        var bloqueado = await EntrarAsync(null, CondicaoEstoqueProdutoAcabado.Quarentena);
        var carrinho = new { itens = new[] { new { produtoId = dados.ProdutoId, produtoVarianteId = dados.VarianteId, quantidade = 1 } } };
        await cliente.PostAsync<JsonElement>(Pedidos, carrinho, HttpStatusCode.Created, Guid.NewGuid());
        Assert.Equal(datado, (await db.ReservasProdutoAcabado.AsNoTracking().SingleAsync()).LoteId);

        const string vendas = "/api/admin/vendas-externas";
        var vendaInput = new CriarVendaExternaInput("Presencial", null, dados.Hoje.AddDays(-1), "Cliente de teste", null,
            [new(dados.ProdutoId, dados.VarianteId, 1, 900)], null);
        var venda = await admin.PostAsync<ResumoVendaExterna>(vendas, vendaInput, HttpStatusCode.Created, Guid.NewGuid());
        var baixa = await db.Set<BaixaVendaExterna>().AsNoTracking().SingleAsync();
        Assert.Equal(concluida.LoteId, baixa.LoteId);
        await admin.PostAsync<ResumoVendaExterna>($"{vendas}/{venda.Id:D}/cancelar",
            new CancelarVendaExternaInput("Cancelamento de teste"), HttpStatusCode.OK, Guid.NewGuid());
        var posicao = (await admin.Http.GetFromJsonAsync<PosicaoProdutoAcabadoAdminResponse>(estoque + "/posicao"))!;
        Assert.Equal((7, 1, 4), (posicao.SaldoFisico, posicao.SaldoReservado, posicao.SaldoDisponivel));
        var lotes = await db.LotesProdutoAcabado.AsNoTracking().ToDictionaryAsync(l => l.Id);
        Assert.Null(lotes[novo].DataValidade);
        Assert.Equal(OrigemValidadeLoteProdutoAcabado.SemControle, lotes[novo].OrigemValidade);
        Assert.Equal(dados.Hoje.AddDays(-3), lotes[vencido].DataValidade);
        Assert.Null(lotes[bloqueado].DataValidade);
        Assert.Equal(1, await db.ReservasProdutoAcabado.CountAsync());
    }

    [SqlServerIntegrationFact]
    public async Task MigrationEstoqueSemValidade_PreservaLoteHistorico()
    {
        await using var banco = await SqlServerTestDatabase.CreateAsync(targetMigration: "20261120144816_CriarAgendaAdministrativa");
        await using var db = banco.CreateVerificationContext();
        var (_, variante, _) = await CriarProdutoCompravelAsync(db, categoriaAnteriorAVersao: true);
        var fabricacao = new DateOnly(2026, 1, 1);
        var validade = fabricacao.AddDays(30);
        var lote = LoteProdutoAcabado.Criar(variante, null, fabricacao, validade);
        db.LotesProdutoAcabado.Add(lote);
        await db.SaveChangesAsync();

        await db.Database.MigrateAsync();

        var preservado = await db.LotesProdutoAcabado.AsNoTracking().SingleAsync();
        Assert.Equal((lote.Id, lote.ProdutoVarianteId, lote.Codigo, fabricacao, (DateOnly?)validade, lote.OrigemValidade),
            (preservado.Id, preservado.ProdutoVarianteId, preservado.Codigo, preservado.DataFabricacao,
                preservado.DataValidade, preservado.OrigemValidade));
    }
}
