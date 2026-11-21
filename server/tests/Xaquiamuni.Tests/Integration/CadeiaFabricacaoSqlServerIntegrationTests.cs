using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Api.Catalogo;
using Xaquiamuni.Api.Estoque;
using Xaquiamuni.Aplicacao.Estoque;
using Xaquiamuni.Dominio.Estoque;
using Xaquiamuni.Infraestrutura.Identidade;
using Xaquiamuni.Infraestrutura.Persistencia;
using static Xaquiamuni.Tests.Integration.ReservarProdutoAcabadoSqlServerIntegrationTests;

namespace Xaquiamuni.Tests.Integration;

[Trait("Category", "SqlServerIntegration")]
public sealed partial class CadeiaFabricacaoSqlServerIntegrationTests
{
    private const string Compras = "/api/admin/compras-materias-primas";
    private const string Producao = "/api/admin/producao";
    private const string Pedidos = "/api/pedidos";

    [SqlServerIntegrationFact]
    public async Task CadeiaFabricacao_ConciliaCompraProducaoCheckoutVendaExternaEFinanceiro()
    {
        await using var ambiente = await Ambiente.CriarAsync();
        using var admin = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Administrador);
        using var cliente = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Cliente);
        await using var db = ambiente.Banco.CreateVerificationContext();
        var dados = await PrepararCompraAsync(admin, db);
        var catalogo = $"/api/catalogo/produtos/{dados.ProdutoId:D}";
        Assert.False((await cliente.Http.GetFromJsonAsync<ProdutoCatalogoDetalheResponse>(catalogo))!.Disponivel);
        await ConferirFisicoAsync(db, dados, 0, 0, 0);

        var chaveRecebimento = Guid.NewGuid();
        var recebido = await admin.PostAsync<JsonElement>(dados.Receber, dados.Recebimento, HttpStatusCode.OK, chaveRecebimento);
        var replay = await admin.PostAsync<JsonElement>(dados.Receber, dados.Recebimento, HttpStatusCode.OK, chaveRecebimento);
        Assert.Equal(recebido.GetRawText(), replay.GetRawText());
        Assert.Equal("Recebida", recebido.GetProperty("situacao").GetString());
        Assert.Equal(dados.FornecedorId, (await db.ComprasMateriasPrimas.AsNoTracking().SingleAsync()).FornecedorId);
        var recebimentos = await db.EntradasRecebimentoCompra.AsNoTracking().OrderBy(e => e.Ordem).ToArrayAsync();
        Assert.Equal(dados.Compra.Linhas.SelectMany(l => l.Componentes).Select(c => c.Id), recebimentos.Select(e => e.ComponenteId));
        Assert.All(recebimentos, e => Assert.Equal(dados.Compra.Resumo.Id, e.CompraId));
        var entradas = await db.MovimentosInsumo.AsNoTracking().ToDictionaryAsync(m => m.Id);
        Assert.Equal(4, entradas.Count);
        var extrato = entradas[recebimentos[0].MovimentoId];
        Assert.Equal(200m, extrato.QuantidadeBase.ValorBase);
        Assert.Equal(24m, extrato.CustoTotal);
        Assert.All(recebimentos.Skip(2), e => Assert.Null(entradas[e.MovimentoId].CustoTotal));
        Assert.Equal("17.5", dados.Compra.Linhas[2].ValorTotal);
        var loteMaisLongo = entradas[recebimentos[1].MovimentoId].LoteId!.Value;
        var loteMaisCurto = entradas[recebimentos[2].MovimentoId].LoteId!.Value;
        var lotes = await db.LotesInsumo.AsNoTracking().ToDictionaryAsync(l => l.Id);
        Assert.Equal(dados.Recebimento.Validades![0].DataValidade, lotes[loteMaisLongo].DataValidade);
        Assert.Equal(dados.Recebimento.Validades[1].DataValidade, lotes[loteMaisCurto].DataValidade);
        Assert.True(lotes[loteMaisCurto].DataValidade < lotes[loteMaisLongo].DataValidade);
        await ConferirFisicoAsync(db, dados, 200, 18, 6);
        var pagamentoFornecedor = await PagarCompraDaCadeiaAsync(admin, db, dados);

        var receita = await CriarReceitaAsync(admin, dados);
        var previa = await PreviaAsync(admin, receita.Resumo.Id);
        ConferirPrevia(previa, dados, extrato: (100, 0, 200), erva: (12, 0, 18), baseG: (4, 0, 6));
        var inicio = new InicioProducaoInput(receita.Resumo.Id, previa.ReceitaVersaoId, 4);
        var chaveInicio = Guid.NewGuid();
        var ordem = await IniciarAsync(admin, inicio, chaveInicio);
        var inicioRepetido = await admin.PostAsync<JsonElement>(Producao, inicio, HttpStatusCode.OK, chaveInicio);
        Assert.Equal(ordem.Resumo.Id, inicioRepetido.GetProperty("ordemId").GetGuid());
        Assert.Equal(previa.ReceitaVersaoId, (await db.OrdensProducao.AsNoTracking().SingleAsync()).ReceitaVersaoId);
        ConferirPrevia(await PreviaAsync(admin, receita.Resumo.Id), dados, (100, 100, 100), (12, 12, 6), (4, 4, 2));
        await ConferirFisicoAsync(db, dados, 200, 18, 6);
        var antes = await db.AlocacoesMateriasPrimas.AsNoTracking().Include(a => a.Saldo).ToArrayAsync();
        Assert.Equal(8m, Assert.Single(antes, a => a.Saldo.LoteId == loteMaisCurto).Reservada);
        Assert.Equal(4m, Assert.Single(antes, a => a.Saldo.LoteId == loteMaisLongo).Reservada);

        var chaveConclusao = Guid.NewGuid();
        var url = $"{Producao}/{ordem.Resumo.Id:D}/concluir";
        var consumo = Conclusao(ordem, dados);
        var concluida = await admin.PostAsync<ConclusaoProducao>(url, consumo, HttpStatusCode.OK, chaveConclusao);
        var conclusaoRepetida = await admin.PostAsync<ConclusaoProducao>(url, consumo, HttpStatusCode.OK, chaveConclusao);
        Assert.Equal(concluida.MovimentoEntradaId, conclusaoRepetida.MovimentoEntradaId);
        Assert.Equal("Concluida", concluida.Situacao);
        Assert.Equal(3, concluida.QuantidadeProduzida);
        Assert.Equal(25m, Assert.Single(concluida.Consumos, c => c.InsumoId == dados.Extrato).Liberada);
        Assert.Equal(1m, Assert.Single(concluida.Consumos, c => c.InsumoId == dados.Erva).Liberada);
        var alocacoes = await db.AlocacoesMateriasPrimas.AsNoTracking().Include(a => a.Saldo).Include(a => a.Necessidade).ToArrayAsync();
        Assert.Equal(8m, Assert.Single(alocacoes, a => a.Saldo.LoteId == loteMaisCurto).Consumida);
        Assert.Equal(3m, Assert.Single(alocacoes, a => a.Saldo.LoteId == loteMaisLongo).Consumida);
        var movimentos = await db.MovimentosInsumo.AsNoTracking().ToDictionaryAsync(m => m.Id);
        Assert.Equal(8, movimentos.Count);
        Assert.All(alocacoes, a =>
        {
            Assert.Equal(ordem.Resumo.Id, a.Necessidade.OrdemId);
            Assert.InRange(a.Consumida, 0, a.Reservada);
            var movimento = movimentos[a.MovimentoConsumoId!.Value];
            Assert.Equal(a.Necessidade.InsumoId, movimento.InsumoId);
            Assert.Equal(a.Saldo.LoteId, movimento.LoteId);
            Assert.Equal(a.Consumida, movimento.QuantidadeBase.ValorBase);
        });
        ConferirPrevia(await PreviaAsync(admin, receita.Resumo.Id), dados, (100, 0, 125), (12, 0, 7), (4, 0, 2));
        await ConferirFisicoAsync(db, dados, 125, 7, 2);
        var entrada = await db.MovimentosProdutoAcabado.AsNoTracking().SingleAsync();
        Assert.Equal(concluida.MovimentoEntradaId, entrada.Id);
        Assert.Equal(concluida.LoteId, entrada.LoteId);
        Assert.Equal(3, entrada.Quantidade);
        Assert.Equal(CondicaoEstoqueProdutoAcabado.Vendavel, entrada.CondicaoDestino);
        var disponivel = (await cliente.Http.GetFromJsonAsync<ProdutoCatalogoDetalheResponse>(catalogo))!;
        Assert.True(Assert.Single(disponivel.Opcoes, o => o.Id == dados.VarianteId).Disponivel);
        await ConferirAcabadosAsync(admin, dados, livre: 3);

        var carrinho = new { itens = new[] { new { produtoId = dados.ProdutoId, produtoVarianteId = dados.VarianteId, quantidade = 1 } } };
        var chavePedido = Guid.NewGuid();
        var pedido = await cliente.PostAsync<JsonElement>(Pedidos, carrinho, HttpStatusCode.Created, chavePedido);
        var pedidoRepetido = await cliente.PostAsync<JsonElement>(Pedidos, carrinho, HttpStatusCode.OK, chavePedido);
        var pedidoId = pedido.GetProperty("id").GetGuid();
        Assert.Equal("AguardandoPagamento", pedido.GetProperty("situacao").GetString());
        Assert.Equal(pedidoId, pedidoRepetido.GetProperty("id").GetGuid());
        var reserva = await db.ReservasProdutoAcabado.AsNoTracking().SingleAsync();
        Assert.Equal(pedidoId, reserva.PedidoId);
        Assert.Equal(concluida.LoteId, reserva.LoteId);
        Assert.Equal(1, reserva.Quantidade);
        Assert.Equal(EstadoReservaProdutoAcabado.Ativa, reserva.Estado);
        Assert.Equal(pedidoId, (await db.Pedidos.AsNoTracking().SingleAsync()).Id);
        await ConferirAcabadosAsync(admin, dados, livre: 2);
        Assert.Equal(1, await db.MovimentosProdutoAcabado.CountAsync());
        var historicoInsumo = (await admin.Http.GetFromJsonAsync<JsonElement>(
            $"/api/admin/insumos/{dados.Extrato:D}/movimentacoes")).GetProperty("itens");
        var consumoHistorico = Assert.Single(historicoInsumo.EnumerateArray(), m => m.GetProperty("tipo").GetString() == "SaidaManual");
        Assert.Equal(ordem.Resumo.Id, consumoHistorico.GetProperty("ordemProducaoId").GetGuid());
        var aquisicao = Assert.Single(historicoInsumo.EnumerateArray(), m => m.GetProperty("tipo").GetString() == "Aquisicao");
        Assert.Equal(JsonValueKind.Null, aquisicao.GetProperty("ordemProducaoId").ValueKind);
        var historicoAcabado = (await admin.Http.GetFromJsonAsync<JsonElement>(
            $"/api/admin/estoque/produtos-acabados/variantes/{dados.VarianteId:D}/movimentacoes")).GetProperty("itens");
        var entradaHistorico = Assert.Single(historicoAcabado.EnumerateArray());
        Assert.Equal("EntradaManual", entradaHistorico.GetProperty("tipo").GetString());
        Assert.Equal(ordem.Resumo.Id, entradaHistorico.GetProperty("ordemProducaoId").GetGuid());
        await ConciliarComercioEFinanceiroAsync(admin, db, dados, pedidoId, concluida.LoteId, pagamentoFornecedor);
    }

    [SqlServerIntegrationFact]
    public async Task CadeiaFabricacao_HistoricoMantemNomesEIdentificaFallbackLegado()
    {
        await using var ambiente = await Ambiente.CriarAsync();
        using var admin = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Administrador);
        await using var db = ambiente.Banco.CreateVerificationContext();
        var dados = await PrepararCompraAsync(admin, db);
        var (receita, nova) = await ReceberEIniciarAsync(admin, dados);
        await admin.PostAsync<JsonElement>($"{Producao}/{nova.Resumo.Id:D}/cancelar", new { },
            HttpStatusCode.OK, Guid.NewGuid());
        var previa = await PreviaAsync(admin, receita);
        var legada = await IniciarAsync(admin, new(receita, previa.ReceitaVersaoId, 4), Guid.NewGuid());
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE OrdensProducao SET NomeProduto = NULL, NomeVariante = NULL WHERE Id = {legada.Resumo.Id}");

        var produto = await db.Produtos.Include(p => p.Variantes).SingleAsync(p => p.Id == dados.ProdutoId);
        produto.AtualizarDados("Sabonete renomeado", null);
        var variante = Assert.Single(produto.Variantes, v => v.Id == dados.VarianteId);
        variante.AtualizarDados("Variante renomeada", "UN", variante.PrecoVenda);
        await db.SaveChangesAsync();
        var lista = (await admin.Http.GetFromJsonAsync<ResumoOrdemProducao[]>(Producao))!;
        var historica = Assert.Single(lista, o => o.Id == nova.Resumo.Id);
        var atual = Assert.Single(lista, o => o.Id == legada.Resumo.Id);
        Assert.False(historica.NomesAtuais);
        Assert.Equal((nova.Resumo.Produto, nova.Resumo.Variante), (historica.Produto, historica.Variante));
        Assert.True(atual.NomesAtuais);
        Assert.Equal(("Sabonete renomeado", "Variante renomeada"), (atual.Produto, atual.Variante));
        foreach (var resumo in new[] { historica, atual })
        {
            var detalhe = (await admin.Http.GetFromJsonAsync<DetalheOrdemProducao>($"{Producao}/{resumo.Id:D}"))!;
            Assert.Equal(resumo, detalhe.Resumo);
            Assert.Equal(dados.ProdutoId, detalhe.Resumo.ProdutoId);
            Assert.Equal(dados.VarianteId, detalhe.VarianteId);
            Assert.Equal(previa.Versao, detalhe.Resumo.Versao);
        }
    }

    [SqlServerIntegrationFact]
    public async Task CadeiaFabricacao_RecebimentoDisputaComInicioSemReservaParcial()
    {
        await using var ambiente = await Ambiente.CriarAsync();
        using var admin = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Administrador);
        await using var db = ambiente.Banco.CreateVerificationContext();
        var dados = await PrepararCompraAsync(admin, db);
        var receita = await CriarReceitaAsync(admin, dados);
        var previa = await PreviaAsync(admin, receita.Resumo.Id);
        var inicio = new InicioProducaoInput(receita.Resumo.Id, previa.ReceitaVersaoId, 4);
        var chave = Guid.NewGuid();
        var respostas = await DisputarAsync(
            () => admin.EnviarAsync(dados.Receber, dados.Recebimento, Guid.NewGuid()),
            () => admin.EnviarAsync(Producao, inicio, chave));
        using var recebido = respostas[0];
        using var tentativa = respostas[1];
        Assert.Equal(HttpStatusCode.OK, recebido.StatusCode);
        Assert.Contains(tentativa.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.Conflict });
        if (tentativa.StatusCode == HttpStatusCode.Conflict)
        {
            Assert.False(await db.AlocacoesMateriasPrimas.AnyAsync());
            await IniciarAsync(admin, inicio, chave);
        }
        await ConferirFisicoAsync(db, dados, 200, 18, 6);
        Assert.Equal(4, await db.MovimentosInsumo.CountAsync());
        ConferirPrevia(await PreviaAsync(admin, receita.Resumo.Id), dados, (100, 100, 100), (12, 12, 6), (4, 4, 2));
        Assert.Equal(1, await db.OrdensProducao.CountAsync());
        Assert.Equal(4, await db.AlocacoesMateriasPrimas.CountAsync());
        Assert.False(await db.AlocacoesMateriasPrimas.AnyAsync(a => a.Reservada <= 0 || a.Consumida != 0));
        Assert.False(await db.MovimentosProdutoAcabado.AnyAsync());
    }

    [SqlServerIntegrationFact]
    public async Task CadeiaFabricacao_DuasOrdensDisputamSaldoRecebido()
    {
        await using var ambiente = await Ambiente.CriarAsync();
        using var admin = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Administrador);
        await using var db = ambiente.Banco.CreateVerificationContext();
        var dados = await PrepararCompraAsync(admin, db);
        await admin.PostAsync<JsonElement>(dados.Receber, dados.Recebimento, HttpStatusCode.OK, Guid.NewGuid());
        var receita = await CriarReceitaAsync(admin, dados);
        var previa = await PreviaAsync(admin, receita.Resumo.Id);
        var inicio = new InicioProducaoInput(receita.Resumo.Id, previa.ReceitaVersaoId, 4);
        var disputa = await DisputarAsync(
            () => admin.EnviarAsync(Producao, inicio, Guid.NewGuid()),
            () => admin.EnviarAsync(Producao, inicio, Guid.NewGuid()));
        using var primeira = disputa[0];
        using var segunda = disputa[1];
        Assert.Single(disputa, r => r.StatusCode == HttpStatusCode.Created);
        Assert.Single(disputa, r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal(1, await db.OrdensProducao.CountAsync(o => o.Situacao == SituacaoOrdemProducao.EmAndamento));
        Assert.Equal(1, await db.OrdensProducao.CountAsync());
        Assert.Equal(4, await db.AlocacoesMateriasPrimas.CountAsync());
        Assert.False(await db.AlocacoesMateriasPrimas.AnyAsync(a => a.Reservada <= 0 || a.Consumida != 0));
        ConferirPrevia(await PreviaAsync(admin, receita.Resumo.Id), dados, (100, 100, 100), (12, 12, 6), (4, 4, 2));
        await ConferirFisicoAsync(db, dados, 200, 18, 6);
        Assert.Equal(4, await db.MovimentosInsumo.CountAsync());
        Assert.False(await db.MovimentosProdutoAcabado.AnyAsync());
    }

    [SqlServerIntegrationFact]
    public async Task CadeiaFabricacao_CancelamentoDisputaComConclusaoSemBaixasParciais()
    {
        await using var ambiente = await Ambiente.CriarAsync();
        using var admin = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Administrador);
        await using var db = ambiente.Banco.CreateVerificationContext();
        var dados = await PrepararCompraAsync(admin, db);
        var (receita, ordem) = await ReceberEIniciarAsync(admin, dados);
        var url = $"{Producao}/{ordem.Resumo.Id:D}";
        var respostas = await DisputarAsync(
            () => admin.EnviarAsync(url + "/cancelar", new { }, Guid.NewGuid()),
            () => admin.EnviarAsync(url + "/concluir", Conclusao(ordem, dados), Guid.NewGuid()));
        using var cancelamento = respostas[0];
        using var conclusao = respostas[1];
        Assert.Single(respostas, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(respostas, r => r.StatusCode == HttpStatusCode.Conflict);
        var situacao = await db.OrdensProducao.Select(o => o.Situacao).SingleAsync();
        if (situacao == SituacaoOrdemProducao.Concluida)
        {
            Assert.Equal(HttpStatusCode.OK, conclusao.StatusCode);
            Assert.Equal(8, await db.MovimentosInsumo.CountAsync());
            Assert.Equal(1, await db.MovimentosProdutoAcabado.CountAsync());
            await ConferirFisicoAsync(db, dados, 125, 7, 2);
        }
        else
        {
            Assert.Equal(SituacaoOrdemProducao.Cancelada, situacao);
            Assert.Equal(HttpStatusCode.OK, cancelamento.StatusCode);
            Assert.Equal(4, await db.MovimentosInsumo.CountAsync());
            Assert.False(await db.MovimentosProdutoAcabado.AnyAsync());
            Assert.False(await db.AlocacoesMateriasPrimas.AnyAsync(a => a.Consumida != 0));
            await ConferirFisicoAsync(db, dados, 200, 18, 6);
        }
        Assert.All((await PreviaAsync(admin, receita)).Materiais, m => Assert.Equal(0m, m.Reservado));
        Assert.False(await db.AlocacoesMateriasPrimas.AnyAsync(a => a.Consumida < 0 || a.Consumida > a.Reservada));
    }

    [SqlServerIntegrationFact]
    public async Task CadeiaFabricacao_FalhaNaEntradaReverteConsumoEMantemReservas()
    {
        await using var ambiente = await Ambiente.CriarAsync();
        using var admin = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Administrador);
        await using var db = ambiente.Banco.CreateVerificationContext();
        var dados = await PrepararCompraAsync(admin, db);
        var (receita, ordem) = await ReceberEIniciarAsync(admin, dados);
        // A conclusão já executou SaveChanges dos consumos quando tenta inserir este lote.
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER [FalhaLoteCadeiaTeste] ON [LotesProdutoAcabado] AFTER INSERT
            AS THROW 51017, 'Falha de teste na entrada de acabado', 1;
            """);
        var chave = Guid.NewGuid();
        var url = $"{Producao}/{ordem.Resumo.Id:D}/concluir";
        using var falha = await admin.EnviarAsync(url, Conclusao(ordem, dados), chave);
        Assert.Equal(HttpStatusCode.InternalServerError, falha.StatusCode);
        Assert.Equal(SituacaoOrdemProducao.EmAndamento, await db.OrdensProducao.Select(o => o.Situacao).SingleAsync());
        Assert.Equal(4, await db.MovimentosInsumo.CountAsync());
        Assert.False(await db.AlocacoesMateriasPrimas.AnyAsync(a => a.Consumida != 0 || a.MovimentoConsumoId != null));
        Assert.False(await db.LotesProdutoAcabado.AnyAsync());
        Assert.False(await db.MovimentosProdutoAcabado.AnyAsync());
        Assert.False(await db.SaldosProdutoAcabado.AnyAsync());
        await ConferirFisicoAsync(db, dados, 200, 18, 6);
        ConferirPrevia(await PreviaAsync(admin, receita), dados, (100, 100, 100), (12, 12, 6), (4, 4, 2));
        await db.Database.ExecuteSqlRawAsync("DROP TRIGGER [FalhaLoteCadeiaTeste]");
        await admin.PostAsync<ConclusaoProducao>(url, Conclusao(ordem, dados), HttpStatusCode.OK, chave);
        await ConferirFisicoAsync(db, dados, 125, 7, 2);
        Assert.Equal(1, await db.MovimentosProdutoAcabado.CountAsync());
    }

    private static async Task<Dados> PrepararCompraAsync(Sessao admin, XaquiamuniDbContext db, bool custoParcial = false)
    {
        var fornecedor = await admin.PostAsync<JsonElement>("/api/admin/fornecedores",
            new { nome = "Fornecedora sintética" }, HttpStatusCode.Created);
        var fornecedorId = fornecedor.GetProperty("id").GetGuid();
        var (produto, variante, _) = await CriarProdutoCompravelAsync(db);
        var extrato = InsumoEstoque.Criar("Extrato sintético", TipoInsumoEstoque.MateriaPrima, UnidadeFisicaEstoque.Mililitro);
        var forma = extrato.AdicionarFormaCompra("Frasco 100 ml", 100, UnidadeFisicaEstoque.Mililitro);
        var erva = InsumoEstoque.Criar("Erva sintética", TipoInsumoEstoque.MateriaPrima, UnidadeFisicaEstoque.Grama,
            controlaLote: true, validadeObrigatoria: true);
        var baseG = InsumoEstoque.Criar("Base sintética", TipoInsumoEstoque.MateriaPrima, UnidadeFisicaEstoque.Grama);
        db.InsumosEstoque.AddRange(extrato, erva, baseG);
        await db.SaveChangesAsync();
        var hoje = DateOnly.FromDateTime(DateTime.UtcNow);
        var compra = await admin.PostAsync<AdminComprasMateriasPrimasEndpoints.DetalheCompra>(Compras,
            new CompraMateriaPrimaInput(fornecedorId, hoje, [
                new(null, 2, 24, [new(extrato.Id, forma.Id, 2, null)]),
                new(null, 10, custoParcial ? null : 5m, [new(erva.Id, null, 10, null)]),
                new("Kit sintético", 1, 17.5m, [new(erva.Id, null, 8, null), new(baseG.Id, null, 6, null)]),
            ], null), HttpStatusCode.Created, Guid.NewGuid());
        var recebimento = new ReceberCompraMateriaPrimaInput(compra.Versao, [
            new(compra.Linhas[1].Componentes[0].Id, hoje.AddDays(20)),
            new(compra.Linhas[2].Componentes[0].Id, hoje.AddDays(10)),
        ]);
        return new(fornecedorId, produto.Id, variante.Id, extrato.Id, erva.Id, baseG.Id, hoje, compra, recebimento);
    }

    private static Task<AdminReceitasProducaoEndpoints.DetalheReceita> CriarReceitaAsync(Sessao admin, Dados d) =>
        admin.PostAsync<AdminReceitasProducaoEndpoints.DetalheReceita>("/api/admin/receitas",
            new ReceitaInput(d.VarianteId, "Receita integrada", 2,
                [new(d.Extrato, "50", "Mililitro"), new(d.Erva, "6", "Grama"), new(d.Base, "2", "Grama")], null),
            HttpStatusCode.Created);

    private static async Task<(Guid Receita, DetalheOrdemProducao Ordem)> ReceberEIniciarAsync(Sessao admin, Dados dados)
    {
        await admin.PostAsync<JsonElement>(dados.Receber, dados.Recebimento, HttpStatusCode.OK, Guid.NewGuid());
        var receita = (await CriarReceitaAsync(admin, dados)).Resumo.Id;
        var previa = await PreviaAsync(admin, receita);
        return (receita, await IniciarAsync(admin, new(receita, previa.ReceitaVersaoId, 4), Guid.NewGuid()));
    }

    private static async Task<DetalheOrdemProducao> IniciarAsync(Sessao admin, InicioProducaoInput input, Guid chave)
    {
        var criada = await admin.PostAsync<JsonElement>(Producao, input, HttpStatusCode.Created, chave);
        return (await admin.Http.GetFromJsonAsync<DetalheOrdemProducao>($"{Producao}/{criada.GetProperty("ordemId").GetGuid():D}"))!;
    }

    private static ConcluirProducaoInput Conclusao(DetalheOrdemProducao ordem, Dados dados) =>
        new(ordem.Resumo.VersaoOrdem, 3, [new(dados.Extrato, "Mililitro", "75"), new(dados.Erva, "Grama", "11"),
            new(dados.Base, "Grama", "4")], null, dados.Hoje.AddDays(30));

    private static async Task<PreviaProducao> PreviaAsync(Sessao admin, Guid receita) =>
        (await admin.Http.GetFromJsonAsync<PreviaProducao>($"{Producao}/previa?receitaId={receita:D}&quantidade=4"))!;

    private static void ConferirPrevia(PreviaProducao previa, Dados dados, (decimal, decimal, decimal) extrato,
        (decimal, decimal, decimal) erva, (decimal, decimal, decimal) baseG)
    {
        foreach (var (id, esperado) in new[] { (dados.Extrato, extrato), (dados.Erva, erva), (dados.Base, baseG) })
        {
            var item = Assert.Single(previa.Materiais, m => m.InsumoId == id);
            Assert.Equal(esperado, (item.Necessario, item.Reservado, item.Livre));
            Assert.Equal(item.Reservado + item.Livre, item.Utilizavel);
        }
    }

    private static async Task ConferirFisicoAsync(XaquiamuniDbContext db, Dados dados, decimal extrato, decimal erva, decimal baseG)
    {
        var saldos = await db.SaldosInsumo.AsNoTracking().ToArrayAsync();
        foreach (var (id, esperado) in new[] { (dados.Extrato, extrato), (dados.Erva, erva), (dados.Base, baseG) })
            Assert.Equal(esperado, saldos.Where(s => s.InsumoId == id).Sum(s => s.QuantidadeFisica.ValorBase));
    }

    private static async Task ConferirAcabadosAsync(Sessao admin, Dados dados, int livre)
    {
        var resposta = (await admin.Http.GetFromJsonAsync<PosicaoProdutoAcabadoAdminResponse>(
            $"/api/admin/estoque/produtos-acabados/variantes/{dados.VarianteId:D}/posicao"))!;
        Assert.Equal(3, resposta.SaldoFisico);
        Assert.Equal(3 - livre, resposta.SaldoReservado);
        Assert.Equal(livre, resposta.SaldoDisponivel);
    }

    private static Task<HttpResponseMessage[]> DisputarAsync(params Func<Task<HttpResponseMessage>>[] comandos)
    {
        var largada = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tarefas = comandos.Select(async comando => { await largada.Task; return await comando(); }).ToArray();
        largada.SetResult();
        return Task.WhenAll(tarefas);
    }

    private sealed record Dados(Guid FornecedorId, Guid ProdutoId, Guid VarianteId, Guid Extrato, Guid Erva, Guid Base,
        DateOnly Hoje, AdminComprasMateriasPrimasEndpoints.DetalheCompra Compra, ReceberCompraMateriaPrimaInput Recebimento)
    {
        public string Receber => $"{Compras}/{Compra.Resumo.Id:D}/receber";
    }

    private sealed class Ambiente(SqlServerTestDatabase banco, HostE2ECompra host, DirectoryInfo diretorio) : IAsyncDisposable
    {
        public SqlServerTestDatabase Banco { get; } = banco;
        public HostE2ECompra Host { get; } = host;

        public static async Task<Ambiente> CriarAsync()
        {
            var banco = await SqlServerTestDatabase.CreateAsync();
            DirectoryInfo? diretorio = null;
            try
            {
                diretorio = Directory.CreateTempSubdirectory("xaquiamuni-cadeia-");
                return new(banco, new HostE2ECompra(banco, diretorio.FullName, diretorio.FullName), diretorio);
            }
            catch
            {
                try { await banco.DisposeAsync(); }
                finally { diretorio?.Delete(recursive: true); }
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            try { await Host.DisposeAsync(); }
            finally
            {
                try { await Banco.DisposeAsync(); }
                finally { diretorio.Delete(recursive: true); }
            }
        }
    }

    [SqlServerIntegrationFact]
    public async Task GerenciarOpcoes_DeProdutoSimplesPreservaLoteReservaEPrecoDoPedido()
    {
        await using var ambiente = await Ambiente.CriarAsync();
        using var admin = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Administrador);
        using var cliente = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Cliente);
        await using var db = ambiente.Banco.CreateVerificationContext();
        var (produto, variante, alternativa) = await CriarProdutoCompravelAsync(db);
        var rota = $"/api/admin/produtos/{produto.Id:D}";
        var rotaVariante = $"{rota}/variantes/{variante.Id:D}";
        var csrf = await admin.Http.GetFromJsonAsync<JsonElement>("/api/auth/csrf");

        async Task<JsonElement> EditarAsync(string url, object corpo, HttpMethod? metodo = null)
        {
            using var request = new HttpRequestMessage(metodo ?? HttpMethod.Put, url) { Content = JsonContent.Create(corpo) };
            request.Headers.Add("X-XSRF-TOKEN", csrf.GetProperty("requestToken").GetString());
            using var resposta = await admin.Http.SendAsync(request);
            Assert.True(resposta.IsSuccessStatusCode, await resposta.Content.ReadAsStringAsync());
            return await resposta.Content.ReadFromJsonAsync<JsonElement>();
        }

        await EditarAsync($"{rota}/variantes/{alternativa.Id:D}/status", new { ativa = false }, HttpMethod.Patch);
        await EditarAsync(rotaVariante, new
        {
            identificacao = variante.Identificacao, unidadeVenda = variante.UnidadeVenda,
            precoVenda = variante.PrecoVenda, pesoLiquidoGramas = 100, atualizarMedidas = true,
        });
        await admin.PostAsync<JsonElement>($"/api/admin/estoque/produtos-acabados/variantes/{variante.Id:D}/entradas",
            new { tipo = "EntradaManual", quantidade = 2, condicaoDestino = "Vendavel", semControleValidade = true, motivo = "Ensaio" },
            HttpStatusCode.Created, Guid.NewGuid());
        var pedido = await cliente.PostAsync<JsonElement>(Pedidos,
            new { itens = new[] { new { produtoId = produto.Id, produtoVarianteId = variante.Id, quantidade = 1 } } },
            HttpStatusCode.Created, Guid.NewGuid());
        var reservaAnterior = await db.ReservasProdutoAcabado.AsNoTracking().SingleAsync();

        var caracteristica = (await EditarAsync(rota, new
        {
            caracteristicas = new[] { new { nome = "Peso", tipoMedidaFisica = "PesoLiquido",
                valores = new[] { new { valorMedidaCanonico = 200 }, new { valorMedidaCanonico = 300 } } } },
        })).GetProperty("caracteristicas")[0];
        object[] Vinculo(int medida) => [new
        {
            caracteristicaId = caracteristica.GetProperty("id").GetGuid(),
            valorId = caracteristica.GetProperty("valores").EnumerateArray()
                .Single(v => v.GetProperty("valorMedidaCanonico").GetDecimal() == medida).GetProperty("id").GetGuid(),
        }];
        await EditarAsync(rotaVariante, new
        {
            identificacao = "Porcao de 200 g", unidadeVenda = variante.UnidadeVenda, precoVenda = 39m,
            pesoLiquidoGramas = 200, atualizarMedidas = true, caracteristicas = Vinculo(200),
        });
        var outra = await admin.PostAsync<JsonElement>($"{rota}/variantes",
            new { identificacao = "Porcao de 300 g", unidadeVenda = "KIT", precoVenda = 56m, caracteristicas = Vinculo(300) },
            HttpStatusCode.OK);
        Assert.Equal(produto.Nome, outra.GetProperty("nome").GetString());
        var atual = await EditarAsync(rota, new { nome = "Nome atualizado", descricao = "Nova descricao", categoriaId = produto.CategoriaId });
        Assert.Single(atual.GetProperty("caracteristicas").EnumerateArray());
        var primeira = atual.GetProperty("variantes").EnumerateArray().Single(v => v.GetProperty("id").GetGuid() == variante.Id);
        Assert.Equal(200m, primeira.GetProperty("pesoLiquidoGramas").GetDecimal());
        Assert.Equal(39m, primeira.GetProperty("precoVenda").GetDecimal());

        var reserva = await db.ReservasProdutoAcabado.AsNoTracking().SingleAsync();
        Assert.Equal(reservaAnterior.Id, reserva.Id);
        Assert.Equal(reservaAnterior.LoteId, reserva.LoteId);
        Assert.Equal(1, reserva.Quantidade);
        Assert.Equal(pedido.GetProperty("id").GetGuid(), reserva.PedidoId);
        Assert.Single(await db.MovimentosProdutoAcabado.AsNoTracking().ToListAsync());
        var pedidoPersistido = await db.Pedidos.AsNoTracking().Include(p => p.Itens).SingleAsync();
        Assert.Equal(1199L, Assert.Single(pedidoPersistido.Itens).PrecoUnitarioCentavos);
        var publico = (await cliente.Http.GetFromJsonAsync<ProdutoCatalogoDetalheResponse>($"/api/catalogo/produtos/{produto.Id:D}"))!;
        Assert.True(publico.Opcoes.Single(o => o.Id == variante.Id).Disponivel);
        Assert.False(publico.Opcoes.Single(o => o.Id != variante.Id).Disponivel);
    }

    private sealed class Sessao(HttpClient http) : IDisposable
    {
        public HttpClient Http { get; } = http;

        public static async Task<Sessao> EntrarAsync(HostE2ECompra host, string papel)
        {
            var conta = await host.CriarContaAsync(papel);
            var origin = new Uri(host.Origin);
            var http = new HttpClient(new HttpClientHandler
            {
                AllowAutoRedirect = false,
                UseProxy = false,
                ServerCertificateCustomValidationCallback = (request, certificado, _, _) =>
                    request.RequestUri == new Uri(origin, request.RequestUri!.PathAndQuery) &&
                    certificado is not null &&
                    Convert.ToBase64String(SHA256.HashData(certificado.PublicKey.ExportSubjectPublicKeyInfo())) == host.PinSpki,
            }) { BaseAddress = origin };
            var sessao = new Sessao(http);
            try
            {
                var rota = papel == XaquiamuniRoles.Administrador ? "/api/admin/auth/login" : "/api/auth/login";
                using var login = await sessao.EnviarAsync(rota, new { conta.Email, conta.Senha });
                Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
                return sessao;
            }
            catch
            {
                sessao.Dispose();
                throw;
            }
        }

        public async Task<HttpResponseMessage> EnviarAsync(string url, object corpo, Guid? chave = null)
        {
            var csrf = await Http.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
            using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(corpo) };
            request.Headers.Add("X-XSRF-TOKEN", csrf.GetProperty("requestToken").GetString());
            if (chave.HasValue) request.Headers.Add("Idempotency-Key", chave.Value.ToString("D"));
            return await Http.SendAsync(request);
        }

        public async Task<T> PostAsync<T>(string url, object corpo, HttpStatusCode esperado, Guid? chave = null)
        {
            using var resposta = await EnviarAsync(url, corpo, chave);
            Assert.True(resposta.StatusCode == esperado,
                $"{url}: esperado {(int)esperado}, recebido {(int)resposta.StatusCode}: {await resposta.Content.ReadAsStringAsync()}");
            return (await resposta.Content.ReadFromJsonAsync<T>())!;
        }

        public void Dispose() => Http.Dispose();
    }
}
