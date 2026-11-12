namespace Xaquiamuni.Aplicacao.Autenticacao;

public interface IPasswordResetMessageComposer
{
    EmailMessage Compose(PasswordResetTokenResult tokenResult);
}
