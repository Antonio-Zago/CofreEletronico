using CofreEletronico.Api.Autenticacao;
using CofreEletronico.Api.Autorizacao;
using CofreEletronico.Api.Endpoints.Admin.Cofres;
using CofreEletronico.Api.Endpoints.Admin.Estantes;
using Microsoft.AspNetCore.Mvc;

namespace CofreEletronico.Api.Endpoints.Admin;

/// <summary>
/// Ponto único das rotas administrativas: o grupo <c>/admin</c>, com a política <see cref="Politicas.Admin"/>
/// aplicada no próprio grupo (sem token: 401; sem o papel Admin: 403).
/// </summary>
/// <remarks>
/// Toda rota administrativa é registrada dentro deste grupo, por um método de extensão sobre o
/// <see cref="RouteGroupBuilder"/> admin chamado em <see cref="MapAdminEndpoints"/>
/// (ex.: <c>grupo.MapEstantesAdmin()</c> na T-0402). Nunca crie rota <c>/admin</c> fora dele nem use
/// <c>AllowAnonymous()</c> dentro dele: o teste <c>RotasAdminTests</c> percorre todos os endpoints da Api e falha.
/// </remarks>
public static class AdminEndpoints
{
    public const string Prefixo = "/admin";

    /// <summary>Cria o grupo <c>/admin</c> e registra nele os sub-grupos administrativos. Devolve o grupo.</summary>
    public static RouteGroupBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup(Prefixo)
            .RequireAuthorization(Politicas.Admin)
            .WithTags("Administração")
            .WithOpenApi();

        // Respostas comuns a todo o grupo (ProblemDetails dos eventos do JwtBearer). ProducesProblem só existe para
        // endpoint, então o metadado equivalente é aplicado direto no grupo.
        grupo.WithMetadata(
            new ProducesResponseTypeMetadata(StatusCodes.Status401Unauthorized, typeof(ProblemDetails), ["application/problem+json"]),
            new ProducesResponseTypeMetadata(StatusCodes.Status403Forbidden, typeof(ProblemDetails), ["application/problem+json"]),
            new ProducesResponseTypeMetadata(StatusCodes.Status429TooManyRequests, typeof(ProblemDetails), ["application/problem+json"]));

        // Limite geral por administrador (RateLimit:Admin), aplicado pelo GlobalLimiter a todo endpoint do grupo.
        grupo.WithMetadata(LimiteAdminMetadata.Instancia);

        // Claim "sub" com Guid válido em toda rota admin (auditoria e rate limit dependem dela); senão, 401.
        grupo.AddEndpointFilter(AdminAutenticado.ExigirSubValidoAsync);

        // Sub-grupos administrativos (um método de extensão sobre o grupo por funcionalidade):
        grupo.MapEstantesAdmin(); // T-0402, /admin/estantes
        grupo.MapCofresAdmin(); // T-0403/T-0405, /admin/estantes/{estanteId}/cofres

        return grupo;
    }
}
