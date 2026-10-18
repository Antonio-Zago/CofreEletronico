using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CofreEletronico.Api.Tests.Infra;

namespace CofreEletronico.Api.Tests.Auth;

public class AuthEndpointsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _cliente = factory.CreateClient();

    private static string EmailNovo() => $"u{Guid.NewGuid():N}@exemplo.com";

    private Task<HttpResponseMessage> Post(string rota, object corpo) => _cliente.PostAsJsonAsync(rota, corpo);

    private static async Task<JsonElement> Json(HttpResponseMessage resposta) =>
        JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement;

    private async Task<JsonElement> RegistrarAsync(string email, string senha = "Senha123")
    {
        var resposta = await Post("/auth/registro", new { nome = "Ana", email, senha });
        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        return await Json(resposta);
    }

    // ---------- registro ----------

    [Fact]
    public async Task Registro_devolve_201_com_tokens_em_camelCase()
    {
        var resposta = await Post("/auth/registro", new { nome = "Ana Souza", email = EmailNovo(), senha = "Senha123", telefone = "11999990000" });

        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        var corpo = await Json(resposta);
        Assert.False(string.IsNullOrEmpty(corpo.GetProperty("accessToken").GetString()));
        Assert.False(string.IsNullOrEmpty(corpo.GetProperty("refreshToken").GetString()));

        // Datas ISO-8601 em UTC.
        var accessExpira = corpo.GetProperty("accessTokenExpiraEm").GetDateTimeOffset();
        var refreshExpira = corpo.GetProperty("refreshTokenExpiraEm").GetDateTimeOffset();
        Assert.Equal(TimeSpan.Zero, accessExpira.Offset);
        Assert.InRange(accessExpira - DateTimeOffset.UtcNow, TimeSpan.FromMinutes(14), TimeSpan.FromMinutes(15.1));
        Assert.InRange(refreshExpira - DateTimeOffset.UtcNow, TimeSpan.FromDays(29.9), TimeSpan.FromDays(30.1));
    }

    [Fact]
    public async Task Access_token_do_registro_acessa_endpoint_protegido_com_papel_Usuario()
    {
        var tokens = await RegistrarAsync(EmailNovo());

        using var requisicao = new HttpRequestMessage(HttpMethod.Get, ApiFactory.RotaProtegida);
        requisicao.Headers.Authorization = new("Bearer", tokens.GetProperty("accessToken").GetString());
        var resposta = await _cliente.SendAsync(requisicao);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var corpo = await Json(resposta);
        Assert.True(Guid.TryParse(corpo.GetProperty("sub").GetString(), out _));
        Assert.Equal(["Usuario"], corpo.GetProperty("papeis").EnumerateArray().Select(p => p.GetString()));
    }

    [Theory]
    [InlineData(null, "a@exemplo.com", "Senha123", null, "nome")]
    [InlineData("Ana", null, "Senha123", null, "email")]
    [InlineData("Ana", "nao-e-email", "Senha123", null, "email")]
    [InlineData("Ana", "Ana <a@exemplo.com>", "Senha123", null, "email")]
    [InlineData("Ana", "a@exemplo.com", "", null, "senha")]
    [InlineData("Ana", "a@exemplo.com", "Senha123", "123456789012345678901", "telefone")]
    public async Task Registro_invalido_devolve_400_com_erro_no_campo(string? nome, string? email, string? senha, string? telefone, string campo)
    {
        var resposta = await Post("/auth/registro", new { nome, email, senha, telefone });

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.Equal("application/problem+json", resposta.Content.Headers.ContentType?.MediaType);
        var corpo = await Json(resposta);
        Assert.True(corpo.GetProperty("errors").TryGetProperty(campo, out _));
    }

    [Fact]
    public async Task Registro_com_nome_acima_de_150_caracteres_devolve_400()
    {
        var resposta = await Post("/auth/registro", new { nome = new string('a', 151), email = EmailNovo(), senha = "Senha123" });

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.True((await Json(resposta)).GetProperty("errors").TryGetProperty("nome", out _));
    }

    [Fact]
    public async Task Campos_acima_do_tamanho_maximo_devolvem_400()
    {
        var senhaLonga = "Aa1" + new string('x', 126); // 129
        var tokenLongo = new string('x', 129);
        var emailLongo = new string('a', 245) + "@exemplo.com"; // 257

        var casos = new (string Rota, object Corpo, string Campo)[]
        {
            ("/auth/registro", new { nome = "Ana", email = EmailNovo(), senha = senhaLonga }, "senha"),
            ("/auth/registro", new { nome = "Ana", email = emailLongo, senha = "Senha123" }, "email"),
            ("/auth/login", new { email = EmailNovo(), senha = senhaLonga }, "senha"),
            ("/auth/login", new { email = emailLongo, senha = "Senha123" }, "email"),
            ("/auth/refresh", new { refreshToken = tokenLongo }, "refreshToken"),
            ("/auth/logout", new { refreshToken = tokenLongo }, "refreshToken"),
        };

        foreach (var (rota, corpo, campo) in casos)
        {
            var resposta = await Post(rota, corpo);

            Assert.True(resposta.StatusCode == HttpStatusCode.BadRequest, $"{rota} ({campo}): {resposta.StatusCode}");
            Assert.True((await Json(resposta)).GetProperty("errors").TryGetProperty(campo, out _), $"{rota} sem erro em {campo}");
        }
    }

    [Fact]
    public async Task Senha_com_exatamente_128_caracteres_e_aceita()
    {
        var resposta = await Post("/auth/registro", new { nome = "Ana", email = EmailNovo(), senha = "Aa1" + new string('x', 125) });

        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
    }

    [Fact]
    public async Task Registro_sem_corpo_devolve_400()
    {
        var resposta = await _cliente.PostAsync("/auth/registro", JsonContent.Create<object?>(null));

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    }

    [Fact]
    public async Task Registro_com_email_existente_devolve_409_generico()
    {
        var email = EmailNovo();
        await RegistrarAsync(email);

        var resposta = await Post("/auth/registro", new { nome = "Outra", email, senha = "Senha123" });

        Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
        Assert.Equal("application/problem+json", resposta.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain(email, await resposta.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    // ---------- login ----------

    [Fact]
    public async Task Login_valido_devolve_200_com_tokens()
    {
        var email = EmailNovo();
        await RegistrarAsync(email);

        var resposta = await Post("/auth/login", new { email, senha = "Senha123" });

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var corpo = await Json(resposta);
        Assert.False(string.IsNullOrEmpty(corpo.GetProperty("accessToken").GetString()));
        Assert.False(string.IsNullOrEmpty(corpo.GetProperty("refreshToken").GetString()));
    }

    [Fact]
    public async Task Falhas_de_login_devolvem_o_mesmo_401()
    {
        var email = EmailNovo();
        await RegistrarAsync(email);
        var bloqueado = EmailNovo();
        var usuarioBloqueado = factory.Identidade.Adicionar(bloqueado, "Senha123", "Usuario");
        factory.Identidade.Bloqueados[usuarioBloqueado.Id] = true;

        var senhaErrada = await Post("/auth/login", new { email, senha = "Errada123" });
        var inexistente = await Post("/auth/login", new { email = EmailNovo(), senha = "Senha123" });
        var contaBloqueada = await Post("/auth/login", new { email = bloqueado, senha = "Senha123" });

        Assert.Equal(HttpStatusCode.Unauthorized, senhaErrada.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, inexistente.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, contaBloqueada.StatusCode);

        var corpo = await senhaErrada.Content.ReadAsStringAsync();
        Assert.Equal(corpo, await inexistente.Content.ReadAsStringAsync());
        Assert.Equal(corpo, await contaBloqueada.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Login_sem_senha_devolve_400()
    {
        var resposta = await Post("/auth/login", new { email = EmailNovo() });

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    }

    // ---------- refresh ----------

    [Fact]
    public async Task Refresh_valido_devolve_200_com_novo_par()
    {
        var tokens = await RegistrarAsync(EmailNovo());
        var refresh = tokens.GetProperty("refreshToken").GetString();

        var resposta = await Post("/auth/refresh", new { refreshToken = refresh });

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var novos = await Json(resposta);
        Assert.NotEqual(refresh, novos.GetProperty("refreshToken").GetString());
    }

    [Fact]
    public async Task Refresh_reutilizado_devolve_401_e_derruba_a_familia()
    {
        var tokens = await RegistrarAsync(EmailNovo());
        var original = tokens.GetProperty("refreshToken").GetString();
        var rotacionado = (await Json(await Post("/auth/refresh", new { refreshToken = original }))).GetProperty("refreshToken").GetString();

        var reuso = await Post("/auth/refresh", new { refreshToken = original });
        var depoisDoReuso = await Post("/auth/refresh", new { refreshToken = rotacionado });

        Assert.Equal(HttpStatusCode.Unauthorized, reuso.StatusCode);
        Assert.Equal("application/problem+json", reuso.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.Unauthorized, depoisDoReuso.StatusCode);
    }

    [Fact]
    public async Task Refresh_inexistente_devolve_401()
    {
        var resposta = await Post("/auth/refresh", new { refreshToken = "nao-existe" });

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
    }

    [Fact]
    public async Task Refresh_sem_token_devolve_400()
    {
        var resposta = await Post("/auth/refresh", new { });

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    }

    // ---------- logout ----------

    [Fact]
    public async Task Logout_devolve_204_e_e_idempotente()
    {
        var tokens = await RegistrarAsync(EmailNovo());
        var refresh = tokens.GetProperty("refreshToken").GetString();

        var primeiro = await Post("/auth/logout", new { refreshToken = refresh });
        var segundo = await Post("/auth/logout", new { refreshToken = refresh });
        var inexistente = await Post("/auth/logout", new { refreshToken = "nao-existe" });
        var refreshDepois = await Post("/auth/refresh", new { refreshToken = refresh });

        Assert.Equal(HttpStatusCode.NoContent, primeiro.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, segundo.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, inexistente.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, refreshDepois.StatusCode);
    }

    [Fact]
    public async Task Senha_rejeitada_pelo_identity_devolve_400()
    {
        // Fábrica própria: o fake passa a rejeitar a criação, sem afetar os outros testes.
        using var outra = new ApiFactory();
        outra.Identidade.ErrosNaCriacao = new Dictionary<string, string[]> { ["senha"] = ["A senha deve conter ao menos um dígito (0-9)."] };
        var cliente = outra.CreateClient();

        var resposta = await cliente.PostAsJsonAsync("/auth/registro", new { nome = "Ana", email = EmailNovo(), senha = "SemDigito" });

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.True((await Json(resposta)).GetProperty("errors").TryGetProperty("senha", out _));
    }
}
