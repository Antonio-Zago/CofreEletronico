using CofreEletronico.Infrastructure.Persistencia;
using CofreEletronico.Infrastructure.Persistencia.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CofreEletronico.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registra as implementações de infraestrutura (persistência, MQTT, Mercado Pago, jobs).
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // A string "Cofre" vem de user-secrets ou da variável ConnectionStrings__Cofre (nunca de appsettings).
        // O AddDbContext é lazy: sem a string, a Api sobe; só falha ao abrir conexão com o banco.
        services.AddDbContext<CofreDbContext>(options =>
            options
                .UseNpgsql(configuration.GetConnectionString(PersistenciaExtensions.ConnectionStringNome))
                .UseSnakeCaseNamingConvention());

        // Só o núcleo do Identity (UserManager/RoleManager + stores EF). SignInManager, autenticação/JWT
        // e token providers entram na T-0302/T-0303.
        services
            .AddIdentityCore<Usuario>(PoliticaIdentity.Configurar)
            .AddRoles<Papel>()
            .AddEntityFrameworkStores<CofreDbContext>();

        return services;
    }
}
