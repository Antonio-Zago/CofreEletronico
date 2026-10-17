using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CofreEletronico.Infrastructure.Persistencia;

public static class PersistenciaExtensions
{
    /// <summary>Nome da string de conexão (<c>ConnectionStrings:Cofre</c>).</summary>
    public const string ConnectionStringNome = "Cofre";

    /// <summary>
    /// Aplica as migrations pendentes do <see cref="CofreDbContext"/>. Pensado para Development:
    /// quem chama decide o ambiente. Sem a string de conexão, só registra um aviso e retorna,
    /// para a Api continuar subindo sem banco. Com a string e o banco indisponível, a exceção propaga.
    /// </summary>
    public static async Task AplicarMigrationsAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(PersistenciaExtensions).FullName!);
        var configuration = provider.GetRequiredService<IConfiguration>();

        if (string.IsNullOrWhiteSpace(configuration.GetConnectionString(ConnectionStringNome)))
        {
            logger.LogWarning(
                "String de conexão '{Nome}' não configurada: migrations não aplicadas. Configure via user-secrets ou ConnectionStrings__{Nome}.",
                ConnectionStringNome, ConnectionStringNome);
            return;
        }

        var context = provider.GetRequiredService<CofreDbContext>();
        try
        {
            await context.Database.MigrateAsync(cancellationToken);
            logger.LogInformation("Migrations do banco aplicadas.");
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Falha ao aplicar migrations: verifique se o PostgreSQL está no ar e a string '{Nome}'.",
                ConnectionStringNome);
            throw;
        }
    }
}
