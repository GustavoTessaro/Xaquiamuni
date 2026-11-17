using Xaquiamuni.Dominio.Entidades;

namespace Xaquiamuni.Dominio.Estoque;

public sealed class LoteProdutoAcabado
{
    public const int CodigoMaxLength = 50;

    private LoteProdutoAcabado(
        Guid id,
        Guid produtoVarianteId,
        string codigo,
        DateOnly dataFabricacao,
        DateOnly dataValidade,
        OrigemValidadeLoteProdutoAcabado origemValidade,
        int? prazoValidadeAplicadoDias)
    {
        Id = id;
        ProdutoVarianteId = produtoVarianteId;
        Codigo = codigo;
        DataFabricacao = dataFabricacao;
        DataValidade = dataValidade;
        OrigemValidade = origemValidade;
        PrazoValidadeAplicadoDias = prazoValidadeAplicadoDias;
    }

    private LoteProdutoAcabado()
    {
    }

    public Guid Id { get; private set; }
    public Guid ProdutoVarianteId { get; private set; }
    public string Codigo { get; private set; } = null!;
    public DateOnly DataFabricacao { get; private set; }
    public DateOnly DataValidade { get; private set; }
    public OrigemValidadeLoteProdutoAcabado OrigemValidade { get; private set; }
    public int? PrazoValidadeAplicadoDias { get; private set; }

    public static LoteProdutoAcabado Criar(
        ProdutoVariante? variante,
        PoliticaEstoqueProdutoVariante? politica,
        DateOnly dataFabricacao,
        DateOnly? dataValidadeInformada = null)
    {
        ArgumentNullException.ThrowIfNull(variante);
        if (variante.Id == Guid.Empty)
        {
            throw new ArgumentException(
                "A variante deve possuir um identificador válido.",
                nameof(variante));
        }

        if (politica is not null && politica.ProdutoVarianteId != variante.Id)
        {
            throw new ArgumentException(
                "A política informada não pertence à variante.",
                nameof(politica));
        }

        DateOnly dataValidade;
        OrigemValidadeLoteProdutoAcabado origem;
        int? prazoAplicado;
        if (dataValidadeInformada is not null)
        {
            dataValidade = dataValidadeInformada.Value;
            origem = OrigemValidadeLoteProdutoAcabado.InformadaManualmente;
            prazoAplicado = null;
        }
        else if (politica?.PrazoValidadePadraoDias is int prazoDias)
        {
            try
            {
                dataValidade = dataFabricacao.AddDays(prazoDias);
            }
            catch (ArgumentOutOfRangeException)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(politica),
                    "O prazo padrão ultrapassa o intervalo de datas suportado.");
            }

            origem = OrigemValidadeLoteProdutoAcabado.PrazoPadrao;
            prazoAplicado = prazoDias;
        }
        else
        {
            throw new ArgumentException(
                "Informe a validade ou configure um prazo padrão para a variante.",
                nameof(dataValidadeInformada));
        }

        if (dataValidade < dataFabricacao)
        {
            throw new ArgumentOutOfRangeException(
                nameof(dataValidadeInformada),
                "A validade não pode ser anterior à fabricação.");
        }

        var id = Guid.NewGuid();
        return new LoteProdutoAcabado(
            id,
            variante.Id,
            GerarCodigo(dataFabricacao, id),
            dataFabricacao,
            dataValidade,
            origem,
            prazoAplicado);
    }

    public bool EstaValidoEm(DateOnly dataReferencia) => DataValidade >= dataReferencia;

    private static string GerarCodigo(DateOnly dataFabricacao, Guid id) =>
        $"PA-{dataFabricacao:yyyyMMdd}-{id:N}".ToUpperInvariant();
}
