using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CofreEletronico.Api.Tests.Infra;
using CofreEletronico.Application.Autenticacao.Portas;
using CofreEletronico.Application.Autorizacao;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace CofreEletronico.Api.Tests.Auth;

/// <summary>
/// Contrato (docs/ARQUITETURA.md): sem token ou com token inválido, 401; sem o papel exigido, 403.
/// Tokens emitidos pelo gerador real da Api (mesma chave, emissor e audiência).
/// </summary>
public class AutorizacaoTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _cliente = factory.CreateClient();

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

    private static async Task<JsonElement> LerProblema(HttpResponseMessage resposta, HttpStatusCode status)
    {
        Assert.Equal(status, resposta.StatusCode);
        Assert.Equal("application/problem+json", resposta.Content.Headers.ContentType?.MediaType);
        var corpo = await resposta.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal((int)status, corpo.GetProperty("status").GetInt32());
        return corpo;
    }

    // ---------- política Admin ----------

    [Fact]
    public async Task Rota_Admin_sem_token_devolve_401_ProblemDetails()
    {
        var resposta = await Get(ApiFactory.RotaAdmin, null);

        var problema = await LerProblema(resposta, HttpStatusCode.Unauthorized);
        Assert.Equal("Não autorizado", problema.GetProperty("title").GetString());
        Assert.Equal("Bearer", resposta.Headers.WwwAuthenticate.ToString());
    }

    [Fact]
    public async Task Rota_Admin_com_papel_Usuario_devolve_403_ProblemDetails()
    {
        var resposta = await Get(ApiFactory.RotaAdmin, Token(Papeis.Usuario));

        var problema = await LerProblema(resposta, HttpStatusCode.Forbidden);
        Assert.Equal("Acesso negado", problema.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Rota_Admin_com_token_sem_papel_devolve_403()
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await Get(ApiFactory.RotaAdmin, Token())).StatusCode);
    }

    [Fact]
    public async Task Rota_Admin_com_papel_Admin_devolve_200()
    {
        var resposta = await Get(ApiFactory.RotaAdmin, Token(Papeis.Admin, Papeis.Usuario));

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
    }

    [Fact]
    public async Task Papel_com_outra_caixa_nao_vale_como_Admin()
    {
        // RequireRole compara com o nome exato do papel.
        Assert.Equal(HttpStatusCode.Forbidden, (await Get(ApiFactory.RotaAdmin, Token("admin"))).StatusCode);
    }

    // ---------- 401 ----------

    [Fact]
    public async Task Token_invalido_devolve_401_ProblemDetails_sem_detalhar_o_motivo()
    {
        var resposta = await Get(ApiFactory.RotaProtegida, "isto-nao-e-um-jwt");

        var problema = await LerProblema(resposta, HttpStatusCode.Unauthorized);
        Assert.Equal("Bearer error=\"invalid_token\"", resposta.Headers.WwwAuthenticate.ToString());
        Assert.DoesNotContain("IDX", problema.GetRawText()); // mensagens internas do IdentityModel
    }


    // ---------- bordas: 401/403 nunca detalham o motivo nem ecoam o token ----------

    private static async Task AssertSemDetalhes(HttpResponseMessage resposta, HttpStatusCode status, string? token)
    {
        var corpo = (await LerProblema(resposta, status)).GetRawText();
        var cabecalho = resposta.Headers.WwwAuthenticate.ToString();

        Assert.DoesNotContain("IDX", corpo); // códigos de erro internos do IdentityModel
        Assert.DoesNotContain("SecurityToken", corpo); // nomes de exceção (SecurityTokenExpiredException etc.)
        Assert.DoesNotContain("error_description", cabecalho);
        Assert.DoesNotContain("IDX", cabecalho);
        if (token is not null)
        {
            foreach (var parte in token.Split('.', StringSplitOptions.RemoveEmptyEntries))
                Assert.DoesNotContain(parte, corpo);
        }
    }

    public static TheoryData<string, string> TokensRecusados() => new()
    {
        { "expirado", TokensDeTeste.Criar(expira: DateTime.UtcNow.AddMinutes(-5)) },
        { "outra chave", TokensDeTeste.Criar(chave: "outra-chave-qualquer-com-mais-de-32-bytes!!") },
        { "alg=none", TokensDeTeste.SemAssinatura(Papeis.Admin) },
        { "admin com outra chave", TokensDeTeste.Criar(chave: "outra-chave-qualquer-com-mais-de-32-bytes!!", papeis: Papeis.Admin) },
    };

    [Theory]
    [MemberData(nameof(TokensRecusados))]
    public async Task Token_recusado_devolve_401_invalid_token_sem_detalhes(string caso, string token)
    {
        foreach (var rota in new[] { ApiFactory.RotaProtegida, ApiFactory.RotaAdmin })
        {
            var resposta = await Get(rota, token);

            Assert.True(resposta.StatusCode == HttpStatusCode.Unauthorized, $"{caso} em {rota}: {resposta.StatusCode}");
            Assert.Equal("Bearer error=\"invalid_token\"", resposta.Headers.WwwAuthenticate.ToString());
            await AssertSemDetalhes(resposta, HttpStatusCode.Unauthorized, token);
        }
    }

    [Fact]
    public async Task Rota_inexistente_sem_token_devolve_401()
    {
        // A FallbackPolicy vale também para requisições sem endpoint: não revela quais rotas existem.
        var resposta = await Get("/rota-que-nao-existe", null);

        await AssertSemDetalhes(resposta, HttpStatusCode.Unauthorized, null);
        Assert.Equal("Bearer", resposta.Headers.WwwAuthenticate.ToString());
    }

    [Fact]
    public async Task Corpo_do_403_nao_detalha_o_token()
    {
        var token = Token(Papeis.Usuario);

        var resposta = await Get(ApiFactory.RotaAdmin, token);

        await AssertSemDetalhes(resposta, HttpStatusCode.Forbidden, token);
        var corpo = await resposta.Content.ReadAsStringAsync();
        Assert.DoesNotContain(Papeis.Usuario, corpo);
        Assert.DoesNotContain(Papeis.Admin, corpo);
    }
    // ---------- FallbackPolicy ----------

    [Fact]
    public async Task Rota_sem_metadado_exige_login_pela_FallbackPolicy()
    {
        await LerProblema(await Get(ApiFactory.RotaSemMetadado, null), HttpStatusCode.Unauthorized);
        Assert.Equal(HttpStatusCode.OK, (await Get(ApiFactory.RotaSemMetadado, Token(Papeis.Usuario))).StatusCode);
    }

    // ---------- rotas anônimas ----------

    [Fact]
    public async Task Ping_continua_anonimo()
    {
        var resposta = await Get("/ping", null);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
    }

    [Fact]
    public async Task Auth_continua_anonimo()
    {
        var registro = await _cliente.PostAsJsonAsync("/auth/registro",
            new { nome = "Ana", email = $"ana-{Guid.NewGuid():N}@exemplo.com", senha = "Senha123" });
        var login = await _cliente.PostAsJsonAsync("/auth/login", new { email = "ninguem@exemplo.com", senha = "Senha123" });

        Assert.Equal(HttpStatusCode.Created, registro.StatusCode);
        // 401 do próprio endpoint (credenciais), não do JwtBearer: sem WWW-Authenticate.
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
        Assert.Empty(login.Headers.WwwAuthenticate);
    }

    // ---------- seed do admin inicial na subida ----------

    [Fact]
    public async Task Api_sobe_com_AdminInicial_configurado_e_sem_banco()
    {
        // Sem string de conexão o seed é pulado (só log); a Api continua subindo.
        using var comAdmin = factory.WithWebHostBuilder(b => b
            .UseSetting("AdminInicial:Email", "admin@cofre.com.br")
            .UseSetting("AdminInicial:Senha", "SenhaForte123"));
        using var cliente = comAdmin.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await cliente.GetAsync("/ping")).StatusCode);
    }
}
