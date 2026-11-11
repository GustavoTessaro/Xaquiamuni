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
}
