namespace Xaquiamuni.Aplicacao.Cadastro;

public sealed class CadastroClienteInputValidationError
{
    internal CadastroClienteInputValidationError(string campo, string mensagem)
    {
        Campo = campo;
        Mensagem = mensagem;
    }

    public string Campo { get; }
    public string Mensagem { get; }

    public override string ToString() => $"[{Campo}]";
}

public sealed class CadastroClienteInputValidationResult
{
    internal CadastroClienteInputValidationResult(IReadOnlyList<CadastroClienteInputValidationError> erros)
    {
        Erros = erros;
    }

    public IReadOnlyList<CadastroClienteInputValidationError> Erros { get; }
    public bool Valido => Erros.Count == 0;

    public override string ToString() => Valido ? "[CadastroClienteInput válido]" : "[CadastroClienteInput inválido]";
}
