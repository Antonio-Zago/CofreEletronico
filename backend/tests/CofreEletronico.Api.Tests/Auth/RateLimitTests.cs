using System.Net;
using System.Net.Http.Json;
using CofreEletronico.Api.Tests.Infra;

namespace CofreEletronico.Api.Tests.Auth;

public class RateLimitTests
{
    private static void AssertTooManyRequests(HttpResponseMessage resposta)
    {
        Assert.Equal(HttpStatusCode.TooManyRequests, resposta.StatusCode);
        Assert.Equal("application/problem+json", resposta.Content.Headers.ContentType?.MediaType);
        var retryAfter = resposta.Headers.RetryAfter?.Delta;
        Assert.NotNull(retryAfter);
        Assert.InRange(retryAfter!.Value, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(60));
    }

    [Fact]
    public async Task Politica_de_credenciais_soma_registro_e_login_e_devolve_429_com_Retry_After()
    {
        using var factory = new ApiFactory { LimiteCredenciais = 3 };
        var cliente = factory.CreateClient();

        var dentroDoLimite = new[]
        {
            await cliente.PostAsJsonAsync("/auth/login", new { email = "a@exemplo.com", senha = "x" }),
            await cliente.PostAsJsonAsync("/auth/registro", new { nome = "Ana", email = "b@exemplo.com", senha = "Senha123" }),
            await cliente.PostAsJsonAsync("/auth/login", new { email = "a@exemplo.com", senha = "x" }),
        };
        var excedenteRegistro = await cliente.PostAsJsonAsync("/auth/registro", new { nome = "Ana", email = "c@exemplo.com", senha = "Senha123" });

        Assert.DoesNotContain(dentroDoLimite, r => r.StatusCode == HttpStatusCode.TooManyRequests);
        AssertTooManyRequests(excedenteRegistro);
    }

    [Fact]
    public async Task Politica_de_sessao_soma_refresh_e_logout_e_devolve_429_com_Retry_After()
    {
        using var factory = new ApiFactory { LimiteSessao = 3 };
        var cliente = factory.CreateClient();

        var dentroDoLimite = new[]
        {
            await cliente.PostAsJsonAsync("/auth/refresh", new { refreshToken = "x" }),
            await cliente.PostAsJsonAsync("/auth/logout", new { refreshToken = "x" }),
            await cliente.PostAsJsonAsync("/auth/refresh", new { refreshToken = "x" }),
        };
        var excedenteLogout = await cliente.PostAsJsonAsync("/auth/logout", new { refreshToken = "x" });

        Assert.DoesNotContain(dentroDoLimite, r => r.StatusCode == HttpStatusCode.TooManyRequests);
        AssertTooManyRequests(excedenteLogout);
    }

    [Fact]
    public async Task Estourar_credenciais_nao_bloqueia_refresh_nem_logout()
    {
        using var factory = new ApiFactory { LimiteCredenciais = 2, LimiteSessao = 10 };
        var cliente = factory.CreateClient();

        // Sessão aberta antes de estourar o limite de credenciais.
        var registro = await cliente.PostAsJsonAsync("/auth/registro", new { nome = "Ana", email = "d@exemplo.com", senha = "Senha123" });
        var refreshToken = (await registro.Content.ReadFromJsonAsync<Tokens>())!.RefreshToken;
        await cliente.PostAsJsonAsync("/auth/login", new { email = "d@exemplo.com", senha = "errada" });
        AssertTooManyRequests(await cliente.PostAsJsonAsync("/auth/login", new { email = "d@exemplo.com", senha = "Senha123" }));

        var refresh = await cliente.PostAsJsonAsync("/auth/refresh", new { refreshToken });
        var novo = (await refresh.Content.ReadFromJsonAsync<Tokens>())!.RefreshToken;
        var logout = await cliente.PostAsJsonAsync("/auth/logout", new { refreshToken = novo });

        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
    }

    [Fact]
    public async Task Estourar_sessao_nao_bloqueia_login()
    {
        using var factory = new ApiFactory { LimiteCredenciais = 10, LimiteSessao = 1 };
        var cliente = factory.CreateClient();
        factory.Identidade.Adicionar("e@exemplo.com", "Senha123", "Usuario");

        await cliente.PostAsJsonAsync("/auth/refresh", new { refreshToken = "x" });
        AssertTooManyRequests(await cliente.PostAsJsonAsync("/auth/refresh", new { refreshToken = "x" }));

        var login = await cliente.PostAsJsonAsync("/auth/login", new { email = "e@exemplo.com", senha = "Senha123" });

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task Ping_nao_tem_rate_limit()
    {
        using var factory = new ApiFactory { LimiteCredenciais = 1, LimiteSessao = 1 };
        var cliente = factory.CreateClient();

        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await cliente.GetAsync("/ping")).StatusCode);
        }
    }

    private sealed record Tokens(string AccessToken, string RefreshToken);
}
