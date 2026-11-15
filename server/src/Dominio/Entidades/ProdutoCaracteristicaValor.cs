namespace Xaquiamuni.Dominio.Entidades;

public sealed class ProdutoCaracteristicaValor
{
    private ProdutoCaracteristicaValor()
    {
    }

    private ProdutoCaracteristicaValor(
        Guid id,
        Guid produtoId,
        Guid produtoCaracteristicaId,
        string nome,
        string nomeNormalizado,
        decimal? valorMedidaCanonico,
        ProdutoCaracteristica caracteristica)
    {
        Id = id;
        ProdutoId = produtoId;
        ProdutoCaracteristicaId = produtoCaracteristicaId;
        Nome = nome;
        NomeNormalizado = nomeNormalizado;
        ValorMedidaCanonico = valorMedidaCanonico;
        Caracteristica = caracteristica;
    }

    public Guid Id { get; private set; }
    public Guid ProdutoId { get; private set; }
    public Guid ProdutoCaracteristicaId { get; private set; }
    public string Nome { get; private set; } = null!;
    public string NomeNormalizado { get; private set; } = null!;
    public decimal? ValorMedidaCanonico { get; private set; }
    public ProdutoCaracteristica Caracteristica { get; private set; } = null!;

    internal void AtualizarDados(
        string nome,
        string nomeNormalizado,
        decimal? valorMedidaCanonico)
    {
        Nome = nome;
        NomeNormalizado = nomeNormalizado;
        ValorMedidaCanonico = valorMedidaCanonico;
    }

    internal static ProdutoCaracteristicaValor Criar(
        ProdutoCaracteristica caracteristica,
        string nome,
        string nomeNormalizado,
        decimal? valorMedidaCanonico) =>
        new(
            Guid.NewGuid(),
            caracteristica.ProdutoId,
            caracteristica.Id,
            nome,
            nomeNormalizado,
            valorMedidaCanonico,
            caracteristica);
}
