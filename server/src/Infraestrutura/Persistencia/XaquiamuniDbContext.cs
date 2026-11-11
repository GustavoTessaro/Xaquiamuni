using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Dominio.Entidades;
using Xaquiamuni.Dominio.Valores;
using Xaquiamuni.Infraestrutura.Identidade;

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

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<XaquiamuniUser>()
            .HasIndex(user => user.NormalizedEmail)
            .IsUnique()
            .HasDatabaseName("EmailIndex")
            .HasFilter("[NormalizedEmail] IS NOT NULL");

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
