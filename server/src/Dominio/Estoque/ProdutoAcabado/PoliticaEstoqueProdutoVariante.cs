using Xaquiamuni.Dominio.Entidades;

namespace Xaquiamuni.Dominio.Estoque;

public sealed class PoliticaEstoqueProdutoVariante
{
    private PoliticaEstoqueProdutoVariante(
        Guid produtoVarianteId,
        int? estoqueMinimo,
        int? prazoValidadePadraoDias)
    {
        ProdutoVarianteId = produtoVarianteId;
        EstoqueMinimo = estoqueMinimo;
        PrazoValidadePadraoDias = prazoValidadePadraoDias;
    }

    private PoliticaEstoqueProdutoVariante()
    {
    }

    public Guid ProdutoVarianteId { get; private set; }
    public int? EstoqueMinimo { get; private set; }
    public int? PrazoValidadePadraoDias { get; private set; }

    public static PoliticaEstoqueProdutoVariante Criar(
        ProdutoVariante? variante,
        int? estoqueMinimo = null,
        int? prazoValidadePadraoDias = null)
    {
        ArgumentNullException.ThrowIfNull(variante);
        ValidarVariante(variante.Id);
        ValidarConfiguracao(estoqueMinimo, prazoValidadePadraoDias);

        return new PoliticaEstoqueProdutoVariante(
            variante.Id,
            estoqueMinimo,
            prazoValidadePadraoDias);
    }

    public void Atualizar(
        ProdutoVariante? variante,
        int? estoqueMinimo,
        int? prazoValidadePadraoDias)
    {
        ArgumentNullException.ThrowIfNull(variante);
        ValidarVariante(variante.Id);
        if (variante.Id != ProdutoVarianteId)
        {
            throw new ArgumentException(
                "A política deve permanecer vinculada à mesma variante.",
                nameof(variante));
        }

        ValidarConfiguracao(estoqueMinimo, prazoValidadePadraoDias);
        EstoqueMinimo = estoqueMinimo;
        PrazoValidadePadraoDias = prazoValidadePadraoDias;
    }

    private static void ValidarVariante(Guid varianteId)
    {
        if (varianteId == Guid.Empty)
        {
            throw new ArgumentException(
                "A variante deve possuir um identificador válido.",
                "variante");
        }
    }

    private static void ValidarConfiguracao(int? estoqueMinimo, int? prazoValidadePadraoDias)
    {
        if (estoqueMinimo < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(estoqueMinimo),
                "O estoque mínimo não pode ser negativo.");
        }

        if (prazoValidadePadraoDias < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(prazoValidadePadraoDias),
                "O prazo padrão de validade não pode ser negativo.");
        }
    }
}
