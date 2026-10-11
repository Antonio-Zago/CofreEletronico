using Microsoft.Extensions.DependencyInjection;

namespace CofreEletronico.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Registra os serviços da camada de aplicação (casos de uso, validadores).
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        return services;
    }
}
