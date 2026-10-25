using CofreEletronico.Application.Autenticacao.Portas;
using CofreEletronico.Application.Cofres.Portas;
using CofreEletronico.Application.Estantes.Portas;
using CofreEletronico.Infrastructure.Autenticacao;
using CofreEletronico.Infrastructure.Autorizacao;
using CofreEletronico.Infrastructure.Criptografia;
using CofreEletronico.Infrastructure.Estantes;
using CofreEletronico.Infrastructure.Persistencia;
using CofreEletronico.Infrastructure.Persistencia.Identity;
using CofreEletronico.Infrastructure.Persistencia.Repositorios;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
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

        AddCriptografia(services, configuration);

        // A string "Cofre" vem de user-secrets ou da variável ConnectionStrings__Cofre (nunca de appsettings).
        // O AddDbContext é lazy: sem a string, a Api sobe; só falha ao abrir conexão com o banco.
        services.AddDbContext<CofreDbContext>(options =>
            options
                .UseNpgsql(configuration.GetConnectionString(PersistenciaExtensions.ConnectionStringNome))
                .UseSnakeCaseNamingConvention()
                // O modelo depende do cifrador injetado no contexto (conversor de estantes.segredo_hmac).
                .ReplaceService<IModelCacheKeyFactory, CofreModelCacheKeyFactory>());

        // Só o núcleo do Identity (UserManager/RoleManager + stores EF), sem SignInManager nem token providers:
        // o login com lockout é feito à mão em ServicoIdentidade.
        services
            .AddIdentityCore<Usuario>(PoliticaIdentity.Configurar)
            .AddRoles<Papel>()
            .AddEntityFrameworkStores<CofreDbContext>()
            .AddErrorDescriber<DescritorErrosIdentityPtBr>();

        AddAutenticacao(services, configuration);
        AddAdminInicial(services, configuration);
        AddEstantes(services);

        return services;
    }

    private static void AddCriptografia(IServiceCollection services, IConfiguration configuration)
    {
        // ValidateOnStart: sem Criptografia:ChaveSegredos válida (base64 de 32 bytes) a Api não sobe.
        // O cifrador só lê a chave ao cifrar/decifrar, então o `dotnet ef` funciona sem ela.
        services.AddOptions<CriptografiaOpcoes>()
            .Bind(configuration.GetSection(CriptografiaOpcoes.Secao))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<CriptografiaOpcoes>, CriptografiaOpcoesValidador>();

        // Tem de ser SINGLETON: o cache de modelo do EF usa a instância do cifrador como chave
        // (CofreModelCacheKeyFactory). Como scoped/transient, cada contexto geraria e guardaria um modelo novo
        // (vazamento de memória e custo de montar o modelo a cada requisição). Há teste garantindo o tempo de vida.
        services.AddSingleton<ICifradorSegredos, CifradorSegredosAesGcm>();
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

    private static void AddEstantes(IServiceCollection services)
    {
        // Sem estado: singleton. O repositório usa o DbContext da requisição: scoped.
        services.AddSingleton<IGeradorCredenciaisEstante, GeradorCredenciaisEstanteSeguro>();
        services.AddScoped<IEstanteRepositorio, EstanteRepositorio>();
        services.AddScoped<ICofreRepositorio, CofreRepositorio>();
    }

    private static void AddAdminInicial(IServiceCollection services, IConfiguration configuration)
    {
        // Opcional (sem e-mail e senha, nada é feito); se configurada, inválida impede a subida.
        // Lida de user-secrets ou de AdminInicial__Email/AdminInicial__Senha (nunca de appsettings).
        services.AddOptions<AdminInicialOpcoes>()
            .Bind(configuration.GetSection(AdminInicialOpcoes.Secao))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<AdminInicialOpcoes>, AdminInicialOpcoesValidador>();
    }
}
