using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;
using Xaquiamuni.Dominio.Estoque;
using Xaquiamuni.Infraestrutura.Persistencia;
using Xaquiamuni.Infraestrutura.Persistencia.Migrations;

namespace Xaquiamuni.Tests;

public sealed class ReservaProdutoAcabadoModelTests
{
    private const string Tabela = "ReservasProdutoAcabado";

    [Fact]
    public void Modelo_MapeiaIdentificadoresQuantidadeTimestampsEControleDeConcorrencia()
    {
        using var context = CriarContexto();
        var reserva = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(ReservaProdutoAcabado))!;

        Assert.Equal(Tabela, reserva.GetTableName());
        var id = Assert.Single(reserva.FindPrimaryKey()!.Properties);
        Assert.Equal(nameof(ReservaProdutoAcabado.Id), id.Name);
        Assert.Equal(ValueGenerated.Never, id.ValueGenerated);
        VerificarColuna(reserva, "Id", "uniqueidentifier");
        VerificarColuna(reserva, "PedidoId", "uniqueidentifier");
        VerificarColuna(reserva, "ProdutoVarianteId", "uniqueidentifier");
        VerificarColuna(reserva, "LoteId", "uniqueidentifier");
        VerificarColuna(reserva, "Quantidade", "int");
        VerificarColuna(reserva, "Estado", "smallint");
        VerificarColuna(reserva, "CriadaEmUtc", "datetimeoffset(7)");
        VerificarColuna(reserva, "ExpiraEmUtc", "datetimeoffset(7)");
        VerificarColuna(reserva, "ConfirmadaEmUtc", "datetimeoffset(7)", opcional: true);
        VerificarColuna(reserva, "EncerradaEmUtc", "datetimeoffset(7)", opcional: true);
        VerificarColuna(reserva, "MotivoEncerramento", "nvarchar(max)", opcional: true);
        VerificarColuna(reserva, "Versao", "rowversion");

        var versao = reserva.FindProperty("Versao")!;
        Assert.True(versao.IsShadowProperty());
        Assert.True(versao.IsConcurrencyToken);
        Assert.Equal(ValueGenerated.OnAddOrUpdate, versao.ValueGenerated);
    }

    [Fact]
    public void Modelo_ReutilizaChaveDoLoteEIndexaPedidoAlocacoesEExpiracao()
    {
        using var context = CriarContexto();
        var reserva = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(ReservaProdutoAcabado))!;
        var lote = Assert.Single(reserva.GetForeignKeys());
        Assert.Equal(typeof(LoteProdutoAcabado), lote.PrincipalEntityType.ClrType);
        Assert.Equal(["ProdutoVarianteId", "LoteId"], lote.Properties.Select(propriedade => propriedade.Name));
        Assert.Equal(["ProdutoVarianteId", "Id"], lote.PrincipalKey.Properties.Select(propriedade => propriedade.Name));
        Assert.Equal(DeleteBehavior.Restrict, lote.DeleteBehavior);
        Assert.True(lote.IsRequired);

        var indices = reserva.GetIndexes().ToArray();
        Assert.Equal(3, indices.Length);
        Assert.All(indices, indice => Assert.False(indice.IsUnique));
        Assert.Contains(indices, indice => indice.Properties.Select(p => p.Name).SequenceEqual(["PedidoId"]));
        Assert.Contains(indices, indice =>
            indice.Properties.Select(p => p.Name).SequenceEqual(["ProdutoVarianteId", "LoteId", "Estado"]));
        Assert.Contains(indices, indice =>
            indice.Properties.Select(p => p.Name).SequenceEqual(["Estado", "ExpiraEmUtc"]));
    }

    [Theory]
    [InlineData(EstadoReservaProdutoAcabado.Ativa, 0)]
    [InlineData(EstadoReservaProdutoAcabado.Confirmada, 1)]
    [InlineData(EstadoReservaProdutoAcabado.Liberada, 2)]
    [InlineData(EstadoReservaProdutoAcabado.Expirada, 3)]
    [InlineData(EstadoReservaProdutoAcabado.Consumida, 4)]
    public void Modelo_ConverteEstadosComCodigosEstaveis(EstadoReservaProdutoAcabado estado, short codigo)
    {
        using var context = CriarContexto();
        var reserva = context.Model.FindEntityType(typeof(ReservaProdutoAcabado))!;
        var conversor = reserva.FindProperty("Estado")!.GetTypeMapping().Converter!;
        Assert.Equal(codigo, Assert.IsType<short>(conversor.ConvertToProvider(estado)));
        Assert.Equal(estado, Assert.IsType<EstadoReservaProdutoAcabado>(conversor.ConvertFromProvider(codigo)));
    }

    [Fact]
    public void Modelo_MaterializaReservaConfirmadaComIdentidadeECompromissoPreservados()
    {
        using var context = CriarContexto();
        var entidade = context.Model.FindEntityType(typeof(ReservaProdutoAcabado))!;
        var criadaEm = new DateTimeOffset(2026, 11, 18, 15, 0, 0, TimeSpan.Zero).AddTicks(1);
        var original = ReservaProdutoAcabado.Criar(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 3, criadaEm);
        original.ConfirmarCompromisso(criadaEm.AddHours(1));

        var propriedades = entidade.GetProperties().ToArray();
        var valores = new object?[propriedades.Length];
        foreach (var propriedade in propriedades)
        {
            valores[propriedade.GetIndex()] = propriedade.IsShadowProperty()
                ? new byte[8]
                : propriedade.PropertyInfo!.GetValue(original);
        }

        var materializar = context.GetService<IStructuralTypeMaterializerSource>().GetMaterializer(entidade);
        var reserva = Assert.IsType<ReservaProdutoAcabado>(
            materializar(new MaterializationContext(new ValueBuffer(valores), context)));

        Assert.Equal(original.Id, reserva.Id);
        Assert.Equal(original.PedidoId, reserva.PedidoId);
        Assert.Equal(original.ProdutoVarianteId, reserva.ProdutoVarianteId);
        Assert.Equal(original.LoteId, reserva.LoteId);
        Assert.Equal(original.Quantidade, reserva.Quantidade);
        Assert.Equal(original.CriadaEmUtc, reserva.CriadaEmUtc);
        Assert.Equal(original.ExpiraEmUtc, reserva.ExpiraEmUtc);
        Assert.Equal(original.ConfirmadaEmUtc, reserva.ConfirmadaEmUtc);
        Assert.Equal(original.Estado, reserva.Estado);
        Assert.True(reserva.EstaAtivaEm(criadaEm.AddDays(2)));
    }

    [Fact]
    public void Migration_AdicionaSomenteTabelaDeReservasComRestricoesEIndices()
    {
        using var context = CriarContexto();
        var migrations = context.GetService<IMigrationsAssembly>();
        var tipo = Assert.Single(migrations.Migrations.Values, tipo =>
            tipo.AsType() == typeof(PersistirReservasProdutoAcabado));
        var migration = migrations.CreateMigration(tipo, context.Database.ProviderName!);

        Assert.Equal(4, migration.UpOperations.Count);
        var tabela = Assert.Single(migration.UpOperations.OfType<CreateTableOperation>());
        Assert.Equal(Tabela, tabela.Name);
        var indices = migration.UpOperations.OfType<CreateIndexOperation>().ToArray();
        Assert.Equal(3, indices.Length);
        Assert.All(indices, indice => Assert.Equal(Tabela, indice.Table));
        Assert.Equal("LotesProdutoAcabado", Assert.Single(tabela.ForeignKeys).PrincipalTable);
        Assert.Equal(
            new[] { "Cronologia_Valid", "Estado_Valid", "Identificadores_NotEmpty", "Motivo_Valid",
                "Prazo_24Horas", "Quantidade_Positive", "Timestamps_Utc" },
            tabela.CheckConstraints.Select(constraint => constraint.Name!.Replace("CK_ReservasProdutoAcabado_", "")));
        Assert.Equal(Tabela, Assert.IsType<DropTableOperation>(Assert.Single(migration.DownOperations)).Name);
        Assert.False(context.Database.HasPendingModelChanges());
    }

    private static void VerificarColuna(IEntityType entidade, string nome, string tipo, bool opcional = false)
    {
        var propriedade = entidade.FindProperty(nome)!;
        Assert.Equal(tipo, propriedade.GetColumnType());
        Assert.Equal(opcional, propriedade.IsNullable);
    }

    private static XaquiamuniDbContext CriarContexto()
    {
        var options = new DbContextOptionsBuilder<XaquiamuniDbContext>()
            .UseSqlServer("Server=127.0.0.1,1;Database=XaquiamuniDesignOnly;Connect Timeout=1")
            .Options;
        return new XaquiamuniDbContext(options);
    }
}
