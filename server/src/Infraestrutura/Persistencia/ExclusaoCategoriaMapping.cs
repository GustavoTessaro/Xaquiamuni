using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Aplicacao.Catalogo;
using Xaquiamuni.Dominio.Entidades;

namespace Xaquiamuni.Infraestrutura.Persistencia;

internal sealed record OperacaoExclusaoCategoria(Guid AutorId, Guid Chave, Guid CategoriaId, string NomeCategoria,
    Guid VersaoEsperada, DateTimeOffset ExcluidaEmUtc, ExclusaoCategoriaStatus Resultado);

internal static class ExclusaoCategoriaMapping
{
    internal static void Configurar(ModelBuilder builder) => builder.Entity<OperacaoExclusaoCategoria>(e =>
    {
        e.ToTable("OperacoesExclusaoCategoria");
        e.HasKey(x => new { x.AutorId, x.Chave });
        e.Property(x => x.NomeCategoria).HasMaxLength(Categoria.NomeMaxLength);
        e.Property(x => x.Resultado).HasConversion<string>().HasMaxLength(24);
        e.HasIndex(x => x.CategoriaId);
    });
}
