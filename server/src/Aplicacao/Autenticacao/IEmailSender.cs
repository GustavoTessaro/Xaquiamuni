namespace Xaquiamuni.Aplicacao.Autenticacao;

public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}
