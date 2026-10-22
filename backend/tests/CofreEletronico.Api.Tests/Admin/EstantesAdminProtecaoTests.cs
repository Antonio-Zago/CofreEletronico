using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CofreEletronico.Api.Tests.Infra;
using CofreEletronico.Application.Autenticacao.Portas;
using CofreEletronico.Application.Autorizacao;
using Microsoft.Extensions.DependencyInjection;

namespace CofreEletronico.Api.Tests.Admin;

/// <summary>
/// Recomendações da revisão de segurança (T-0406/T-0402): 409 de concorrência, exigência de <c>sub</c> válido,
/// rate limit do /admin por administrador e 500 genérico em ProblemDetails.
/// </summary>
public class EstantesAdminProtecaoTests
{
    private const string Base = "/admin/estantes";

    private static string TokenAdmin(ApiFactory factory, Guid? id = null) =>
        factory.Services.GetRequiredService<IGeradorAccessToken>()
            .Gerar(new UsuarioAutenticado(id ?? Guid.NewGuid(), "admin@exemplo.com", [Papeis.Admin]))
            .Token;

    private static async Task<HttpResponseMessage> Enviar(
        HttpClient cliente, HttpMethod metodo, string rota, string token, object? corpo = null)
    {
        using var requisicao = new HttpRequestMessage(metodo, rota);
        requisicao.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (corpo is not null)
            requisicao.Content = JsonContent.Create(corpo);
        return await cliente.SendAsync(requisicao);
    }

    private static object CorpoValido => new { nome = "Estante", endereco = "Rua A", latitude = 0.0, longitude = 0.0 };

    private static void AssertTooManyRequests(HttpResponseMessage resposta)
    {
        Assert.Equal(HttpStatusCode.TooManyRequests, resposta.StatusCode);
        Assert.Equal("application/problem+json", resposta.Content.Headers.ContentType?.MediaType);
        var retryAfter = resposta.Headers.RetryAfter?.Delta;
        Assert.NotNull(retryAfter);
        Assert.InRange(retryAfter!.Value, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(60));
    }

    // ---------- R2: conflito de concorrência → 409 ----------

    [Theory]
    [InlineData("PUT", "")]
    [InlineData("POST", "/desativar")]
    [InlineData("POST", "/reativar")]
    [InlineData("POST", "/credenciais")]
    public async Task Conflito_de_concorrencia_ao_salvar_devolve_409_generico(string metodo, string sufixo)
    {
        using var factory = new ApiFactory();
        var cliente = factory.CreateClient();
        var estante = factory.Estantes.Adicionar("Alfa", ativa: sufixo != "/reativar");
        factory.Estantes.ConflitoNoSalvar = true;

        var resposta = await Enviar(cliente, new HttpMethod(metodo), $"{Base}/{estante.Id}{sufixo}", TokenAdmin(factory),
            metodo == "PUT" ? CorpoValido : null);

        Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
        Assert.Equal("application/problem+json", resposta.Content.Headers.ContentType?.MediaType);
        var corpo = await resposta.Content.ReadAsStringAsync();
        Assert.Contains("alterada por outra operação", corpo);
        Assert.DoesNotContain("segredo", corpo, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("senhaMqtt", corpo, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(estante.DeviceId, corpo);
    }

    // ---------- R6: claim sub obrigatória e válida ----------

    [Theory]
    [InlineData(null)]
    [InlineData("nao-e-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task Token_Admin_sem_sub_valido_devolve_401(string? sub)
    {
        using var factory = new ApiFactory();
        var cliente = factory.CreateClient();
        var token = TokensDeTeste.ComSub(sub, Papeis.Admin);

        var criar = await Enviar(cliente, HttpMethod.Post, Base, token, CorpoValido);
        var listar = await Enviar(cliente, HttpMethod.Get, Base, token);

        foreach (var resposta in new[] { criar, listar })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
            Assert.Equal("application/problem+json", resposta.Content.Headers.ContentType?.MediaType);
        }

        Assert.Empty((await factory.Estantes.ListarAsync(null, 1, 100, default)).Itens);
    }

    [Fact]
    public async Task Token_Admin_com_sub_Guid_e_aceito()
    {
        using var factory = new ApiFactory();
        var cliente = factory.CreateClient();

        var resposta = await Enviar(cliente, HttpMethod.Get, Base, TokensDeTeste.ComSub(Guid.NewGuid().ToString(), Papeis.Admin));

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
    }

    // ---------- R7: rate limit do /admin por administrador ----------

    [Fact]
    public async Task Politica_sensivel_soma_criar_e_regenerar_e_devolve_429_com_Retry_After()
    {
        using var factory = new ApiFactory { LimiteAdminSensivel = 3 };
        var cliente = factory.CreateClient();
        var token = TokenAdmin(factory);

        var criacao = await Enviar(cliente, HttpMethod.Post, Base, token, CorpoValido);
        var id = (await criacao.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var dentroDoLimite = new[]
        {
            criacao,
            await Enviar(cliente, HttpMethod.Post, $"{Base}/{id}/credenciais", token),
            await Enviar(cliente, HttpMethod.Post, Base, token, CorpoValido),
        };
        var excedenteCredenciais = await Enviar(cliente, HttpMethod.Post, $"{Base}/{id}/credenciais", token);
        var excedenteCriacao = await Enviar(cliente, HttpMethod.Post, Base, token, CorpoValido);

        Assert.DoesNotContain(dentroDoLimite, r => r.StatusCode == HttpStatusCode.TooManyRequests);
        AssertTooManyRequests(excedenteCredenciais);
        AssertTooManyRequests(excedenteCriacao);
        Assert.DoesNotContain("segredoHmac", await excedenteCredenciais.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Estourar_a_politica_sensivel_nao_bloqueia_as_demais_rotas_nem_outro_admin()
    {
        using var factory = new ApiFactory { LimiteAdminSensivel = 1 };
        var cliente = factory.CreateClient();
        var token = TokenAdmin(factory);

        await Enviar(cliente, HttpMethod.Post, Base, token, CorpoValido);
        AssertTooManyRequests(await Enviar(cliente, HttpMethod.Post, Base, token, CorpoValido));

        Assert.Equal(HttpStatusCode.OK, (await Enviar(cliente, HttpMethod.Get, Base, token)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Enviar(cliente, HttpMethod.Post, Base, TokenAdmin(factory), CorpoValido)).StatusCode);
    }

    [Fact]
    public async Task Limite_geral_vale_para_todas_as_rotas_admin_por_administrador()
    {
        using var factory = new ApiFactory { LimiteAdmin = 3 };
        var cliente = factory.CreateClient();
        var adminId = Guid.NewGuid();

        var dentroDoLimite = new[]
        {
            await Enviar(cliente, HttpMethod.Get, Base, TokenAdmin(factory, adminId)),
            await Enviar(cliente, HttpMethod.Post, Base, TokenAdmin(factory, adminId), CorpoValido), // sensível também conta
            await Enviar(cliente, HttpMethod.Get, $"{Base}/{Guid.NewGuid()}", TokenAdmin(factory, adminId)),
        };
        // Novo token do mesmo admin (mesmo sub): mesmo contador.
        var excedente = await Enviar(cliente, HttpMethod.Get, Base, TokenAdmin(factory, adminId));
        var outroAdmin = await Enviar(cliente, HttpMethod.Get, Base, TokenAdmin(factory));

        Assert.DoesNotContain(dentroDoLimite, r => r.StatusCode == HttpStatusCode.TooManyRequests);
        AssertTooManyRequests(excedente);
        Assert.Equal(HttpStatusCode.OK, outroAdmin.StatusCode);
    }

    [Fact]
    public async Task Limite_geral_do_admin_nao_afeta_rotas_fora_do_admin()
    {
        using var factory = new ApiFactory { LimiteAdmin = 1 };
        var cliente = factory.CreateClient();
        var token = TokenAdmin(factory);

        await Enviar(cliente, HttpMethod.Get, Base, token);
        AssertTooManyRequests(await Enviar(cliente, HttpMethod.Get, Base, token));

        Assert.Equal(HttpStatusCode.OK, (await cliente.GetAsync("/ping")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Enviar(cliente, HttpMethod.Get, ApiFactory.RotaProtegida, token)).StatusCode);
    }

    // ---------- R5: exceção não tratada → 500 genérico em ProblemDetails ----------

    [Fact]
    public async Task Excecao_nao_tratada_devolve_500_ProblemDetails_generico_sem_detalhes()
    {
        using var factory = new ApiFactory();
        var cliente = factory.CreateClient();

        var resposta = await Enviar(cliente, HttpMethod.Get, ApiFactory.RotaExcecao, TokenAdmin(factory));

        Assert.Equal(HttpStatusCode.InternalServerError, resposta.StatusCode);
        Assert.Equal("application/problem+json", resposta.Content.Headers.ContentType?.MediaType);

        var texto = await resposta.Content.ReadAsStringAsync();
        Assert.DoesNotContain(ApiFactory.MensagemExcecao, texto);
        Assert.DoesNotContain(nameof(InvalidOperationException), texto);
        Assert.DoesNotContain("stack", texto, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(" at ", texto);

        var corpo = JsonDocument.Parse(texto).RootElement;
        Assert.Equal(500, corpo.GetProperty("status").GetInt32());
        Assert.Equal("Erro interno", corpo.GetProperty("title").GetString());
        Assert.Equal("Ocorreu um erro inesperado. Tente novamente mais tarde.", corpo.GetProperty("detail").GetString());
        Assert.All(corpo.EnumerateObject(), p => Assert.Contains(p.Name, new[] { "type", "title", "status", "detail", "traceId" }));
    }
}
