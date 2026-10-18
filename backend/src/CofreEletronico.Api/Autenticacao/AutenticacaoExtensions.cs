using System.Globalization;
using System.Threading.RateLimiting;
using CofreEletronico.Infrastructure.Autenticacao;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;

namespace CofreEletronico.Api.Autenticacao;

/// <summary>
/// Seção <c>RateLimit</c>: uma subseção por política (os testes usam valores baixos).
/// Padrões do contrato (docs/ARQUITETURA.md): credenciais 10/min e sessão 60/min, por IP.
/// </summary>
public sealed class RateLimitOpcoes
{
    public const string Secao = "RateLimit";

    /// <summary>Política de <c>/auth/registro</c> e <c>/auth/login</c> (contador somado entre os dois).</summary>
    public const string PoliticaCredenciais = "auth-credenciais";

    /// <summary>Política de <c>/auth/refresh</c> e <c>/auth/logout</c> (contador somado entre os dois).</summary>
    public const string PoliticaSessao = "auth-sessao";

    public JanelaFixaOpcoes AuthCredenciais { get; set; } = new() { Limite = 10 };

    public JanelaFixaOpcoes AuthSessao { get; set; } = new() { Limite = 60 };
}

public sealed class JanelaFixaOpcoes
{
    public int Limite { get; set; }

    public int JanelaSegundos { get; set; } = 60;

    public bool Valida => Limite > 0 && JanelaSegundos > 0;
}

public static class AutenticacaoExtensions
{
    /// <summary>
    /// JwtBearer validando emissor, audiência, tempo de vida, assinatura e algoritmo (só HS256).
    /// Os parâmetros vêm de <see cref="JwtOpcoes"/> de forma lazy, então montar o host (inclusive no
    /// `dotnet ef`) não exige a chave; a subida da Api exige (ValidateOnStart em AddInfrastructure).
    /// </summary>
    public static IServiceCollection AddAutenticacaoJwt(this IServiceCollection services)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();

        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOpcoes>>((options, jwt) =>
            {
                // Mantém "sub", "email" e "role" como vieram no token (sem os tipos longos do .NET).
                options.MapInboundClaims = false;
                options.TokenValidationParameters = jwt.Value.CriarParametrosValidacao();
            });

        // Políticas por papel (403) entram na T-0303.
        services.AddAuthorization();

        return services;
    }

    /// <summary>
    /// Rate limiter nativo com duas políticas de janela fixa, particionadas por IP (IPv6 por /64, ver
    /// <see cref="ChaveRateLimit"/>). Cada política tem contadores próprios: estourar a de credenciais
    /// não bloqueia refresh/logout. 429 com <c>Retry-After</c> nas duas.
    /// </summary>
    public static IServiceCollection AddRateLimitAuth(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<RateLimitOpcoes>()
            .Bind(configuration.GetSection(RateLimitOpcoes.Secao))
            .Validate(o => o.AuthCredenciais.Valida,
                "RateLimit:AuthCredenciais:Limite e RateLimit:AuthCredenciais:JanelaSegundos devem ser positivos.")
            .Validate(o => o.AuthSessao.Valida,
                "RateLimit:AuthSessao:Limite e RateLimit:AuthSessao:JanelaSegundos devem ser positivos.")
            .ValidateOnStart();

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.AddPolicy(RateLimitOpcoes.PoliticaCredenciais, httpContext =>
                JanelaPorIp(httpContext, Opcoes(httpContext).AuthCredenciais));
            options.AddPolicy(RateLimitOpcoes.PoliticaSessao, httpContext =>
                JanelaPorIp(httpContext, Opcoes(httpContext).AuthSessao));

            options.OnRejected = async (contexto, cancellationToken) =>
            {
                // A janela fixa informa quanto falta para reabrir; se não informar, usa a maior janela.
                var segundos = contexto.Lease.TryGetMetadata(MetadataName.RetryAfter, out var esperar)
                    ? (int)Math.Ceiling(esperar.TotalSeconds)
                    : Math.Max(Opcoes(contexto.HttpContext).AuthCredenciais.JanelaSegundos, Opcoes(contexto.HttpContext).AuthSessao.JanelaSegundos);
                contexto.HttpContext.Response.Headers.RetryAfter = Math.Max(segundos, 1).ToString(CultureInfo.InvariantCulture);

                await Results.Problem(
                        statusCode: StatusCodes.Status429TooManyRequests,
                        title: "Muitas requisições",
                        detail: "Limite de tentativas excedido. Aguarde o tempo indicado em Retry-After e tente novamente.")
                    .ExecuteAsync(contexto.HttpContext);
            };
        });

        return services;
    }

    private static RateLimitOpcoes Opcoes(HttpContext httpContext) =>
        httpContext.RequestServices.GetRequiredService<IOptions<RateLimitOpcoes>>().Value;

    private static RateLimitPartition<string> JanelaPorIp(HttpContext httpContext, JanelaFixaOpcoes limites)
    {
        // ATENÇÃO: atrás de proxy reverso o RemoteIpAddress é o IP do proxy e todos os clientes
        // dividiriam o mesmo limite. Antes de publicar atrás de proxy, configure ForwardedHeaders
        // com a lista de proxies confiáveis (T-1203).
        var chave = ChaveRateLimit.Gerar(httpContext.Connection.RemoteIpAddress);

        return RateLimitPartition.GetFixedWindowLimiter(chave, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = limites.Limite,
            Window = TimeSpan.FromSeconds(limites.JanelaSegundos),
            QueueLimit = 0,
        });
    }

    /// <summary>Swagger com o esquema Bearer (botão Authorize para colar o access token).</summary>
    public static IServiceCollection AddSwaggerComBearer(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            var esquema = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description = "Access token obtido em /auth/login ou /auth/registro.",
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" },
            };

            options.AddSecurityDefinition("Bearer", esquema);
            options.AddSecurityRequirement(new OpenApiSecurityRequirement { [esquema] = [] });
        });

        return services;
    }
}
