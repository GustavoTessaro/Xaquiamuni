namespace Xaquiamuni.Aplicacao.Autenticacao;

public interface IEmailConfirmationOutbox
{
    void EnqueueEmailConfirmation(Guid userId);
}
