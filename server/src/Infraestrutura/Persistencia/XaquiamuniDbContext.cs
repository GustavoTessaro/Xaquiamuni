using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Infraestrutura.Identidade;

namespace Xaquiamuni.Infraestrutura.Persistencia;

public sealed class XaquiamuniDbContext
    : IdentityDbContext<XaquiamuniUser, IdentityRole<Guid>, Guid>
{
    public XaquiamuniDbContext(DbContextOptions<XaquiamuniDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<XaquiamuniUser>()
            .HasIndex(user => user.NormalizedEmail)
            .IsUnique()
            .HasDatabaseName("EmailIndex")
            .HasFilter("[NormalizedEmail] IS NOT NULL");
    }
}
