using Xaquiamuni.Aplicacao.Catalogo;
using Xaquiamuni.Dominio.Entidades;

namespace Xaquiamuni.Tests;

public sealed class DescontosFreteTests
{
    private static readonly Guid Autor = Guid.NewGuid();
    private static readonly DateTimeOffset Agora = new(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);
    private static ConfiguracaoPromocaoFrete Config(TipoDescontoFrete tipo, long valor) =>
        new("Campanha de teste", tipo, valor, ModalidadesPromocaoFrete.EntregaPropria, 1000,
            Agora, Agora.AddHours(1));

    private static PromocaoFrete Campanha(ConfiguracaoPromocaoFrete dados, bool ativa = true)
    {
        var campanha = PromocaoFrete.Criar(dados, Autor, Agora);
        campanha.AlterarStatus(ativa, Autor, Agora);
        return campanha;
    }

    [Theory]
    [InlineData(TipoDescontoFrete.Percentual, 5000, 101, 51)]
    [InlineData(TipoDescontoFrete.Percentual, 4900, 101, 49)]
    [InlineData(TipoDescontoFrete.Percentual, 10000, long.MaxValue, long.MaxValue)]
    [InlineData(TipoDescontoFrete.ValorFixo, 50, 101, 50)]
    [InlineData(TipoDescontoFrete.ValorFixo, 200, 101, 101)]
    [InlineData(TipoDescontoFrete.FreteGratis, 0, 101, 101)]
    public void CalculaEmCentavosComArredondamentoELimite(TipoDescontoFrete tipo, long valor, long frete, long desconto)
    {
        var campanha = Campanha(Config(tipo, valor));
        var resultado = DescontosFrete.Resolver(ModalidadeEntregaPedido.EntregaPropria, frete, 1000, Agora, [campanha]);
        Assert.Equal(new ResultadoDescontoFrete(campanha.Id, frete, desconto, frete - desconto), resultado);
    }

    [Fact]
    public void ExigeModalidadeSubtotalAtivacaoEVigenciaComFimExclusivo()
    {
        var dados = Config(TipoDescontoFrete.FreteGratis, 0);
        var campanha = Campanha(dados);
        Assert.Equal(MotivoSemDescontoFrete.ModalidadeIncompativel,
            DescontosFrete.Resolver(ModalidadeEntregaPedido.RetiradaLocal, 0, 1000, Agora, [campanha]).Motivo);
        Assert.Equal(MotivoSemDescontoFrete.FreteZero,
            DescontosFrete.Resolver(ModalidadeEntregaPedido.EntregaPropria, 0, 1000, Agora, [campanha]).Motivo);
        Assert.Null(DescontosFrete.Resolver(ModalidadeEntregaPedido.Correios, 100, 1000, Agora, [campanha]).CampanhaId);
        Assert.Null(DescontosFrete.Resolver(ModalidadeEntregaPedido.EntregaPropria, 100, 999, Agora, [campanha]).CampanhaId);
        Assert.Null(DescontosFrete.Resolver(ModalidadeEntregaPedido.EntregaPropria, 100, 1000, Agora.AddTicks(-1), [campanha]).CampanhaId);
        Assert.Null(DescontosFrete.Resolver(ModalidadeEntregaPedido.EntregaPropria, 100, 1000, Agora.AddHours(1), [campanha]).CampanhaId);
        Assert.Null(DescontosFrete.Resolver(ModalidadeEntregaPedido.EntregaPropria, 100, 1000, Agora, [Campanha(dados, false)]).CampanhaId);
    }

    [Fact]
    public void SelecionaMaiorDescontoEDesempataPorIdIndependentementeDaOrdem()
    {
        var dados = Config(TipoDescontoFrete.ValorFixo, 80) with { Modalidades = ModalidadesPromocaoFrete.CorreiosManual };
        var primeira = Campanha(dados);
        var segunda = Campanha(dados);
        var menor = Campanha(dados with { Valor = 60 });
        var esperado = new[] { primeira.Id, segunda.Id }.Min();
        foreach (var campanhas in new[] { new[] { menor, primeira, segunda }, [segunda, primeira, menor] })
        {
            var resultado = DescontosFrete.Resolver(ModalidadeEntregaPedido.Correios, 100, 2000, Agora, campanhas);
            Assert.Equal(esperado, resultado.CampanhaId);
            Assert.Equal(80, resultado.DescontoCentavos);
            Assert.Equal(20, resultado.LiquidoCentavos);
        }
    }

    [Fact]
    public void RejeitaConfiguracoesEEntradasInvalidas()
    {
        var dados = Config(TipoDescontoFrete.Percentual, 2500);
        foreach (var invalida in new[] { dados with { Valor = 10_001 }, dados with { Valor = 0 },
            dados with { Modalidades = (ModalidadesPromocaoFrete)4 }, dados with { SubtotalMinimoCentavos = -1 },
            dados with { Nome = " " }, dados with { FimUtc = Agora },
            dados with { Tipo = TipoDescontoFrete.FreteGratis }, dados with { Tipo = TipoDescontoFrete.ValorFixo, Valor = -1 } })
            Assert.Throws<ArgumentException>(() => Campanha(invalida));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DescontosFrete.Resolver(ModalidadeEntregaPedido.EntregaPropria, -1, 0, Agora, []));
    }
}
