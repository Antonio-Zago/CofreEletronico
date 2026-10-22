using CofreEletronico.Api.Autorizacao;
using CofreEletronico.Application.Autorizacao;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;

namespace CofreEletronico.Api.Tests.Infra;

/// <summary>
/// Confere, nos metadados de cada endpoint, que toda rota cujo padrão começa com <c>/admin</c> exige a política Admin
/// (por nome, via <see cref="IAuthorizeData"/>, ou por uma <see cref="AuthorizationPolicy"/> que exige o papel Admin)
/// e não tem <see cref="IAllowAnonymous"/>.
/// </summary>
internal static class VerificadorRotasAdmin
{
    /// <summary>Rotas cujo primeiro segmento é o literal <c>admin</c> (sem diferenciar caixa, como o roteamento).</summary>
    public static IReadOnlyList<RouteEndpoint> RotasAdmin(IEnumerable<Endpoint> endpoints) =>
        endpoints.OfType<RouteEndpoint>().Where(EhRotaAdmin).ToList();

    /// <summary>Uma descrição por rota /admin irregular; lista vazia quando todas estão protegidas.</summary>
    public static IReadOnlyList<string> Violacoes(IEnumerable<Endpoint> endpoints)
    {
        var violacoes = new List<string>();
        foreach (var rota in RotasAdmin(endpoints))
        {
            var nome = $"{rota.RoutePattern.RawText} ({rota.DisplayName})";

            if (rota.Metadata.OfType<IAllowAnonymous>().Any())
                violacoes.Add($"{nome}: tem AllowAnonymous");

            if (!ExigePoliticaAdmin(rota.Metadata))
                violacoes.Add($"{nome}: não exige a política {Politicas.Admin}");
        }

        return violacoes;
    }

    /// <summary>
    /// Rotas que podem atender uma URL <c>/admin/...</c> sem ter o literal <c>admin</c> como primeiro segmento, e por
    /// isso escapariam de <see cref="Violacoes"/>: primeiro segmento com parâmetro (<c>/{area}/...</c>, opcional,
    /// catch-all <c>/{**x}</c>), primeiro segmento composto (<c>admin{sufixo}</c>, <c>{a}.{b}</c>) e endpoints de
    /// fallback (<c>MapFallback</c>, ordem <see cref="int.MaxValue"/>). Exceções só por <paramref name="permitidos"/>
    /// (padrões <c>RawText</c> exatos), hoje vazia.
    /// </summary>
    public static IReadOnlyList<string> RotasDeCapturaAmpla(
        IEnumerable<Endpoint> endpoints, IReadOnlyCollection<string>? permitidos = null)
    {
        var achados = new List<string>();
        foreach (var rota in endpoints.OfType<RouteEndpoint>())
        {
            var padrao = rota.RoutePattern.RawText ?? string.Empty;
            if (permitidos?.Contains(padrao) == true)
                continue;

            var nome = $"{padrao} ({rota.DisplayName})";
            var primeiro = rota.RoutePattern.PathSegments.FirstOrDefault();

            if (rota.Order == int.MaxValue)
                achados.Add($"{nome}: endpoint de fallback");
            else if (primeiro is null)
                continue; // só a raiz "/": não atende /admin/...
            else if (primeiro.Parts.Count > 1)
                achados.Add($"{nome}: primeiro segmento composto");
            else if (primeiro.Parts[0] is RoutePatternParameterPart)
                achados.Add($"{nome}: primeiro segmento é parâmetro");
        }

        return achados;
    }

    private static bool EhRotaAdmin(RouteEndpoint rota)
    {
        var primeiro = rota.RoutePattern.PathSegments.FirstOrDefault();
        if (primeiro is { Parts: [RoutePatternLiteralPart literal] }
            && string.Equals(literal.Content, "admin", StringComparison.OrdinalIgnoreCase))
            return true;

        // Redundância: padrão textual (ex.: "admin/x" sem barra inicial, ou "/admin" em caixa diferente).
        var texto = (rota.RoutePattern.RawText ?? string.Empty).TrimStart('/', '~');
        return texto.Equals("admin", StringComparison.OrdinalIgnoreCase)
            || texto.StartsWith("admin/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ExigePoliticaAdmin(EndpointMetadataCollection metadados)
    {
        // RequireAuthorization(Politicas.Admin) gera um IAuthorizeData com Policy = "Admin". Vários IAuthorizeData
        // são combinados com E pelo middleware: um a mais nunca enfraquece o Admin.
        if (metadados.OfType<IAuthorizeData>().Any(d => d.Policy == Politicas.Admin))
            return true;

        // RequireAuthorization(policy) com objeto: aceita só se a política exigir usuário autenticado com o papel Admin.
        return metadados.OfType<AuthorizationPolicy>().Any(politica =>
            politica.Requirements.OfType<DenyAnonymousAuthorizationRequirement>().Any()
            && politica.Requirements.OfType<RolesAuthorizationRequirement>()
                .Any(r => r.AllowedRoles.Count() == 1 && r.AllowedRoles.Single() == Papeis.Admin));
    }
}
