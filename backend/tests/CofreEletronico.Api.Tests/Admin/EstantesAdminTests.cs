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
/// T-0402: /admin/estantes pela Api real (JWT, autorização, endpoints e casos de uso reais; repositório em memória;
/// gerador de credenciais real).
/// </summary>
public class EstantesAdminTests : IClassFixture<ApiFactory>
{
    private const string Base = "/admin/estantes";

    private static readonly string[] CamposEstante =
        ["id", "nome", "endereco", "latitude", "longitude", "deviceId", "online", "ultimoHeartbeat", "ativa", "criadoEm"];

    private static readonly string[] CamposCredenciais = ["deviceId", "segredoHmac", "usuarioMqtt", "senhaMqtt"];

    private readonly ApiFactory _factory;
    private readonly HttpClient _cliente;

    public EstantesAdminTests(ApiFactory factory)
    {
        _factory = factory;
        _cliente = factory.CreateClient();
        factory.Estantes.Limpar(); // os testes da classe rodam em sequência, com a mesma fábrica
    }

    private string Token(params string[] papeis) =>
        _factory.Services.GetRequiredService<IGeradorAccessToken>()
            .Gerar(new UsuarioAutenticado(Guid.NewGuid(), "admin@exemplo.com", papeis))
            .Token;

    private async Task<HttpResponseMessage> Enviar(HttpMethod metodo, string rota, object? corpo = null, string? token = "admin")
    {
        using var requisicao = new HttpRequestMessage(metodo, rota);
        if (corpo is not null)
            requisicao.Content = JsonContent.Create(corpo);
        if (token is not null)
            requisicao.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer", token == "admin" ? Token(Papeis.Admin, Papeis.Usuario) : token);
        return await _cliente.SendAsync(requisicao);
    }

    private static object CorpoValido(string nome = "Estante Centro") =>
        new { nome, endereco = "Rua A, 100", latitude = -23.55, longitude = -46.63 };

    private static async Task<JsonElement> Json(HttpResponseMessage resposta) =>
        await resposta.Content.ReadFromJsonAsync<JsonElement>();

    private static string[] Propriedades(JsonElement objeto) => objeto.EnumerateObject().Select(p => p.Name).ToArray();

    private static void AssertSemSegredos(string corpo)
    {
        Assert.DoesNotContain("segredoHmac", corpo, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("senhaMqtt", corpo, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("credenciais", corpo, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("segredo", corpo, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertNaoArmazenavel(HttpResponseMessage resposta)
    {
        Assert.True(resposta.Headers.CacheControl?.NoStore, "Cache-Control: no-store ausente.");
        Assert.Contains("no-cache", resposta.Headers.Pragma.Select(p => p.Name));
    }

    // ---------- POST /admin/estantes ----------

    [Fact]
    public async Task Criar_devolve_201_com_Location_estante_e_credenciais_sem_cache()
    {
        var resposta = await Enviar(HttpMethod.Post, Base, CorpoValido());

        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        AssertNaoArmazenavel(resposta);

        var corpo = await Json(resposta);
        Assert.Equal([.. CamposEstante, "credenciais"], Propriedades(corpo));

        var id = corpo.GetProperty("id").GetGuid();
        Assert.Equal($"{Base}/{id}", resposta.Headers.Location?.ToString());
        Assert.Equal("Estante Centro", corpo.GetProperty("nome").GetString());
        Assert.True(corpo.GetProperty("ativa").GetBoolean());
        Assert.False(corpo.GetProperty("online").GetBoolean());
        Assert.Equal(JsonValueKind.Null, corpo.GetProperty("ultimoHeartbeat").ValueKind);

        var deviceId = corpo.GetProperty("deviceId").GetString()!;
        Assert.Matches("^est-[a-z0-9]{6}$", deviceId);

        var credenciais = corpo.GetProperty("credenciais");
        Assert.Equal(CamposCredenciais, Propriedades(credenciais));
        Assert.Equal(deviceId, credenciais.GetProperty("deviceId").GetString());
        Assert.Equal(deviceId, credenciais.GetProperty("usuarioMqtt").GetString());
        Assert.Matches("^[A-Za-z0-9]{24}$", credenciais.GetProperty("senhaMqtt").GetString());

        // O segredo devolvido é o que ficou guardado (e que o backend vai usar para assinar).
        var segredoHex = credenciais.GetProperty("segredoHmac").GetString()!;
        Assert.Matches("^[0-9a-f]{64}$", segredoHex);
        var estante = _factory.Estantes.Obter(id)!;
        Assert.Equal(Convert.FromHexString(segredoHex), estante.ObterSegredoHmac());
    }

    [Fact]
    public async Task Criar_com_corpo_invalido_devolve_400_com_erros_por_campo()
    {
        var resposta = await Enviar(HttpMethod.Post, Base,
            new { nome = " ", endereco = new string('x', 201), latitude = 90.5, longitude = (double?)null });

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.Equal("application/problem+json", resposta.Content.Headers.ContentType?.MediaType);
        var erros = (await Json(resposta)).GetProperty("errors");
        Assert.Equal(["nome", "endereco", "latitude", "longitude"], Propriedades(erros));
        Assert.Empty((await _factory.Estantes.ListarAsync(null, 1, 100, default)).Itens);
    }

    [Fact]
    public async Task Criar_sem_corpo_devolve_400()
    {
        using var requisicao = new HttpRequestMessage(HttpMethod.Post, Base)
        {
            Content = new StringContent("null", System.Text.Encoding.UTF8, "application/json"),
        };
        requisicao.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token(Papeis.Admin));

        var resposta = await _cliente.SendAsync(requisicao);

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    }

    [Theory]
    [InlineData(-90.0, -180.0)]
    [InlineData(90.0, 180.0)]
    public async Task Criar_aceita_coordenadas_nos_limites(double latitude, double longitude)
    {
        var resposta = await Enviar(HttpMethod.Post, Base, new { nome = "Limite", endereco = "Rua", latitude, longitude });

        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
    }

    [Fact]
    public async Task Criar_sem_device_id_livre_devolve_500_generico_sem_credenciais()
    {
        _factory.Estantes.RecusarTodasAsInsercoes = true;

        var resposta = await Enviar(HttpMethod.Post, Base, CorpoValido());

        Assert.Equal(HttpStatusCode.InternalServerError, resposta.StatusCode);
        Assert.Equal("application/problem+json", resposta.Content.Headers.ContentType?.MediaType);
        var corpo = await resposta.Content.ReadAsStringAsync();
        AssertSemSegredos(corpo);
        Assert.DoesNotContain("est-", corpo);
    }

    // ---------- GET /admin/estantes ----------

    [Fact]
    public async Task Listar_usa_padroes_ordena_por_nome_e_nao_expoe_segredos()
    {
        _factory.Estantes.Adicionar("Bravo");
        _factory.Estantes.Adicionar("Alfa");
        _factory.Estantes.Adicionar("Charlie", ativa: false);

        var resposta = await Enviar(HttpMethod.Get, Base);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var texto = await resposta.Content.ReadAsStringAsync();
        AssertSemSegredos(texto);

        var corpo = JsonDocument.Parse(texto).RootElement;
        Assert.Equal(["itens", "pagina", "tamanhoPagina", "total"], Propriedades(corpo));
        Assert.Equal(1, corpo.GetProperty("pagina").GetInt32());
        Assert.Equal(20, corpo.GetProperty("tamanhoPagina").GetInt32());
        Assert.Equal(3, corpo.GetProperty("total").GetInt32());
        var itens = corpo.GetProperty("itens").EnumerateArray().ToList();
        Assert.Equal(["Alfa", "Bravo", "Charlie"], itens.Select(i => i.GetProperty("nome").GetString()));
        Assert.All(itens, i => Assert.Equal(CamposEstante, Propriedades(i)));
    }

    [Theory]
    [InlineData("true", new[] { "Alfa", "Bravo" })]
    [InlineData("false", new[] { "Charlie" })]
    [InlineData("TRUE", new[] { "Alfa", "Bravo" })]
    public async Task Listar_filtra_por_ativa(string ativa, string[] esperados)
    {
        _factory.Estantes.Adicionar("Bravo");
        _factory.Estantes.Adicionar("Alfa");
        _factory.Estantes.Adicionar("Charlie", ativa: false);

        var corpo = await Json(await Enviar(HttpMethod.Get, $"{Base}?ativa={ativa}"));

        Assert.Equal(esperados, corpo.GetProperty("itens").EnumerateArray().Select(i => i.GetProperty("nome").GetString()));
        Assert.Equal(esperados.Length, corpo.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Listar_pagina()
    {
        foreach (var nome in new[] { "A", "B", "C", "D", "E" })
            _factory.Estantes.Adicionar(nome);

        var corpo = await Json(await Enviar(HttpMethod.Get, $"{Base}?pagina=2&tamanhoPagina=2"));

        Assert.Equal(["C", "D"], corpo.GetProperty("itens").EnumerateArray().Select(i => i.GetProperty("nome").GetString()));
        Assert.Equal(2, corpo.GetProperty("pagina").GetInt32());
        Assert.Equal(2, corpo.GetProperty("tamanhoPagina").GetInt32());
        Assert.Equal(5, corpo.GetProperty("total").GetInt32());
    }

    [Theory]
    [InlineData("pagina=0", "pagina")]
    [InlineData("pagina=-1", "pagina")]
    [InlineData("pagina=abc", "pagina")]
    [InlineData("pagina=99999999999", "pagina")]
    [InlineData("tamanhoPagina=0", "tamanhoPagina")]
    [InlineData("tamanhoPagina=101", "tamanhoPagina")]
    [InlineData("tamanhoPagina=1.5", "tamanhoPagina")]
    [InlineData("ativa=talvez", "ativa")]
    public async Task Listar_com_query_invalida_devolve_400(string query, string campo)
    {
        var resposta = await Enviar(HttpMethod.Get, $"{Base}?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.True((await Json(resposta)).GetProperty("errors").TryGetProperty(campo, out _));
    }

    [Fact]
    public async Task Listar_aceita_tamanho_maximo()
    {
        var resposta = await Enviar(HttpMethod.Get, $"{Base}?tamanhoPagina=100");

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
    }

    // ---------- GET /admin/estantes/{id} ----------

    [Fact]
    public async Task Obter_devolve_200_com_os_campos_do_contrato_e_sem_segredos()
    {
        var estante = _factory.Estantes.Adicionar("Alfa");

        var resposta = await Enviar(HttpMethod.Get, $"{Base}/{estante.Id}");

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var texto = await resposta.Content.ReadAsStringAsync();
        AssertSemSegredos(texto);
        var corpo = JsonDocument.Parse(texto).RootElement;
        Assert.Equal(CamposEstante, Propriedades(corpo));
        Assert.Equal(estante.Id, corpo.GetProperty("id").GetGuid());
        Assert.Equal(estante.DeviceId, corpo.GetProperty("deviceId").GetString());
    }

    [Fact]
    public async Task Obter_inexistente_devolve_404_ProblemDetails()
    {
        var resposta = await Enviar(HttpMethod.Get, $"{Base}/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
        Assert.Equal("application/problem+json", resposta.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Id_que_nao_e_guid_devolve_404()
    {
        var resposta = await Enviar(HttpMethod.Get, $"{Base}/nao-e-guid");

        Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
    }

    // ---------- PUT /admin/estantes/{id} ----------

    [Fact]
    public async Task Atualizar_devolve_200_com_os_dados_novos_e_sem_segredos()
    {
        var estante = _factory.Estantes.Adicionar("Alfa");
        var deviceId = estante.DeviceId;
        var segredo = estante.ObterSegredoHmac();

        var resposta = await Enviar(HttpMethod.Put, $"{Base}/{estante.Id}",
            new { nome = "Alfa Nova", endereco = "Rua B, 200", latitude = 1.5, longitude = 2.5 });

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var texto = await resposta.Content.ReadAsStringAsync();
        AssertSemSegredos(texto);
        var corpo = JsonDocument.Parse(texto).RootElement;
        Assert.Equal("Alfa Nova", corpo.GetProperty("nome").GetString());
        Assert.Equal("Rua B, 200", corpo.GetProperty("endereco").GetString());
        Assert.Equal(1.5, corpo.GetProperty("latitude").GetDouble());
        Assert.Equal(2.5, corpo.GetProperty("longitude").GetDouble());
        Assert.Equal(deviceId, corpo.GetProperty("deviceId").GetString());
        Assert.Equal(segredo, estante.ObterSegredoHmac());
    }

    [Fact]
    public async Task Atualizar_com_corpo_invalido_devolve_400()
    {
        var estante = _factory.Estantes.Adicionar("Alfa");

        var resposta = await Enviar(HttpMethod.Put, $"{Base}/{estante.Id}",
            new { nome = new string('n', 101), endereco = "Rua", latitude = 0, longitude = -180.1 });

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        var erros = (await Json(resposta)).GetProperty("errors");
        Assert.Equal(["nome", "longitude"], Propriedades(erros));
        Assert.Equal("Alfa", estante.Nome);
    }

    [Fact]
    public async Task Atualizar_inexistente_devolve_404()
    {
        var resposta = await Enviar(HttpMethod.Put, $"{Base}/{Guid.NewGuid()}", CorpoValido());

        Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
    }

    // ---------- desativar / reativar ----------

    [Fact]
    public async Task Desativar_devolve_200_e_e_idempotente()
    {
        var estante = _factory.Estantes.Adicionar("Alfa");

        foreach (var _ in Enumerable.Range(0, 2))
        {
            var resposta = await Enviar(HttpMethod.Post, $"{Base}/{estante.Id}/desativar");

            Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
            var texto = await resposta.Content.ReadAsStringAsync();
            AssertSemSegredos(texto);
            Assert.False(JsonDocument.Parse(texto).RootElement.GetProperty("ativa").GetBoolean());
        }

        Assert.False(estante.Ativa);
    }

    [Fact]
    public async Task Desativar_com_cofre_reservado_ou_ocupado_devolve_409()
    {
        var estante = _factory.Estantes.Adicionar("Alfa");
        _factory.Estantes.MarcarCofreEmUso(estante.Id);

        var resposta = await Enviar(HttpMethod.Post, $"{Base}/{estante.Id}/desativar");

        Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
        Assert.Equal("application/problem+json", resposta.Content.Headers.ContentType?.MediaType);
        Assert.True(estante.Ativa);
    }

    [Fact]
    public async Task Reativar_devolve_200_e_e_idempotente()
    {
        var estante = _factory.Estantes.Adicionar("Alfa", ativa: false);

        foreach (var _ in Enumerable.Range(0, 2))
        {
            var resposta = await Enviar(HttpMethod.Post, $"{Base}/{estante.Id}/reativar");

            Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
            var texto = await resposta.Content.ReadAsStringAsync();
            AssertSemSegredos(texto);
            Assert.True(JsonDocument.Parse(texto).RootElement.GetProperty("ativa").GetBoolean());
        }
    }

    [Theory]
    [InlineData("desativar")]
    [InlineData("reativar")]
    [InlineData("credenciais")]
    public async Task Acoes_em_estante_inexistente_devolvem_404(string acao)
    {
        var resposta = await Enviar(HttpMethod.Post, $"{Base}/{Guid.NewGuid()}/{acao}");

        Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
    }

    // ---------- POST /admin/estantes/{id}/credenciais ----------

    [Fact]
    public async Task Regenerar_devolve_credenciais_novas_mantendo_o_device_id_e_sem_cache()
    {
        var criacao = await Json(await Enviar(HttpMethod.Post, Base, CorpoValido()));
        var id = criacao.GetProperty("id").GetGuid();
        var anteriores = criacao.GetProperty("credenciais");

        var resposta = await Enviar(HttpMethod.Post, $"{Base}/{id}/credenciais");

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        AssertNaoArmazenavel(resposta);
        var novas = await Json(resposta);
        Assert.Equal(CamposCredenciais, Propriedades(novas));
        Assert.Equal(anteriores.GetProperty("deviceId").GetString(), novas.GetProperty("deviceId").GetString());
        Assert.Equal(anteriores.GetProperty("deviceId").GetString(), novas.GetProperty("usuarioMqtt").GetString());
        Assert.NotEqual(anteriores.GetProperty("segredoHmac").GetString(), novas.GetProperty("segredoHmac").GetString());
        Assert.NotEqual(anteriores.GetProperty("senhaMqtt").GetString(), novas.GetProperty("senhaMqtt").GetString());
        Assert.Matches("^[0-9a-f]{64}$", novas.GetProperty("segredoHmac").GetString());
        Assert.Matches("^[A-Za-z0-9]{24}$", novas.GetProperty("senhaMqtt").GetString());

        var estante = _factory.Estantes.Obter(id)!;
        Assert.Equal(Convert.FromHexString(novas.GetProperty("segredoHmac").GetString()!), estante.ObterSegredoHmac());
    }

    // ---------- autorização em todas as rotas ----------

    public static TheoryData<string, string> Rotas()
    {
        var id = Guid.NewGuid();
        return new TheoryData<string, string>
        {
            { "POST", Base },
            { "GET", Base },
            { "GET", $"{Base}/{id}" },
            { "PUT", $"{Base}/{id}" },
            { "POST", $"{Base}/{id}/desativar" },
            { "POST", $"{Base}/{id}/reativar" },
            { "POST", $"{Base}/{id}/credenciais" },
        };
    }

    [Theory]
    [MemberData(nameof(Rotas))]
    public async Task Sem_token_devolve_401(string metodo, string rota)
    {
        var resposta = await Enviar(new HttpMethod(metodo), rota, CorpoValido(), token: null);

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
        Assert.Equal("application/problem+json", resposta.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [MemberData(nameof(Rotas))]
    public async Task Token_de_Usuario_devolve_403(string metodo, string rota)
    {
        var resposta = await Enviar(new HttpMethod(metodo), rota, CorpoValido(), token: Token(Papeis.Usuario));

        Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
        Assert.Equal("application/problem+json", resposta.Content.Headers.ContentType?.MediaType);
        Assert.Empty((await _factory.Estantes.ListarAsync(null, 1, 100, default)).Itens); // nada foi criado
    }

    [Fact]
    public void Rotas_reais_de_estantes_estao_no_grupo_admin_com_a_politica()
    {
        var endpoints = _factory.Services.GetRequiredService<Microsoft.AspNetCore.Routing.EndpointDataSource>().Endpoints;
        var padroes = VerificadorRotasAdmin.RotasAdmin(endpoints).Select(r => r.RoutePattern.RawText!.TrimEnd('/')).ToHashSet(); // o MapPost("") do sub-grupo gera "/admin/estantes/"

        Assert.Contains("/admin/estantes", padroes);
        Assert.Contains("/admin/estantes/{id:guid}", padroes);
        Assert.Contains("/admin/estantes/{id:guid}/desativar", padroes);
        Assert.Contains("/admin/estantes/{id:guid}/reativar", padroes);
        Assert.Contains("/admin/estantes/{id:guid}/credenciais", padroes);
        Assert.Empty(VerificadorRotasAdmin.Violacoes(endpoints));
    }
}
