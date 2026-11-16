using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xaquiamuni.Aplicacao.Estoque;
using Xaquiamuni.Dominio.Estoque;
using Xaquiamuni.Infraestrutura.Persistencia;

namespace Xaquiamuni.Tests.Integration;

[Trait("Category", "SqlServerIntegration")]
public sealed class ConsultarEstoqueInsumoSqlServerIntegrationTests
{
    private static readonly DateOnly DataReferencia = new(2026, 11, 16);
    private static readonly Guid ResponsavelId = Guid.Parse("280282b9-a942-4625-981e-b30b20ce5104");

    [SqlServerIntegrationFact]
    public async Task ListAndDetail_SeparatePhysicalEligibleAndAvailableBalancesWithoutWrites()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateVerificationContext();

        var aggregated = InsumoEstoque.Criar(
            "Base A",
            TipoInsumoEstoque.MateriaPrima,
            UnidadeFisicaEstoque.Grama,
            QuantidadeEstoque.Criar(5m, UnidadeFisicaEstoque.Grama));
        var lotTracked = InsumoEstoque.Criar(
            "Cera B",
            TipoInsumoEstoque.MateriaPrima,
            UnidadeFisicaEstoque.Grama,
            QuantidadeEstoque.Criar(10m, UnidadeFisicaEstoque.Grama),
            controlaLote: true,
            validadeObrigatoria: true);
        lotTracked.AdicionarFormaCompra("Saco 1 kg", 1m, UnidadeFisicaEstoque.Quilograma);
        var noMinimum = InsumoEstoque.Criar(
            "Essência C",
            TipoInsumoEstoque.MateriaPrima,
            UnidadeFisicaEstoque.Grama);
        var empty = InsumoEstoque.Criar(
            "Potes D",
            TipoInsumoEstoque.Embalagem,
            UnidadeFisicaEstoque.Unidade);
        empty.DefinirAtivo(false);
        aggregated.AdicionarFormaCompra("Pacote 500 g", 500m, UnidadeFisicaEstoque.Grama);
        context.InsumosEstoque.AddRange(aggregated, lotTracked, noMinimum, empty);
        await context.SaveChangesAsync();

        var entradas = database.Services.GetRequiredService<IEstoqueInsumoRepository>();
        await RegistrarEntradaAsync(entradas, aggregated.Id, 5.000001m, null, "base");
        await RegistrarEntradaAsync(entradas, noMinimum.Id, 2.123456m, null, "essencia");

        var elegivel = await RegistrarEntradaAsync(
            entradas,
            lotTracked.Id,
            10m,
            "LOTE-VALIDO",
            "valido",
            DataReferencia);
        var vencido = await RegistrarEntradaAsync(
            entradas,
            lotTracked.Id,
            20m,
            "LOTE-VENCIDO",
            "vencido",
            DataReferencia.AddDays(-1));
        var bloqueado = await RegistrarEntradaAsync(
            entradas,
            lotTracked.Id,
            30m,
            "LOTE-BLOQUEADO",
            "bloqueado",
            DataReferencia.AddDays(10),
            bloqueado: true);
        context.LotesInsumo.Add(LoteInsumo.Criar(
            lotTracked,
            "LOTE-SEM-SALDO",
            DataReferencia,
            DataReferencia.AddDays(20)));
        await context.SaveChangesAsync();

        var consulta = database.Services.GetRequiredService<IEstoqueInsumoConsultaRepository>();
        var all = await consulta.ListarInsumosAsync(
            new ListarInsumosEstoqueFiltro(Pagina: 1, ItensPorPagina: 2),
            DataReferencia);
        Assert.Equal(4, all.TotalItens);
        Assert.Equal(new[] { "Base A", "Cera B" }, all.Itens.Select(item => item.Nome));
        Assert.Equal(5.000001m, all.Itens[0].SaldoFisico);
        Assert.Equal(5.000001m, all.Itens[0].SaldoUtilizavel);
        Assert.Equal(5.000001m, all.Itens[0].SaldoDisponivel);
        Assert.Equal(0m, all.Itens[0].SaldoReservado);
        Assert.Equal(SituacaoEstoqueInsumo.Normal, all.Itens[0].Situacao);
        Assert.Equal(60m, all.Itens[1].SaldoFisico);
        Assert.Equal(10m, all.Itens[1].SaldoUtilizavel);
        Assert.Equal(10m, all.Itens[1].SaldoDisponivel);
        Assert.Equal(SituacaoEstoqueInsumo.AbaixoOuNoMinimo, all.Itens[1].Situacao);

        var nextPage = await consulta.ListarInsumosAsync(
            new ListarInsumosEstoqueFiltro(Pagina: 2, ItensPorPagina: 2),
            DataReferencia);
        Assert.Equal(new[] { "Essência C", "Potes D" }, nextPage.Itens.Select(item => item.Nome));
        Assert.Equal(SituacaoEstoqueInsumo.MinimoNaoConfigurado, nextPage.Itens[0].Situacao);
        Assert.Equal(SituacaoEstoqueInsumo.SemEstoque, nextPage.Itens[1].Situacao);

        var filtered = await consulta.ListarInsumosAsync(
            new ListarInsumosEstoqueFiltro(
                Nome: "base",
                Ativo: true,
                Situacao: SituacaoEstoqueInsumo.Normal),
            DataReferencia);
        Assert.Equal(1, filtered.TotalItens);
        Assert.Equal(aggregated.Id, Assert.Single(filtered.Itens).InsumoId);

        var noMinimumPage = await consulta.ListarInsumosAsync(
            new ListarInsumosEstoqueFiltro(Situacao: SituacaoEstoqueInsumo.MinimoNaoConfigurado),
            DataReferencia);
        Assert.Equal(
            SituacaoEstoqueInsumo.MinimoNaoConfigurado,
            noMinimumPage.Itens.Single(item => item.Nome == "Essência C").Situacao);
        var semEstoque = await consulta.ListarInsumosAsync(
            new ListarInsumosEstoqueFiltro(Situacao: SituacaoEstoqueInsumo.SemEstoque),
            DataReferencia);
        Assert.Equal(empty.Id, Assert.Single(semEstoque.Itens).InsumoId);

        var detalheAgregado = await consulta.ConsultarInsumoAsync(aggregated.Id, DataReferencia);
        Assert.NotNull(detalheAgregado);
        var posicaoAgregada = Assert.Single(detalheAgregado!.Posicoes);
        Assert.Null(posicaoAgregada.LoteId);
        Assert.True(posicaoAgregada.TemRegistroSaldo);
        Assert.Equal(5.000001m, posicaoAgregada.SaldoFisico);
        var inativos = await consulta.ListarInsumosAsync(new ListarInsumosEstoqueFiltro(Ativo: false), DataReferencia);
        Assert.Equal(empty.Id, Assert.Single(inativos.Itens).InsumoId);

        var detail = await consulta.ConsultarInsumoAsync(lotTracked.Id, DataReferencia);
        Assert.NotNull(detail);
        Assert.Single(detail!.FormasCompra);
        Assert.Equal(4, detail.Posicoes.Count);
        var positionValida = detail.Posicoes.Single(item => item.LoteId == elegivel.Movimento!.LoteId);
        Assert.True(positionValida.Elegivel);
        Assert.Equal(10m, positionValida.SaldoFisico);
        Assert.Equal(10m, positionValida.SaldoUtilizavel);
        Assert.Equal(0m, positionValida.SaldoReservado);
        var positionVencida = detail.Posicoes.Single(item => item.LoteId == vencido.Movimento!.LoteId);
        Assert.False(positionVencida.Elegivel);
        Assert.Equal(20m, positionVencida.SaldoFisico);
        Assert.Equal(0m, positionVencida.SaldoUtilizavel);
        var positionBloqueada = detail.Posicoes.Single(item => item.LoteId == bloqueado.Movimento!.LoteId);
        Assert.False(positionBloqueada.Elegivel);
        Assert.Equal(30m, positionBloqueada.SaldoFisico);
        Assert.Equal(0m, positionBloqueada.SaldoDisponivel);
        var semSaldo = detail.Posicoes.Single(item => item.IdentificacaoLote == "LOTE-SEM-SALDO");
        Assert.False(semSaldo.TemRegistroSaldo);
        Assert.Equal(0m, semSaldo.SaldoFisico);

        var countBefore = await context.MovimentosInsumo.CountAsync();
        var balancesBefore = await context.SaldosInsumo
            .OrderBy(item => item.InsumoId)
            .ThenBy(item => item.LoteId)
            .Select(item => new { item.InsumoId, item.LoteId, item.QuantidadeFisica.ValorBase })
            .ToArrayAsync();
        await consulta.ConsultarInsumoAsync(lotTracked.Id, DataReferencia);
        await consulta.ListarMovimentosAsync(new ListarMovimentosInsumoFiltro(aggregated.Id));
        Assert.Equal(countBefore, await context.MovimentosInsumo.CountAsync());
        Assert.Equal(
            balancesBefore,
            await context.SaldosInsumo
                .OrderBy(item => item.InsumoId)
                .ThenBy(item => item.LoteId)
                .Select(item => new { item.InsumoId, item.LoteId, item.QuantidadeFisica.ValorBase })
                .ToArrayAsync());
    }

    [SqlServerIntegrationFact]
    public async Task MovementHistory_IsPagedDeterministicallyAndIncludesAllMovementKindsAndReversalReference()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateVerificationContext();
        var insumo = InsumoEstoque.Criar(
            "Base histórica",
            TipoInsumoEstoque.MateriaPrima,
            UnidadeFisicaEstoque.Grama);
        var formaCompra = insumo.AdicionarFormaCompra("Pacote 2 g", 2m, UnidadeFisicaEstoque.Grama);
        context.InsumosEstoque.Add(insumo);
        await context.SaveChangesAsync();
        var repository = database.Services.GetRequiredService<IEstoqueInsumoRepository>();

        var entrada = await RegistrarEntradaAsync(repository, insumo.Id, 20m, null, "historico");
        var aquisicao = await RegistrarEntradaAsync(
            repository,
            insumo.Id,
            1m,
            null,
            "historico-aquisicao",
            tipo: TipoMovimentoInsumo.Aquisicao,
            formaCompraId: formaCompra.Id);
        var saida = await repository.RegistrarSaidaAsync(new RegistrarSaidaInsumoInput(
            insumo.Id,
            ResponsavelId,
            TipoMovimentoInsumo.SaidaManual,
            2m,
            UnidadeFisicaEstoque.Grama,
            null,
            "Uso operacional",
            "historico-saida"));
        Assert.True(saida.Sucesso, saida.Erro);

        var versao = await context.SaldosInsumo
            .Where(saldo => saldo.InsumoId == insumo.Id)
            .Select(saldo => EF.Property<byte[]>(saldo, "Versao"))
            .SingleAsync();
        var ajuste = await repository.AjustarContagemAsync(new AjustarContagemInsumoInput(
            insumo.Id,
            ResponsavelId,
            17m,
            UnidadeFisicaEstoque.Grama,
            null,
            versao,
            "Contagem",
            "historico-ajuste"));
        Assert.True(ajuste.Sucesso, ajuste.Erro);

        var perda = await repository.RegistrarSaidaAsync(new RegistrarSaidaInsumoInput(
            insumo.Id,
            ResponsavelId,
            TipoMovimentoInsumo.Perda,
            1m,
            UnidadeFisicaEstoque.Grama,
            null,
            "Perda",
            "historico-perda"));
        Assert.True(perda.Sucesso, perda.Erro);
        var descarte = await repository.RegistrarSaidaAsync(new RegistrarSaidaInsumoInput(
            insumo.Id,
            ResponsavelId,
            TipoMovimentoInsumo.Descarte,
            1m,
            UnidadeFisicaEstoque.Grama,
            null,
            "Descarte",
            "historico-descarte"));
        Assert.True(descarte.Sucesso, descarte.Erro);
        var estorno = await repository.EstornarMovimentoAsync(new EstornarMovimentoInsumoInput(
            saida.Movimento!.Id,
            ResponsavelId,
            "Estorno de saída",
            "historico-estorno"));
        Assert.True(estorno.Sucesso, estorno.Erro);

        var consulta = database.Services.GetRequiredService<IEstoqueInsumoConsultaRepository>();
        var firstPage = await consulta.ListarMovimentosAsync(
            new ListarMovimentosInsumoFiltro(insumo.Id, Pagina: 1, ItensPorPagina: 2));
        var secondPage = await consulta.ListarMovimentosAsync(
            new ListarMovimentosInsumoFiltro(insumo.Id, Pagina: 2, ItensPorPagina: 2));
        var thirdPage = await consulta.ListarMovimentosAsync(
            new ListarMovimentosInsumoFiltro(insumo.Id, Pagina: 3, ItensPorPagina: 2));
        var fourthPage = await consulta.ListarMovimentosAsync(
            new ListarMovimentosInsumoFiltro(insumo.Id, Pagina: 4, ItensPorPagina: 2));
        var history = firstPage.Itens.Concat(secondPage.Itens).Concat(thirdPage.Itens)
            .Concat(fourthPage.Itens).ToArray();

        Assert.Equal(7, firstPage.TotalItens);
        Assert.Equal(2, firstPage.Itens.Count);
        Assert.Equal(7, history.Select(item => item.Id).Distinct().Count());
        Assert.Equal(
            new[]
            {
                TipoMovimentoInsumo.EstoqueInicial,
                TipoMovimentoInsumo.Aquisicao,
                TipoMovimentoInsumo.SaidaManual,
                TipoMovimentoInsumo.AjusteInventario,
                TipoMovimentoInsumo.Perda,
                TipoMovimentoInsumo.Descarte,
                TipoMovimentoInsumo.Estorno
            }.OrderBy(tipo => tipo),
            history.Select(item => item.Tipo).OrderBy(tipo => tipo));
        var movimentoEstorno = history.Single(item => item.Tipo == TipoMovimentoInsumo.Estorno);
        Assert.Equal(saida.Movimento.Id, movimentoEstorno.MovimentoOriginalId);
        Assert.Equal(UnidadeFisicaEstoque.Grama, movimentoEstorno.UnidadeBase);
        Assert.Equal(2m, movimentoEstorno.QuantidadeBase);
        Assert.Equal(OrigemMovimentoInsumo.MovimentoOriginal, movimentoEstorno.Origem);
        Assert.Equal(ResponsavelId, movimentoEstorno.ResponsavelId);
        Assert.NotEqual(default, movimentoEstorno.RegistradoEmUtc);
        var movimentoAjuste = history.Single(item => item.Tipo == TipoMovimentoInsumo.AjusteInventario);
        Assert.Equal("Contagem", movimentoAjuste.Motivo);
        Assert.Equal(OrigemMovimentoInsumo.ContagemFisica, movimentoAjuste.Origem);
        var movimentoAquisicao = history.Single(item => item.Tipo == TipoMovimentoInsumo.Aquisicao);
        Assert.Equal(formaCompra.Id, movimentoAquisicao.FormaCompraId);
        Assert.Equal("Pacote 2 g", movimentoAquisicao.FormaCompraNome);
        Assert.Equal(OrigemMovimentoInsumo.FormaCompra, movimentoAquisicao.Origem);

        var saidas = await consulta.ListarMovimentosAsync(new ListarMovimentosInsumoFiltro(
            insumo.Id,
            Tipo: TipoMovimentoInsumo.SaidaManual));
        Assert.Equal(1, saidas.TotalItens);
        Assert.Equal(saida.Movimento!.Id, Assert.Single(saidas.Itens).Id);
        var unrelated = InsumoEstoque.Criar(
            "Outro insumo histórico",
            TipoInsumoEstoque.MateriaPrima,
            UnidadeFisicaEstoque.Grama);
        context.InsumosEstoque.Add(unrelated);
        await context.SaveChangesAsync();
        Assert.Empty((await consulta.ListarMovimentosAsync(
            new ListarMovimentosInsumoFiltro(unrelated.Id))).Itens);
    }

    private static async Task<RegistrarEntradaInsumoResult> RegistrarEntradaAsync(
        IEstoqueInsumoRepository repository,
        Guid insumoId,
        decimal quantidade,
        string? identificacaoLote,
        string chave,
        DateOnly? dataValidade = null,
        bool bloqueado = false,
        TipoMovimentoInsumo tipo = TipoMovimentoInsumo.EstoqueInicial,
        Guid? formaCompraId = null)
    {
        var resultado = await repository.RegistrarEntradaAsync(new RegistrarEntradaInsumoInput(
            insumoId,
            ResponsavelId,
            tipo,
            quantidade,
            formaCompraId is null ? UnidadeFisicaEstoque.Grama : null,
            formaCompraId,
            null,
            identificacaoLote,
            identificacaoLote is null ? null : DataReferencia,
            dataValidade,
            null,
            bloqueado,
            null,
            chave));
        Assert.True(resultado.Sucesso, resultado.Erro);
        return resultado;
    }
}
