using CofreEletronico.Application.Autenticacao.Portas;
using CofreEletronico.Infrastructure.Autenticacao;
using CofreEletronico.Infrastructure.Persistencia;
using CofreEletronico.Infrastructure.Persistencia.Identity;
using CofreEletronico.Infrastructure.Persistencia.Repositorios;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace CofreEletronico.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registra as implementações de infraestrutura (persistência, MQTT, Mercado Pago, jobs).
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);

        // A string "Cofre" vem de user-secrets ou da variável ConnectionStrings__Cofre (nunca de appsettings).
        // O AddDbContext é lazy: sem a string, a Api sobe; só falha ao abrir conexão com o banco.
        services.AddDbContext<CofreDbContext>(options =>
            options
                .UseNpgsql(configuration.GetConnectionString(PersistenciaExtensions.ConnectionStringNome))
                .UseSnakeCaseNamingConvention());

        // Só o núcleo do Identity (UserManager/RoleManager + stores EF), sem SignInManager nem token providers:
        // o login com lockout é feito à mão em ServicoIdentidade.
        services
            .AddIdentityCore<Usuario>(PoliticaIdentity.Configurar)
            .AddRoles<Papel>()
            .AddEntityFrameworkStores<CofreDbContext>()
            .AddErrorDescriber<DescritorErrosIdentityPtBr>();

        AddAutenticacao(services, configuration);

        return services;
    }

    private static void AddAutenticacao(IServiceCollection services, IConfiguration configuration)
    {
        // ValidateOnStart: sem Jwt:ChaveAssinatura (ou com menos de 32 bytes) a Api não sobe.
        // O `dotnet ef` não chega a iniciar o host, então continua funcionando sem a chave.
        services.AddOptions<JwtOpcoes>()
            .Bind(configuration.GetSection(JwtOpcoes.Secao))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<JwtOpcoes>, JwtOpcoesValidador>();

        services.AddSingleton<IGeradorAccessToken, GeradorAccessTokenJwt>();
        services.AddSingleton<IGeradorRefreshToken, GeradorRefreshTokenSeguro>();
        services.AddSingleton<VerificacaoSenhaFicticia>();
        services.AddHostedService<PreaquecimentoVerificacaoSenha>();
        services.AddScoped<IServicoIdentidade, ServicoIdentidade>();
        services.AddScoped<IRefreshTokenRepositorio, RefreshTokenRepositorio>();
    }
}
