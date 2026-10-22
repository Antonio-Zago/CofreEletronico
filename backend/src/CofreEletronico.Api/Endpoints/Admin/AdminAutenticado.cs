using System.Security.Claims;

namespace CofreEletronico.Api.Endpoints.Admin;

/// <summary>
/// Identidade do administrador nas rotas <c>/admin</c>: a claim <c>sub</c> tem de ser um Guid válido. É o que
/// identifica o admin nos logs de auditoria e no rate limit; sem ela, a requisição é recusada com 401 (nunca
/// registrada como <see cref="Guid.Empty"/>).
/// </summary>
public static class AdminAutenticado
{
    public const string ClaimSub = "sub";

    /// <summary>Endpoint filter do grupo admin: <c>sub</c> ausente ou inválido → 401 em ProblemDetails + warning.</summary>
    public static async ValueTask<object?> ExigirSubValidoAsync(EndpointFilterInvocationContext contexto, EndpointFilterDelegate proximo)
    {
        if (TentarObterId(contexto.HttpContext.User, out _))
            return await proximo(contexto);

        // Sem o valor da claim no log: pode ser lixo arbitrário vindo de um token assinado com a nossa chave.
        contexto.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(AdminAutenticado).FullName!)
            .LogWarning(
                "Token aceito na rota admin {Rota} sem claim 'sub' com Guid válido. Requisição recusada (401).",
                contexto.HttpContext.Request.Path.Value);

        return Results.Problem(
            statusCode: StatusCodes.Status401Unauthorized,
            title: "Não autorizado",
            detail: "Autenticação necessária. Envie um access token válido no cabeçalho Authorization (Bearer).");
    }

    /// <summary>Id do admin. Só use depois do filtro <see cref="ExigirSubValidoAsync"/> (garante que existe).</summary>
    public static Guid Id(ClaimsPrincipal usuario) =>
        TentarObterId(usuario, out var id)
            ? id
            : throw new InvalidOperationException("Rota admin executada sem o filtro de 'sub' válido.");

    private static bool TentarObterId(ClaimsPrincipal usuario, out Guid id) =>
        Guid.TryParse(usuario.FindFirstValue(ClaimSub), out id) && id != Guid.Empty;
}
