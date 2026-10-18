using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CofreEletronico.Infrastructure.Tests;

/// <summary>
/// Monta o container real de AddInfrastructure. A string de conexão e a chave JWT são fictícias:
/// nenhum teste daqui abre conexão com o banco (isso é da T-0307, com Testcontainers).
/// </summary>
internal static class InfrastructureFixture
{
    public const string ConnectionStringFicticia = "Host=localhost;Database=fake;Username=fake;Password=fake";

    /// <summary>Chave de teste com 40 bytes (o mínimo é 32). Não é segredo: só existe nos testes.</summary>
    public const string ChaveJwtFicticia = "chave-de-teste-com-mais-de-32-bytes-0001";

    public const string Emissor = "cofre-eletronico-api";
    public const string Audiencia = "cofre-eletronico-app";

    public static ServiceProvider CriarProvider(
        string? connectionString = ConnectionStringFicticia,
        IDictionary<string, string?>? configuracaoExtra = null,
        TimeProvider? relogio = null)
    {
        var valores = new Dictionary<string, string?>
        {
            ["Jwt:Emissor"] = Emissor,
            ["Jwt:Audiencia"] = Audiencia,
            ["Jwt:ChaveAssinatura"] = ChaveJwtFicticia,
        };
        if (connectionString is not null)
        {
            valores["ConnectionStrings:Cofre"] = connectionString;
        }

        foreach (var (chave, valor) in configuracaoExtra ?? new Dictionary<string, string?>())
        {
            valores[chave] = valor;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(valores).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        if (relogio is not null)
        {
            services.AddSingleton(relogio); // AddInfrastructure só registra TimeProvider.System se não houver outro
        }

        services.AddInfrastructure(configuration);

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }
}

internal sealed class RelogioFixo(DateTimeOffset agora) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => agora;
}
