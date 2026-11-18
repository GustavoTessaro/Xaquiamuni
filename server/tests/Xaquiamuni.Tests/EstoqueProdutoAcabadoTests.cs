using Xaquiamuni.Dominio.Entidades;
using Xaquiamuni.Dominio.Estoque;

namespace Xaquiamuni.Tests;

public sealed class EstoqueProdutoAcabadoTests
{
    private static readonly Guid ResponsavelId = Guid.NewGuid();
    private static readonly DateTimeOffset RegistradoEmUtc =
        new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Politica_ValidaMinimoEPrazoEPermaneceVinculadaAVariante()
    {
        var variante = CriarVariante();
        var politica = PoliticaEstoqueProdutoVariante.Criar(variante, estoqueMinimo: 0, prazoValidadePadraoDias: 0);

        Assert.Equal(variante.Id, politica.ProdutoVarianteId);
        Assert.Equal(0, politica.EstoqueMinimo);
        Assert.Equal(0, politica.PrazoValidadePadraoDias);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PoliticaEstoqueProdutoVariante.Criar(variante, estoqueMinimo: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PoliticaEstoqueProdutoVariante.Criar(variante, prazoValidadePadraoDias: -1));

        var outraVariante = CriarVariante();
        Assert.Throws<ArgumentException>(() =>
            politica.Atualizar(outraVariante, estoqueMinimo: 5, prazoValidadePadraoDias: 20));
        Assert.Equal(0, politica.EstoqueMinimo);
        Assert.Equal(0, politica.PrazoValidadePadraoDias);
    }

    [Fact]
    public void Lote_GeraCodigoLegivelECalculaValidadeComPrazoCopiado()
    {
        var variante = CriarVariante();
        var politica = PoliticaEstoqueProdutoVariante.Criar(
            variante,
            estoqueMinimo: 3,
            prazoValidadePadraoDias: 90);
        var fabricacao = new DateOnly(2026, 11, 17);

        var lote = LoteProdutoAcabado.Criar(variante, politica, fabricacao);
        politica.Atualizar(variante, estoqueMinimo: 10, prazoValidadePadraoDias: 12);
        var outroLote = LoteProdutoAcabado.Criar(variante, politica, fabricacao);

        Assert.NotEqual(Guid.Empty, lote.Id);
        Assert.StartsWith("PA-20261117-", lote.Codigo);
        Assert.True(lote.Codigo.Length <= LoteProdutoAcabado.CodigoMaxLength);
        Assert.NotEqual(lote.Codigo, outroLote.Codigo);
        Assert.Equal(new DateOnly(2027, 2, 15), lote.DataValidade);
        Assert.Equal(OrigemValidadeLoteProdutoAcabado.PrazoPadrao, lote.OrigemValidade);
        Assert.Equal(90, lote.PrazoValidadeAplicadoDias);
        Assert.True(lote.EstaValidoEm(lote.DataValidade));
        Assert.False(lote.EstaValidoEm(lote.DataValidade.AddDays(1)));
    }

    [Fact]
    public void Lote_AceitaValidadeManualNaoAnteriorAFabricacao()
    {
        var variante = CriarVariante();
        var fabricacao = new DateOnly(2026, 11, 17);
        var validadeInformada = fabricacao.AddDays(40);

        var lote = LoteProdutoAcabado.Criar(
            variante,
            politica: null,
            fabricacao,
            validadeInformada);

        Assert.Equal(validadeInformada, lote.DataValidade);
        Assert.Equal(OrigemValidadeLoteProdutoAcabado.InformadaManualmente, lote.OrigemValidade);
        Assert.Null(lote.PrazoValidadeAplicadoDias);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            LoteProdutoAcabado.Criar(variante, null, fabricacao, fabricacao.AddDays(-1)));
    }

    [Fact]
    public void Lote_ExigePrazoOuValidadeManualEPoliticaDaMesmaVariante()
    {
        var variante = CriarVariante();
        var outraVariante = CriarVariante();
        var politicaOutraVariante = PoliticaEstoqueProdutoVariante.Criar(
            outraVariante,
            prazoValidadePadraoDias: 10);
        var fabricacao = new DateOnly(2026, 11, 17);

        Assert.Throws<ArgumentException>(() =>
            LoteProdutoAcabado.Criar(variante, null, fabricacao));
        Assert.Throws<ArgumentException>(() =>
            LoteProdutoAcabado.Criar(variante, politicaOutraVariante, fabricacao));
    }

    [Fact]
    public void Lote_RejeitaPrazoQueUltrapassaDataSuportada()
    {
        var variante = CriarVariante();
        var politica = PoliticaEstoqueProdutoVariante.Criar(variante, prazoValidadePadraoDias: 2);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            LoteProdutoAcabado.Criar(variante, politica, new DateOnly(9999, 12, 30)));
    }

    [Fact]
    public void Saldo_EntradasTransferenciasEDescartesMantemCondicoesEHistorico()
    {
        var (variante, _, lote, saldo) = CriarPosicao();
        var entrada = saldo.RegistrarEntrada(
            lote,
            5,
            CondicaoEstoqueProdutoAcabado.Vendavel,
            TipoMovimentoProdutoAcabado.EstoqueInicial,
            ResponsavelId,
            "Abertura inicial",
            RegistradoEmUtc,
            "entrada-1",
            Fingerprint());
        saldo.RegistrarEntrada(
            lote,
            3,
            CondicaoEstoqueProdutoAcabado.Danificada,
            TipoMovimentoProdutoAcabado.EntradaManual,
            ResponsavelId,
            "Recebimento danificado",
            RegistradoEmUtc,
            "entrada-2",
            Fingerprint());
        saldo.RegistrarEntrada(
            lote,
            2,
            CondicaoEstoqueProdutoAcabado.Quarentena,
            TipoMovimentoProdutoAcabado.EntradaManual,
            ResponsavelId,
            "Inspeção pendente",
            RegistradoEmUtc,
            "entrada-3",
            Fingerprint());
        var transferencia = saldo.TransferirCondicao(
            lote,
            1,
            CondicaoEstoqueProdutoAcabado.Quarentena,
            CondicaoEstoqueProdutoAcabado.Vendavel,
            ResponsavelId,
            "Quarentena liberada após inspeção",
            RegistradoEmUtc,
            "transferencia-1",
            Fingerprint());
        var descarte = saldo.RegistrarDescarte(
            lote,
            2,
            CondicaoEstoqueProdutoAcabado.Danificada,
            ResponsavelId,
            "Descarte de unidades danificadas",
            RegistradoEmUtc,
            "descarte-1",
            Fingerprint());

        Assert.Equal(variante.Id, entrada.ProdutoVarianteId);
        Assert.Equal(TipoMovimentoProdutoAcabado.TransferenciaCondicao, transferencia.Tipo);
        Assert.Equal(10, transferencia.SaldoAnterior.QuantidadeFisica);
        Assert.Equal(10, transferencia.SaldoPosterior.QuantidadeFisica);
        Assert.Equal(8, descarte.SaldoPosterior.QuantidadeFisica);
        Assert.Equal(6, saldo.QuantidadeVendavel);
        Assert.Equal(1, saldo.QuantidadeDanificada);
        Assert.Equal(1, saldo.QuantidadeQuarentena);
        Assert.Equal(8, saldo.QuantidadeFisica);
    }

    [Fact]
    public void Saldo_RejeitaQuantidadeInvalidaESaidaAcimaDaCondicao()
    {
        var (variante, _, lote, saldo) = CriarPosicao();

        Assert.Throws<ArgumentOutOfRangeException>(() => saldo.RegistrarEntrada(
            lote,
            0,
            CondicaoEstoqueProdutoAcabado.Vendavel,
            TipoMovimentoProdutoAcabado.EntradaManual,
            ResponsavelId,
            "Entrada",
            RegistradoEmUtc,
            "entrada-zero",
            Fingerprint()));
        Assert.Throws<ArgumentOutOfRangeException>(() => saldo.RegistrarEntrada(
            lote,
            -1,
            CondicaoEstoqueProdutoAcabado.Vendavel,
            TipoMovimentoProdutoAcabado.EntradaManual,
            ResponsavelId,
            "Entrada",
            RegistradoEmUtc,
            "entrada-negativa",
            Fingerprint()));

        saldo.RegistrarEntrada(
            lote,
            2,
            CondicaoEstoqueProdutoAcabado.Quarentena,
            TipoMovimentoProdutoAcabado.EstoqueInicial,
            ResponsavelId,
            "Abertura",
            RegistradoEmUtc,
            "entrada-quarentena",
            Fingerprint());

        Assert.Throws<InvalidOperationException>(() => saldo.RegistrarPerda(
            lote,
            3,
            CondicaoEstoqueProdutoAcabado.Quarentena,
            ResponsavelId,
            "Perda",
            RegistradoEmUtc,
            "perda-excedente",
            Fingerprint()));
        Assert.Throws<InvalidOperationException>(() => saldo.RegistrarSaidaManual(
            variante,
            lote,
            1,
            lote.DataValidade,
            ResponsavelId,
            "Saída",
            RegistradoEmUtc,
            "saida-sem-saldo",
            Fingerprint()));
        Assert.Equal(2, saldo.QuantidadeFisica);
    }

    [Fact]
    public void Saldo_SaidaManualExigeVarianteAtivaLoteValidoECondicaoVendavel()
    {
        var (variante, _, lote, saldo) = CriarPosicao();
        saldo.RegistrarEntrada(
            lote,
            3,
            CondicaoEstoqueProdutoAcabado.Vendavel,
            TipoMovimentoProdutoAcabado.EstoqueInicial,
            ResponsavelId,
            "Abertura",
            RegistradoEmUtc,
            "entrada-vendavel",
            Fingerprint());

        var saida = saldo.RegistrarSaidaManual(
            variante,
            lote,
            2,
            lote.DataValidade,
            ResponsavelId,
            "Saída manual",
            RegistradoEmUtc,
            "saida-valida",
            Fingerprint());

        Assert.Equal(TipoMovimentoProdutoAcabado.SaidaManual, saida.Tipo);
        Assert.Equal(1, saldo.QuantidadeVendavel);
        Assert.Throws<InvalidOperationException>(() => saldo.RegistrarSaidaManual(
            variante,
            lote,
            1,
            lote.DataValidade.AddDays(1),
            ResponsavelId,
            "Lote vencido",
            RegistradoEmUtc,
            "saida-vencida",
            Fingerprint()));
        var varianteInativa = CriarVarianteAtiva(false);
        var loteInativo = LoteProdutoAcabado.Criar(
            varianteInativa,
            politica: null,
            lote.DataFabricacao,
            lote.DataValidade);
        var saldoInativo = SaldoLoteProdutoAcabado.CriarZero(loteInativo);
        saldoInativo.RegistrarEntrada(
            loteInativo,
            1,
            CondicaoEstoqueProdutoAcabado.Vendavel,
            TipoMovimentoProdutoAcabado.EntradaManual,
            ResponsavelId,
            "Entrada para teste",
            RegistradoEmUtc,
            "entrada-inativa",
            Fingerprint());
        Assert.Throws<InvalidOperationException>(() => saldoInativo.RegistrarSaidaManual(
            varianteInativa,
            loteInativo,
            1,
            loteInativo.DataValidade,
            ResponsavelId,
            "Variante inativa",
            RegistradoEmUtc,
            "saida-inativa",
            Fingerprint()));
        Assert.Equal(1, saldoInativo.QuantidadeVendavel);
        Assert.Equal(1, saldo.QuantidadeVendavel);
    }

    [Fact]
    public void Saldo_ContagemAplicaDiferencaPorCondicaoESemDiferencaNaoCriaMovimento()
    {
        var (_, _, lote, saldo) = CriarPosicao();
        var aumento = saldo.Contar(
            lote,
            CondicaoEstoqueProdutoAcabado.Quarentena,
            4,
            ResponsavelId,
            "Contagem inicial",
            RegistradoEmUtc,
            "contagem-1",
            Fingerprint());
        var semDiferenca = saldo.Contar(
            lote,
            CondicaoEstoqueProdutoAcabado.Quarentena,
            4,
            ResponsavelId,
            "Conferência",
            RegistradoEmUtc,
            "contagem-2",
            Fingerprint());
        var reducao = saldo.Contar(
            lote,
            CondicaoEstoqueProdutoAcabado.Quarentena,
            1,
            ResponsavelId,
            "Contagem corrigida",
            RegistradoEmUtc,
            "contagem-3",
            Fingerprint());

        Assert.NotNull(aumento);
        Assert.Equal(TipoMovimentoProdutoAcabado.AjusteInventario, aumento.Tipo);
        Assert.Equal(4, aumento.QuantidadeContada);
        Assert.Null(semDiferenca);
        Assert.Equal(3, reducao!.Quantidade);
        Assert.Equal(1, saldo.QuantidadeQuarentena);
        Assert.Equal(1, saldo.QuantidadeFisica);
    }

    [Fact]
    public void Saldo_ContagemSemDiferencaAindaValidaResponsavelMotivoEIdempotencia()
    {
        var (_, _, lote, saldo) = CriarPosicao();

        Assert.Throws<ArgumentException>(() => saldo.Contar(
            lote,
            CondicaoEstoqueProdutoAcabado.Vendavel,
            0,
            Guid.Empty,
            "Contagem",
            RegistradoEmUtc,
            "contagem",
            Fingerprint()));
        Assert.Throws<ArgumentException>(() => saldo.Contar(
            lote,
            CondicaoEstoqueProdutoAcabado.Vendavel,
            0,
            ResponsavelId,
            " ",
            RegistradoEmUtc,
            "contagem",
            Fingerprint()));
        Assert.Throws<ArgumentException>(() => saldo.Contar(
            lote,
            CondicaoEstoqueProdutoAcabado.Vendavel,
            0,
            ResponsavelId,
            "Contagem",
            RegistradoEmUtc,
            " ",
            Fingerprint()));
    }

    [Fact]
    public void Resumo_ContaFisicoPorCondicaoEUtilizavelSomenteVendavelNaoVencido()
    {
        var variante = CriarVariante();
        var politica = PoliticaEstoqueProdutoVariante.Criar(variante, estoqueMinimo: 3);
        var valido = LoteProdutoAcabado.Criar(
            variante,
            politica: null,
            new DateOnly(2026, 11, 1),
            new DateOnly(2026, 11, 17));
        var vencido = LoteProdutoAcabado.Criar(
            variante,
            politica: null,
            new DateOnly(2026, 10, 1),
            new DateOnly(2026, 11, 16));
        var saldoValido = SaldoLoteProdutoAcabado.CriarZero(valido);
        var saldoVencido = SaldoLoteProdutoAcabado.CriarZero(vencido);
        saldoValido.RegistrarEntrada(
            valido,
            5,
            CondicaoEstoqueProdutoAcabado.Vendavel,
            TipoMovimentoProdutoAcabado.EstoqueInicial,
            ResponsavelId,
            "Abertura",
            RegistradoEmUtc,
            "valido-1",
            Fingerprint());
        saldoValido.RegistrarEntrada(
            valido,
            2,
            CondicaoEstoqueProdutoAcabado.Danificada,
            TipoMovimentoProdutoAcabado.EntradaManual,
            ResponsavelId,
            "Avaria",
            RegistradoEmUtc,
            "valido-2",
            Fingerprint());
        saldoVencido.RegistrarEntrada(
            vencido,
            4,
            CondicaoEstoqueProdutoAcabado.Vendavel,
            TipoMovimentoProdutoAcabado.EstoqueInicial,
            ResponsavelId,
            "Abertura vencida",
            RegistradoEmUtc,
            "vencido-1",
            Fingerprint());

        var resumo = ResumoSaldoProdutoAcabado.Calcular(
            variante,
            politica,
            [valido, vencido],
            [saldoValido, saldoVencido],
            new DateOnly(2026, 11, 17));

        Assert.Equal(11, resumo.SaldoFisico);
        Assert.Equal(5, resumo.SaldoUtilizavel);
        Assert.Equal(0, resumo.SaldoReservado);
        Assert.Equal(5, resumo.SaldoDisponivel);
        Assert.Equal(3, resumo.EstoqueMinimo);
    }

    [Fact]
    public void Resumo_VarianteInativaPreservaFisicoMasNaoFicaDisponivel()
    {
        var variante = CriarVarianteAtiva(false);
        var lote = LoteProdutoAcabado.Criar(
            variante,
            politica: null,
            new DateOnly(2026, 11, 1),
            new DateOnly(2026, 12, 1));
        var saldo = SaldoLoteProdutoAcabado.CriarZero(lote);
        saldo.RegistrarEntrada(
            lote,
            6,
            CondicaoEstoqueProdutoAcabado.Vendavel,
            TipoMovimentoProdutoAcabado.EstoqueInicial,
            ResponsavelId,
            "Abertura",
            RegistradoEmUtc,
            "inativa-1",
            Fingerprint());

        var resumo = ResumoSaldoProdutoAcabado.Calcular(
            variante,
            politica: null,
            [lote],
            [saldo],
            new DateOnly(2026, 11, 17));

        Assert.Equal(6, resumo.SaldoFisico);
        Assert.Equal(6, resumo.SaldoUtilizavel);
        Assert.Equal(0, resumo.SaldoDisponivel);
    }

    [Fact]
    public void Estorno_CriaMovimentoCompensatorioSemAlterarOriginal()
    {
        var (_, _, lote, saldo) = CriarPosicao();
        var entrada = saldo.RegistrarEntrada(
            lote,
            5,
            CondicaoEstoqueProdutoAcabado.Vendavel,
            TipoMovimentoProdutoAcabado.EstoqueInicial,
            ResponsavelId,
            "Abertura",
            RegistradoEmUtc,
            "entrada-estorno",
            Fingerprint());
        var saldoAntesEstorno = entrada.SaldoPosterior;
        var historico = new List<MovimentoProdutoAcabado> { entrada };

        var estorno = saldo.Estornar(
            lote,
            entrada,
            historico,
            ResponsavelId,
            "Correção da entrada",
            RegistradoEmUtc,
            "estorno-1",
            Fingerprint());
        historico.Add(estorno);

        Assert.Equal(TipoMovimentoProdutoAcabado.Estorno, estorno.Tipo);
        Assert.Equal(entrada.Id, estorno.MovimentoOriginalId);
        Assert.Equal(0, saldo.QuantidadeFisica);
        Assert.Equal(5, entrada.SaldoPosterior.QuantidadeFisica);
        Assert.Equal(saldoAntesEstorno, entrada.SaldoPosterior);
        Assert.Contains(entrada, historico);
        Assert.Contains(estorno, historico);
        Assert.Throws<InvalidOperationException>(() => saldo.Estornar(
            lote,
            entrada,
            historico,
            ResponsavelId,
            "Estorno duplicado",
            RegistradoEmUtc,
            "estorno-2",
            Fingerprint()));
    }

    [Fact]
    public void Estorno_RejeitaOriginalDeOutraPosicaoECompensacaoSemSaldoNaCondicao()
    {
        var (_, _, lote, saldo) = CriarPosicao();
        saldo.RegistrarEntrada(
            lote,
            2,
            CondicaoEstoqueProdutoAcabado.Vendavel,
            TipoMovimentoProdutoAcabado.EstoqueInicial,
            ResponsavelId,
            "Abertura",
            RegistradoEmUtc,
            "entrada-antes-transferencia",
            Fingerprint());
        var transferencia = saldo.TransferirCondicao(
            lote,
            2,
            CondicaoEstoqueProdutoAcabado.Vendavel,
            CondicaoEstoqueProdutoAcabado.Quarentena,
            ResponsavelId,
            "Inspeção",
            RegistradoEmUtc,
            "transferencia-estorno",
            Fingerprint());
        var movimentos = new List<MovimentoProdutoAcabado>
        {
            saldo.RegistrarEntrada(
                lote,
                1,
                CondicaoEstoqueProdutoAcabado.Vendavel,
                TipoMovimentoProdutoAcabado.EntradaManual,
                ResponsavelId,
                "Reposição",
                RegistradoEmUtc,
                "entrada-extra",
                Fingerprint()),
            transferencia
        };
        saldo.RegistrarDescarte(
            lote,
            2,
            CondicaoEstoqueProdutoAcabado.Quarentena,
            ResponsavelId,
            "Descarte em quarentena",
            RegistradoEmUtc,
            "descarte-quarentena",
            Fingerprint());

        Assert.Throws<InvalidOperationException>(() => saldo.Estornar(
            lote,
            transferencia,
            movimentos,
            ResponsavelId,
            "Reverter transferência",
            RegistradoEmUtc,
            "estorno-insuficiente",
            Fingerprint()));

        var outroLote = LoteProdutoAcabado.Criar(
            CriarVariante(),
            politica: null,
            new DateOnly(2026, 11, 1),
            new DateOnly(2026, 12, 1));
        Assert.Throws<ArgumentException>(() => saldo.Estornar(
            lote,
            MovimentoProdutoAcabadoFake.Criar(outroLote),
            movimentos,
            ResponsavelId,
            "Movimento de outra variante",
            RegistradoEmUtc,
            "estorno-alheio",
            Fingerprint()));
    }

    [Fact]
    public void Movimento_ExigeMotivoResponsavelUtcEFingerprintImutavel()
    {
        var (_, _, lote, saldo) = CriarPosicao();

        Assert.Throws<ArgumentException>(() => saldo.RegistrarEntrada(
            lote,
            1,
            CondicaoEstoqueProdutoAcabado.Vendavel,
            TipoMovimentoProdutoAcabado.EntradaManual,
            Guid.Empty,
            "Entrada",
            RegistradoEmUtc,
            "chave",
            Fingerprint()));
        Assert.Throws<ArgumentException>(() => saldo.RegistrarEntrada(
            lote,
            1,
            CondicaoEstoqueProdutoAcabado.Vendavel,
            TipoMovimentoProdutoAcabado.EntradaManual,
            ResponsavelId,
            " ",
            RegistradoEmUtc,
            "chave",
            Fingerprint()));
        Assert.Throws<ArgumentException>(() => saldo.RegistrarEntrada(
            lote,
            1,
            CondicaoEstoqueProdutoAcabado.Vendavel,
            TipoMovimentoProdutoAcabado.EntradaManual,
            ResponsavelId,
            "Entrada",
            RegistradoEmUtc.ToOffset(TimeSpan.FromHours(-3)),
            "chave",
            Fingerprint()));
        Assert.Throws<ArgumentException>(() => saldo.RegistrarEntrada(
            lote,
            1,
            CondicaoEstoqueProdutoAcabado.Vendavel,
            TipoMovimentoProdutoAcabado.EntradaManual,
            ResponsavelId,
            "Entrada",
            RegistradoEmUtc,
            "chave",
            new byte[31]));

        var fingerprint = Fingerprint();
        var movimento = saldo.RegistrarEntrada(
            lote,
            1,
            CondicaoEstoqueProdutoAcabado.Vendavel,
            TipoMovimentoProdutoAcabado.EntradaManual,
            ResponsavelId,
            "  Entrada   manual ",
            RegistradoEmUtc,
            " chave ",
            fingerprint);
        var recebido = movimento.FingerprintRequisicao;
        recebido[0] ^= 0xFF;

        Assert.Equal("Entrada manual", movimento.Motivo);
        Assert.Equal("chave", movimento.ChaveIdempotencia);
        Assert.NotEqual(recebido[0], movimento.FingerprintRequisicao[0]);
    }

    [Fact]
    public void Saldo_RejeitaOverflowSemAlterarEstado()
    {
        var (_, _, lote, saldo) = CriarPosicao();
        saldo.RegistrarEntrada(
            lote,
            int.MaxValue,
            CondicaoEstoqueProdutoAcabado.Vendavel,
            TipoMovimentoProdutoAcabado.EstoqueInicial,
            ResponsavelId,
            "Entrada máxima",
            RegistradoEmUtc,
            "entrada-maxima",
            Fingerprint());

        Assert.Throws<ArgumentOutOfRangeException>(() => saldo.RegistrarEntrada(
            lote,
            1,
            CondicaoEstoqueProdutoAcabado.Danificada,
            TipoMovimentoProdutoAcabado.EntradaManual,
            ResponsavelId,
            "Entrada excedente",
            RegistradoEmUtc,
            "entrada-excedente",
            Fingerprint()));
        Assert.Equal(int.MaxValue, saldo.QuantidadeFisica);
        Assert.Equal(0, saldo.QuantidadeDanificada);
    }

    [Fact]
    public void Resumo_RejeitaOverflowNaSomaDeLotes()
    {
        var variante = CriarVariante();
        var loteUm = LoteProdutoAcabado.Criar(
            variante,
            null,
            new DateOnly(2026, 11, 1),
            new DateOnly(2026, 12, 1));
        var loteDois = LoteProdutoAcabado.Criar(
            variante,
            null,
            new DateOnly(2026, 11, 1),
            new DateOnly(2026, 12, 1));
        var saldoUm = SaldoLoteProdutoAcabado.CriarZero(loteUm);
        var saldoDois = SaldoLoteProdutoAcabado.CriarZero(loteDois);
        saldoUm.RegistrarEntrada(
            loteUm,
            int.MaxValue,
            CondicaoEstoqueProdutoAcabado.Vendavel,
            TipoMovimentoProdutoAcabado.EstoqueInicial,
            ResponsavelId,
            "Entrada máxima",
            RegistradoEmUtc,
            "overflow-1",
            Fingerprint());
        saldoDois.RegistrarEntrada(
            loteDois,
            1,
            CondicaoEstoqueProdutoAcabado.Vendavel,
            TipoMovimentoProdutoAcabado.EstoqueInicial,
            ResponsavelId,
            "Entrada adicional",
            RegistradoEmUtc,
            "overflow-2",
            Fingerprint());

        Assert.Throws<ArgumentOutOfRangeException>(() => ResumoSaldoProdutoAcabado.Calcular(
            variante,
            politica: null,
            [loteUm, loteDois],
            [saldoUm, saldoDois],
            new DateOnly(2026, 11, 17)));
    }

    private static (ProdutoVariante Variante, PoliticaEstoqueProdutoVariante Politica,
        LoteProdutoAcabado Lote, SaldoLoteProdutoAcabado Saldo) CriarPosicao()
    {
        var variante = CriarVariante();
        var politica = PoliticaEstoqueProdutoVariante.Criar(
            variante,
            estoqueMinimo: 3,
            prazoValidadePadraoDias: 30);
        var lote = LoteProdutoAcabado.Criar(
            variante,
            politica,
            new DateOnly(2026, 11, 1));
        return (variante, politica, lote, SaldoLoteProdutoAcabado.CriarZero(lote));
    }

    [Fact]
    public void Retirada_BaixaVendavelComVinculoEReservaConsumida()
    {
        var variante = CriarVariante();
        var dia = new DateOnly(2026, 11, 17);
        var lote = LoteProdutoAcabado.Criar(variante, null, dia, dia);
        var saldo = SaldoLoteProdutoAcabado.CriarZero(lote);
        saldo.RegistrarEntrada(lote, 3, CondicaoEstoqueProdutoAcabado.Vendavel,
            TipoMovimentoProdutoAcabado.EntradaManual, ResponsavelId, "Entrada",
            RegistradoEmUtc, "entrada-retirada", Fingerprint());
        var reserva = ReservaProdutoAcabado.Criar(Guid.NewGuid(), variante.Id, lote.Id, 2, RegistradoEmUtc);
        reserva.ConfirmarCompromisso(RegistradoEmUtc);

        var movimento = saldo.RegistrarRetirada(reserva, variante, lote, dia, ResponsavelId, RegistradoEmUtc);

        Assert.Equal(reserva.Id, movimento.ReservaProdutoAcabadoId);
        Assert.Equal(lote.Id, movimento.LoteId);
        Assert.Equal(3, movimento.SaldoAnterior.QuantidadeVendavel);
        Assert.Equal(1, movimento.SaldoPosterior.QuantidadeVendavel);
        Assert.Equal(1, saldo.QuantidadeFisica);
        Assert.Equal(EstadoReservaProdutoAcabado.Consumida, reserva.Estado);
        Assert.Throws<InvalidOperationException>(() =>
            saldo.RegistrarRetirada(reserva, variante, lote, dia, ResponsavelId, RegistradoEmUtc));
        Assert.Throws<ArgumentException>(() => saldo.Estornar(lote, movimento, [movimento],
            ResponsavelId, "Correção", RegistradoEmUtc, "estorno-retirada", Fingerprint()));
        Assert.Equal(1, saldo.QuantidadeFisica);
    }

    [Fact]
    public void Retirada_RejeitaLoteVencidoOuSemSaldoVendavelSemConsumirAReserva()
    {
        var variante = CriarVariante();
        var dia = new DateOnly(2026, 11, 17);
        var lote = LoteProdutoAcabado.Criar(variante, null, dia, dia);
        var saldo = SaldoLoteProdutoAcabado.CriarZero(lote);
        saldo.RegistrarEntrada(lote, 3, CondicaoEstoqueProdutoAcabado.Quarentena,
            TipoMovimentoProdutoAcabado.EntradaManual, ResponsavelId, "Entrada",
            RegistradoEmUtc, "retirada-quarentena", Fingerprint());
        var reserva = ReservaProdutoAcabado.Criar(Guid.NewGuid(), variante.Id, lote.Id, 2, RegistradoEmUtc);
        reserva.ConfirmarCompromisso(RegistradoEmUtc);

        Assert.Throws<InvalidOperationException>(() =>
            saldo.RegistrarRetirada(reserva, variante, lote, dia.AddDays(1), ResponsavelId, RegistradoEmUtc));
        Assert.Throws<InvalidOperationException>(() =>
            saldo.RegistrarRetirada(reserva, variante, lote, dia, ResponsavelId, RegistradoEmUtc));
        Assert.Equal(EstadoReservaProdutoAcabado.Confirmada, reserva.Estado);
        Assert.Equal(3, saldo.QuantidadeFisica);
        Assert.Equal(0, saldo.QuantidadeVendavel);
    }

    private static ProdutoVariante CriarVariante() =>
        Produto.CriarSimples(
            "Sabonete de Lavanda",
            null,
            Categoria.Criar("Sabonetes", null, 0),
            "UN",
            15m).Variantes.Single();

    private static ProdutoVariante CriarVarianteAtiva(bool ativa)
    {
        if (ativa)
        {
            return CriarVariante();
        }

        var produto = Produto.Criar(
            "Velas artesanais",
            null,
            Categoria.Criar("Velas", null, 0),
            [
                ProdutoVariante.Criar("Padrão", "UN", 20m, padrao: true),
                ProdutoVariante.Criar("Variante adicional", "UN", 15m)
            ]);
        var variante = produto.Variantes.Single(item => !item.Padrao);
        produto.DesativarVariante(variante.Id);
        return variante;
    }

    private static byte[] Fingerprint() => System.Security.Cryptography.SHA256.HashData("estoque"u8);

    private static class MovimentoProdutoAcabadoFake
    {
        public static MovimentoProdutoAcabado Criar(LoteProdutoAcabado lote)
        {
            var saldo = SaldoLoteProdutoAcabado.CriarZero(lote);
            return saldo.RegistrarEntrada(
                lote,
                1,
                CondicaoEstoqueProdutoAcabado.Vendavel,
                TipoMovimentoProdutoAcabado.EntradaManual,
                ResponsavelId,
                "Movimento de outro lote",
                RegistradoEmUtc,
                "movimento-outro-lote",
                Fingerprint());
        }
    }
}
