using CofreEletronico.Application.Autenticacao;
using CofreEletronico.Application.Autorizacao;
using CofreEletronico.Application.Estantes;
using Microsoft.Extensions.DependencyInjection;

namespace CofreEletronico.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Registra os serviços da camada de aplicação (casos de uso, validadores).
    /// As portas (IServicoIdentidade, geradores de token, repositórios) e o TimeProvider vêm da Infrastructure.
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // Autenticação (T-0302)
        services.AddScoped<EmissorTokens>();
        services.AddScoped<RegistrarUsuario>();
        services.AddScoped<AutenticarUsuario>();
        services.AddScoped<RenovarTokens>();
        services.AddScoped<EncerrarSessao>();

        // Autorização (T-0303)
        services.AddScoped<GarantirAdminInicial>();

        // Administração de estantes (T-0402)
        services.AddScoped<CriarEstante>();
        services.AddScoped<ListarEstantes>();
        services.AddScoped<ObterEstante>();
        services.AddScoped<AtualizarEstante>();
        services.AddScoped<DesativarEstante>();
        services.AddScoped<ReativarEstante>();
        services.AddScoped<RegenerarCredenciaisEstante>();

        return services;
    }
}
