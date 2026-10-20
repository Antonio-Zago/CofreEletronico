using System.Globalization;
using System.Security.Claims;
using CofreEletronico.Api.Autorizacao;
using CofreEletronico.Application.Autenticacao.Portas;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CofreEletronico.Api.Tests.Infra;

/// <summary>
/// Sobe a Api real (Program) em memória, trocando só as portas que tocam o banco por fakes.
/// Geradores de token, JwtBearer, rate limiter e endpoints são os de produção.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    /// <summary>Chave usada só nos testes (mais de 64 bytes, o bastante também para HS512). Não é segredo.</summary>
    public const string ChaveJwt = "chave-de-teste-da-api-com-mais-de-64-bytes-para-servir-ao-teste-de-hs512";

    /// <summary>Chave AES fictícia (bytes 0..31 em base64), só para os testes. Não é segredo.</summary>
    public const string ChaveSegredos = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=";

    public const string RotaProtegida = "/teste/protegido";

    public const string RotaAdmin = "/teste/admin";

    public const string RotaSemMetadado = "/teste/sem-metadado";

    /// <summary>Altos por padrão para não interferir; os testes de 429 criam uma fábrica com limites baixos.</summary>
    public int LimiteCredenciais { get; init; } = 10_000;

    public int LimiteSessao { get; init; } = 10_000;

    internal ServicoIdentidadeFalso Identidade { get; } = new();

    internal RefreshTokenRepositorioFalso Repositorio { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Fora de Development: sem user-secrets, sem migrations e sem Swagger.
        builder.UseEnvironment("Testing");
        builder.UseSetting("Jwt:ChaveAssinatura", ChaveJwt);
        builder.UseSetting("Criptografia:ChaveSegredos", ChaveSegredos);
        builder.UseSetting("RateLimit:AuthCredenciais:Limite", LimiteCredenciais.ToString(CultureInfo.InvariantCulture));
        builder.UseSetting("RateLimit:AuthSessao:Limite", LimiteSessao.ToString(CultureInfo.InvariantCulture));

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IServicoIdentidade>();
            services.AddSingleton<IServicoIdentidade>(Identidade);
            services.RemoveAll<IRefreshTokenRepositorio>();
            services.AddSingleton<IRefreshTokenRepositorio>(Repositorio);

            services.AddSingleton<IStartupFilter, EndpointProtegidoFiltro>();
        });
    }

    /// <summary>
    /// Endpoints que só existem nos testes, em ramos com o mesmo pipeline de autenticação e autorização da Api:
    /// <see cref="RotaProtegida"/> (RequireAuthorization), <see cref="RotaAdmin"/> (política Admin) e
    /// <see cref="RotaSemMetadado"/> (nenhum metadado: cai na FallbackPolicy). Devolvem o "sub" e os papéis do token.
    /// </summary>
    private sealed class EndpointProtegidoFiltro : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            Ramo(app, RotaProtegida, endpoint => endpoint.RequireAuthorization());
            Ramo(app, RotaAdmin, endpoint => endpoint.RequireAuthorization(Politicas.Admin));
            Ramo(app, RotaSemMetadado, _ => { });

            next(app);
        };

        private static void Ramo(IApplicationBuilder app, string rota, Action<RouteHandlerBuilder> configurar) =>
            app.Map(rota, ramo =>
            {
                ramo.UseRouting();
                ramo.UseAuthentication();
                ramo.UseAuthorization();
                ramo.UseEndpoints(endpoints => configurar(endpoints
                    .MapGet("/", (ClaimsPrincipal usuario) => Results.Ok(new
                    {
                        sub = usuario.Identity!.Name,
                        papeis = usuario.FindAll("role").Select(c => c.Value).ToArray(),
                    }))));
            });
    }
}
