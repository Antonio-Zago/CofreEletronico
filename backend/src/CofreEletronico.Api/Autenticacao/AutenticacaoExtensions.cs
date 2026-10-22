using System.Globalization;
using System.Threading.RateLimiting;
using CofreEletronico.Api.Autorizacao;
using CofreEletronico.Application.Autorizacao;
using CofreEletronico.Infrastructure.Autenticacao;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;

namespace CofreEletronico.Api.Autenticacao;

/// <summary>
/// Seção <c>RateLimit</c>: uma subseção por política (os testes usam valores baixos).
/// Padrões do contrato (docs/ARQUITETURA.md): credenciais 10/min e sessão 60/min, por IP;
/// administração 120/min e operações sensíveis de administração 10/min, por administrador (claim <c>sub</c>).
/// </summary>
public sealed class RateLimitOpcoes
{
    public const string Secao = "RateLimit";

    /// <summary>Política de <c>/auth/registro</c> e <c>/auth/login</c> (contador somado entre os dois).</summary>
    public const string PoliticaCredenciais = "auth-credenciais";

    /// <summary>Política de <c>/auth/refresh</c> e <c>/auth/logout</c> (contador somado entre os dois).</summary>
    public const string PoliticaSessao = "auth-sessao";

    /// <summary>
    /// Política das rotas admin que geram credenciais (<c>POST /admin/estantes</c> e
    /// <c>POST /admin/estantes/{id}/credenciais</c>, contador somado), além do limite geral <see cref="Admin"/>.
    /// </summary>
    public const string PoliticaAdminSensivel = "admin-sensivel";

    public JanelaFixaOpcoes AuthCredenciais { get; set; } = new() { Limite = 10 };

    public JanelaFixaOpcoes AuthSessao { get; set; } = new() { Limite = 60 };

    /// <summary>Limite geral de todas as rotas <c>/admin</c>, por administrador (GlobalLimiter, ver <see cref="LimiteAdminMetadata"/>).</summary>
    public JanelaFixaOpcoes Admin { get; set; } = new() { Limite = 120 };

    /// <summary>Limite de <see cref="PoliticaAdminSensivel"/>, por administrador.</summary>
    public JanelaFixaOpcoes AdminSensivel { get; set; } = new() { Limite = 10 };

    /// <summary>Maior janela configurada (Retry-After quando o limitador não informa o tempo).</summary>
    public int MaiorJanelaSegundos =>
        new[] { AuthCredenciais, AuthSessao, Admin, AdminSensivel }.Max(j => j.JanelaSegundos);
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
                options.Events = new JwtBearerEvents
                {
                    OnChallenge = EscreverNaoAutorizadoAsync,
                    OnForbidden = EscreverAcessoNegadoAsync,
                };
            });

        services.AddAuthorizationBuilder()
            // Todo endpoint sem metadado de autorização exige login. Rotas públicas: AllowAnonymous() explícito.
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
            .AddPolicy(Politicas.Admin, politica => politica.RequireAuthenticatedUser().RequireRole(Papeis.Admin));

        return services;
    }

    /// <summary>
    /// 401 (sem token ou token inválido) como ProblemDetails, no mesmo formato dos 401 de /auth.
    /// O motivo da recusa não vai para o corpo nem para o WWW-Authenticate (só <c>invalid_token</c>).
    /// </summary>
    private static Task EscreverNaoAutorizadoAsync(JwtBearerChallengeContext contexto)
    {
        contexto.HandleResponse(); // substitui a resposta padrão (corpo vazio)
        contexto.Response.Headers.WWWAuthenticate = contexto.AuthenticateFailure is null
            ? JwtBearerDefaults.AuthenticationScheme
            : $"{JwtBearerDefaults.AuthenticationScheme} error=\"invalid_token\"";

        return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Não autorizado",
                detail: "Autenticação necessária. Envie um access token válido no cabeçalho Authorization (Bearer).")
            .ExecuteAsync(contexto.HttpContext);
    }

    /// <summary>403 (autenticado, mas sem o papel exigido) como ProblemDetails.</summary>
    private static Task EscreverAcessoNegadoAsync(ForbiddenContext contexto) =>
        Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Acesso negado",
                detail: "Você não tem permissão para acessar este recurso.")
            .ExecuteAsync(contexto.HttpContext);

    /// <summary>
    /// Rate limiter nativo. /auth: duas políticas de janela fixa, particionadas por IP (IPv6 por /64, ver
    /// <see cref="ChaveRateLimit"/>). Cada política tem contadores próprios: estourar a de credenciais
    /// não bloqueia refresh/logout. /admin: limite geral (GlobalLimiter) e política sensível, por administrador.
    /// 429 com <c>Retry-After</c> em todas.
    /// </summary>
    public static IServiceCollection AddRateLimitAuth(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<RateLimitOpcoes>()
            .Bind(configuration.GetSection(RateLimitOpcoes.Secao))
            .Validate(o => o.AuthCredenciais.Valida,
                "RateLimit:AuthCredenciais:Limite e RateLimit:AuthCredenciais:JanelaSegundos devem ser positivos.")
            .Validate(o => o.AuthSessao.Valida,
                "RateLimit:AuthSessao:Limite e RateLimit:AuthSessao:JanelaSegundos devem ser positivos.")
            .Validate(o => o.Admin.Valida,
                "RateLimit:Admin:Limite e RateLimit:Admin:JanelaSegundos devem ser positivos.")
            .Validate(o => o.AdminSensivel.Valida,
                "RateLimit:AdminSensivel:Limite e RateLimit:AdminSensivel:JanelaSegundos devem ser positivos.")
            .ValidateOnStart();

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.AddPolicy(RateLimitOpcoes.PoliticaCredenciais, httpContext =>
                JanelaPorIp(httpContext, Opcoes(httpContext).AuthCredenciais));
            options.AddPolicy(RateLimitOpcoes.PoliticaSessao, httpContext =>
                JanelaPorIp(httpContext, Opcoes(httpContext).AuthSessao));

            // Administração: por administrador (o UseRateLimiter vem depois do UseAuthorization, então só chegam
            // aqui requisições já autenticadas como Admin). O geral vale para todo endpoint marcado com
            // LimiteAdminMetadata (grupo /admin); o sensível soma-se a ele nas rotas que geram credenciais.
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
                httpContext.GetEndpoint()?.Metadata.GetMetadata<LimiteAdminMetadata>() is null
                    ? RateLimitPartition.GetNoLimiter(string.Empty)
                    : JanelaPorUsuario(httpContext, Opcoes(httpContext).Admin));
            options.AddPolicy(RateLimitOpcoes.PoliticaAdminSensivel, httpContext =>
                JanelaPorUsuario(httpContext, Opcoes(httpContext).AdminSensivel));

            options.OnRejected = async (contexto, cancellationToken) =>
            {
                // A janela fixa informa quanto falta para reabrir; se não informar, usa a maior janela.
                var segundos = contexto.Lease.TryGetMetadata(MetadataName.RetryAfter, out var esperar)
                    ? (int)Math.Ceiling(esperar.TotalSeconds)
                    : Opcoes(contexto.HttpContext).MaiorJanelaSegundos;
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

    /// <summary>
    /// Janela fixa por usuário (claim <c>sub</c>). Sem <c>sub</c> (não deveria acontecer nas rotas admin, que exigem
    /// autenticação), cai na partição do IP, para nunca juntar todos num contador só nem ficar sem limite.
    /// </summary>
    private static RateLimitPartition<string> JanelaPorUsuario(HttpContext httpContext, JanelaFixaOpcoes limites)
    {
        var sub = httpContext.User.FindFirst("sub")?.Value;
        var chave = string.IsNullOrEmpty(sub)
            ? "ip:" + ChaveRateLimit.Gerar(httpContext.Connection.RemoteIpAddress)
            : "sub:" + sub;

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
