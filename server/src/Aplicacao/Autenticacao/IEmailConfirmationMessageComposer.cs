namespace Xaquiamuni.Aplicacao.Autenticacao;

public interface IEmailConfirmationMessageComposer
{
    EmailMessage Compose(EmailConfirmationTokenResult tokenResult);
}
