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
        return services;
    }
}
