using CofreEletronico.Api.Autenticacao;
using CofreEletronico.Application.Autenticacao;

namespace CofreEletronico.Api.Endpoints.Auth;

/// <summary>
/// Endpoints de /auth (contrato em docs/ARQUITETURA.md, "Contrato REST: autenticação").
/// Todos anônimos, com rate limit por IP: credenciais (registro/login) e sessão (refresh/logout).
/// </summary>
public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/auth")
            .WithTags("Autenticação")
            .AllowAnonymous()
            .WithOpenApi();

        grupo.MapPost("/registro", RegistrarAsync)
            .RequireRateLimiting(RateLimitOpcoes.PoliticaCredenciais)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .WithName("Registrar")
            .WithSummary("Cadastra o usuário (papel Usuario) e já devolve os tokens.")
            .Produces<TokensDto>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        grupo.MapPost("/login", LoginAsync)
            .RequireRateLimiting(RateLimitOpcoes.PoliticaCredenciais)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .WithName("Login")
            .WithSummary("Autentica com e-mail e senha.")
            .Produces<TokensDto>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        grupo.MapPost("/refresh", RefreshAsync)
            .RequireRateLimiting(RateLimitOpcoes.PoliticaSessao)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .WithName("Refresh")
            .WithSummary("Troca o refresh token por um novo par de tokens (rotação).")
            .Produces<TokensDto>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        grupo.MapPost("/logout", LogoutAsync)
            .RequireRateLimiting(RateLimitOpcoes.PoliticaSessao)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .WithName("Logout")
            .WithSummary("Revoga o refresh token. Idempotente.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem();

        return app;
    }

    private static async Task<IResult> RegistrarAsync(RegistroRequisicao? requisicao, RegistrarUsuario casoDeUso, CancellationToken cancellationToken)
    {
        var erros = ValidacaoAuth.Validar(requisicao);
        if (erros.Count > 0)
            return ProblemasAuth.Validacao(erros);

        var resultado = await casoDeUso.ExecutarAsync(
            new RegistrarUsuarioComando(requisicao!.Nome!, requisicao.Email!, requisicao.Senha!, requisicao.Telefone),
            cancellationToken);

        return resultado.Status == StatusAutenticacao.Sucesso
            ? Results.Created((string?)null, resultado.Tokens)
            : ProblemasAuth.Traduzir(resultado);
    }

    private static async Task<IResult> LoginAsync(LoginRequisicao? requisicao, AutenticarUsuario casoDeUso, CancellationToken cancellationToken)
    {
        var erros = ValidacaoAuth.Validar(requisicao);
        if (erros.Count > 0)
            return ProblemasAuth.Validacao(erros);

        var resultado = await casoDeUso.ExecutarAsync(new LoginComando(requisicao!.Email!, requisicao.Senha!), cancellationToken);

        return resultado.Status == StatusAutenticacao.Sucesso ? Results.Ok(resultado.Tokens) : ProblemasAuth.Traduzir(resultado);
    }

    private static async Task<IResult> RefreshAsync(RefreshTokenRequisicao? requisicao, RenovarTokens casoDeUso, CancellationToken cancellationToken)
    {
        var erros = ValidacaoAuth.Validar(requisicao);
        if (erros.Count > 0)
            return ProblemasAuth.Validacao(erros);

        var resultado = await casoDeUso.ExecutarAsync(new RenovarTokensComando(requisicao!.RefreshToken!), cancellationToken);

        return resultado.Status == StatusAutenticacao.Sucesso ? Results.Ok(resultado.Tokens) : ProblemasAuth.Traduzir(resultado);
    }

    private static async Task<IResult> LogoutAsync(RefreshTokenRequisicao? requisicao, EncerrarSessao casoDeUso, CancellationToken cancellationToken)
    {
        var erros = ValidacaoAuth.Validar(requisicao);
        if (erros.Count > 0)
            return ProblemasAuth.Validacao(erros);

        await casoDeUso.ExecutarAsync(new LogoutComando(requisicao!.RefreshToken!), cancellationToken);
        return Results.NoContent();
    }
}

/// <summary>Respostas de erro de /auth como ProblemDetails, com mensagens genéricas em português.</summary>
internal static class ProblemasAuth
{
    public static IResult Validacao(IDictionary<string, string[]> erros) =>
        Results.ValidationProblem(erros, title: "Dados inválidos.");

    public static IResult Traduzir(ResultadoAutenticacao resultado) => resultado.Status switch
    {
        StatusAutenticacao.Validacao => Validacao(resultado.Erros.ToDictionary(e => e.Key, e => e.Value)),

        // Sem ecoar o e-mail.
        StatusAutenticacao.EmailEmUso => Results.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "Cadastro não concluído",
            detail: "Já existe uma conta com este e-mail."),

        // Mesmo corpo para e-mail inexistente, senha errada e conta bloqueada.
        StatusAutenticacao.CredenciaisInvalidas => Results.Problem(
            statusCode: StatusCodes.Status401Unauthorized,
            title: "Não autorizado",
            detail: "E-mail ou senha inválidos."),

        StatusAutenticacao.TokenInvalido => Results.Problem(
            statusCode: StatusCodes.Status401Unauthorized,
            title: "Não autorizado",
            detail: "Sessão inválida ou expirada. Faça login novamente."),

        _ => throw new InvalidOperationException($"Status sem tradução HTTP: {resultado.Status}."),
    };
}
