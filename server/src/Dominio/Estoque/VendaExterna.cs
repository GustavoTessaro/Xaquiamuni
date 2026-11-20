using Xaquiamuni.Dominio.Entidades;

namespace Xaquiamuni.Dominio.Estoque;

public enum CanalVendaExterna { Presencial = 1, Feira, WhatsApp, Outro }
public enum MeioRecebimentoVendaExterna { Pix = 1, Dinheiro, Cartao, Outro }
public enum SituacaoVendaExterna { Registrada = 1, Cancelada }

public sealed class VendaExterna
{
    private readonly List<ItemVendaExterna> _itens = [];
    private readonly List<RecebimentoVendaExterna> _recebimentos = [];
    public Guid Id { get; private set; }
    public CanalVendaExterna Canal { get; private set; }
    public string? DescricaoCanal { get; private set; }
    public DateOnly DataVenda { get; private set; }
    public string? Comprador { get; private set; }
    public DateOnly? Vencimento { get; private set; }
    public long TotalCentavos { get; private set; }
    public SituacaoVendaExterna Situacao { get; private set; }
    public Guid ResponsavelId { get; private set; }
    public DateTimeOffset CriadaEmUtc { get; private set; }
    public Guid Versao { get; private set; }
    public Guid? CanceladaPorId { get; private set; }
    public DateTimeOffset? CanceladaEmUtc { get; private set; }
    public string? MotivoCancelamento { get; private set; }
    public IReadOnlyList<ItemVendaExterna> Itens => _itens.AsReadOnly();
    public IReadOnlyList<RecebimentoVendaExterna> Recebimentos => _recebimentos.AsReadOnly();
    public long RecebidoCentavos => _recebimentos.Sum(r => r.ValorCentavos);
    public long SaldoCentavos => Situacao == SituacaoVendaExterna.Cancelada ? 0 : TotalCentavos - RecebidoCentavos;
    public string SituacaoPagamento => Situacao == SituacaoVendaExterna.Cancelada ? "Cancelada" :
        SaldoCentavos == 0 ? "Paga" : RecebidoCentavos == 0 ? "Pendente" : "Parcial";

    private VendaExterna() { }

    public static VendaExterna Criar(CanalVendaExterna canal, string? descricao, DateOnly data, string? comprador,
        DateOnly? vencimento, IEnumerable<ItemVendaExterna> itens, Guid autor, DateTimeOffset agora)
    {
        descricao = Texto(descricao, 100);
        comprador = Texto(comprador, 160);
        if (!Enum.IsDefined(canal) || (canal == CanalVendaExterna.Outro) != (descricao is not null) ||
            data == default || vencimento < data || autor == Guid.Empty)
            throw new ArgumentException("Confira canal, data da venda, vencimento e responsável.");
        var venda = new VendaExterna
        {
            Id = Guid.NewGuid(), Canal = canal, DescricaoCanal = descricao, DataVenda = data,
            Comprador = comprador, Vencimento = vencimento, ResponsavelId = autor,
            CriadaEmUtc = agora.ToUniversalTime(), Versao = Guid.NewGuid(), Situacao = SituacaoVendaExterna.Registrada,
        };
        venda._itens.AddRange(itens);
        if (venda._itens.Count is < 1 or > 50 || venda._itens.Select(i => i.VarianteId).Distinct().Count() != venda._itens.Count)
            throw new ArgumentException("Informe de um a 50 itens, com uma linha por variante.");
        venda.TotalCentavos = venda._itens.Sum(i => i.SubtotalCentavos);
        if (venda.TotalCentavos <= 0)
            throw new ArgumentException("O total da venda deve ser positivo.");
        return venda;
    }

    public void ValidarComprador()
    {
        if (SaldoCentavos > 0 && Comprador is null)
            throw new ArgumentException("Informe o nome ou a referência do comprador para a venda com saldo a receber.");
    }

    public RecebimentoVendaExterna Receber(long valor, DateOnly data, MeioRecebimentoVendaExterna meio, Guid autor, Guid chave, DateTimeOffset agora)
    {
        if (Situacao != SituacaoVendaExterna.Registrada || valor > SaldoCentavos)
            throw new InvalidOperationException("A venda não aceita este valor de recebimento.");
        if (valor <= 0 || data < DataVenda || !Enum.IsDefined(meio) || autor == Guid.Empty || chave == Guid.Empty)
            throw new ArgumentException("Confira valor, data e meio do recebimento.");
        var recebimento = new RecebimentoVendaExterna(Id, valor, data, meio, autor, chave, agora.ToUniversalTime());
        _recebimentos.Add(recebimento);
        Versao = Guid.NewGuid();
        return recebimento;
    }

    public void Cancelar(string? motivo, Guid autor, DateTimeOffset agora)
    {
        if (Situacao != SituacaoVendaExterna.Registrada || _recebimentos.Count != 0)
            throw new InvalidOperationException("Somente vendas registradas sem recebimentos podem ser canceladas.");
        MotivoCancelamento = Texto(motivo, 500) ?? throw new ArgumentException("Informe o motivo do cancelamento.");
        CanceladaPorId = autor;
        CanceladaEmUtc = agora.ToUniversalTime();
        Situacao = SituacaoVendaExterna.Cancelada;
        Versao = Guid.NewGuid();
    }

    private static string? Texto(string? valor, int limite)
    {
        if (string.IsNullOrWhiteSpace(valor)) return null;
        var texto = valor.Trim();
        return texto.Length <= limite ? texto : throw new ArgumentException($"O texto deve ter até {limite} caracteres.");
    }
}

public sealed class ItemVendaExterna
{
    public Guid Id { get; private set; }
    public Guid ProdutoId { get; private set; }
    public Guid VarianteId { get; private set; }
    public string NomeProduto { get; private set; } = null!;
    public string NomeVariante { get; private set; } = null!;
    public int Quantidade { get; private set; }
    public long PrecoUnitarioCentavos { get; private set; }
    public long DescontoCentavos { get; private set; }
    public long SubtotalCentavos { get; private set; }

    private ItemVendaExterna() { }

    public ItemVendaExterna(ProdutoVariante variante, int quantidade, long preco, long desconto)
    {
        if (!variante.Ativa || quantidade <= 0 || preco < 0 || desconto < 0)
            throw new ArgumentException("Confira variante ativa, quantidade, preço e desconto.");
        var bruto = checked(preco * quantidade);
        if (desconto > bruto) throw new ArgumentException("O desconto excede o valor do item.");
        Id = Guid.NewGuid();
        ProdutoId = variante.ProdutoId;
        VarianteId = variante.Id;
        NomeProduto = variante.Produto.Nome;
        NomeVariante = variante.Identificacao;
        Quantidade = quantidade;
        PrecoUnitarioCentavos = preco;
        DescontoCentavos = desconto;
        SubtotalCentavos = bruto - desconto;
    }
}

public sealed class RecebimentoVendaExterna
{
    public Guid Id { get; private set; }
    public Guid VendaId { get; private set; }
    public long ValorCentavos { get; private set; }
    public DateOnly Data { get; private set; }
    public MeioRecebimentoVendaExterna Meio { get; private set; }
    public Guid ResponsavelId { get; private set; }
    public Guid Chave { get; private set; }
    public DateTimeOffset RegistradoEmUtc { get; private set; }

    private RecebimentoVendaExterna() { }

    internal RecebimentoVendaExterna(Guid venda, long valor, DateOnly data, MeioRecebimentoVendaExterna meio,
        Guid autor, Guid chave, DateTimeOffset agora)
    {
        Id = Guid.NewGuid();
        VendaId = venda;
        ValorCentavos = valor;
        Data = data;
        Meio = meio;
        ResponsavelId = autor;
        Chave = chave;
        RegistradoEmUtc = agora;
    }
}

public sealed class BaixaVendaExterna
{
    public Guid MovimentoId { get; private set; }
    public Guid ItemId { get; private set; }
    public Guid LoteId { get; private set; }
    public int Quantidade { get; private set; }
    public Guid? MovimentoCancelamentoId { get; private set; }

    private BaixaVendaExterna() { }

    public BaixaVendaExterna(Guid item, MovimentoProdutoAcabado movimento)
    {
        MovimentoId = movimento.Id;
        ItemId = item;
        LoteId = movimento.LoteId;
        Quantidade = movimento.Quantidade;
    }

    public void VincularCancelamento(MovimentoProdutoAcabado movimento)
    {
        if (MovimentoCancelamentoId.HasValue || movimento.MovimentoOriginalId != MovimentoId)
            throw new InvalidOperationException("A compensação não corresponde à baixa original.");
        MovimentoCancelamentoId = movimento.Id;
    }
}
