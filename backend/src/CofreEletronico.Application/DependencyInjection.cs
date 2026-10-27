using CofreEletronico.Application.Autenticacao;
using CofreEletronico.Application.Autorizacao;
using CofreEletronico.Application.Cofres;
using CofreEletronico.Application.Comandos;
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

        // Administração de cofres (T-0403, T-0405)
        services.AddScoped<CriarCofre>();
        services.AddScoped<ListarCofres>();
        services.AddScoped<ObterCofre>();
        services.AddScoped<AtualizarCofre>();
        services.AddScoped<DesativarCofre>();
        services.AddScoped<ReativarCofre>();
        services.AddScoped<ColocarCofreEmManutencao>();
        services.AddScoped<RetirarCofreDaManutencao>();

        // Comandos MQTT de abertura (T-0602)
        services.AddScoped<EnviarComandoAbertura>();

        return services;
    }
}
