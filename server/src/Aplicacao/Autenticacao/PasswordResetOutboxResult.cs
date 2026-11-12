namespace Xaquiamuni.Aplicacao.Autenticacao;

public enum PasswordResetOutboxResult
{
    Enqueued,
    NotEligible,
    AlreadyQueued,
    CooldownActive,
    HourlyLimitReached
}
