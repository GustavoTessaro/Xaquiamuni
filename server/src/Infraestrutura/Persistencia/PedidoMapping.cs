using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Dominio.Entidades;

namespace Xaquiamuni.Infraestrutura.Persistencia;

internal static class PedidoMapping
{
    private const string GuidVazio = "'00000000-0000-0000-0000-000000000000'";

    internal static void Configurar(ModelBuilder builder)
    {
        builder.Entity<Pedido>(pedido =>
        {
            pedido.ToTable("Pedidos", tabela =>
            {
                tabela.HasCheckConstraint("CK_Pedidos_NumeroPublico_Formato",
                    "LEN([NumeroPublico]) = 13 AND LEFT([NumeroPublico], 3) COLLATE Latin1_General_100_BIN2 = 'XQ-' "
                    + "AND SUBSTRING([NumeroPublico], 4, 10) COLLATE Latin1_General_100_BIN2 "
                    + $"NOT LIKE '%[^{NumeroPublicoPedido.Alfabeto}]%'");
                tabela.HasCheckConstraint("CK_Pedidos_Identificadores_NotEmpty",
                    $"[Id] <> {GuidVazio} AND [ClienteId] <> {GuidVazio}");
                tabela.HasCheckConstraint("CK_Pedidos_Modalidade_Valid",
                    "((([ModalidadeEntrega] = 1 AND [FreteCentavos] = 0) OR "
                    + "([ModalidadeEntrega] = 2 AND [Entrega] IS NOT NULL)) "
                    + "AND [CotacaoCorreiosId] IS NULL AND [RevisaoCotacaoCorreios] IS NULL) OR "
                    + "([ModalidadeEntrega] = 3 AND [Entrega] IS NOT NULL AND [CotacaoCorreiosId] IS NOT NULL "
                    + "AND [RevisaoCotacaoCorreios] IS NOT NULL AND [RevisaoCotacaoCorreios] > 0 "
                    + "AND [RetiradaAutorizadaEmUtc] IS NULL AND [Situacao] NOT IN (4, 8))");
                tabela.HasCheckConstraint("CK_Pedidos_Frete_Valid",
                    "[FreteCentavos] >= 0 AND [FreteCentavos] <= [TotalCentavos]");
                tabela.HasCheckConstraint("CK_Pedidos_Total_NonNegative", "[TotalCentavos] >= 0");
                tabela.HasCheckConstraint("CK_Pedidos_Prazo_Utc24Horas",
                    "DATEPART(TZOFFSET, [CriadoEmUtc]) = 0 AND DATEPART(TZOFFSET, [PrazoPagamentoEmUtc]) = 0 "
                    + "AND DATEDIFF_BIG(hour, [CriadoEmUtc], [PrazoPagamentoEmUtc]) = 24 "
                    + "AND CAST([CriadoEmUtc] AS time(7)) = CAST([PrazoPagamentoEmUtc] AS time(7))");
                tabela.HasCheckConstraint("CK_Pedidos_Auditoria_Valid",
                    ParAuditoria("PagamentoConfirmadoEmUtc", "PagamentoConfirmadoPorId") + " AND "
                    + ParAuditoria("PreparacaoIniciadaEmUtc", "PreparacaoIniciadaPorId") + " AND "
                    + ParAuditoria("RetiradaAutorizadaEmUtc", "RetiradaAutorizadaPorId") + " AND "
                    + ParAuditoria("SaidaEntregaEmUtc", "SaidaEntregaPorId"));
                tabela.HasCheckConstraint("CK_Pedidos_Cronologia_Valid",
                    "([PagamentoConfirmadoEmUtc] IS NULL OR ([PagamentoConfirmadoEmUtc] >= [CriadoEmUtc] "
                    + "AND [PagamentoConfirmadoEmUtc] < [PrazoPagamentoEmUtc])) "
                    + "AND ([PreparacaoIniciadaEmUtc] IS NULL OR ([PagamentoConfirmadoEmUtc] IS NOT NULL "
                    + "AND [PreparacaoIniciadaEmUtc] >= [PagamentoConfirmadoEmUtc])) "
                    + "AND ([RetiradaAutorizadaEmUtc] IS NULL OR ([PreparacaoIniciadaEmUtc] IS NOT NULL "
                    + "AND [RetiradaAutorizadaEmUtc] >= [PreparacaoIniciadaEmUtc])) "
                    + "AND ([EncerradoEmUtc] IS NULL OR (DATEPART(TZOFFSET, [EncerradoEmUtc]) = 0 "
                    + "AND [EncerradoEmUtc] >= COALESCE([SaidaEntregaEmUtc], [RetiradaAutorizadaEmUtc], [PreparacaoIniciadaEmUtc], "
                    + "[PagamentoConfirmadoEmUtc], [CriadoEmUtc])))");
                tabela.HasCheckConstraint("CK_Pedidos_SaidaEntrega_Valid",
                    "([SaidaEntregaEmUtc] IS NULL AND [Situacao] NOT IN (8, 9) "
                    + "AND ([Situacao] <> 5 OR [ModalidadeEntrega] = 1)) "
                    + "OR ([SaidaEntregaEmUtc] IS NOT NULL "
                    + "AND (([ModalidadeEntrega] = 2 AND [Situacao] IN (5, 8)) "
                    + "OR ([ModalidadeEntrega] = 3 AND [Situacao] IN (5, 9))) "
                    + "AND [RetiradaAutorizadaEmUtc] IS NULL "
                    + "AND [PreparacaoIniciadaEmUtc] IS NOT NULL AND [SaidaEntregaEmUtc] >= [PreparacaoIniciadaEmUtc])");
                tabela.HasCheckConstraint("CK_Pedidos_Postagem_Valid",
                    "(([ModalidadeEntrega] <> 3 OR [SaidaEntregaEmUtc] IS NULL) "
                    + "AND [ServicoPostalUtilizado] IS NULL AND [CodigoRastreamento] IS NULL) "
                    + "OR ([ModalidadeEntrega] = 3 AND [SaidaEntregaEmUtc] IS NOT NULL "
                    + "AND [ServicoPostalUtilizado] IS NOT NULL AND LEN(LTRIM(RTRIM([ServicoPostalUtilizado]))) > 0 "
                    + "AND ([CodigoRastreamento] IS NULL OR LEN([CodigoRastreamento]) = 13))");
                tabela.HasCheckConstraint("CK_Pedidos_Situacao_Valid",
                    "([Situacao] = 1 AND [PagamentoConfirmadoEmUtc] IS NULL AND [EncerradoEmUtc] IS NULL) "
                    + "OR ([Situacao] = 2 AND [PagamentoConfirmadoEmUtc] IS NOT NULL "
                    + "AND [PreparacaoIniciadaEmUtc] IS NULL AND [EncerradoEmUtc] IS NULL) "
                    + "OR ([Situacao] = 3 AND [PreparacaoIniciadaEmUtc] IS NOT NULL "
                    + "AND [RetiradaAutorizadaEmUtc] IS NULL AND [EncerradoEmUtc] IS NULL) "
                    + "OR ([Situacao] = 4 AND [RetiradaAutorizadaEmUtc] IS NOT NULL AND [EncerradoEmUtc] IS NULL) "
                    + "OR ([Situacao] = 5 AND COALESCE([RetiradaAutorizadaEmUtc], [SaidaEntregaEmUtc]) IS NOT NULL "
                    + "AND [EncerradoEmUtc] IS NOT NULL) "
                    + "OR ([Situacao] IN (8, 9) AND [SaidaEntregaEmUtc] IS NOT NULL AND [EncerradoEmUtc] IS NULL) "
                    + "OR ([Situacao] = 6 AND [EncerradoEmUtc] IS NOT NULL "
                    + "AND ([PagamentoConfirmadoEmUtc] IS NOT NULL OR [EncerradoEmUtc] < [PrazoPagamentoEmUtc])) "
                    + "OR ([Situacao] = 7 AND [PagamentoConfirmadoEmUtc] IS NULL "
                    + "AND [EncerradoEmUtc] IS NOT NULL AND [EncerradoEmUtc] >= [PrazoPagamentoEmUtc])");
                tabela.HasCheckConstraint("CK_Pedidos_Encerramento_Valid",
                    $"(([Situacao] IN (5, 6) AND [EncerradoPorId] IS NOT NULL AND [EncerradoPorId] <> {GuidVazio}) "
                    + "OR ([Situacao] NOT IN (5, 6) AND [EncerradoPorId] IS NULL)) "
                    + "AND (([Situacao] = 6 AND [MotivoCancelamento] IS NOT NULL "
                    + "AND LEN(LTRIM(RTRIM([MotivoCancelamento]))) > 0) "
                    + "OR ([Situacao] <> 6 AND [MotivoCancelamento] IS NULL))");
            });
            pedido.HasKey(entity => entity.Id);
            pedido.Property(entity => entity.Id).ValueGeneratedNever();
            pedido.Property(entity => entity.NumeroPublico).HasMaxLength(13).IsUnicode(false).IsRequired();
            pedido.HasIndex(entity => entity.NumeroPublico).IsUnique();
            pedido.Property(entity => entity.Situacao).HasConversion<short>().HasColumnType("smallint");
            pedido.Property(entity => entity.ModalidadeEntrega).HasConversion<short>().HasColumnType("smallint");
            pedido.Property(entity => entity.Entrega).HasConversion(
                dados => System.Text.Json.JsonSerializer.Serialize(dados, (System.Text.Json.JsonSerializerOptions?)null),
                json => System.Text.Json.JsonSerializer.Deserialize<DadosEntregaPedido>(json, (System.Text.Json.JsonSerializerOptions?)null));
            pedido.Property(entity => entity.HashOfertaEntrega).HasMaxLength(64).IsUnicode(false);
            pedido.Property(entity => entity.SubtotalProdutosCentavos)
                .HasComputedColumnSql("[TotalCentavos] - [FreteCentavos]", stored: true);
            pedido.Property(entity => entity.MotivoCancelamento).HasMaxLength(Pedido.MotivoCancelamentoMaxLength);
            pedido.Property(entity => entity.ServicoPostalUtilizado).HasMaxLength(60);
            pedido.Property(entity => entity.CodigoRastreamento).HasMaxLength(13).IsUnicode(false);
            pedido.Property(entity => entity.Versao).IsRowVersion().IsRequired();
            pedido.HasOne<ClientePerfil>().WithMany().HasForeignKey(entity => entity.ClienteId)
                .OnDelete(DeleteBehavior.Restrict);
            pedido.HasMany(entity => entity.Itens).WithOne().HasForeignKey(item => item.PedidoId)
                .OnDelete(DeleteBehavior.Restrict);
            pedido.Navigation(entity => entity.Itens).HasField("_itens").UsePropertyAccessMode(PropertyAccessMode.Field);
            pedido.Property<Guid?>("ChaveIdempotencia");
            pedido.HasIndex("ClienteId", "ChaveIdempotencia").IsUnique()
                .HasFilter("[ChaveIdempotencia] IS NOT NULL");
            pedido.HasIndex(entity => new { entity.ClienteId, entity.CriadoEmUtc });
            pedido.HasIndex(entity => new { entity.Situacao, entity.PrazoPagamentoEmUtc });
            pedido.HasIndex(entity => entity.CotacaoCorreiosId).IsUnique().HasFilter("[CotacaoCorreiosId] IS NOT NULL");
            pedido.HasOne<PropostaCorreios>().WithMany().HasForeignKey(entity =>
                new { entity.CotacaoCorreiosId, entity.RevisaoCotacaoCorreios }).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<OperacaoEntregaPropria>(operacao =>
        {
            operacao.ToTable("OperacoesEntregaPropria");
            operacao.HasKey(x => new { x.AdministradorId, x.Chave });
            operacao.Property(x => x.VersaoEsperada).HasMaxLength(8).IsFixedLength();
            operacao.HasOne<Pedido>().WithMany().HasForeignKey(x => x.PedidoId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<OperacaoEntregaPostal>(operacao =>
        {
            operacao.ToTable("OperacoesEntregaPostal");
            operacao.HasKey(x => new { x.AdministradorId, x.Chave });
            operacao.Property(x => x.Impressao).HasMaxLength(64).IsUnicode(false);
            operacao.Property(x => x.CodigoAnterior).HasMaxLength(13).IsUnicode(false);
            operacao.Property(x => x.CodigoNovo).HasMaxLength(13).IsUnicode(false);
            operacao.HasOne<Pedido>().WithMany().HasForeignKey(x => x.PedidoId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<ItemPedido>(item =>
        {
            item.ToTable("ItensPedido", tabela =>
            {
                tabela.HasCheckConstraint("CK_ItensPedido_Identificadores_NotEmpty",
                    $"[Id] <> {GuidVazio} AND [PedidoId] <> {GuidVazio} "
                    + $"AND [ProdutoId] <> {GuidVazio} AND [ProdutoVarianteId] <> {GuidVazio}");
                tabela.HasCheckConstraint("CK_ItensPedido_Quantidade_Positive", "[Quantidade] > 0");
                tabela.HasCheckConstraint("CK_ItensPedido_PrecoCentavos_Valid", "[PrecoUnitarioCentavos] >= 0 "
                    + "AND [PrecoUnitarioCentavos] <= CONVERT(bigint, 9223372036854775807) "
                    + "/ NULLIF(CONVERT(bigint, [Quantidade]), 0)");
                tabela.HasCheckConstraint("CK_ItensPedido_Nomes_NotBlank",
                    "LEN(LTRIM(RTRIM([NomeProduto]))) > 0 AND LEN(LTRIM(RTRIM([NomeVariante]))) > 0");
            });
            item.HasKey(entity => entity.Id);
            item.Property(entity => entity.Id).ValueGeneratedNever();
            item.Property(entity => entity.NomeProduto).HasMaxLength(Produto.NomeMaxLength).IsRequired();
            item.Property(entity => entity.NomeVariante).HasMaxLength(ProdutoVariante.IdentificacaoMaxLength).IsRequired();
            item.HasOne<ProdutoVariante>().WithMany()
                .HasForeignKey(entity => new { entity.ProdutoId, entity.ProdutoVarianteId })
                .HasPrincipalKey(entity => new { entity.ProdutoId, entity.Id })
                .OnDelete(DeleteBehavior.Restrict);
            item.HasIndex(entity => new { entity.PedidoId, entity.ProdutoVarianteId }).IsUnique();
        });
    }

    private static string ParAuditoria(string data, string responsavel) =>
        $"([{data}] IS NULL AND [{responsavel}] IS NULL OR "
        + $"([{data}] IS NOT NULL AND [{responsavel}] IS NOT NULL AND [{responsavel}] <> {GuidVazio} "
        + $"AND DATEPART(TZOFFSET, [{data}]) = 0))";
}
