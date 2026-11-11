namespace Xaquiamuni.Aplicacao.Autenticacao;

public sealed class EmailDeliveryException : Exception
{
    public EmailDeliveryException(bool isTransient)
        : base("Email delivery failed.")
    {
        IsTransient = isTransient;
    }

    public bool IsTransient { get; }
}
