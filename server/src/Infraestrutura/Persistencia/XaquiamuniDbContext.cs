using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Dominio.Entidades;
using Xaquiamuni.Dominio.Valores;
using Xaquiamuni.Infraestrutura.Identidade;
using Xaquiamuni.Infraestrutura.Persistencia.Outbox;

namespace Xaquiamuni.Infraestrutura.Persistencia;

public sealed class XaquiamuniDbContext
    : IdentityDbContext<XaquiamuniUser, IdentityRole<Guid>, Guid>
{
    public XaquiamuniDbContext(DbContextOptions<XaquiamuniDbContext> options)
        : base(options)
    {
    }

    public DbSet<ClientePerfil> ClientePerfis => Set<ClientePerfil>();
    public DbSet<ClienteEndereco> ClienteEnderecos => Set<ClienteEndereco>();
    public DbSet<EmailOutboxMessage> EmailOutboxMessages => Set<EmailOutboxMessage>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<XaquiamuniUser>()
            .HasIndex(user => user.NormalizedEmail)
            .IsUnique()
            .HasDatabaseName("EmailIndex")
            .HasFilter("[NormalizedEmail] IS NOT NULL");

        builder.Entity<EmailOutboxMessage>(outbox =>
        {
            outbox.ToTable("EmailOutbox", table =>
            {
                table.HasCheckConstraint(
                    "CK_EmailOutbox_Attempts_NonNegative",
                    "[Attempts] >= 0");
                table.HasCheckConstraint(
                    "CK_EmailOutbox_MessageType_Valid",
                    "[MessageType] IN (1)");
                table.HasCheckConstraint(
                    "CK_EmailOutbox_Status_Valid",
                    "[Status] IN (1, 2, 3, 4, 5)");
                table.HasCheckConstraint(
                    "CK_EmailOutbox_Timestamps_Utc",
                    "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0 AND "
                    + "DATEPART(TZOFFSET, [NextAttemptAtUtc]) = 0 AND "
                    + "([CompletedAtUtc] IS NULL OR DATEPART(TZOFFSET, [CompletedAtUtc]) = 0) AND "
                    + "([LeaseExpiresAtUtc] IS NULL OR DATEPART(TZOFFSET, [LeaseExpiresAtUtc]) = 0)");
            });
            outbox.HasKey(message => message.Id);
            outbox.Property(message => message.Id)
                .ValueGeneratedNever();
            outbox.Property(message => message.UserId)
                .IsRequired();
            outbox.Property(message => message.MessageType)
                .HasConversion<short>()
                .HasColumnType("smallint")
                .IsRequired();
            outbox.Property(message => message.Status)
                .HasConversion<short>()
                .HasColumnType("smallint")
                .IsRequired();
            outbox.Property(message => message.Attempts)
                .HasDefaultValue(0)
                .IsRequired();
            outbox.Property(message => message.CreatedAtUtc)
                .HasColumnType("datetimeoffset(7)")
                .IsRequired();
            outbox.Property(message => message.NextAttemptAtUtc)
                .HasColumnType("datetimeoffset(7)")
                .IsRequired();
            outbox.Property(message => message.CompletedAtUtc)
                .HasColumnType("datetimeoffset(7)");
            outbox.Property(message => message.ActiveDeduplicationKey)
                .HasMaxLength(EmailOutboxMessage.ActiveDeduplicationKeyMaxLength);
            outbox.Property(message => message.LeaseExpiresAtUtc)
                .HasColumnType("datetimeoffset(7)");
            outbox.Property(message => message.RowVersion)
                .IsRowVersion()
                .HasColumnType("rowversion")
                .IsRequired();

            outbox.HasOne<XaquiamuniUser>()
                .WithMany()
                .HasForeignKey(message => message.UserId)
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();

            outbox.HasIndex(message => new
                {
                    message.Status,
                    message.NextAttemptAtUtc,
                    message.CreatedAtUtc
                })
                .HasDatabaseName("IX_EmailOutbox_Queue");
            outbox.HasIndex(message => message.ActiveDeduplicationKey)
                .IsUnique()
                .HasDatabaseName("UX_EmailOutbox_ActiveDeduplicationKey")
                .HasFilter("[ActiveDeduplicationKey] IS NOT NULL");
            outbox.HasIndex(message => message.UserId)
                .HasDatabaseName("IX_EmailOutbox_UserId");
        });

        builder.Entity<ClientePerfil>(profile =>
        {
            profile.ToTable("ClientePerfis");
            profile.HasKey(entity => entity.UserId);
            profile.Property(entity => entity.UserId)
                .HasColumnName("UserId")
                .ValueGeneratedNever();
            profile.Property(entity => entity.NomeCompleto)
                .HasColumnName("NomeCompleto")
                .HasMaxLength(150)
                .IsRequired();

            profile.HasOne<XaquiamuniUser>()
                .WithOne()
                .HasForeignKey<ClientePerfil>(entity => entity.UserId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired();

            profile.OwnsOne(entity => entity.CpfProtegido, cpf =>
            {
                cpf.Property(value => value.Ciphertext)
                    .HasColumnName("CpfCiphertext")
                    .HasMaxLength(16)
                    .IsRequired();
                cpf.Property(value => value.Nonce)
                    .HasColumnName("CpfNonce")
                    .HasMaxLength(16)
                    .IsRequired();
                cpf.Property(value => value.AuthenticationTag)
                    .HasColumnName("CpfAuthenticationTag")
                    .HasMaxLength(24)
                    .IsRequired();
                cpf.Property(value => value.Fingerprint)
                    .HasColumnName("CpfFingerprint")
                    .HasMaxLength(64)
                    .IsRequired();
                cpf.Property(value => value.EncryptionKeyVersion)
                    .HasColumnName("CpfEncryptionKeyVersion")
                    .HasMaxLength(32)
                    .IsRequired();
                cpf.Property(value => value.FingerprintKeyVersion)
                    .HasColumnName("CpfFingerprintKeyVersion")
                    .HasMaxLength(32)
                    .IsRequired();
                cpf.HasIndex(value => value.Fingerprint)
                    .IsUnique()
                    .HasDatabaseName("UX_ClientePerfis_CpfFingerprint");
            });
            profile.Navigation(entity => entity.CpfProtegido).IsRequired();
        });

        builder.Entity<ClienteEndereco>(address =>
        {
            address.ToTable("ClienteEnderecos");
            address.HasKey(entity => entity.Id);
            address.Property(entity => entity.Id)
                .HasColumnName("Id")
                .ValueGeneratedNever();
            address.Property(entity => entity.ClienteUserId)
                .HasColumnName("ClienteUserId")
                .IsRequired();
            address.Property(entity => entity.Cep)
                .HasColumnName("Cep")
                .HasMaxLength(8)
                .IsRequired();
            address.Property(entity => entity.Uf)
                .HasColumnName("Uf")
                .HasMaxLength(2)
                .IsRequired();
            address.Property(entity => entity.Cidade)
                .HasColumnName("Cidade")
                .HasMaxLength(100)
                .IsRequired();
            address.Property(entity => entity.Bairro)
                .HasColumnName("Bairro")
                .HasMaxLength(100)
                .IsRequired();
            address.Property(entity => entity.Logradouro)
                .HasColumnName("Logradouro")
                .HasMaxLength(150)
                .IsRequired();
            address.Property(entity => entity.Numero)
                .HasColumnName("Numero")
                .HasMaxLength(20)
                .IsRequired();
            address.Property(entity => entity.Complemento)
                .HasColumnName("Complemento")
                .HasMaxLength(100);
            address.Property(entity => entity.IsDefault)
                .HasColumnName("IsDefault")
                .IsRequired();

            address.HasOne<ClientePerfil>()
                .WithMany()
                .HasForeignKey(entity => entity.ClienteUserId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired();

            address.HasIndex(entity => entity.ClienteUserId)
                .HasDatabaseName("IX_ClienteEnderecos_ClienteUserId");
            address.HasIndex(entity => entity.ClienteUserId, "UX_ClienteEnderecos_DefaultPerClient")
                .HasDatabaseName("UX_ClienteEnderecos_DefaultPerClient")
                .IsUnique()
                .HasFilter("[IsDefault] = 1");
        });
    }
}
