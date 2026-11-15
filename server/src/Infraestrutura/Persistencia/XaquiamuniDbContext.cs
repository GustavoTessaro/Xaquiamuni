using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Aplicacao.Catalogo;
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
    public DbSet<Categoria> Categorias => Set<Categoria>();
    public DbSet<Produto> Produtos => Set<Produto>();
    public DbSet<ProdutoVariante> ProdutoVariantes => Set<ProdutoVariante>();
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
                    "[MessageType] IN (1, 2)");
                table.HasCheckConstraint(
                    "CK_EmailOutbox_CredentialGenerationFingerprint_Valid",
                    "([MessageType] = 1 AND [CredentialGenerationFingerprint] IS NULL) OR "
                    + "([MessageType] = 2 AND [CredentialGenerationFingerprint] IS NOT NULL)");
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
            outbox.Property(message => message.CredentialGenerationFingerprint)
                .HasField("_credentialGenerationFingerprint")
                .UsePropertyAccessMode(PropertyAccessMode.Field)
                .HasColumnType("binary(32)");
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

        builder.Entity<Categoria>(categoria =>
        {
            categoria.ToTable("Categorias", table =>
                table.HasCheckConstraint(
                    "CK_Categorias_OrdemExibicao_NonNegative",
                    "[OrdemExibicao] >= 0"));
            categoria.HasKey(entity => entity.Id);
            categoria.Property(entity => entity.Id)
                .ValueGeneratedNever();
            categoria.Property(entity => entity.Nome)
                .HasMaxLength(Categoria.NomeMaxLength)
                .IsRequired();
            categoria.Property(entity => entity.NomeNormalizado)
                .HasMaxLength(Categoria.NomeMaxLength)
                .UseCollation("Latin1_General_100_BIN2")
                .IsRequired();
            categoria.Property(entity => entity.Descricao)
                .HasMaxLength(Categoria.DescricaoMaxLength);
            categoria.Property(entity => entity.Ativa)
                .IsRequired();
            categoria.Property(entity => entity.OrdemExibicao)
                .IsRequired();
            categoria.HasIndex(entity => entity.NomeNormalizado)
                .IsUnique()
                .HasDatabaseName("UX_Categorias_NomeNormalizado");
        });

        builder.Entity<Produto>(produto =>
        {
            produto.ToTable("Produtos", table =>
                table.HasCheckConstraint(
                    "CK_Produtos_Nome_NotBlank",
                    "LEN(LTRIM(RTRIM([Nome]))) > 0"));
            produto.HasKey(entity => entity.Id);
            produto.Property(entity => entity.Id)
                .ValueGeneratedNever();
            produto.Property(entity => entity.Nome)
                .HasMaxLength(Produto.NomeMaxLength)
                .IsRequired();
            produto.Property(entity => entity.Descricao)
                .HasMaxLength(Produto.DescricaoMaxLength);
            produto.Property(entity => entity.CategoriaId)
                .IsRequired();
            produto.Property(entity => entity.Publicado)
                .IsRequired();

            produto.HasOne(entity => entity.Categoria)
                .WithMany()
                .HasForeignKey(entity => entity.CategoriaId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired();
            produto.HasMany(entity => entity.Variantes)
                .WithOne(variant => variant.Produto)
                .HasForeignKey(variant => variant.ProdutoId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired();
            produto.Navigation(entity => entity.Variantes)
                .HasField("_variantes")
                .UsePropertyAccessMode(PropertyAccessMode.Field);

            produto.HasIndex(entity => entity.CategoriaId)
                .HasDatabaseName("IX_Produtos_CategoriaId");
        });

        builder.Entity<ProdutoVariante>(variante =>
        {
            variante.ToTable("ProdutoVariantes", table =>
            {
                table.HasCheckConstraint(
                    "CK_ProdutoVariantes_Identificacao_NotBlank",
                    "LEN(LTRIM(RTRIM([Identificacao]))) > 0");
                table.HasCheckConstraint(
                    "CK_ProdutoVariantes_UnidadeVenda_NotBlank",
                    "LEN(LTRIM(RTRIM([UnidadeVenda]))) > 0");
                table.HasCheckConstraint(
                    "CK_ProdutoVariantes_PrecoVenda_Positive",
                    "[PrecoVenda] > 0");
                table.HasCheckConstraint(
                    "CK_ProdutoVariantes_Default_Active",
                    "[Padrao] = 0 OR [Ativa] = 1");
            });
            variante.HasKey(entity => entity.Id);
            variante.Property(entity => entity.Id)
                .ValueGeneratedNever();
            variante.Property(entity => entity.ProdutoId)
                .IsRequired();
            variante.Property(entity => entity.Identificacao)
                .HasMaxLength(ProdutoVariante.IdentificacaoMaxLength)
                .IsRequired();
            variante.Property(entity => entity.IdentificacaoNormalizada)
                .HasMaxLength(ProdutoVariante.IdentificacaoMaxLength)
                .UseCollation("Latin1_General_100_BIN2")
                .IsRequired();
            variante.Property(entity => entity.UnidadeVenda)
                .HasMaxLength(ProdutoVariante.UnidadeVendaMaxLength)
                .IsRequired();
            variante.Property(entity => entity.PrecoVenda)
                .HasPrecision(18, 2)
                .IsRequired();
            variante.Property(entity => entity.Ativa)
                .IsRequired();
            variante.Property(entity => entity.Padrao)
                .IsRequired();

            variante.HasIndex(entity => new { entity.ProdutoId, entity.IdentificacaoNormalizada })
                .IsUnique()
                .HasDatabaseName("UX_ProdutoVariantes_ProdutoId_IdentificacaoNormalizada");
            variante.HasIndex(entity => entity.ProdutoId)
                .IsUnique()
                .HasFilter("[Padrao] = 1")
                .HasDatabaseName("UX_ProdutoVariantes_DefaultPerProduto");
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
