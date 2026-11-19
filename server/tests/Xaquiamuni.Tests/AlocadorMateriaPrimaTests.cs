using Xaquiamuni.Dominio.Estoque;

namespace Xaquiamuni.Tests;

public sealed class AlocadorMateriaPrimaTests
{
    [Fact]
    public void DistribuiPorFefoDepoisPorEntradaEIdEIgnoraInelegiveis()
    {
        var hoje = new DateOnly(2026, 11, 19);
        var a = Guid.Parse("01000000-0000-4000-8000-000000000001");
        var b = Guid.Parse("01000000-0000-4000-8000-000000000002");
        var c = Guid.Parse("01000000-0000-4000-8000-000000000003");
        var d = Guid.Parse("01000000-0000-4000-8000-000000000004");
        var e = Guid.Parse("01000000-0000-4000-8000-000000000005");
        PosicaoParaReserva[] posicoes = [
            new(d, d, hoje, null, true, 2, 0),
            new(c, c, hoje, hoje.AddDays(1), true, 2, 0),
            new(b, b, hoje, hoje, true, 2, 0),
            new(a, a, hoje, hoje, true, 3, 2),
            new(e, e, hoje.AddDays(-1), null, true, 2, 0),
            new(Guid.NewGuid(), Guid.NewGuid(), hoje, hoje.AddDays(-1), false, 200, 0),
        ];
        var resultado = AlocadorMateriaPrima.Planejar(QuantidadeEstoque.Criar(8.125m, UnidadeFisicaEstoque.Grama), true, posicoes);
        Assert.Equal([a, b, c, e, d], resultado!.Select(p => p.SaldoId));
        Assert.Equal([1m, 2m, 2m, 2m, 1.125m], resultado!.Select(p => p.Quantidade));
        Assert.Null(AlocadorMateriaPrima.Planejar(QuantidadeEstoque.Criar(10, UnidadeFisicaEstoque.Grama), true, posicoes));
    }
}
