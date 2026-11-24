using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.Linq.Expressions;
using Xaquiamuni.Aplicacao.Catalogo;
using Xaquiamuni.Dominio.Estoque;
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

    public DbSet<ConfiguracaoTemaLoja> ConfiguracoesTemaLoja => Set<ConfiguracaoTemaLoja>();
    public DbSet<ClientePerfil> ClientePerfis => Set<ClientePerfil>();
    public DbSet<ConfiguracaoPixManual> ConfiguracoesPixManual => Set<ConfiguracaoPixManual>();

    public DbSet<Pedido> Pedidos => Set<Pedido>();
    public DbSet<ItemPedido> ItensPedido => Set<ItemPedido>();
    public DbSet<ClienteEndereco> ClienteEnderecos => Set<ClienteEndereco>();
    public DbSet<EntradaRecebimentoCompra> EntradasRecebimentoCompra => Set<EntradaRecebimentoCompra>();
    public DbSet<CompraMateriaPrima> ComprasMateriasPrimas => Set<CompraMateriaPrima>();
    public DbSet<OrdemProducao> OrdensProducao => Set<OrdemProducao>();
    public DbSet<AlocacaoMateriaPrima> AlocacoesMateriasPrimas => Set<AlocacaoMateriaPrima>();
    public DbSet<ReceitaProducao> ReceitasProducao => Set<ReceitaProducao>();
    public DbSet<Fornecedor> Fornecedores => Set<Fornecedor>();
    public DbSet<Categoria> Categorias => Set<Categoria>();
    public DbSet<Produto> Produtos => Set<Produto>();
    public DbSet<ProdutoVariante> ProdutoVariantes => Set<ProdutoVariante>();
    public DbSet<ProdutoImagem> ProdutoImagens => Set<ProdutoImagem>();
    public DbSet<ProdutoImagemVariante> ProdutoImagemVariantes => Set<ProdutoImagemVariante>();
    public DbSet<ProdutoCaracteristica> ProdutoCaracteristicas => Set<ProdutoCaracteristica>();
    public DbSet<ProdutoCaracteristicaValor> ProdutoCaracteristicaValores =>
        Set<ProdutoCaracteristicaValor>();
    public DbSet<ProdutoVarianteCaracteristicaValor> ProdutoVarianteCaracteristicaValores =>
        Set<ProdutoVarianteCaracteristicaValor>();
    public DbSet<EmailOutboxMessage> EmailOutboxMessages => Set<EmailOutboxMessage>();
    public DbSet<InsumoEstoque> InsumosEstoque => Set<InsumoEstoque>();
    public DbSet<FormaCompraInsumo> FormasCompraInsumo => Set<FormaCompraInsumo>();
    public DbSet<LoteInsumo> LotesInsumo => Set<LoteInsumo>();
    public DbSet<SaldoInsumo> SaldosInsumo => Set<SaldoInsumo>();
    public DbSet<MovimentoInsumo> MovimentosInsumo => Set<MovimentoInsumo>();
    public DbSet<AjusteSemDiferencaInsumo> AjustesSemDiferencaInsumo =>
        Set<AjusteSemDiferencaInsumo>();
    public DbSet<PoliticaEstoqueProdutoVariante> PoliticasEstoqueProdutoVariante =>
        Set<PoliticaEstoqueProdutoVariante>();
    public DbSet<LoteProdutoAcabado> LotesProdutoAcabado => Set<LoteProdutoAcabado>();
    public DbSet<SaldoLoteProdutoAcabado> SaldosProdutoAcabado =>
        Set<SaldoLoteProdutoAcabado>();
    public DbSet<ReservaProdutoAcabado> ReservasProdutoAcabado =>
        Set<ReservaProdutoAcabado>();
    public DbSet<MovimentoProdutoAcabado> MovimentosProdutoAcabado =>
        Set<MovimentoProdutoAcabado>();
    public DbSet<ContagemSemDiferencaProdutoAcabado> ContagensSemDiferencaProdutoAcabado =>
        Set<ContagemSemDiferencaProdutoAcabado>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ValidarMovimentosESaldos();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        ValidarMovimentosESaldos();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.MapearReceitasProducao();
        builder.MapearOrdensProducao();
        builder.MapearVendasExternas();
        builder.MapearComprasMateriasPrimas();
        builder.MapearFinanceiro();
        builder.MapearAgenda();
        builder.MapearPromocoes();
        builder.MapearPromocoesFrete();
        builder.Entity<Fornecedor>(fornecedor =>
        {
            fornecedor.ToTable("Fornecedores");
            fornecedor.Property(f => f.Versao).HasDefaultValueSql("NEWID()").IsConcurrencyToken();
            fornecedor.HasKey(f => f.Id);
            fornecedor.Property(f => f.Nome).HasMaxLength(Fornecedor.NomeMax).IsRequired();
            fornecedor.Property(f => f.NomeNormalizado).HasMaxLength(Fornecedor.NomeMax).IsRequired();
            fornecedor.Property(f => f.Telefone).HasMaxLength(Fornecedor.TelefoneMax);
            fornecedor.Property(f => f.Email).HasMaxLength(Fornecedor.EmailMax);
            fornecedor.Property(f => f.Observacoes).HasMaxLength(Fornecedor.ObservacoesMax);
            fornecedor.HasIndex(f => f.NomeNormalizado).IsUnique().HasDatabaseName("UX_Fornecedores_NomeNormalizado");
        });
        builder.Entity<ExclusaoFornecedor>(operacao =>
        {
            operacao.ToTable("OperacoesExclusaoFornecedor");
            operacao.HasKey(x => new { x.Autor, x.Chave });
            operacao.Property(x => x.Nome).HasMaxLength(Fornecedor.NomeMax);
            operacao.Property(x => x.Resultado).HasMaxLength(24);
        });
        PedidoMapping.Configurar(builder);
        AtendimentoMapping.Configurar(builder);
        CorreiosManualMapping.Configurar(builder);
        builder.Entity<EnvioComprovantePix>(e =>
        {
            e.ToTable("EnviosComprovantePix");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Tipo).HasMaxLength(20);
            e.Property(x => x.Sha256).HasMaxLength(64).IsFixedLength();
            e.HasIndex(x => new { x.ClienteId, x.Chave }).IsUnique();
            e.HasOne<Pedido>().WithMany().HasForeignKey(x => x.PedidoId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<ComprovantePixAtivo>(e =>
        {
            e.ToTable("ComprovantesPixAtivos");
            e.HasKey(x => x.PedidoId);
            e.Property(x => x.PedidoId).ValueGeneratedNever();
            e.Property(x => x.EnvioId).IsConcurrencyToken();
            e.Property(x => x.Bytes).HasColumnType("varbinary(max)");
            e.HasOne<Pedido>().WithOne().HasForeignKey<ComprovantePixAtivo>(x => x.PedidoId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Envio).WithOne().HasForeignKey<ComprovantePixAtivo>(x => x.EnvioId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<ConfiguracaoTemaLoja>(tema =>
        {
            tema.ToTable("ConfiguracoesTemaLoja", tabela =>
                tabela.HasCheckConstraint("CK_ConfiguracoesTemaLoja_Unica", "[Id] = 1"));
            tema.HasKey(t => t.Id);
            tema.Property(t => t.Id).ValueGeneratedNever();
            tema.Property(t => t.Chave).HasMaxLength(32).IsRequired();
            tema.Property(t => t.Revisao).IsConcurrencyToken();
        });

        builder.Entity<ConfiguracaoPixManual>(pix =>
        {
            pix.ToTable("ConfiguracoesPixManual", tabela =>
            {
                tabela.HasCheckConstraint("CK_ConfiguracoesPixManual_Singleton", "[Id] = 1");
                tabela.HasCheckConstraint("CK_ConfiguracoesPixManual_Tipo", "[TipoChave] IN (1, 2, 3, 4)");
            });
            pix.Property(p => p.Id).ValueGeneratedNever();
            pix.Property(p => p.ChavePix).HasMaxLength(ConfiguracaoPixManual.ChaveMaxLength);
            pix.Property(p => p.NomeRecebedor).HasMaxLength(ConfiguracaoPixManual.NomeMaxLength);
            pix.Property(p => p.InstrucoesAdicionais).HasMaxLength(ConfiguracaoPixManual.InstrucoesMaxLength);
        });

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

        builder.Entity<InsumoEstoque>(insumo =>
        {
            insumo.ToTable("InsumosEstoque", table =>
            {
                table.HasCheckConstraint(
                    "CK_InsumosEstoque_Nome_NotBlank",
                    "LEN(LTRIM(RTRIM([Nome]))) > 0 AND "
                    + "LEN(LTRIM(RTRIM([NomeNormalizado]))) > 0");
                table.HasCheckConstraint(
                    "CK_InsumosEstoque_Tipo_Valid",
                    "[Tipo] IN (1, 2, 3)");
                table.HasCheckConstraint(
                    "CK_InsumosEstoque_UnidadeBase_Valid",
                    "[UnidadeBase] IN (1, 3, 5)");
                table.HasCheckConstraint(
                    "CK_InsumosEstoque_ValidadeRequerLote",
                    "[ValidadeObrigatoria] = 0 OR [ControlaLote] = 1");
                table.HasCheckConstraint(
                    "CK_InsumosEstoque_EstoqueMinimo_Valid",
                    "([EstoqueMinimo_ValorBase] IS NULL AND "
                    + "[EstoqueMinimo_UnidadeBase] IS NULL AND "
                    + "[EstoqueMinimo_Dimensao] IS NULL) OR "
                    + "([EstoqueMinimo_ValorBase] IS NOT NULL AND "
                    + "[EstoqueMinimo_UnidadeBase] IS NOT NULL AND "
                    + "[EstoqueMinimo_Dimensao] IS NOT NULL AND "
                    + "[EstoqueMinimo_UnidadeBase] = [UnidadeBase] AND "
                    + "([EstoqueMinimo_UnidadeBase] = 1 AND [EstoqueMinimo_Dimensao] = 1 OR "
                    + "[EstoqueMinimo_UnidadeBase] = 3 AND [EstoqueMinimo_Dimensao] = 2 OR "
                    + "[EstoqueMinimo_UnidadeBase] = 5 AND [EstoqueMinimo_Dimensao] = 3) AND "
                    + "[EstoqueMinimo_ValorBase] >= 0 AND "
                    + "([UnidadeBase] <> 5 OR "
                    + "[EstoqueMinimo_ValorBase] = FLOOR([EstoqueMinimo_ValorBase])))");
            });
            insumo.HasKey(entity => entity.Id);
            insumo.Property(entity => entity.Id)
                .ValueGeneratedNever();
            insumo.Property(entity => entity.Nome)
                .HasMaxLength(InsumoEstoque.NomeMaxLength)
                .IsRequired();
            insumo.Property(entity => entity.NomeNormalizado)
                .HasMaxLength(InsumoEstoque.NomeMaxLength)
                .UseCollation("Latin1_General_100_BIN2")
                .IsRequired();
            insumo.Property(entity => entity.Tipo)
                .HasConversion<short>()
                .HasColumnType("smallint")
                .IsRequired();
            insumo.Property(entity => entity.UnidadeBase)
                .HasConversion<short>()
                .HasColumnType("smallint")
                .IsRequired();
            insumo.Property(entity => entity.ControlaLote)
                .IsRequired();
            insumo.Property(entity => entity.ValidadeObrigatoria)
                .IsRequired();
            insumo.Property(entity => entity.Ativo)
                .IsRequired();
            insumo.Property<byte[]>("Versao")
                .HasColumnName("Versao")
                .IsRowVersion()
                .IsRequired();

            insumo.OwnsOne(entity => entity.EstoqueMinimo, quantidade =>
            {
                quantidade.Property(value => value.ValorBase)
                    .HasColumnName("EstoqueMinimo_ValorBase")
                    .HasPrecision(QuantidadeEstoque.Precision, QuantidadeEstoque.Scale);
                quantidade.Property(value => value.UnidadeBase)
                    .HasColumnName("EstoqueMinimo_UnidadeBase")
                    .HasConversion<short>()
                    .HasColumnType("smallint");
                quantidade.Property(value => value.Dimensao)
                    .HasColumnName("EstoqueMinimo_Dimensao")
                    .HasConversion<short>()
                    .HasColumnType("smallint");
            });
            insumo.Navigation(entity => entity.EstoqueMinimo)
                .IsRequired(false);
            insumo.HasMany(entity => entity.FormasCompra)
                .WithOne()
                .HasForeignKey(forma => forma.InsumoId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_FormasCompraInsumo_InsumosEstoque_InsumoId")
                .IsRequired();
            insumo.Navigation(entity => entity.FormasCompra)
                .HasField("_formasCompra")
                .UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        builder.Entity<FormaCompraInsumo>(forma =>
        {
            forma.ToTable("FormasCompraInsumo", table =>
            {
                table.HasCheckConstraint(
                    "CK_FormasCompraInsumo_Nome_NotBlank",
                    "LEN(LTRIM(RTRIM([Nome]))) > 0 AND "
                    + "LEN(LTRIM(RTRIM([NomeNormalizado]))) > 0");
                table.HasCheckConstraint(
                    "CK_FormasCompraInsumo_Fator_Valid",
                    "[Fator_ValorBase] > 0 AND "
                    + "([Fator_UnidadeBase] = 1 AND [Fator_Dimensao] = 1 OR "
                    + "[Fator_UnidadeBase] = 3 AND [Fator_Dimensao] = 2 OR "
                    + "[Fator_UnidadeBase] = 5 AND [Fator_Dimensao] = 3) AND "
                    + "([Fator_UnidadeBase] <> 5 OR "
                    + "[Fator_ValorBase] = FLOOR([Fator_ValorBase]))");
            });
            forma.HasKey(entity => entity.Id);
            forma.Property(entity => entity.Id)
                .ValueGeneratedNever();
            forma.Property(entity => entity.InsumoId)
                .IsRequired();
            forma.Property(entity => entity.Nome)
                .HasMaxLength(FormaCompraInsumo.NomeMaxLength)
                .IsRequired();
            forma.Property(entity => entity.NomeNormalizado)
                .HasMaxLength(FormaCompraInsumo.NomeMaxLength)
                .UseCollation("Latin1_General_100_BIN2")
                .IsRequired();
            forma.Property(entity => entity.Ativa)
                .IsRequired();
            forma.Property<byte[]>("Versao")
                .HasColumnName("Versao")
                .IsRowVersion()
                .IsRequired();

            forma.OwnsOne(entity => entity.QuantidadeEquivalentePorEmbalagem, quantidade =>
            {
                quantidade.Property(value => value.ValorBase)
                    .HasColumnName("Fator_ValorBase")
                    .HasPrecision(QuantidadeEstoque.Precision, QuantidadeEstoque.Scale)
                    .IsRequired();
                quantidade.Property(value => value.UnidadeBase)
                    .HasColumnName("Fator_UnidadeBase")
                    .HasConversion<short>()
                    .HasColumnType("smallint")
                    .IsRequired();
                quantidade.Property(value => value.Dimensao)
                    .HasColumnName("Fator_Dimensao")
                    .HasConversion<short>()
                    .HasColumnType("smallint")
                    .IsRequired();
            });

            builder.Entity<PoliticaEstoqueProdutoVariante>(politica =>
            {
                politica.ToTable("PoliticasEstoqueProdutoVariante", table =>
                {
                    table.HasCheckConstraint(
                        "CK_PoliticasEstoqueProdutoVariante_EstoqueMinimo_NonNegative",
                        "[EstoqueMinimo] IS NULL OR [EstoqueMinimo] >= 0");
                    table.HasCheckConstraint(
                        "CK_PoliticasEstoqueProdutoVariante_PrazoValidade_NonNegative",
                        "[PrazoValidadePadraoDias] IS NULL OR [PrazoValidadePadraoDias] >= 0");
                });
                politica.HasKey(entity => entity.ProdutoVarianteId);
                politica.Property(entity => entity.ProdutoVarianteId)
                    .ValueGeneratedNever();
                politica.Property(entity => entity.EstoqueMinimo);
                politica.Property(entity => entity.PrazoValidadePadraoDias);
                politica.HasOne<ProdutoVariante>()
                    .WithOne()
                    .HasForeignKey<PoliticaEstoqueProdutoVariante>(entity => entity.ProdutoVarianteId)
                    .OnDelete(DeleteBehavior.Restrict)
                    .HasConstraintName("FK_PoliticasEstoqueProdutoVariante_ProdutoVariantes_ProdutoVarianteId")
                    .IsRequired();
            });

            builder.Entity<LoteProdutoAcabado>(lote =>
            {
                lote.ToTable("LotesProdutoAcabado", table =>
                {
                    table.HasCheckConstraint(
                        "CK_LotesProdutoAcabado_Codigo_NotBlank",
                        "LEN(LTRIM(RTRIM([Codigo]))) > 0");
                    table.HasCheckConstraint(
                        "CK_LotesProdutoAcabado_Validade_NotBeforeFabricacao",
                        "[DataValidade] >= [DataFabricacao]");
                    table.HasCheckConstraint(
                        "CK_LotesProdutoAcabado_ValidadeOrigem_Valid",
                        "([OrigemValidade] = 1 AND [DataValidade] IS NOT NULL AND [PrazoValidadeAplicadoDias] IS NOT NULL "
                        + "AND [PrazoValidadeAplicadoDias] >= 0) OR "
                        + "([OrigemValidade] = 2 AND [PrazoValidadeAplicadoDias] IS NULL AND [DataValidade] IS NOT NULL) OR "
                        + "([OrigemValidade] = 3 AND [PrazoValidadeAplicadoDias] IS NULL AND [DataValidade] IS NULL)");
                });
                lote.HasKey(entity => entity.Id);
                lote.Property(entity => entity.Id)
                    .ValueGeneratedNever();
                lote.Property(entity => entity.ProdutoVarianteId)
                    .IsRequired();
                lote.Property(entity => entity.Codigo)
                    .HasMaxLength(LoteProdutoAcabado.CodigoMaxLength)
                    .UseCollation("Latin1_General_100_BIN2")
                    .IsRequired();
                lote.Property(entity => entity.DataFabricacao)
                    .HasColumnType("date")
                    .IsRequired();
                lote.Property(entity => entity.DataValidade)
                    .HasColumnType("date");
                lote.Property(entity => entity.OrigemValidade)
                    .HasConversion<short>()
                    .HasColumnType("smallint")
                    .IsRequired();
                lote.Property(entity => entity.PrazoValidadeAplicadoDias);
                lote.HasOne<ProdutoVariante>()
                    .WithMany()
                    .HasForeignKey(entity => entity.ProdutoVarianteId)
                    .OnDelete(DeleteBehavior.Restrict)
                    .HasConstraintName("FK_LotesProdutoAcabado_ProdutoVariantes_ProdutoVarianteId")
                    .IsRequired();
                lote.HasAlternateKey(entity => new { entity.ProdutoVarianteId, entity.Id })
                    .HasName("AK_LotesProdutoAcabado_ProdutoVarianteId_Id");
                lote.HasIndex(entity => entity.Codigo)
                    .IsUnique()
                    .HasDatabaseName("UX_LotesProdutoAcabado_Codigo");
                lote.HasIndex(entity => new { entity.ProdutoVarianteId, entity.DataValidade })
                    .HasDatabaseName("IX_LotesProdutoAcabado_ProdutoVarianteId_DataValidade");
            });

            builder.Entity<SaldoLoteProdutoAcabado>(saldo =>
            {
                saldo.ToTable("SaldosProdutoAcabado", table =>
                {
                    table.HasCheckConstraint(
                        "CK_SaldosProdutoAcabado_Quantidades_NonNegative",
                        "[QuantidadeVendavel] >= 0 AND [QuantidadeDanificada] >= 0 "
                        + "AND [QuantidadeQuarentena] >= 0");
                    table.HasCheckConstraint(
                        "CK_SaldosProdutoAcabado_QuantidadeFisica_IntRange",
                        "CONVERT(bigint, [QuantidadeVendavel]) + "
                        + "CONVERT(bigint, [QuantidadeDanificada]) + "
                        + "CONVERT(bigint, [QuantidadeQuarentena]) <= 2147483647");
                });
                saldo.HasKey(entity => entity.Id);
                saldo.Property(entity => entity.Id)
                    .ValueGeneratedNever();
                saldo.Property(entity => entity.ProdutoVarianteId)
                    .IsRequired();
                saldo.Property(entity => entity.LoteId)
                    .IsRequired();
                saldo.Property(entity => entity.QuantidadeVendavel)
                    .HasField("_quantidadeVendavel")
                    .UsePropertyAccessMode(PropertyAccessMode.Field)
                    .IsRequired();
                saldo.Property(entity => entity.QuantidadeDanificada)
                    .HasField("_quantidadeDanificada")
                    .UsePropertyAccessMode(PropertyAccessMode.Field)
                    .IsRequired();
                saldo.Property(entity => entity.QuantidadeQuarentena)
                    .HasField("_quantidadeQuarentena")
                    .UsePropertyAccessMode(PropertyAccessMode.Field)
                    .IsRequired();
                saldo.Property<byte[]>("Versao")
                    .HasColumnName("Versao")
                    .IsRowVersion()
                    .IsRequired();
                saldo.HasOne<LoteProdutoAcabado>()
                    .WithMany()
                    .HasForeignKey(entity => new { entity.ProdutoVarianteId, entity.LoteId })
                    .HasPrincipalKey(entity => new { entity.ProdutoVarianteId, entity.Id })
                    .OnDelete(DeleteBehavior.Restrict)
                    .HasConstraintName("FK_SaldosProdutoAcabado_LotesProdutoAcabado_ProdutoVarianteId_LoteId")
                    .IsRequired();
                saldo.HasIndex(entity => new { entity.ProdutoVarianteId, entity.LoteId })
                    .IsUnique()
                    .HasDatabaseName("UX_SaldosProdutoAcabado_ProdutoVarianteId_LoteId");
            });

            builder.Entity<ReservaProdutoAcabado>(reserva =>
            {
                reserva.ToTable("ReservasProdutoAcabado", table =>
                {
                    table.HasCheckConstraint(
                        "CK_ReservasProdutoAcabado_Identificadores_NotEmpty",
                        "[Id] <> '00000000-0000-0000-0000-000000000000' "
                        + "AND [PedidoId] <> '00000000-0000-0000-0000-000000000000' "
                        + "AND [ProdutoVarianteId] <> '00000000-0000-0000-0000-000000000000' "
                        + "AND [LoteId] <> '00000000-0000-0000-0000-000000000000'");
                    table.HasCheckConstraint(
                        "CK_ReservasProdutoAcabado_Quantidade_Positive",
                        "[Quantidade] > 0");
                    table.HasCheckConstraint(
                        "CK_ReservasProdutoAcabado_Timestamps_Utc",
                        "DATEPART(TZOFFSET, [CriadaEmUtc]) = 0 AND DATEPART(TZOFFSET, [ExpiraEmUtc]) = 0 "
                        + "AND ([ConfirmadaEmUtc] IS NULL OR DATEPART(TZOFFSET, [ConfirmadaEmUtc]) = 0) "
                        + "AND ([EncerradaEmUtc] IS NULL OR DATEPART(TZOFFSET, [EncerradaEmUtc]) = 0)");
                    table.HasCheckConstraint(
                        "CK_ReservasProdutoAcabado_Prazo_Ate24Horas",
                        "[ExpiraEmUtc] > [CriadaEmUtc] AND ("
                        + "DATEDIFF_BIG(hour, [CriadaEmUtc], [ExpiraEmUtc]) < 24 OR ("
                        + "DATEDIFF_BIG(hour, [CriadaEmUtc], [ExpiraEmUtc]) = 24 "
                        + "AND CAST([ExpiraEmUtc] AS time(7)) <= CAST([CriadaEmUtc] AS time(7))))");
                    table.HasCheckConstraint(
                        "CK_ReservasProdutoAcabado_Cronologia_Valid",
                        "([ConfirmadaEmUtc] IS NULL OR ([ConfirmadaEmUtc] >= [CriadaEmUtc] "
                        + "AND [ConfirmadaEmUtc] < [ExpiraEmUtc])) "
                        + "AND ([EncerradaEmUtc] IS NULL OR [EncerradaEmUtc] >= "
                        + "COALESCE([ConfirmadaEmUtc], [CriadaEmUtc]))");
                    table.HasCheckConstraint(
                        "CK_ReservasProdutoAcabado_Estado_Valid",
                        "([Estado] = 0 AND [ConfirmadaEmUtc] IS NULL AND [EncerradaEmUtc] IS NULL) "
                        + "OR ([Estado] = 1 AND [ConfirmadaEmUtc] IS NOT NULL AND [EncerradaEmUtc] IS NULL) "
                        + "OR ([Estado] = 2 AND [EncerradaEmUtc] IS NOT NULL "
                        + "AND ([ConfirmadaEmUtc] IS NOT NULL OR [EncerradaEmUtc] < [ExpiraEmUtc])) "
                        + "OR ([Estado] = 3 AND [ConfirmadaEmUtc] IS NULL "
                        + "AND [EncerradaEmUtc] IS NOT NULL AND [EncerradaEmUtc] >= [ExpiraEmUtc]) "
                        + "OR ([Estado] = 4 AND [ConfirmadaEmUtc] IS NOT NULL AND [EncerradaEmUtc] IS NOT NULL)");
                    table.HasCheckConstraint(
                        "CK_ReservasProdutoAcabado_Motivo_Valid",
                        "([Estado] = 2 AND [MotivoEncerramento] IS NOT NULL "
                        + "AND LEN(LTRIM(RTRIM([MotivoEncerramento]))) > 0) "
                        + "OR ([Estado] <> 2 AND [MotivoEncerramento] IS NULL)");
                });
                reserva.HasKey(entity => entity.Id);
                reserva.Property(entity => entity.Id).ValueGeneratedNever();
                reserva.Property(entity => entity.PedidoId).IsRequired();
                reserva.Property(entity => entity.ProdutoVarianteId).IsRequired();
                reserva.Property(entity => entity.LoteId).IsRequired();
                reserva.Property(entity => entity.Quantidade).HasColumnType("int").IsRequired();
                reserva.Property(entity => entity.CriadaEmUtc).HasColumnType("datetimeoffset(7)").IsRequired();
                reserva.Property(entity => entity.ExpiraEmUtc).HasColumnType("datetimeoffset(7)").IsRequired();
                reserva.Property(entity => entity.ConfirmadaEmUtc).HasColumnType("datetimeoffset(7)");
                reserva.Property(entity => entity.EncerradaEmUtc).HasColumnType("datetimeoffset(7)");
                reserva.Property(entity => entity.Estado)
                    .HasConversion<short>()
                    .HasColumnType("smallint")
                    .IsRequired();
                reserva.Property(entity => entity.MotivoEncerramento);
                reserva.Property<byte[]>("Versao").IsRowVersion().IsRequired();
                reserva.HasOne<LoteProdutoAcabado>()
                    .WithMany()
                    .HasForeignKey(entity => new { entity.ProdutoVarianteId, entity.LoteId })
                    .HasPrincipalKey(entity => new { entity.ProdutoVarianteId, entity.Id })
                    .OnDelete(DeleteBehavior.Restrict)
                    .IsRequired();
                reserva.HasIndex(entity => entity.PedidoId);
                reserva.HasIndex(entity => new { entity.ProdutoVarianteId, entity.LoteId, entity.Estado });
                reserva.HasIndex(entity => new { entity.Estado, entity.ExpiraEmUtc });
            });

            builder.Entity<MovimentoProdutoAcabado>(movimento =>
            {
                movimento.ToTable("MovimentosProdutoAcabado", table =>
                {
                    table.HasCheckConstraint(
                        "CK_MovimentosProdutoAcabado_Tipo_Valid",
                        "[Tipo] IN (1, 2, 3, 4, 5, 6, 7, 8)");
                    table.HasCheckConstraint(
                        "CK_MovimentosProdutoAcabado_Contagem_NotNull",
                        "[Tipo] <> 6 OR [QuantidadeContada] IS NOT NULL");
                    table.HasCheckConstraint(
                        "CK_MovimentosProdutoAcabado_Quantidade_Positive",
                        "[Quantidade] > 0");
                    table.HasCheckConstraint(
                        "CK_MovimentosProdutoAcabado_Condicoes_Valid",
                        "([CondicaoOrigem] IS NULL OR [CondicaoOrigem] IN (1, 2, 3)) AND "
                        + "([CondicaoDestino] IS NULL OR [CondicaoDestino] IN (1, 2, 3)) AND "
                        + "(([CondicaoOrigem] IS NULL AND [CondicaoDestino] IS NOT NULL) OR "
                        + "([CondicaoOrigem] IS NOT NULL AND [CondicaoDestino] IS NULL) OR "
                        + "([CondicaoOrigem] IS NOT NULL AND [CondicaoDestino] IS NOT NULL "
                        + "AND [CondicaoOrigem] <> [CondicaoDestino]))");
                    table.HasCheckConstraint(
                        "CK_MovimentosProdutoAcabado_Saldos_NonNegativeAndIntRange",
                        "[SaldoAnterior_QuantidadeVendavel] >= 0 AND "
                        + "[SaldoAnterior_QuantidadeDanificada] >= 0 AND "
                        + "[SaldoAnterior_QuantidadeQuarentena] >= 0 AND "
                        + "CONVERT(bigint, [SaldoAnterior_QuantidadeVendavel]) + "
                        + "CONVERT(bigint, [SaldoAnterior_QuantidadeDanificada]) + "
                        + "CONVERT(bigint, [SaldoAnterior_QuantidadeQuarentena]) <= 2147483647 AND "
                        + "[SaldoPosterior_QuantidadeVendavel] >= 0 AND "
                        + "[SaldoPosterior_QuantidadeDanificada] >= 0 AND "
                        + "[SaldoPosterior_QuantidadeQuarentena] >= 0 AND "
                        + "CONVERT(bigint, [SaldoPosterior_QuantidadeVendavel]) + "
                        + "CONVERT(bigint, [SaldoPosterior_QuantidadeDanificada]) + "
                        + "CONVERT(bigint, [SaldoPosterior_QuantidadeQuarentena]) <= 2147483647");
                    table.HasCheckConstraint(
                        "CK_MovimentosProdutoAcabado_Saldos_TransitionValid",
                        "CONVERT(bigint, [SaldoPosterior_QuantidadeVendavel]) = "
                        + "CASE WHEN [CondicaoOrigem] = 1 "
                        + "THEN CONVERT(bigint, [SaldoAnterior_QuantidadeVendavel]) - [Quantidade] "
                        + "WHEN [CondicaoDestino] = 1 "
                        + "THEN CONVERT(bigint, [SaldoAnterior_QuantidadeVendavel]) + [Quantidade] "
                        + "ELSE [SaldoAnterior_QuantidadeVendavel] END AND "
                        + "CONVERT(bigint, [SaldoPosterior_QuantidadeDanificada]) = "
                        + "CASE WHEN [CondicaoOrigem] = 2 "
                        + "THEN CONVERT(bigint, [SaldoAnterior_QuantidadeDanificada]) - [Quantidade] "
                        + "WHEN [CondicaoDestino] = 2 "
                        + "THEN CONVERT(bigint, [SaldoAnterior_QuantidadeDanificada]) + [Quantidade] "
                        + "ELSE [SaldoAnterior_QuantidadeDanificada] END AND "
                        + "CONVERT(bigint, [SaldoPosterior_QuantidadeQuarentena]) = "
                        + "CASE WHEN [CondicaoOrigem] = 3 "
                        + "THEN CONVERT(bigint, [SaldoAnterior_QuantidadeQuarentena]) - [Quantidade] "
                        + "WHEN [CondicaoDestino] = 3 "
                        + "THEN CONVERT(bigint, [SaldoAnterior_QuantidadeQuarentena]) + [Quantidade] "
                        + "ELSE [SaldoAnterior_QuantidadeQuarentena] END");
                    table.HasCheckConstraint(
                        "CK_MovimentosProdutoAcabado_Tipo_DadosValid",
                        "(([Tipo] IN (1, 2) AND [CondicaoOrigem] IS NULL "
                        + "AND [CondicaoDestino] IS NOT NULL AND [QuantidadeContada] IS NULL "
                        + "AND [MovimentoOriginalId] IS NULL) OR "
                        + "([Tipo] IN (3, 4, 5) AND [CondicaoOrigem] IS NOT NULL "
                        + "AND [CondicaoDestino] IS NULL AND [QuantidadeContada] IS NULL "
                        + "AND [MovimentoOriginalId] IS NULL) OR "
                        + "([Tipo] = 6 AND (([CondicaoOrigem] IS NULL AND [CondicaoDestino] IS NOT NULL) "
                        + "OR ([CondicaoOrigem] IS NOT NULL AND [CondicaoDestino] IS NULL)) "
                        + "AND [QuantidadeContada] >= 0 "
                        + "AND [MovimentoOriginalId] IS NULL) OR "
                        + "([Tipo] = 7 AND [CondicaoOrigem] IS NOT NULL "
                        + "AND [CondicaoDestino] IS NOT NULL AND [QuantidadeContada] IS NULL "
                        + "AND [MovimentoOriginalId] IS NULL) OR "
                        + "([Tipo] = 8 AND [MovimentoOriginalId] IS NOT NULL "
                        + "AND [QuantidadeContada] IS NULL))");
                    table.HasCheckConstraint(
                        "CK_MovimentosProdutoAcabado_Contagem_Conciliada",
                        "[Tipo] <> 6 OR "
                        + "([CondicaoOrigem] IS NULL AND [CondicaoDestino] = 1 "
                        + "AND [QuantidadeContada] = [SaldoPosterior_QuantidadeVendavel] "
                        + "AND CONVERT(bigint, [Quantidade]) = ABS("
                        + "CONVERT(bigint, [QuantidadeContada]) - [SaldoAnterior_QuantidadeVendavel])) OR "
                        + "([CondicaoOrigem] IS NULL AND [CondicaoDestino] = 2 "
                        + "AND [QuantidadeContada] = [SaldoPosterior_QuantidadeDanificada] "
                        + "AND CONVERT(bigint, [Quantidade]) = ABS("
                        + "CONVERT(bigint, [QuantidadeContada]) - [SaldoAnterior_QuantidadeDanificada])) OR "
                        + "([CondicaoOrigem] IS NULL AND [CondicaoDestino] = 3 "
                        + "AND [QuantidadeContada] = [SaldoPosterior_QuantidadeQuarentena] "
                        + "AND CONVERT(bigint, [Quantidade]) = ABS("
                        + "CONVERT(bigint, [QuantidadeContada]) - [SaldoAnterior_QuantidadeQuarentena])) OR "
                        + "([CondicaoDestino] IS NULL AND [CondicaoOrigem] = 1 "
                        + "AND [QuantidadeContada] = [SaldoPosterior_QuantidadeVendavel] "
                        + "AND CONVERT(bigint, [Quantidade]) = ABS("
                        + "CONVERT(bigint, [QuantidadeContada]) - [SaldoAnterior_QuantidadeVendavel])) OR "
                        + "([CondicaoDestino] IS NULL AND [CondicaoOrigem] = 2 "
                        + "AND [QuantidadeContada] = [SaldoPosterior_QuantidadeDanificada] "
                        + "AND CONVERT(bigint, [Quantidade]) = ABS("
                        + "CONVERT(bigint, [QuantidadeContada]) - [SaldoAnterior_QuantidadeDanificada])) OR "
                        + "([CondicaoDestino] IS NULL AND [CondicaoOrigem] = 3 "
                        + "AND [QuantidadeContada] = [SaldoPosterior_QuantidadeQuarentena] "
                        + "AND CONVERT(bigint, [Quantidade]) = ABS("
                        + "CONVERT(bigint, [QuantidadeContada]) - [SaldoAnterior_QuantidadeQuarentena]))");
                    table.HasCheckConstraint(
                        "CK_MovimentosProdutoAcabado_Timestamps_Utc",
                        "DATEPART(TZOFFSET, [RegistradoEmUtc]) = 0");
                    table.HasCheckConstraint(
                        "CK_MovimentosProdutoAcabado_Responsavel_NotEmpty",
                        "[ResponsavelId] <> '00000000-0000-0000-0000-000000000000'");
                    table.HasCheckConstraint(
                        "CK_MovimentosProdutoAcabado_Motivo_NotBlank",
                        "LEN(LTRIM(RTRIM([Motivo]))) > 0");
                    table.HasCheckConstraint(
                        "CK_MovimentosProdutoAcabado_Chave_NotBlank",
                        "LEN(LTRIM(RTRIM([ChaveIdempotencia]))) > 0");
                    table.HasCheckConstraint(
                        "CK_MovimentosProdutoAcabado_Estorno_NotSelf",
                        "[MovimentoOriginalId] IS NULL OR [MovimentoOriginalId] <> [Id]");
                    table.HasCheckConstraint(
                        "CK_MovimentosProdutoAcabado_Reserva_SaidaVendavel",
                        "[ReservaProdutoAcabadoId] IS NULL OR ([Tipo] = 3 "
                        + "AND [CondicaoOrigem] IS NOT NULL AND [CondicaoOrigem] = 1 AND [CondicaoDestino] IS NULL)");
                });
                movimento.HasKey(entity => entity.Id);
                movimento.Property(entity => entity.Id)
                    .ValueGeneratedNever();
                movimento.Property(entity => entity.ProdutoVarianteId)
                    .IsRequired();
                movimento.Property(entity => entity.LoteId)
                    .IsRequired();
                movimento.Property(entity => entity.Tipo)
                    .HasConversion<short>()
                    .HasColumnType("smallint")
                    .IsRequired();
                movimento.Property(entity => entity.Quantidade)
                    .IsRequired();
                movimento.Property(entity => entity.CondicaoOrigem)
                    .HasConversion<short?>()
                    .HasColumnType("smallint");
                movimento.Property(entity => entity.CondicaoDestino)
                    .HasConversion<short?>()
                    .HasColumnType("smallint");
                movimento.Property(entity => entity.QuantidadeContada);
                movimento.Property(entity => entity.MovimentoOriginalId);
                movimento.Property(entity => entity.ReservaProdutoAcabadoId);
                movimento.Property(entity => entity.ResponsavelId)
                    .IsRequired();
                movimento.Property(entity => entity.Motivo)
                    .HasMaxLength(MovimentoProdutoAcabado.MotivoMaxLength)
                    .IsRequired();
                movimento.Property(entity => entity.Observacao)
                    .HasMaxLength(MovimentoProdutoAcabado.ObservacaoMaxLength);
                movimento.Property(entity => entity.RegistradoEmUtc)
                    .HasColumnType("datetimeoffset(7)")
                    .IsRequired();
                movimento.Property(entity => entity.ChaveIdempotencia)
                    .HasMaxLength(MovimentoProdutoAcabado.ChaveIdempotenciaMaxLength)
                    .UseCollation("Latin1_General_100_BIN2")
                    .IsRequired();
                movimento.Property(entity => entity.FingerprintRequisicao)
                    .HasField("_fingerprintRequisicao")
                    .HasColumnName("FingerprintRequisicao")
                    .HasColumnType("binary(32)")
                    .UsePropertyAccessMode(PropertyAccessMode.Field)
                    .IsRequired();
                movimento.OwnsOne(entity => entity.SaldoAnterior, saldoAnterior =>
                {
                    saldoAnterior.Property(value => value.QuantidadeVendavel)
                        .HasColumnName("SaldoAnterior_QuantidadeVendavel")
                        .IsRequired();
                    saldoAnterior.Property(value => value.QuantidadeDanificada)
                        .HasColumnName("SaldoAnterior_QuantidadeDanificada")
                        .IsRequired();
                    saldoAnterior.Property(value => value.QuantidadeQuarentena)
                        .HasColumnName("SaldoAnterior_QuantidadeQuarentena")
                        .IsRequired();
                });
                movimento.Navigation(entity => entity.SaldoAnterior)
                    .IsRequired();
                movimento.OwnsOne(entity => entity.SaldoPosterior, saldoPosterior =>
                {
                    saldoPosterior.Property(value => value.QuantidadeVendavel)
                        .HasColumnName("SaldoPosterior_QuantidadeVendavel")
                        .IsRequired();
                    saldoPosterior.Property(value => value.QuantidadeDanificada)
                        .HasColumnName("SaldoPosterior_QuantidadeDanificada")
                        .IsRequired();
                    saldoPosterior.Property(value => value.QuantidadeQuarentena)
                        .HasColumnName("SaldoPosterior_QuantidadeQuarentena")
                        .IsRequired();
                });
                movimento.Navigation(entity => entity.SaldoPosterior)
                    .IsRequired();
                movimento.HasOne<LoteProdutoAcabado>()
                    .WithMany()
                    .HasForeignKey(entity => new { entity.ProdutoVarianteId, entity.LoteId })
                    .HasPrincipalKey(entity => new { entity.ProdutoVarianteId, entity.Id })
                    .OnDelete(DeleteBehavior.Restrict)
                    .HasConstraintName("FK_MovimentosProdutoAcabado_LotesProdutoAcabado_ProdutoVarianteId_LoteId")
                    .IsRequired();
                movimento.HasOne<MovimentoProdutoAcabado>()
                    .WithMany()
                    .HasForeignKey(entity => entity.MovimentoOriginalId)
                    .OnDelete(DeleteBehavior.Restrict)
                    .HasConstraintName("FK_MovimentosProdutoAcabado_MovimentosProdutoAcabado_MovimentoOriginalId");
                movimento.HasOne<ReservaProdutoAcabado>()
                    .WithMany()
                    .HasForeignKey(entity => new
                    {
                        entity.ReservaProdutoAcabadoId, entity.ProdutoVarianteId, entity.LoteId, entity.Quantidade
                    })
                    .HasPrincipalKey(entity => new { entity.Id, entity.ProdutoVarianteId, entity.LoteId, entity.Quantidade })
                    .OnDelete(DeleteBehavior.Restrict)
                    .HasConstraintName("FK_MovimentosProdutoAcabado_Reserva_Retirada");
                movimento.HasIndex(entity => new
                    {
                        entity.ReservaProdutoAcabadoId, entity.ProdutoVarianteId, entity.LoteId, entity.Quantidade
                    })
                    .IsUnique()
                    .HasDatabaseName("UX_MovimentosProdutoAcabado_Reserva_Retirada")
                    .HasFilter("[ReservaProdutoAcabadoId] IS NOT NULL");
                movimento.HasIndex(entity => new { entity.ResponsavelId, entity.ChaveIdempotencia })
                    .IsUnique()
                    .HasDatabaseName("UX_MovimentosProdutoAcabado_ResponsavelId_ChaveIdempotencia");
                movimento.HasIndex(entity => new
                    {
                        entity.ProdutoVarianteId,
                        entity.LoteId,
                        entity.RegistradoEmUtc
                    })
                    .HasDatabaseName("IX_MovimentosProdutoAcabado_ProdutoVarianteId_LoteId_RegistradoEmUtc");
                movimento.HasIndex(entity => new { entity.ProdutoVarianteId, entity.RegistradoEmUtc })
                    .HasDatabaseName("IX_MovimentosProdutoAcabado_ProdutoVarianteId_RegistradoEmUtc");
                movimento.HasIndex(entity => entity.MovimentoOriginalId)
                    .IsUnique()
                    .HasDatabaseName("UX_MovimentosProdutoAcabado_MovimentoOriginalId")
                    .HasFilter("[MovimentoOriginalId] IS NOT NULL");
            });

            builder.Entity<ContagemSemDiferencaProdutoAcabado>(operacao =>
            {
                operacao.ToTable("ContagensSemDiferencaProdutoAcabado", table =>
                {
                    table.HasCheckConstraint(
                        "CK_ContagensSemDiferencaProdutoAcabado_Responsavel_NotEmpty",
                        "[ResponsavelId] <> '00000000-0000-0000-0000-000000000000'");
                    table.HasCheckConstraint(
                        "CK_ContagensSemDiferencaProdutoAcabado_Chave_NotBlank",
                        "LEN(LTRIM(RTRIM([ChaveIdempotencia]))) > 0");
                    table.HasCheckConstraint(
                        "CK_ContagensSemDiferencaProdutoAcabado_Timestamp_Utc",
                        "DATEPART(TZOFFSET, [RegistradoEmUtc]) = 0");
                });
                operacao.HasKey(entity => new { entity.ResponsavelId, entity.ChaveIdempotencia });
                operacao.Property(entity => entity.ResponsavelId)
                    .ValueGeneratedNever()
                    .IsRequired();
                operacao.Property(entity => entity.ChaveIdempotencia)
                    .HasMaxLength(MovimentoProdutoAcabado.ChaveIdempotenciaMaxLength)
                    .UseCollation("Latin1_General_100_BIN2")
                    .IsRequired();
                operacao.Property(entity => entity.FingerprintRequisicao)
                    .HasField("_fingerprintRequisicao")
                    .HasColumnName("FingerprintRequisicao")
                    .HasColumnType("binary(32)")
                    .UsePropertyAccessMode(PropertyAccessMode.Field)
                    .IsRequired();
                operacao.Property(entity => entity.RegistradoEmUtc)
                    .HasColumnType("datetimeoffset(7)")
                    .IsRequired();
            });

            forma.HasIndex(entity => new { entity.InsumoId, entity.NomeNormalizado })
                .IsUnique()
                .HasDatabaseName("UX_FormasCompraInsumo_InsumoId_NomeNormalizado");
        });

        builder.Entity<LoteInsumo>(lote =>
        {
            lote.ToTable("LotesInsumo", table =>
            {
                table.HasCheckConstraint(
                    "CK_LotesInsumo_Identificacao_NotBlank",
                    "LEN(LTRIM(RTRIM([Identificacao]))) > 0");
                table.HasCheckConstraint(
                    "CK_LotesInsumo_Fornecedor_NotBlank",
                    "[Fornecedor] IS NULL OR LEN(LTRIM(RTRIM([Fornecedor]))) > 0");
            });
            lote.HasKey(entity => entity.Id);
            lote.Property(entity => entity.Id)
                .ValueGeneratedNever();
            lote.Property(entity => entity.InsumoId)
                .IsRequired();
            lote.Property(entity => entity.Identificacao)
                .HasMaxLength(LoteInsumo.IdentificacaoMaxLength)
                .IsRequired();
            lote.Property(entity => entity.DataEntrada)
                .HasColumnType("date")
                .IsRequired();
            lote.Property(entity => entity.DataValidade)
                .HasColumnType("date");
            lote.Property(entity => entity.Fornecedor)
                .HasMaxLength(LoteInsumo.FornecedorMaxLength);
            lote.Property(entity => entity.Bloqueado)
                .IsRequired();
            lote.Property<byte[]>("Versao")
                .HasColumnName("Versao")
                .IsRowVersion()
                .IsRequired();

            lote.HasOne<InsumoEstoque>()
                .WithMany()
                .HasForeignKey(entity => entity.InsumoId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_LotesInsumo_InsumosEstoque_InsumoId")
                .IsRequired();
            lote.HasAlternateKey(entity => new { entity.InsumoId, entity.Id })
                .HasName("AK_LotesInsumo_InsumoId_Id");
            lote.HasIndex(entity => new { entity.InsumoId, entity.Identificacao })
                .HasDatabaseName("IX_LotesInsumo_InsumoId_Identificacao");
            lote.HasIndex(entity => new { entity.InsumoId, entity.DataValidade })
                .HasDatabaseName("IX_LotesInsumo_InsumoId_DataValidade");
        });

        builder.Entity<SaldoInsumo>(saldo =>
        {
            saldo.ToTable("SaldosInsumo", table =>
            {
                table.HasCheckConstraint(
                    "CK_SaldosInsumo_Quantidade_Valid",
                    "[Quantidade_ValorBase] >= 0 AND "
                    + "([Quantidade_UnidadeBase] = 1 AND [Quantidade_Dimensao] = 1 OR "
                    + "[Quantidade_UnidadeBase] = 3 AND [Quantidade_Dimensao] = 2 OR "
                    + "[Quantidade_UnidadeBase] = 5 AND [Quantidade_Dimensao] = 3) AND "
                    + "([Quantidade_UnidadeBase] <> 5 OR "
                    + "[Quantidade_ValorBase] = FLOOR([Quantidade_ValorBase]))");
            });
            saldo.HasKey(entity => entity.Id);
            saldo.Property(entity => entity.Id)
                .ValueGeneratedNever();
            saldo.Property(entity => entity.InsumoId)
                .IsRequired();
            saldo.Property(entity => entity.LoteId);
            saldo.Property<byte[]>("Versao")
                .HasColumnName("Versao")
                .IsRowVersion()
                .IsRequired();

            saldo.OwnsOne(entity => entity.QuantidadeFisica, quantidade =>
            {
                quantidade.Property(value => value.ValorBase)
                    .HasColumnName("Quantidade_ValorBase")
                    .HasPrecision(QuantidadeEstoque.Precision, QuantidadeEstoque.Scale)
                    .IsRequired();
                quantidade.Property(value => value.UnidadeBase)
                    .HasColumnName("Quantidade_UnidadeBase")
                    .HasConversion<short>()
                    .HasColumnType("smallint")
                    .IsRequired();
                quantidade.Property(value => value.Dimensao)
                    .HasColumnName("Quantidade_Dimensao")
                    .HasConversion<short>()
                    .HasColumnType("smallint")
                    .IsRequired();
            });
            saldo.Navigation(entity => entity.QuantidadeFisica)
                .IsRequired();

            saldo.HasOne<InsumoEstoque>()
                .WithMany()
                .HasForeignKey(entity => entity.InsumoId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_SaldosInsumo_InsumosEstoque_InsumoId")
                .IsRequired();
            saldo.HasOne<LoteInsumo>()
                .WithMany()
                .HasForeignKey(entity => new { entity.InsumoId, entity.LoteId })
                .HasPrincipalKey(entity => new { entity.InsumoId, entity.Id })
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_SaldosInsumo_LotesInsumo_InsumoId_LoteId");
            saldo.HasIndex(entity => entity.InsumoId)
                .IsUnique()
                .HasDatabaseName("UX_SaldosInsumo_InsumoId_Agregado")
                .HasFilter("[LoteId] IS NULL");
            saldo.HasIndex(entity => new { entity.InsumoId, entity.LoteId })
                .IsUnique()
                .HasDatabaseName("UX_SaldosInsumo_InsumoId_LoteId")
                .HasFilter("[LoteId] IS NOT NULL");
        });

        builder.Entity<MovimentoInsumo>(movimento =>
        {
            movimento.ToTable("MovimentosInsumo", table =>
            {
                table.HasCheckConstraint(
                    "CK_MovimentosInsumo_Tipo_Valid",
                    "[Tipo] IN (1, 2, 3, 4, 5, 6, 7)");
                table.HasCheckConstraint(
                    "CK_MovimentosInsumo_Direcao_Saldo_Valid",
                    "[Direcao] IN (-1, 1) AND [QuantidadeBase_ValorBase] > 0 AND "
                    + "[SaldoAnterior_ValorBase] >= 0 AND "
                    + "(([Direcao] = 1 AND "
                    + "[SaldoPosterior_ValorBase] = [SaldoAnterior_ValorBase] + [QuantidadeBase_ValorBase]) OR "
                    + "([Direcao] = -1 AND [SaldoPosterior_ValorBase] = "
                    + "[SaldoAnterior_ValorBase] - [QuantidadeBase_ValorBase] "
                    + "AND [SaldoPosterior_ValorBase] >= 0)) AND "
                    + "[SaldoAnterior_UnidadeBase] = [QuantidadeBase_UnidadeBase] AND "
                    + "[SaldoPosterior_UnidadeBase] = [QuantidadeBase_UnidadeBase] AND "
                    + "[SaldoAnterior_Dimensao] = [QuantidadeBase_Dimensao] AND "
                    + "[SaldoPosterior_Dimensao] = [QuantidadeBase_Dimensao]");
                table.HasCheckConstraint(
                    "CK_MovimentosInsumo_Origem_Valid",
                    "(([Tipo] IN (1, 2) AND (([FormaCompraId] IS NULL AND [FormaCompraNome] IS NULL "
                    + "AND [FatorCompra_ValorBase] IS NULL AND [FatorCompra_UnidadeBase] IS NULL "
                    + "AND [FatorCompra_Dimensao] IS NULL AND [UnidadeOriginal] IS NOT NULL "
                    + "AND [QuantidadeOriginal] > 0) OR "
                    + "([FormaCompraId] IS NOT NULL AND [FormaCompraNome] IS NOT NULL "
                    + "AND LEN(LTRIM(RTRIM([FormaCompraNome]))) > 0 "
                    + "AND [FatorCompra_ValorBase] > 0 AND [FatorCompra_UnidadeBase] IS NOT NULL "
                    + "AND [FatorCompra_Dimensao] IS NOT NULL AND [UnidadeOriginal] IS NULL "
                    + "AND [QuantidadeOriginal] = FLOOR([QuantidadeOriginal]) "
                    + "AND [FatorCompra_Dimensao] = [QuantidadeBase_Dimensao] "
                    + "AND (([FatorCompra_UnidadeBase] = 1 AND [FatorCompra_Dimensao] = 1) OR "
                    + "([FatorCompra_UnidadeBase] = 3 AND [FatorCompra_Dimensao] = 2) OR "
                    + "([FatorCompra_UnidadeBase] = 5 AND [FatorCompra_Dimensao] = 3))))) OR "
                    + "([Tipo] IN (3, 4, 5, 6, 7) AND [FormaCompraId] IS NULL "
                    + "AND [FormaCompraNome] IS NULL AND [FatorCompra_ValorBase] IS NULL "
                    + "AND [FatorCompra_UnidadeBase] IS NULL AND [FatorCompra_Dimensao] IS NULL "
                    + "AND [UnidadeOriginal] IS NOT NULL "
                    + "AND (([Tipo] = 6 AND [QuantidadeOriginal] >= 0) OR "
                    + "([Tipo] <> 6 AND [QuantidadeOriginal] > 0)))) "
                    + "AND (([UnidadeOriginal] IS NULL) OR ([UnidadeOriginal] = 1 "
                    + "AND [QuantidadeBase_UnidadeBase] = 1) OR "
                    + "([UnidadeOriginal] = 2 AND [QuantidadeBase_UnidadeBase] = 1 "
                    + "OR [UnidadeOriginal] IN (3, 4) AND [QuantidadeBase_UnidadeBase] = 3 "
                    + "OR [UnidadeOriginal] = 5 AND [QuantidadeBase_UnidadeBase] = 5))");
                table.HasCheckConstraint(
                    "CK_MovimentosInsumo_Custo_Valid",
                    "([CustoTotal] IS NULL AND [CustoPorUnidadeBase] IS NULL) OR "
                    + "([Tipo] IN (1, 2) AND [CustoTotal] IS NOT NULL "
                    + "AND [CustoPorUnidadeBase] IS NOT NULL "
                    + "AND [CustoTotal] >= 0 AND [CustoPorUnidadeBase] >= 0)");
                table.HasCheckConstraint(
                    "CK_MovimentosInsumo_Tipo_Direcao_Valid",
                    "([Tipo] IN (1, 2) AND [Direcao] = 1) OR "
                    + "([Tipo] IN (3, 4, 5) AND [Direcao] = -1) OR "
                    + "([Tipo] IN (6, 7) AND [Direcao] IN (-1, 1))");
                table.HasCheckConstraint(
                    "CK_MovimentosInsumo_Contagem_Valid",
                    "(([Tipo] = 6 AND [QuantidadeContada_ValorBase] IS NOT NULL "
                    + "AND [QuantidadeContada_UnidadeBase] IS NOT NULL "
                    + "AND [QuantidadeContada_Dimensao] IS NOT NULL "
                    + "AND [QuantidadeContada_ValorBase] >= 0 "
                    + "AND [QuantidadeContada_UnidadeBase] = [QuantidadeBase_UnidadeBase] "
                    + "AND [QuantidadeContada_Dimensao] = [QuantidadeBase_Dimensao] "
                    + "AND (([UnidadeOriginal] = 1 AND [QuantidadeOriginal] = [QuantidadeContada_ValorBase]) "
                    + "OR ([UnidadeOriginal] = 2 AND [QuantidadeOriginal] * 1000 = [QuantidadeContada_ValorBase]) "
                    + "OR ([UnidadeOriginal] = 3 AND [QuantidadeOriginal] = [QuantidadeContada_ValorBase]) "
                    + "OR ([UnidadeOriginal] = 4 AND [QuantidadeOriginal] * 1000 = [QuantidadeContada_ValorBase]) "
                    + "OR ([UnidadeOriginal] = 5 AND [QuantidadeOriginal] = [QuantidadeContada_ValorBase]))) "
                    + "OR ([Tipo] <> 6 AND [QuantidadeContada_ValorBase] IS NULL "
                    + "AND [QuantidadeContada_UnidadeBase] IS NULL "
                    + "AND [QuantidadeContada_Dimensao] IS NULL))");
                table.HasCheckConstraint(
                    "CK_MovimentosInsumo_Contagem_Conciliada",
                    "[Tipo] <> 6 OR ([QuantidadeContada_ValorBase] = [SaldoPosterior_ValorBase] "
                    + "AND [QuantidadeBase_ValorBase] = "
                    + "ABS([QuantidadeContada_ValorBase] - [SaldoAnterior_ValorBase]))");
                table.HasCheckConstraint(
                    "CK_MovimentosInsumo_Estorno_Valid",
                    "([Tipo] = 7 AND [MovimentoOriginalId] IS NOT NULL AND "
                    + "[MovimentoOriginalId] <> [Id] AND [CustoTotal] IS NULL "
                    + "AND [CustoPorUnidadeBase] IS NULL) OR "
                    + "([Tipo] <> 7 AND [MovimentoOriginalId] IS NULL)");
                table.HasCheckConstraint(
                    "CK_MovimentosInsumo_Timestamps_Utc",
                    "DATEPART(TZOFFSET, [RegistradoEmUtc]) = 0");
                table.HasCheckConstraint(
                    "CK_MovimentosInsumo_Responsavel_NotEmpty",
                    "[ResponsavelId] <> '00000000-0000-0000-0000-000000000000'");
            });
            movimento.HasKey(entity => entity.Id);
            movimento.Property(entity => entity.Id)
                .ValueGeneratedNever();
            movimento.Property(entity => entity.InsumoId)
                .IsRequired();
            movimento.Property(entity => entity.LoteId);
            movimento.Property(entity => entity.ResponsavelId)
                .IsRequired();
            movimento.Property(entity => entity.Tipo)
                .HasConversion<short>()
                .HasColumnType("smallint")
                .IsRequired();
            movimento.Property(entity => entity.Direcao)
                .HasConversion<short>()
                .HasColumnType("smallint")
                .HasDefaultValue(DirecaoMovimentoInsumo.Entrada)
                .IsRequired();
            movimento.Property(entity => entity.QuantidadeOriginal)
                .HasPrecision(QuantidadeEstoque.Precision, QuantidadeEstoque.Scale)
                .IsRequired();
            movimento.Property(entity => entity.UnidadeOriginal)
                .HasConversion<short?>()
                .HasColumnType("smallint");
            movimento.Property(entity => entity.FormaCompraId);
            movimento.Property(entity => entity.MovimentoOriginalId);
            movimento.Property(entity => entity.FormaCompraNome)
                .HasMaxLength(FormaCompraInsumo.NomeMaxLength);
            movimento.Property(entity => entity.CustoTotal)
                .HasPrecision(CustoAquisicaoInsumo.CustoTotalPrecision, CustoAquisicaoInsumo.CustoTotalScale);
            movimento.Property(entity => entity.CustoPorUnidadeBase)
                .HasPrecision(
                    CustoAquisicaoInsumo.CustoUnitarioPrecision,
                    CustoAquisicaoInsumo.CustoUnitarioScale);
            movimento.Property(entity => entity.Motivo)
                .HasMaxLength(MovimentoInsumo.MotivoMaxLength)
                .IsRequired();
            movimento.Property(entity => entity.Observacao)
                .HasMaxLength(MovimentoInsumo.ObservacaoMaxLength);
            movimento.Property(entity => entity.RegistradoEmUtc)
                .HasColumnType("datetimeoffset(7)")
                .IsRequired();
            movimento.Property(entity => entity.ChaveIdempotencia)
                .HasMaxLength(MovimentoInsumo.ChaveIdempotenciaMaxLength)
                .UseCollation("Latin1_General_100_BIN2")
                .IsRequired();
            movimento.Property(entity => entity.FingerprintRequisicao)
                .HasColumnType("binary(32)")
                .IsRequired();

            ConfigurarQuantidadeMovimento(
                movimento,
                entity => entity.QuantidadeBase,
                "QuantidadeBase",
                required: true);
            ConfigurarQuantidadeMovimento(
                movimento,
                entity => entity.SaldoAnterior,
                "SaldoAnterior",
                required: true);
            ConfigurarQuantidadeMovimento(
                movimento,
                entity => entity.SaldoPosterior,
                "SaldoPosterior",
                required: true);
            ConfigurarQuantidadeMovimento(
                movimento,
                entity => entity.FatorCompra,
                "FatorCompra",
                required: false);
            ConfigurarQuantidadeMovimento(
                movimento,
                entity => entity.QuantidadeContada,
                "QuantidadeContada",
                required: false);

            movimento.HasOne<InsumoEstoque>()
                .WithMany()
                .HasForeignKey(entity => entity.InsumoId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_MovimentosInsumo_InsumosEstoque_InsumoId")
                .IsRequired();
            movimento.HasOne<LoteInsumo>()
                .WithMany()
                .HasForeignKey(entity => new { entity.InsumoId, entity.LoteId })
                .HasPrincipalKey(entity => new { entity.InsumoId, entity.Id })
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_MovimentosInsumo_LotesInsumo_InsumoId_LoteId");
            movimento.HasOne<MovimentoInsumo>()
                .WithMany()
                .HasForeignKey(entity => entity.MovimentoOriginalId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_MovimentosInsumo_MovimentosInsumo_MovimentoOriginalId");
            movimento.HasIndex(entity => new { entity.ResponsavelId, entity.ChaveIdempotencia })
                .IsUnique()
                .HasDatabaseName("UX_MovimentosInsumo_ResponsavelId_ChaveIdempotencia");
            movimento.HasIndex(entity => new { entity.InsumoId, entity.LoteId, entity.RegistradoEmUtc })
                .HasDatabaseName("IX_MovimentosInsumo_InsumoId_LoteId_RegistradoEmUtc");
            movimento.HasIndex(entity => entity.MovimentoOriginalId)
                .IsUnique()
                .HasDatabaseName("UX_MovimentosInsumo_MovimentoOriginalId")
                .HasFilter("[MovimentoOriginalId] IS NOT NULL");
        });

        builder.Entity<AjusteSemDiferencaInsumo>(operacao =>
        {
            operacao.ToTable("AjustesSemDiferencaInsumo", table =>
            {
                table.HasCheckConstraint(
                    "CK_AjustesSemDiferencaInsumo_Responsavel_NotEmpty",
                    "[ResponsavelId] <> '00000000-0000-0000-0000-000000000000'");
                table.HasCheckConstraint(
                    "CK_AjustesSemDiferencaInsumo_Chave_NotBlank",
                    "LEN(LTRIM(RTRIM([ChaveIdempotencia]))) > 0");
                table.HasCheckConstraint(
                    "CK_AjustesSemDiferencaInsumo_Timestamp_Utc",
                    "DATEPART(TZOFFSET, [RegistradoEmUtc]) = 0");
            });
            operacao.HasKey(entity => new { entity.ResponsavelId, entity.ChaveIdempotencia });
            operacao.Property(entity => entity.ResponsavelId)
                .ValueGeneratedNever()
                .IsRequired();
            operacao.Property(entity => entity.ChaveIdempotencia)
                .HasMaxLength(MovimentoInsumo.ChaveIdempotenciaMaxLength)
                .UseCollation("Latin1_General_100_BIN2")
                .IsRequired();
            operacao.Property(entity => entity.FingerprintRequisicao)
                .HasColumnType("binary(32)")
                .IsRequired();
            operacao.Property(entity => entity.RegistradoEmUtc)
                .HasColumnType("datetimeoffset(7)")
                .IsRequired();
        });

        ExclusaoCategoriaMapping.Configurar(builder);
        builder.Entity<OperacaoExclusaoProduto>(e =>
        {
            e.ToTable("OperacoesExclusaoProduto");
            e.HasKey(x => new { x.Autor, x.Chave });
            e.Property(x => x.Nome).HasMaxLength(Produto.NomeMaxLength);
            e.Property(x => x.Versao).HasMaxLength(64).IsUnicode(false);
            e.Property(x => x.Resultado).HasMaxLength(24);
        });
        builder.Entity<Xaquiamuni.Infraestrutura.Imagens.LimpezaImagemProduto>(e =>
        {
            e.ToTable("LimpezasImagensProdutos");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Chave).HasMaxLength(ProdutoImagem.ChaveArmazenamentoMaxLength);
            e.HasIndex(x => new { x.ProximaTentativaUtc, x.Id });
        });
        builder.Entity<OperacaoExclusaoVariante>(e =>
        {
            e.ToTable("OperacoesExclusaoVariante");
            e.HasKey(x => new { x.Autor, x.Chave });
            e.Property(x => x.Versao).HasMaxLength(64).IsUnicode(false);
            e.Property(x => x.Identificacao).HasMaxLength(ProdutoVariante.IdentificacaoMaxLength);
            e.Property(x => x.Resultado).HasMaxLength(24);
        });
        builder.Entity<Categoria>(categoria =>
        {
            categoria.Property(x => x.Versao).HasDefaultValueSql("NEWID()").IsConcurrencyToken();
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
            produto.HasMany(entity => entity.Caracteristicas)
                .WithOne(characteristic => characteristic.Produto)
                .HasForeignKey(characteristic => characteristic.ProdutoId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired();
            produto.Navigation(entity => entity.Caracteristicas)
                .HasField("_caracteristicas")
                .UsePropertyAccessMode(PropertyAccessMode.Field);
            produto.HasMany(entity => entity.Imagens)
                .WithOne(image => image.Produto)
                .HasForeignKey(image => image.ProdutoId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_ProdutoImagens_Produtos_ProdutoId")
                .IsRequired();
            produto.Navigation(entity => entity.Imagens)
                .HasField("_imagens")
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
                    "CK_ProdutoVariantes_PesoLiquidoGramas_Positive",
                    "[PesoLiquidoGramas] IS NULL OR [PesoLiquidoGramas] > 0");
                table.HasCheckConstraint(
                    "CK_ProdutoVariantes_VolumeLiquidoMililitros_Positive",
                    "[VolumeLiquidoMililitros] IS NULL OR [VolumeLiquidoMililitros] > 0");
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
            variante.Property<decimal?>("_pesoLiquidoGramas")
                .HasColumnName("PesoLiquidoGramas")
                .HasPrecision(MedidaFisicaCatalogo.Precision, MedidaFisicaCatalogo.Scale);
            variante.Property<decimal?>("_volumeLiquidoMililitros")
                .HasColumnName("VolumeLiquidoMililitros")
                .HasPrecision(MedidaFisicaCatalogo.Precision, MedidaFisicaCatalogo.Scale);
            variante.Property<byte[]?>("_combinacaoHash")
                .HasColumnName("CombinacaoHash")
                .HasColumnType("binary(32)");
            variante.HasAlternateKey(entity => new { entity.ProdutoId, entity.Id })
                .HasName("AK_ProdutoVariantes_ProdutoId_Id");
            variante.HasMany(entity => entity.CaracteristicaValores)
                .WithOne(association => association.Variante)
                .HasForeignKey(association => new
                {
                    association.ProdutoId,
                    association.ProdutoVarianteId
                })
                .HasPrincipalKey(entity => new { entity.ProdutoId, entity.Id })
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired();
            variante.Navigation(entity => entity.CaracteristicaValores)
                .HasField("_caracteristicaValores")
                .UsePropertyAccessMode(PropertyAccessMode.Field);

            variante.HasIndex(entity => new { entity.ProdutoId, entity.IdentificacaoNormalizada })
                .IsUnique()
                .HasDatabaseName("UX_ProdutoVariantes_ProdutoId_IdentificacaoNormalizada");
            variante.HasIndex(entity => entity.ProdutoId)
                .IsUnique()
                .HasFilter("[Padrao] = 1")
                .HasDatabaseName("UX_ProdutoVariantes_DefaultPerProduto");
            variante.HasIndex("ProdutoId", "_combinacaoHash")
                .IsUnique()
                .HasFilter("[CombinacaoHash] IS NOT NULL")
                .HasDatabaseName("UX_ProdutoVariantes_ProdutoId_CombinacaoHash");
        });

        builder.Entity<ProdutoImagem>(imagem =>
        {
            imagem.ToTable("ProdutoImagens", table =>
            {
                table.HasCheckConstraint(
                    "CK_ProdutoImagens_TamanhoBytes_Positive",
                    "[TamanhoBytes] > 0");
                table.HasCheckConstraint(
                    "CK_ProdutoImagens_Dimensoes_Positive",
                    "[LarguraPixels] > 0 AND [AlturaPixels] > 0");
                table.HasCheckConstraint(
                    "CK_ProdutoImagens_OrdemExibicao_Valid",
                    "[OrdemExibicao] BETWEEN 1 AND 5");
                table.HasCheckConstraint(
                    "CK_ProdutoImagens_SlotGaleria_Valid",
                    "[SlotGaleria] BETWEEN 1 AND 5");
                table.HasCheckConstraint(
                    "CK_ProdutoImagens_CriadoEmUtc",
                    "DATEPART(TZOFFSET, [CriadoEmUtc]) = 0");
                table.HasCheckConstraint(
                    "CK_ProdutoImagens_Metadata_NotBlank",
                    "LEN(LTRIM(RTRIM([ChaveArmazenamento]))) > 0 AND "
                    + "LEN(LTRIM(RTRIM([NomeOriginal]))) > 0 AND "
                    + "LEN(LTRIM(RTRIM([TipoConteudo]))) > 0");
            });
            imagem.HasKey(entity => entity.Id);
            imagem.Property(entity => entity.Id)
                .ValueGeneratedNever();
            imagem.Property(entity => entity.ProdutoId)
                .IsRequired();
            imagem.Property(entity => entity.ChaveArmazenamento)
                .HasMaxLength(ProdutoImagem.ChaveArmazenamentoMaxLength)
                .UseCollation("Latin1_General_100_BIN2")
                .IsRequired();
            imagem.Property(entity => entity.NomeOriginal)
                .HasMaxLength(ProdutoImagem.NomeOriginalMaxLength)
                .IsRequired();
            imagem.Property(entity => entity.TipoConteudo)
                .HasMaxLength(ProdutoImagem.TipoConteudoMaxLength)
                .IsRequired();
            imagem.Property(entity => entity.TamanhoBytes)
                .IsRequired();
            imagem.Property(entity => entity.LarguraPixels)
                .IsRequired();
            imagem.Property(entity => entity.AlturaPixels)
                .IsRequired();
            imagem.Property(entity => entity.OrdemExibicao)
                .IsRequired();
            imagem.Property(entity => entity.SlotGaleria)
                .IsRequired();
            imagem.Property(entity => entity.Principal)
                .IsRequired();
            imagem.Property(entity => entity.CriadoEmUtc)
                .HasColumnType("datetimeoffset(7)")
                .IsRequired();
            imagem.HasAlternateKey(entity => new { entity.ProdutoId, entity.Id })
                .HasName("AK_ProdutoImagens_ProdutoId_Id");
            imagem.HasMany(entity => entity.Variantes)
                .WithOne(association => association.Imagem)
                .HasForeignKey(association => new
                {
                    association.ProdutoId,
                    association.ProdutoImagemId
                })
                .HasPrincipalKey(entity => new { entity.ProdutoId, entity.Id })
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName(
                    "FK_ProdutoImagemVariantes_ProdutoImagens_ProdutoId_ProdutoImagemId")
                .IsRequired();
            imagem.Navigation(entity => entity.Variantes)
                .HasField("_variantes")
                .UsePropertyAccessMode(PropertyAccessMode.Field);

            imagem.HasIndex(entity => entity.ChaveArmazenamento)
                .IsUnique()
                .HasDatabaseName("UX_ProdutoImagens_ChaveArmazenamento");
            imagem.HasIndex(entity => new { entity.ProdutoId, entity.SlotGaleria })
                .IsUnique()
                .HasDatabaseName("UX_ProdutoImagens_ProdutoId_SlotGaleria");
            imagem.HasIndex(entity => entity.ProdutoId)
                .IsUnique()
                .HasFilter("[Principal] = 1")
                .HasDatabaseName("UX_ProdutoImagens_PrincipalPorProduto");
        });

        builder.Entity<ProdutoCaracteristica>(caracteristica =>
        {
            caracteristica.ToTable("ProdutoCaracteristicas", table =>
            {
                table.HasCheckConstraint(
                    "CK_ProdutoCaracteristicas_Nome_NotBlank",
                    "LEN(LTRIM(RTRIM([Nome]))) > 0");
                table.HasCheckConstraint(
                    "CK_ProdutoCaracteristicas_TipoMedidaFisica_Valid",
                    "[TipoMedidaFisica] IS NULL OR [TipoMedidaFisica] IN (1, 2)");
            });
            caracteristica.HasKey(entity => entity.Id);
            caracteristica.Property(entity => entity.Id)
                .ValueGeneratedNever();
            caracteristica.Property(entity => entity.ProdutoId)
                .IsRequired();
            caracteristica.Property(entity => entity.Nome)
                .HasMaxLength(ProdutoCaracteristica.NomeMaxLength)
                .IsRequired();
            caracteristica.Property(entity => entity.NomeNormalizado)
                .HasMaxLength(ProdutoCaracteristica.NomeMaxLength)
                .UseCollation("Latin1_General_100_BIN2")
                .IsRequired();
            caracteristica.Property(entity => entity.TipoMedidaFisica)
                .HasConversion<short>()
                .HasColumnType("smallint");
            caracteristica.HasAlternateKey(entity => new { entity.ProdutoId, entity.Id })
                .HasName("AK_ProdutoCaracteristicas_ProdutoId_Id");
            caracteristica.HasMany(entity => entity.Valores)
                .WithOne(value => value.Caracteristica)
                .HasForeignKey(value => new
                {
                    value.ProdutoId,
                    value.ProdutoCaracteristicaId
                })
                .HasPrincipalKey(entity => new { entity.ProdutoId, entity.Id })
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired();
            caracteristica.Navigation(entity => entity.Valores)
                .HasField("_valores")
                .UsePropertyAccessMode(PropertyAccessMode.Field);

            caracteristica.HasIndex(entity => new { entity.ProdutoId, entity.NomeNormalizado })
                .IsUnique()
                .HasDatabaseName("UX_ProdutoCaracteristicas_ProdutoId_NomeNormalizado");
            caracteristica.HasIndex(entity => new { entity.ProdutoId, entity.TipoMedidaFisica })
                .IsUnique()
                .HasFilter("[TipoMedidaFisica] IS NOT NULL")
                .HasDatabaseName("UX_ProdutoCaracteristicas_ProdutoId_TipoMedidaFisica");
        });

        builder.Entity<ProdutoCaracteristicaValor>(valor =>
        {
            valor.ToTable("ProdutoCaracteristicaValores", table =>
                table.HasCheckConstraint(
                    "CK_ProdutoCaracteristicaValores_ValorMedidaCanonico_Positive",
                    "[ValorMedidaCanonico] IS NULL OR [ValorMedidaCanonico] > 0"));
            valor.HasKey(entity => entity.Id);
            valor.Property(entity => entity.Id)
                .ValueGeneratedNever();
            valor.Property(entity => entity.ProdutoId)
                .IsRequired();
            valor.Property(entity => entity.ProdutoCaracteristicaId)
                .IsRequired();
            valor.Property(entity => entity.Nome)
                .HasMaxLength(ProdutoCaracteristica.NomeMaxLength)
                .IsRequired();
            valor.Property(entity => entity.NomeNormalizado)
                .HasMaxLength(ProdutoCaracteristica.NomeMaxLength)
                .UseCollation("Latin1_General_100_BIN2")
                .IsRequired();
            valor.Property(entity => entity.ValorMedidaCanonico)
                .HasPrecision(MedidaFisicaCatalogo.Precision, MedidaFisicaCatalogo.Scale);
            valor.HasAlternateKey(entity => new
                {
                    entity.ProdutoId,
                    entity.ProdutoCaracteristicaId,
                    entity.Id
                })
                .HasName("AK_ProdutoCaracteristicaValores_Produto_Caracteristica_Id");

            valor.HasIndex(entity => new
                {
                    entity.ProdutoId,
                    entity.ProdutoCaracteristicaId,
                    entity.NomeNormalizado
                })
                .IsUnique()
                .HasDatabaseName("UX_ProdutoCaracteristicaValores_Produto_Caracteristica_NomeNormalizado");
        });

        builder.Entity<ProdutoVarianteCaracteristicaValor>(associacao =>
        {
            associacao.ToTable("ProdutoVarianteCaracteristicaValores");
            associacao.HasKey(entity => new
            {
                entity.ProdutoVarianteId,
                entity.ProdutoCaracteristicaId
            });
            associacao.Property(entity => entity.ProdutoId)
                .IsRequired();
            associacao.Property(entity => entity.ProdutoVarianteId)
                .IsRequired();
            associacao.Property(entity => entity.ProdutoCaracteristicaId)
                .IsRequired();
            associacao.Property(entity => entity.ProdutoCaracteristicaValorId)
                .IsRequired();

            associacao.HasOne(entity => entity.Caracteristica)
                .WithMany()
                .HasForeignKey(entity => new
                {
                    entity.ProdutoId,
                    entity.ProdutoCaracteristicaId
                })
                .HasPrincipalKey(entity => new { entity.ProdutoId, entity.Id })
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired();
            associacao.HasOne(entity => entity.Valor)
                .WithMany()
                .HasForeignKey(entity => new
                {
                    entity.ProdutoId,
                    entity.ProdutoCaracteristicaId,
                    entity.ProdutoCaracteristicaValorId
                })
                .HasPrincipalKey(entity => new
                {
                    entity.ProdutoId,
                    entity.ProdutoCaracteristicaId,
                    entity.Id
                })
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired();

            associacao.HasIndex(entity => new { entity.ProdutoId, entity.ProdutoVarianteId })
                .HasDatabaseName("IX_ProdutoVarianteCaracteristicaValores_Produto_Variante");
        });

        builder.Entity<ProdutoImagemVariante>(associacao =>
        {
            associacao.ToTable("ProdutoImagemVariantes");
            associacao.HasKey(entity => new
            {
                entity.ProdutoImagemId,
                entity.ProdutoVarianteId
            });
            associacao.Property(entity => entity.ProdutoId)
                .IsRequired();
            associacao.Property(entity => entity.ProdutoImagemId)
                .IsRequired();
            associacao.Property(entity => entity.ProdutoVarianteId)
                .IsRequired();

            associacao.HasOne(entity => entity.Variante)
                .WithMany()
                .HasForeignKey(entity => new
                {
                    entity.ProdutoId,
                    entity.ProdutoVarianteId
                })
                .HasPrincipalKey(entity => new { entity.ProdutoId, entity.Id })
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName(
                    "FK_ProdutoImagemVariantes_ProdutoVariantes_ProdutoId_ProdutoVarianteId")
                .IsRequired();

            associacao.HasIndex(entity => new { entity.ProdutoId, entity.ProdutoVarianteId })
                .HasDatabaseName("IX_ProdutoImagemVariantes_Produto_Variante");
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

    private static void ConfigurarQuantidadeMovimento(
        EntityTypeBuilder<MovimentoInsumo> movimento,
        Expression<Func<MovimentoInsumo, QuantidadeEstoque?>> propriedade,
        string prefixo,
        bool required)
    {
        movimento.OwnsOne(propriedade, quantidade =>
        {
            quantidade.Property(value => value.ValorBase)
                .HasColumnName($"{prefixo}_ValorBase")
                .HasPrecision(QuantidadeEstoque.Precision, QuantidadeEstoque.Scale);
            quantidade.Property(value => value.UnidadeBase)
                .HasColumnName($"{prefixo}_UnidadeBase")
                .HasConversion<short>()
                .HasColumnType("smallint");
            quantidade.Property(value => value.Dimensao)
                .HasColumnName($"{prefixo}_Dimensao")
                .HasConversion<short>()
                .HasColumnType("smallint");
            if (required)
            {
                quantidade.Property(value => value.ValorBase).IsRequired();
                quantidade.Property(value => value.UnidadeBase).IsRequired();
                quantidade.Property(value => value.Dimensao).IsRequired();
            }
        });
        movimento.Navigation(propriedade)
            .IsRequired(required);
    }

    private void ValidarMovimentosESaldos()
    {
        ChangeTracker.DetectChanges();

        if (ChangeTracker.Entries<MovimentoInsumo>()
            .Any(entry => entry.State is EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException("Movimentos de insumo são append-only.");
        }

        if (ChangeTracker.Entries<AjusteSemDiferencaInsumo>()
            .Any(entry => entry.State is EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException("Registros de idempotência também são imutáveis.");
        }

        if (ChangeTracker.Entries<MovimentoProdutoAcabado>()
            .Any(entry => entry.State is EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException("Movimentos de produto acabado são append-only.");
        }

        if (ChangeTracker.Entries<ContagemSemDiferencaProdutoAcabado>()
            .Any(entry => entry.State is EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException("Registros de idempotência também são imutáveis.");
        }

        var movimentosAdicionados = ChangeTracker.Entries<MovimentoInsumo>()
            .Where(entry => entry.State == EntityState.Added)
            .Select(entry => entry.Entity)
            .ToArray();
        var saldosAlterados = ChangeTracker.Entries<SaldoInsumo>()
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified
                || entry.Reference(saldo => saldo.QuantidadeFisica).TargetEntry?.State
                    is EntityState.Added or EntityState.Modified)
            .ToArray();

        foreach (var movement in movimentosAdicionados)
        {
            var balance = saldosAlterados.SingleOrDefault(entry =>
                entry.Entity.InsumoId == movement.InsumoId
                && entry.Entity.LoteId == movement.LoteId);
            if (balance is null)
            {
                throw new InvalidOperationException(
                    "Um movimento de entrada deve atualizar exatamente a posição de saldo correspondente.");
            }

            if (!SaldoCorrespondeAoMovimento(balance, movement))
            {
                throw new InvalidOperationException(
                    "O movimento não corresponde ao saldo físico posterior da posição.");
            }
        }

        foreach (var balance in saldosAlterados)
        {
            var movement = movimentosAdicionados.SingleOrDefault(item =>
                item.InsumoId == balance.Entity.InsumoId
                && item.LoteId == balance.Entity.LoteId);
            if (movement is null
                && balance.State == EntityState.Added
                && balance.Entity.QuantidadeFisica.ValorBase == 0m)
            {
                continue;
            }

            if (movement is null || !SaldoCorrespondeAoMovimento(balance, movement))
            {
                throw new InvalidOperationException(
                    "O saldo físico só pode ser alterado junto com o movimento correspondente.");
            }
        }

        var movimentosProdutoAcabadoAdicionados = ChangeTracker.Entries<MovimentoProdutoAcabado>()
            .Where(entry => entry.State == EntityState.Added)
            .Select(entry => entry.Entity)
            .ToArray();
        ValidarBaixasDeReservas(movimentosProdutoAcabadoAdicionados);
        var saldosProdutoAcabadoAlterados = ChangeTracker.Entries<SaldoLoteProdutoAcabado>()
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified)
            .ToArray();

        foreach (var balance in saldosProdutoAcabadoAlterados)
        {
            var movements = movimentosProdutoAcabadoAdicionados
                .Where(item =>
                    item.ProdutoVarianteId == balance.Entity.ProdutoVarianteId
                    && item.LoteId == balance.Entity.LoteId)
                .ToArray();
            if (movements.Length == 0
                && balance.State == EntityState.Added
                && balance.Entity.QuantidadeFisica == 0)
            {
                continue;
            }

            if (!SaldoProdutoAcabadoCorrespondeASequencia(balance, movements))
            {
                throw new InvalidOperationException(
                    "O saldo de produto acabado só pode ser alterado junto com os movimentos correspondentes.");
            }
        }

        foreach (var movement in movimentosProdutoAcabadoAdicionados)
        {
            var balance = saldosProdutoAcabadoAlterados.SingleOrDefault(entry =>
                entry.Entity.ProdutoVarianteId == movement.ProdutoVarianteId
                && entry.Entity.LoteId == movement.LoteId);
            if (balance is null)
            {
                throw new InvalidOperationException(
                    "Um movimento de produto acabado deve atualizar a posição de saldo correspondente.");
            }
        }
    }

    private void ValidarBaixasDeReservas(IReadOnlyCollection<MovimentoProdutoAcabado> movimentosAdicionados)
    {
        var consumidas = ChangeTracker.Entries<ReservaProdutoAcabado>()
            .Where(entry => entry.Entity.Estado == EstadoReservaProdutoAcabado.Consumida
                && (entry.State == EntityState.Added || (entry.State == EntityState.Modified
                    && entry.Property(reserva => reserva.Estado).OriginalValue != EstadoReservaProdutoAcabado.Consumida)))
            .Select(entry => entry.Entity)
            .ToDictionary(reserva => reserva.Id);

        foreach (var movimento in movimentosAdicionados.Where(movimento => movimento.ReservaProdutoAcabadoId.HasValue))
        {
            if (!consumidas.Remove(movimento.ReservaProdutoAcabadoId!.Value, out var reserva)
                || reserva.EncerradaEmUtc != movimento.RegistradoEmUtc)
            {
                throw new InvalidOperationException("O movimento de retirada exige o consumo correspondente na mesma gravação.");
            }
        }

        if (consumidas.Count != 0)
        {
            throw new InvalidOperationException("O consumo de uma reserva exige a baixa correspondente na mesma gravação.");
        }
    }

    private static bool SaldoCorrespondeAoMovimento(
        Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<SaldoInsumo> balance,
        MovimentoInsumo movement)
    {
        var quantity = balance.Entity.QuantidadeFisica;
        var delta = movement.Direcao == DirecaoMovimentoInsumo.Entrada
            ? movement.QuantidadeBase.ValorBase
            : -movement.QuantidadeBase.ValorBase;

        return (balance.State != EntityState.Added || movement.SaldoAnterior.ValorBase == 0m)
            && movement.SaldoAnterior.ValorBase >= 0m
            && movement.SaldoPosterior == quantity
            && movement.SaldoPosterior.ValorBase - movement.SaldoAnterior.ValorBase
                == delta
            && (movement.Tipo != TipoMovimentoInsumo.AjusteInventario
                || movement.QuantidadeContada == movement.SaldoPosterior)
            && movement.SaldoAnterior.UnidadeBase == movement.QuantidadeBase.UnidadeBase
            && quantity.UnidadeBase == movement.QuantidadeBase.UnidadeBase;
    }

    private static bool SaldoProdutoAcabadoCorrespondeASequencia(
        Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<SaldoLoteProdutoAcabado> balance,
        IReadOnlyCollection<MovimentoProdutoAcabado> movements)
    {
        if (movements.Count == 0)
        {
            return false;
        }

        var anteriorVendavel = balance.State == EntityState.Added
            ? 0
            : balance.Property(item => item.QuantidadeVendavel).OriginalValue;
        var anteriorDanificada = balance.State == EntityState.Added
            ? 0
            : balance.Property(item => item.QuantidadeDanificada).OriginalValue;
        var anteriorQuarentena = balance.State == EntityState.Added
            ? 0
            : balance.Property(item => item.QuantidadeQuarentena).OriginalValue;

        var restantes = movements.ToList();
        while (restantes.Count > 0)
        {
            var movement = restantes.FirstOrDefault(item =>
                item.SaldoAnterior.QuantidadeVendavel == anteriorVendavel
                && item.SaldoAnterior.QuantidadeDanificada == anteriorDanificada
                && item.SaldoAnterior.QuantidadeQuarentena == anteriorQuarentena);
            if (movement is null)
            {
                return false;
            }

            restantes.Remove(movement);
            anteriorVendavel = movement.SaldoPosterior.QuantidadeVendavel;
            anteriorDanificada = movement.SaldoPosterior.QuantidadeDanificada;
            anteriorQuarentena = movement.SaldoPosterior.QuantidadeQuarentena;
        }

        return anteriorVendavel == balance.Entity.QuantidadeVendavel
            && anteriorDanificada == balance.Entity.QuantidadeDanificada
            && anteriorQuarentena == balance.Entity.QuantidadeQuarentena;
    }

    private static bool SaldoProdutoAcabadoCorrespondeAoMovimento(
        Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<SaldoLoteProdutoAcabado> balance,
        MovimentoProdutoAcabado movement)
    {
        var saldo = balance.Entity;
        var anteriorVendavel = balance.State == EntityState.Added
            ? 0
            : balance.Property(item => item.QuantidadeVendavel).OriginalValue;
        var anteriorDanificada = balance.State == EntityState.Added
            ? 0
            : balance.Property(item => item.QuantidadeDanificada).OriginalValue;
        var anteriorQuarentena = balance.State == EntityState.Added
            ? 0
            : balance.Property(item => item.QuantidadeQuarentena).OriginalValue;

        return movement.SaldoAnterior.QuantidadeVendavel == anteriorVendavel
            && movement.SaldoAnterior.QuantidadeDanificada == anteriorDanificada
            && movement.SaldoAnterior.QuantidadeQuarentena == anteriorQuarentena
            && movement.SaldoPosterior.QuantidadeVendavel == saldo.QuantidadeVendavel
            && movement.SaldoPosterior.QuantidadeDanificada == saldo.QuantidadeDanificada
            && movement.SaldoPosterior.QuantidadeQuarentena == saldo.QuantidadeQuarentena;
    }
}
