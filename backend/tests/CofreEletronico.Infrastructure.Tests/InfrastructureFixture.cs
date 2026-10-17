using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CofreEletronico.Infrastructure.Tests;

/// <summary>
/// Monta o container real de AddInfrastructure. A string de conexão é fictícia:
/// nenhum teste daqui abre conexão com o banco (isso é da T-0307, com Testcontainers).
/// </summary>
internal static class InfrastructureFixture
{
    public const string ConnectionStringFicticia = "Host=localhost;Database=fake;Username=fake;Password=fake";

    public static ServiceProvider CriarProvider(string? connectionString = ConnectionStringFicticia)
    {
        var valores = new Dictionary<string, string?>();
        if (connectionString is not null)
        {
            valores["ConnectionStrings:Cofre"] = connectionString;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(valores).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddInfrastructure(configuration);

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }
}
