using CofreEletronico.Infrastructure.Persistencia.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace CofreEletronico.Infrastructure.Persistencia;

/// <summary>
/// Contexto EF Core da aplicação: tabelas do ASP.NET Identity e, depois, as entidades de domínio.
/// </summary>
public class CofreDbContext(DbContextOptions<CofreDbContext> options)
    : IdentityDbContext<Usuario, Papel, Guid>(options)
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Ponto de entrada das classes IEntityTypeConfiguration desta camada.
        builder.ApplyConfigurationsFromAssembly(typeof(CofreDbContext).Assembly);
    }
}
