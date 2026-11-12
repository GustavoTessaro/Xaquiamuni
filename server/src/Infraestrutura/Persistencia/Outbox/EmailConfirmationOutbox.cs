using Xaquiamuni.Aplicacao.Autenticacao;
using Xaquiamuni.Aplicacao.Outbox;

namespace Xaquiamuni.Infraestrutura.Persistencia.Outbox;

public sealed class EmailConfirmationOutbox : IEmailConfirmationOutbox
{
    private const string DeduplicationKeyPrefix = "email-confirmation:";

    private readonly XaquiamuniDbContext _dbContext;
    private readonly TimeProvider _timeProvider;

    public EmailConfirmationOutbox(
        XaquiamuniDbContext dbContext,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
    }

    public void EnqueueEmailConfirmation(Guid userId)
    {
        var deduplicationKey = $"{DeduplicationKeyPrefix}{userId:N}";
        if (_dbContext.EmailOutboxMessages.Local.Any(message =>
                string.Equals(
                    message.ActiveDeduplicationKey,
                    deduplicationKey,
                    StringComparison.Ordinal)))
        {
            return;
        }

        var now = _timeProvider.GetUtcNow();
        _dbContext.EmailOutboxMessages.Add(new EmailOutboxMessage(
            Guid.NewGuid(),
            userId,
            EmailOutboxMessageType.EmailConfirmation,
            EmailOutboxMessageStatus.Pending,
            attempts: 0,
            createdAtUtc: now,
            nextAttemptAtUtc: now,
            activeDeduplicationKey: deduplicationKey));
    }
}
