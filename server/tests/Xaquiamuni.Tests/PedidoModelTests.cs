using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xaquiamuni.Dominio.Entidades;
using Xaquiamuni.Dominio.Estoque;
using Xaquiamuni.Infraestrutura.Persistencia;
using Xaquiamuni.Infraestrutura.Persistencia.Migrations;

namespace Xaquiamuni.Tests;

public sealed class PedidoModelTests
{
    [Fact]
    public void Modelo_MapeiaPedidoClienteItensSnapshotsEConcorrencia()
    {
        using var context = CriarContexto();
        var model = context.GetService<IDesignTimeModel>().Model;
        var pedido = model.FindEntityType(typeof(Pedido))!;
        var item = model.FindEntityType(typeof(ItemPedido))!;
        var reserva = model.FindEntityType(typeof(ReservaProdutoAcabado))!;

        Assert.Equal("Pedidos", pedido.GetTableName());
        Assert.Equal("ItensPedido", item.GetTableName());
        var cliente = Assert.Single(pedido.GetForeignKeys(), fk => fk.PrincipalEntityType.ClrType == typeof(ClientePerfil));
        Assert.Equal(typeof(ClientePerfil), cliente.PrincipalEntityType.ClrType);
        Assert.Equal(nameof(ClientePerfil.UserId), Assert.Single(cliente.PrincipalKey.Properties).Name);
        Assert.Equal(DeleteBehavior.Restrict, cliente.DeleteBehavior);
        Assert.True(cliente.IsRequired);
        var proposta = Assert.Single(pedido.GetForeignKeys(), fk => fk.PrincipalEntityType.GetTableName() == "PropostasCorreios");
        Assert.Equal(["CotacaoCorreiosId", "RevisaoCotacaoCorreios"], proposta.Properties.Select(p => p.Name));
        Assert.Equal(["SolicitacaoId", "Revisao"], proposta.PrincipalKey.Properties.Select(p => p.Name));
        Assert.Equal(DeleteBehavior.Restrict, proposta.DeleteBehavior);
        Assert.False(proposta.IsRequired);
        var conversao = Assert.Single(pedido.GetIndexes(), i => i.Properties.Select(p => p.Name).SequenceEqual(["CotacaoCorreiosId"]));
        Assert.True(conversao.IsUnique);
        Assert.Equal("[CotacaoCorreiosId] IS NOT NULL", conversao.GetFilter());
        var vinculoVariante = Assert.Single(item.GetForeignKeys(), fk => fk.PrincipalEntityType.ClrType == typeof(ProdutoVariante));
        Assert.Equal(["ProdutoId", "ProdutoVarianteId"], vinculoVariante.Properties.Select(p => p.Name));
        Assert.Equal(["ProdutoId", "Id"], vinculoVariante.PrincipalKey.Properties.Select(p => p.Name));
        Assert.Equal(DeleteBehavior.Restrict, vinculoVariante.DeleteBehavior);
        Assert.Equal(typeof(Pedido), pedido.FindNavigation(nameof(Pedido.Itens))!.ForeignKey.PrincipalEntityType.ClrType);
        Assert.Equal(PropertyAccessMode.Field, pedido.FindNavigation(nameof(Pedido.Itens))!.GetPropertyAccessMode());
        Assert.Equal("datetimeoffset", pedido.FindProperty(nameof(Pedido.PrazoPagamentoEmUtc))!.GetColumnType());
        Assert.Equal("smallint", pedido.FindProperty(nameof(Pedido.Situacao))!.GetColumnType());
        Assert.Equal("smallint", pedido.FindProperty(nameof(Pedido.ModalidadeEntrega))!.GetColumnType());
        Assert.Equal("bigint", pedido.FindProperty(nameof(Pedido.TotalCentavos))!.GetColumnType());
        Assert.Equal("bigint", item.FindProperty(nameof(ItemPedido.PrecoUnitarioCentavos))!.GetColumnType());
        Assert.Equal("int", item.FindProperty(nameof(ItemPedido.Quantidade))!.GetColumnType());
        Assert.Null(item.FindProperty(nameof(ItemPedido.SubtotalCentavos)));
        Assert.Equal(Produto.NomeMaxLength, item.FindProperty(nameof(ItemPedido.NomeProduto))!.GetMaxLength());
        Assert.Equal(ProdutoVariante.IdentificacaoMaxLength, item.FindProperty(nameof(ItemPedido.NomeVariante))!.GetMaxLength());
        var versao = pedido.FindProperty("Versao")!;
        Assert.Equal("rowversion", versao.GetColumnType());
        Assert.True(versao.IsConcurrencyToken);
        Assert.Contains(pedido.GetIndexes(), i => i.Properties.Select(p => p.Name).SequenceEqual(["ClienteId", "CriadoEmUtc"]));
        Assert.Contains(pedido.GetIndexes(), i => i.Properties.Select(p => p.Name).SequenceEqual(["Situacao", "PrazoPagamentoEmUtc"]));
        Assert.Contains(item.GetIndexes(), i => i.IsUnique && i.Properties.Select(p => p.Name).SequenceEqual(["PedidoId", "ProdutoVarianteId"]));
        Assert.Equal(pedido.FindPrimaryKey()!.Properties.Single().GetColumnType(),
            reserva.FindProperty(nameof(ReservaProdutoAcabado.PedidoId))!.GetColumnType());
        Assert.Contains(reserva.GetIndexes(), i => i.Properties.Select(p => p.Name).SequenceEqual(["PedidoId"]));
        Assert.DoesNotContain(reserva.GetForeignKeys(), fk => fk.PrincipalEntityType.ClrType == typeof(Pedido));
    }

    [Fact]
    public void Migration_AdicionaApenasTabelasDePedidoEIndicesComSnapshotSincronizado()
    {
        using var context = CriarContexto();
        var migrations = context.GetService<IMigrationsAssembly>();
        var tipo = Assert.Single(migrations.Migrations.Values, t => t.AsType() == typeof(PersistirPedidosCompra));
        var migration = migrations.CreateMigration(tipo, context.Database.ProviderName!);
        var tabelas = migration.UpOperations.OfType<CreateTableOperation>().ToDictionary(t => t.Name);

        Assert.Equal(["ItensPedido", "Pedidos"], tabelas.Keys.Order());
        Assert.All(migration.UpOperations, operacao =>
        {
            if (operacao is not CreateTableOperation)
            {
                Assert.Contains(Assert.IsType<CreateIndexOperation>(operacao).Table, tabelas.Keys);
            }
        });
        Assert.Equal("ClientePerfis", Assert.Single(tabelas["Pedidos"].ForeignKeys).PrincipalTable);
        Assert.Equal(["Pedidos", "ProdutoVariantes"], tabelas["ItensPedido"].ForeignKeys.Select(fk => fk.PrincipalTable).Order());
        Assert.Equal(["ItensPedido", "Pedidos"], migration.DownOperations.Select(o => Assert.IsType<DropTableOperation>(o).Name));
        Assert.False(context.Database.HasPendingModelChanges());
    }

    private static XaquiamuniDbContext CriarContexto() => new(
        new DbContextOptionsBuilder<XaquiamuniDbContext>()
            .UseSqlServer("Server=127.0.0.1,1;Database=XaquiamuniDesignOnly;Connect Timeout=1").Options);
}
