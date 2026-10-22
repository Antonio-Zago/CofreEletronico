using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CofreEletronico.Api.Autorizacao;
using CofreEletronico.Api.Endpoints.Admin;
using CofreEletronico.Api.Tests.Infra;
using CofreEletronico.Application.Autenticacao.Portas;
using CofreEletronico.Application.Autorizacao;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace CofreEletronico.Api.Tests.Admin;

/// <summary>
/// T-0406: toda rota /admin exige a política Admin. O verificador percorre o EndpointDataSource da Api real, então
/// continua valendo quando as próximas tarefas (T-0402 em diante) registrarem rotas no grupo de MapAdminEndpoints.
/// </summary>
public class RotasAdminTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _cliente = factory.CreateClient();

    private IReadOnlyList<Endpoint> EndpointsDaApi() =>
        factory.Services.GetRequiredService<EndpointDataSource>().Endpoints;

    private string Token(params string[] papeis) =>
        factory.Services.GetRequiredService<IGeradorAccessToken>()
            .Gerar(new UsuarioAutenticado(Guid.NewGuid(), "alguem@exemplo.com", papeis))
            .Token;

    private async Task<HttpResponseMessage> Get(string rota, string? token)
    {
        using var requisicao = new HttpRequestMessage(HttpMethod.Get, rota);
        if (token is not null)
            requisicao.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _cliente.SendAsync(requisicao);
    }

    // ---------- garantia sobre os endpoints reais ----------

    [Fact]
    public void Toda_rota_admin_da_Api_exige_a_politica_Admin()
    {
        var endpoints = EndpointsDaApi();

        // Não passa "no vazio": a rota de teste montada pelo MapAdminEndpoints está entre as rotas verificadas.
        var rotasAdmin = VerificadorRotasAdmin.RotasAdmin(endpoints);
        Assert.Contains(rotasAdmin, r => r.RoutePattern.RawText == ApiFactory.PadraoRotaTesteAdmin);

        var violacoes = VerificadorRotasAdmin.Violacoes(endpoints);
        Assert.True(violacoes.Count == 0, "Rotas /admin sem a política Admin:\n" + string.Join('\n', violacoes));

        // Rotas que atenderiam /admin/... sem passar pela verificação acima (parâmetro/catch-all no primeiro
        // segmento, segmento composto, fallback). Lista de permitidos explícita; hoje vazia.
        var capturaAmpla = VerificadorRotasAdmin.RotasDeCapturaAmpla(endpoints, RotasDeCapturaAmplaPermitidas);
        Assert.True(capturaAmpla.Count == 0, "Rotas que podem atender /admin/...:\n" + string.Join('\n', capturaAmpla));
    }

    /// <summary>Padrões (RawText) autorizados a ter parâmetro no primeiro segmento. Inclua só com revisão de segurança.</summary>
    private static readonly string[] RotasDeCapturaAmplaPermitidas = [];

    [Fact]
    public async Task Verificador_detecta_rotas_de_captura_ampla()
    {
        var builder = WebApplication.CreateBuilder();
        await using var app = builder.Build();

        app.MapPost("/{**resto}", () => "x"); // métodos diferentes só para o analisador ASP0022 não acusar conflito
        app.MapGet("/{area}/painel", () => "x");
        app.MapGet("/{id:int}", () => "x");
        app.MapDelete("/{opcional?}", () => "x");
        app.MapGet("/admin{sufixo}/x", () => "x");
        app.MapGet("/{a}.{b}", () => "x");
        app.MapFallback(() => "x");
        app.MapGet("/permitida/{x}", () => "x"); // controle: parâmetro fora do primeiro segmento
        app.MapGet("/{liberada}/z", () => "x");  // controle: na lista de permitidos do teste

        // Controles que não podem ser acusados.
        app.MapGet("/", () => "x");
        app.MapGet("/ping", () => "x");
        app.MapGet("/admin/estantes/{id:guid}", () => "x");

        var endpoints = ((IEndpointRouteBuilder)app).DataSources.SelectMany(d => d.Endpoints).ToList();
        var achados = VerificadorRotasAdmin.RotasDeCapturaAmpla(endpoints, ["/{liberada}/z"]);

        string[] esperados = ["/{**resto}", "/{area}/painel", "/{id:int}", "/{opcional?}", "/admin{sufixo}/x", "/{a}.{b}"];
        foreach (var padrao in esperados)
            Assert.Contains(achados, a => a.StartsWith(padrao + " ", StringComparison.Ordinal));
        Assert.Contains(achados, a => a.Contains("fallback", StringComparison.Ordinal));

        Assert.Equal(esperados.Length + 1, achados.Count);
        Assert.DoesNotContain(achados, a => a.Contains("liberada", StringComparison.Ordinal));
        Assert.DoesNotContain(achados, a => a.Contains("permitida", StringComparison.Ordinal));
        Assert.DoesNotContain(achados, a => a.Contains("/ping", StringComparison.Ordinal));
        Assert.DoesNotContain(achados, a => a.StartsWith("/admin/", StringComparison.Ordinal));
    }

    [Fact]
    public void Grupo_admin_aplica_a_politica_Admin_e_as_tags_de_OpenAPI()
    {
        var rota = Assert.Single(VerificadorRotasAdmin.RotasAdmin(EndpointsDaApi()),
            r => r.RoutePattern.RawText == ApiFactory.PadraoRotaTesteAdmin);

        // Política vinda do grupo (a rota de teste não declara nada sozinha).
        Assert.Contains(rota.Metadata.OfType<IAuthorizeData>(), d => d.Policy == Politicas.Admin);
        Assert.Empty(rota.Metadata.OfType<IAllowAnonymous>());
        Assert.Contains(rota.Metadata.OfType<ITagsMetadata>(), t => t.Tags.Contains("Administração"));
        Assert.Contains(rota.Metadata.OfType<IProducesResponseTypeMetadata>(), p => p.StatusCode == StatusCodes.Status401Unauthorized);
        Assert.Contains(rota.Metadata.OfType<IProducesResponseTypeMetadata>(), p => p.StatusCode == StatusCodes.Status403Forbidden);
    }

    [Fact]
    public void Prefixo_do_grupo_e_admin()
    {
        Assert.Equal("/admin", AdminEndpoints.Prefixo);
    }

    // ---------- o verificador detecta rotas mal registradas ----------

    [Fact]
    public async Task Verificador_detecta_rota_admin_mal_registrada()
    {
        var builder = WebApplication.CreateBuilder();
        await using var app = builder.Build();

        app.MapGet("/admin/sem-autorizacao", () => "x");
        app.MapGet("/admin/so-autenticado", () => "x").RequireAuthorization();
        app.MapGet("/admin/outra-politica", () => "x").RequireAuthorization("OutraPolitica");
        app.MapGet("/Admin/caixa-diferente", () => "x");
        app.MapGet("admin/sem-barra", () => "x");
        app.MapGet("/admin", () => "x");
        app.MapAdminEndpoints().MapGet("/anonima-no-grupo", () => "x").AllowAnonymous();
        app.MapGet("/admin/politica-e-anonima", () => "x").RequireAuthorization(Politicas.Admin).AllowAnonymous();
        app.MapGet("/admin/papel-errado", () => "x")
            .RequireAuthorization(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().RequireRole(Papeis.Usuario).Build());

        // Controles que não podem ser acusados.
        app.MapAdminEndpoints().MapGet("/correta", () => "x");
        app.MapGet("/admin/politica-por-objeto", () => "x")
            .RequireAuthorization(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().RequireRole(Papeis.Admin).Build());
        app.MapGet("/administracao-nao-e-admin", () => "x");
        app.MapGet("/ping", () => "x").AllowAnonymous();

        var endpoints = ((IEndpointRouteBuilder)app).DataSources.SelectMany(d => d.Endpoints).ToList();
        var violacoes = VerificadorRotasAdmin.Violacoes(endpoints);

        string[] esperadas =
        [
            "/admin/sem-autorizacao", "/admin/so-autenticado", "/admin/outra-politica", "/Admin/caixa-diferente",
            "admin/sem-barra", "/admin", "/admin/anonima-no-grupo", "/admin/politica-e-anonima", "/admin/papel-errado",
        ];
        foreach (var rota in esperadas)
            Assert.Contains(violacoes, v => v.StartsWith(rota + " ", StringComparison.Ordinal));

        Assert.Equal(esperadas.Length, violacoes.Count);
        Assert.DoesNotContain(violacoes, v => v.Contains("/correta", StringComparison.Ordinal));
        Assert.DoesNotContain(violacoes, v => v.Contains("politica-por-objeto", StringComparison.Ordinal));
        Assert.DoesNotContain(violacoes, v => v.Contains("administracao", StringComparison.Ordinal));
    }

    // ---------- HTTP na rota de teste do grupo admin ----------

    [Fact]
    public async Task Rota_do_grupo_admin_sem_token_devolve_401_ProblemDetails()
    {
        var resposta = await Get(ApiFactory.RotaTesteGrupoAdmin, null);

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
        Assert.Equal("application/problem+json", resposta.Content.Headers.ContentType?.MediaType);
        Assert.Equal("Bearer", resposta.Headers.WwwAuthenticate.ToString());
    }

    [Fact]
    public async Task Rota_do_grupo_admin_com_papel_Usuario_devolve_403_ProblemDetails()
    {
        var resposta = await Get(ApiFactory.RotaTesteGrupoAdmin, Token(Papeis.Usuario));

        Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
        Assert.Equal("application/problem+json", resposta.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Rota_do_grupo_admin_com_papel_Admin_devolve_200()
    {
        var resposta = await Get(ApiFactory.RotaTesteGrupoAdmin, Token(Papeis.Admin));

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var corpo = await resposta.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(Papeis.Admin, corpo.GetProperty("papeis").EnumerateArray().Select(p => p.GetString()));
    }

    [Fact]
    public async Task Rota_de_teste_do_grupo_admin_nao_vaza_para_o_roteador_principal()
    {
        // O ramo de teste tem roteador próprio: quando a T-0402 trouxer rotas reais, elas não ficam ambíguas.
        var resposta = await Get(ApiFactory.PadraoRotaTesteAdmin, Token(Papeis.Admin));

        Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
    }
}
