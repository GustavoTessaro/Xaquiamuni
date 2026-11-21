namespace Xaquiamuni.Dominio.Financeiro;

public enum TipoLancamento { Entrada = 1, Saida }
public enum SituacaoLancamento { Pendente, Realizado, Cancelado }
public enum MeioFinanceiro { Pix = 1, Dinheiro, Cartao, Transferencia, Boleto, Outro }
public sealed class ConflitoFinanceiroException(string mensagem) : InvalidOperationException(mensagem);

public sealed class LancamentoFinanceiro
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public TipoLancamento Tipo { get; private set; }
    public string Categoria { get; private set; } = null!;
    public string Descricao { get; private set; } = null!;
    public long ValorCentavos { get; private set; }
    public DateOnly? Data { get; private set; }
    public MeioFinanceiro? Meio { get; private set; }
    public DateOnly? Vencimento { get; private set; }
    public string? Contraparte { get; private set; }
    public string? Observacoes { get; private set; }
    public Guid? QuitadoPorId { get; private set; }
    public DateTimeOffset? QuitadoEmUtc { get; private set; }
    public Guid ResponsavelId { get; private set; }
    public DateTimeOffset CriadoEmUtc { get; private set; }
    public Guid? CanceladoPorId { get; private set; }
    public DateTimeOffset? CanceladoEmUtc { get; private set; }
    public string? MotivoCancelamento { get; private set; }
    public SituacaoLancamento Situacao => CanceladoEmUtc.HasValue ? SituacaoLancamento.Cancelado :
        Data.HasValue ? SituacaoLancamento.Realizado : SituacaoLancamento.Pendente;

    private LancamentoFinanceiro() { }

    public static LancamentoFinanceiro Criar(TipoLancamento tipo, string? categoria, string? descricao,
        long valor, DateOnly data, MeioFinanceiro meio, Guid autor, DateTimeOffset agora,
        string? contraparte = null, string? observacoes = null)
    {
        ValidacaoFinanceira.ValidarMovimento(valor, data, meio, autor);
        var registro = CriarRegistro(tipo, categoria, descricao, valor, autor, agora, contraparte, observacoes);
        registro.Data = data;
        registro.Meio = meio;
        return registro;
    }

    public static LancamentoFinanceiro CriarPendente(TipoLancamento tipo, string? categoria, string? descricao,
        long valor, DateOnly? vencimento, Guid autor, DateTimeOffset agora, string? contraparte, string? observacoes)
    {
        if (vencimento is null || vencimento == default(DateOnly))
            throw new ArgumentException("Informe uma data de vencimento válida.");
        var registro = CriarRegistro(tipo, categoria, descricao, valor, autor, agora, contraparte, observacoes);
        registro.Vencimento = vencimento;
        return registro;
    }

    private static LancamentoFinanceiro CriarRegistro(TipoLancamento tipo, string? categoria, string? descricao,
        long valor, Guid autor, DateTimeOffset agora, string? contraparte, string? observacoes)
    {
        if (!Enum.IsDefined(tipo)) throw new ArgumentException("Informe Entrada ou Saida.");
        if (valor <= 0 || autor == Guid.Empty)
            throw new ArgumentException("Informe valor positivo em centavos e responsável válido.");
        return new()
        {
            Tipo = tipo, Categoria = ValidacaoFinanceira.Texto(categoria, 80, "categoria", obrigatorio: true)!,
            Descricao = ValidacaoFinanceira.Texto(descricao, 300, "descrição", obrigatorio: true)!,
            ValorCentavos = valor, ResponsavelId = autor, CriadoEmUtc = agora.ToUniversalTime(),
            Contraparte = ValidacaoFinanceira.Texto(contraparte, 150, "contraparte"),
            Observacoes = ValidacaoFinanceira.Texto(observacoes, 500, "observações")
        };
    }

    public void Quitar(DateOnly data, MeioFinanceiro meio, Guid autor, DateTimeOffset agora, DateOnly dataOperacional)
    {
        if (Situacao == SituacaoLancamento.Cancelado)
            throw new ConflitoFinanceiroException("O lançamento foi cancelado e não pode ser quitado.");
        if (Situacao == SituacaoLancamento.Realizado)
            throw new ConflitoFinanceiroException("O lançamento já foi realizado.");
        ValidacaoFinanceira.ValidarMovimento(ValorCentavos, data, meio, autor);
        if (data > dataOperacional)
            throw new ArgumentException("A data efetiva não pode ser futura em relação à data operacional.");
        Data = data;
        Meio = meio;
        QuitadoPorId = autor;
        QuitadoEmUtc = agora.ToUniversalTime();
    }

    public void Cancelar(string? motivo, Guid autor, DateTimeOffset agora)
    {
        if (CanceladoEmUtc.HasValue) throw new ConflitoFinanceiroException("O lançamento já foi cancelado.");
        if (QuitadoEmUtc.HasValue) throw new ConflitoFinanceiroException("Um compromisso quitado não pode ser cancelado.");
        if (autor == Guid.Empty) throw new ArgumentException("Informe o responsável.");
        MotivoCancelamento = ValidacaoFinanceira.Texto(motivo, 500, "motivo", obrigatorio: true);
        CanceladoPorId = autor;
        CanceladoEmUtc = agora.ToUniversalTime();
    }
}

public sealed class PagamentoFornecedor
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid CompraId { get; private set; }
    public long ValorCentavos { get; private set; }
    public DateOnly Data { get; private set; }
    public MeioFinanceiro Meio { get; private set; }
    public string? Referencia { get; private set; }
    public Guid ResponsavelId { get; private set; }
    public DateTimeOffset CriadoEmUtc { get; private set; }

    private PagamentoFornecedor() { }

    public static PagamentoFornecedor Registrar(Guid compraId, long valor, long? saldo, DateOnly data,
        MeioFinanceiro meio, string? referencia, Guid autor, DateTimeOffset agora)
    {
        ValidacaoFinanceira.ValidarMovimento(valor, data, meio, autor);
        if (compraId == Guid.Empty) throw new ArgumentException("Informe a compra.");
        if (saldo is null) throw new ConflitoFinanceiroException("Valor a conferir. O registro exige um total confirmado.");
        if (valor > saldo) throw new ConflitoFinanceiroException("O pagamento excede o saldo devido.");
        return new()
        {
            CompraId = compraId, ValorCentavos = valor, Data = data, Meio = meio,
            Referencia = ValidacaoFinanceira.Texto(referencia, 300, "referência"),
            ResponsavelId = autor, CriadoEmUtc = agora.ToUniversalTime()
        };
    }
}

internal static class ValidacaoFinanceira
{
    public static void ValidarMovimento(long valor, DateOnly data, MeioFinanceiro meio, Guid autor)
    {
        if (valor <= 0 || data == default || !Enum.IsDefined(meio) || autor == Guid.Empty)
            throw new ArgumentException("Informe valor positivo em centavos, data, meio e responsável válidos.");
    }

    public static string? Texto(string? valor, int limite, string campo, bool obrigatorio = false)
    {
        valor = string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
        if ((obrigatorio && valor is null) || valor?.Length > limite)
            throw new ArgumentException($"Confira o campo {campo}. O limite é de {limite} caracteres.");
        return valor;
    }
}
