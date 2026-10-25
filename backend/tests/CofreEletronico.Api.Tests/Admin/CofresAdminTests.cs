using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CofreEletronico.Api.Tests.Infra;
using CofreEletronico.Application.Autenticacao.Portas;
using CofreEletronico.Application.Autorizacao;
using CofreEletronico.Application.Cofres.Portas;
using CofreEletronico.Domain.Estantes;
using Microsoft.Extensions.DependencyInjection;

namespace CofreEletronico.Api.Tests.Admin;

/// <summary>
/// T-0403/T-0405: /admin/estantes/{estanteId}/cofres pela Api real (JWT, autorização, endpoints e casos de uso reais;
/// repositórios em memória).
/// </summary>
public class CofresAdminTests : IClassFixture<ApiFactory>
{
    private static readonly string[] CamposCofre = ["id", "estanteId", "numero", "canalRele", "tamanho", "status", "ativo"];

    private readonly ApiFactory _factory;
    private readonly HttpClient _cliente;

    public CofresAdminTests(ApiFactory factory)
    {
        _factory = factory;
        _cliente = factory.CreateClient();
        factory.Estantes.Limpar(); // os testes da classe rodam em sequência, com a mesma fábrica
        factory.Cofres.Limpar();
    }

    private static string Base(Guid estanteId) => $"/admin/estantes/{estanteId}/cofres";

    private static string Rota(Cofre cofre, string sufixo = "") => $"{Base(cofre.EstanteId)}/{cofre.Id}{sufixo}";

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

    private static object Corpo(int numero = 1, int canalRele = 0, string tamanho = "medio") => new { numero, canalRele, tamanho };

    private static async Task<JsonElement> Json(HttpResponseMessage resposta) =>
        await resposta.Content.ReadFromJsonAsync<JsonElement>();

    private static string[] Propriedades(JsonElement objeto) => objeto.EnumerateObject().Select(p => p.Name).ToArray();

    private static async Task<string> AssertProblema(HttpResponseMessage resposta, HttpStatusCode status)
    {
        Assert.Equal(status, resposta.StatusCode);
        Assert.Equal("application/problem+json", resposta.Content.Headers.ContentType?.MediaType);
        return (await Json(resposta)).TryGetProperty("detail", out var detalhe) ? detalhe.GetString() ?? "" : "";
    }

    /// <summary>400 ValidationProblem; devolve o objeto <c>errors</c>.</summary>
    private static async Task<JsonElement> AssertValidacao(HttpResponseMessage resposta)
    {
        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.Equal("application/problem+json", resposta.Content.Headers.ContentType?.MediaType);
        return (await Json(resposta)).GetProperty("errors");
    }

    // ---------- POST ----------

    [Theory]
    [InlineData("pequeno")]
    [InlineData("medio")]
    [InlineData("grande")]
    public async Task Criar_devolve_201_com_Location_e_o_JSON_do_contrato(string tamanho)
    {
        var estante = _factory.Estantes.Adicionar("Alfa");

        var resposta = await Enviar(HttpMethod.Post, Base(estante.Id), Corpo(3, 15, tamanho));

        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        var corpo = await Json(resposta);
        Assert.Equal(CamposCofre, Propriedades(corpo));
        var id = corpo.GetProperty("id").GetGuid();
        Assert.Equal($"{Base(estante.Id)}/{id}", resposta.Headers.Location?.ToString());
        Assert.Equal(estante.Id, corpo.GetProperty("estanteId").GetGuid());
        Assert.Equal(3, corpo.GetProperty("numero").GetInt32());
        Assert.Equal(15, corpo.GetProperty("canalRele").GetInt32());
        Assert.Equal(tamanho, corpo.GetProperty("tamanho").GetString());
        Assert.Equal("livre", corpo.GetProperty("status").GetString());
        Assert.True(corpo.GetProperty("ativo").GetBoolean());
        Assert.NotNull(_factory.Cofres.Obter(id));
    }

    [Fact]
    public async Task Criar_em_estante_desativada_e_permitido()
    {
        var estante = _factory.Estantes.Adicionar("Alfa", ativa: false);

        var resposta = await Enviar(HttpMethod.Post, Base(estante.Id), Corpo());

        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
    }

    [Fact]
    public async Task Criar_com_campos_ausentes_devolve_400_com_erros_por_campo()
    {
        var estante = _factory.Estantes.Adicionar("Alfa");

        var resposta = await Enviar(HttpMethod.Post, Base(estante.Id), new { });

        var erros = await AssertValidacao(resposta);
        Assert.Equal(["numero", "canalRele", "tamanho"], Propriedades(erros));
        Assert.Empty(_factory.Cofres.Todos());
    }

    [Theory]
    [InlineData(0, 0, "medio", "numero")]
    [InlineData(-5, 0, "medio", "numero")]
    [InlineData(1, -1, "medio", "canalRele")]
    [InlineData(1, 16, "medio", "canalRele")]
    [InlineData(1, 0, "enorme", "tamanho")]
    [InlineData(1, 0, "Medio", "tamanho")]
    [InlineData(1, 0, "médio", "tamanho")]
    [InlineData(1, 0, "2", "tamanho")]
    [InlineData(1, 0, " ", "tamanho")]
    public async Task Criar_com_valor_invalido_devolve_400(int numero, int canal, string tamanho, string campo)
    {
        var estante = _factory.Estantes.Adicionar("Alfa");

        var resposta = await Enviar(HttpMethod.Post, Base(estante.Id), Corpo(numero, canal, tamanho));

        var erros = await AssertValidacao(resposta);
        Assert.Equal([campo], Propriedades(erros));
        Assert.Empty(_factory.Cofres.Todos());
    }

    [Fact]
    public async Task Criar_com_tamanho_numerico_no_JSON_devolve_400()
    {
        var estante = _factory.Estantes.Adicionar("Alfa");

        var resposta = await Enviar(HttpMethod.Post, Base(estante.Id), new { numero = 1, canalRele = 0, tamanho = 2 });

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.Empty(_factory.Cofres.Todos());
    }

    [Fact]
    public async Task Criar_em_estante_inexistente_devolve_404()
    {
        var detalhe = await AssertProblema(await Enviar(HttpMethod.Post, Base(Guid.NewGuid()), Corpo()), HttpStatusCode.NotFound);

        Assert.Equal("Estante não encontrada.", detalhe);
    }

    [Fact]
    public async Task Criar_com_numero_ja_usado_devolve_409()
    {
        var estante = _factory.Estantes.Adicionar("Alfa");
        _factory.Cofres.Adicionar(estante.Id, numero: 1, canal: 0);

        var detalhe = await AssertProblema(await Enviar(HttpMethod.Post, Base(estante.Id), Corpo(1, 5)), HttpStatusCode.Conflict);

        Assert.Contains("número", detalhe);
        Assert.Single(_factory.Cofres.Todos());
    }

    [Fact]
    public async Task Criar_com_canal_ja_usado_devolve_409()
    {
        var estante = _factory.Estantes.Adicionar("Alfa");
        _factory.Cofres.Adicionar(estante.Id, numero: 1, canal: 0);

        var detalhe = await AssertProblema(await Enviar(HttpMethod.Post, Base(estante.Id), Corpo(2, 0)), HttpStatusCode.Conflict);

        Assert.Contains("canal", detalhe);
    }

    [Theory]
    [InlineData(ResultadoGravacaoCofre.NumeroEmUso, "número")]
    [InlineData(ResultadoGravacaoCofre.CanalEmUso, "canal")]
    public async Task Criar_recusado_pelo_indice_unico_devolve_409(ResultadoGravacaoCofre recusa, string trecho)
    {
        var estante = _factory.Estantes.Adicionar("Alfa");
        _factory.Cofres.RecusaNaProximaGravacao = recusa;

        var detalhe = await AssertProblema(await Enviar(HttpMethod.Post, Base(estante.Id), Corpo()), HttpStatusCode.Conflict);

        Assert.Contains(trecho, detalhe);
        Assert.Empty(_factory.Cofres.Todos());
    }

    // ---------- GET ----------

    [Fact]
    public async Task Listar_devolve_os_cofres_da_estante_ordenados_por_numero()
    {
        var estante = _factory.Estantes.Adicionar("Alfa");
        _factory.Cofres.Adicionar(estante.Id, numero: 2, canal: 1, StatusCofre.Manutencao);
        _factory.Cofres.Adicionar(estante.Id, numero: 1, canal: 0, ativo: false);
        _factory.Cofres.Adicionar(_factory.Estantes.Adicionar("Bravo").Id, numero: 9, canal: 9);

        var resposta = await Enviar(HttpMethod.Get, Base(estante.Id));

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var itens = (await Json(resposta)).EnumerateArray().ToList();
        Assert.Equal([1, 2], itens.Select(i => i.GetProperty("numero").GetInt32()));
        Assert.All(itens, i => Assert.Equal(CamposCofre, Propriedades(i)));
        Assert.Equal("manutencao", itens[1].GetProperty("status").GetString());
        Assert.False(itens[0].GetProperty("ativo").GetBoolean());
    }

    [Fact]
    public async Task Listar_estante_sem_cofres_devolve_lista_vazia()
    {
        var estante = _factory.Estantes.Adicionar("Alfa");

        var resposta = await Enviar(HttpMethod.Get, Base(estante.Id));

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var corpo = await Json(resposta);
        Assert.Equal(JsonValueKind.Array, corpo.ValueKind);
        Assert.Empty(corpo.EnumerateArray());
    }

    [Fact]
    public async Task Listar_estante_inexistente_devolve_404()
    {
        await AssertProblema(await Enviar(HttpMethod.Get, Base(Guid.NewGuid())), HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Obter_devolve_200_com_os_campos_do_contrato()
    {
        var estante = _factory.Estantes.Adicionar("Alfa");
        var cofre = _factory.Cofres.Adicionar(estante.Id, numero: 4, canal: 7, StatusCofre.Ocupado);

        var resposta = await Enviar(HttpMethod.Get, Rota(cofre));

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var corpo = await Json(resposta);
        Assert.Equal(CamposCofre, Propriedades(corpo));
        Assert.Equal(cofre.Id, corpo.GetProperty("id").GetGuid());
        Assert.Equal("ocupado", corpo.GetProperty("status").GetString());
        Assert.Equal("medio", corpo.GetProperty("tamanho").GetString());
    }

    [Fact]
    public async Task Obter_cofre_de_outra_estante_devolve_404()
    {
        var cofre = _factory.Cofres.Adicionar(_factory.Estantes.Adicionar("Alfa").Id, 1, 0);
        var outra = _factory.Estantes.Adicionar("Bravo");

        var detalhe = await AssertProblema(await Enviar(HttpMethod.Get, $"{Base(outra.Id)}/{cofre.Id}"), HttpStatusCode.NotFound);

        Assert.Equal("Cofre não encontrado.", detalhe);
    }

    [Theory]
    [InlineData("/admin/estantes/nao-e-guid/cofres")]
    [InlineData("/admin/estantes/00000000-0000-0000-0000-000000000001/cofres/nao-e-guid")]
    public async Task Ids_que_nao_sao_guid_devolvem_404(string rota)
    {
        Assert.Equal(HttpStatusCode.NotFound, (await Enviar(HttpMethod.Get, rota)).StatusCode);
    }

    // ---------- PUT ----------

    [Theory]
    [InlineData(StatusCofre.Livre, "livre")]
    [InlineData(StatusCofre.Manutencao, "manutencao")]
    public async Task Atualizar_devolve_200_com_os_dados_novos(StatusCofre status, string texto)
    {
        var estante = _factory.Estantes.Adicionar("Alfa", ativa: false);
        var cofre = _factory.Cofres.Adicionar(estante.Id, 1, 0, status);

        var resposta = await Enviar(HttpMethod.Put, Rota(cofre), Corpo(8, 12, "grande"));

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var corpo = await Json(resposta);
        Assert.Equal(CamposCofre, Propriedades(corpo));
        Assert.Equal(8, corpo.GetProperty("numero").GetInt32());
        Assert.Equal(12, corpo.GetProperty("canalRele").GetInt32());
        Assert.Equal("grande", corpo.GetProperty("tamanho").GetString());
        Assert.Equal(texto, corpo.GetProperty("status").GetString());
        Assert.Equal(TamanhoCofre.Grande, cofre.Tamanho);
    }

    [Theory]
    [InlineData(StatusCofre.Reservado, "O cofre está reservado e não pode ser editado.")]
    [InlineData(StatusCofre.Ocupado, "O cofre está ocupado e não pode ser editado.")]
    public async Task Atualizar_reservado_ou_ocupado_devolve_409(StatusCofre status, string mensagem)
    {
        var cofre = _factory.Cofres.Adicionar(_factory.Estantes.Adicionar("Alfa").Id, 1, 0, status);

        var detalhe = await AssertProblema(await Enviar(HttpMethod.Put, Rota(cofre), Corpo(8, 12, "grande")), HttpStatusCode.Conflict);

        Assert.Equal(mensagem, detalhe);
        Assert.Equal(1, cofre.Numero);
    }

    [Fact]
    public async Task Atualizar_com_numero_ou_canal_de_outro_cofre_devolve_409()
    {
        var estante = _factory.Estantes.Adicionar("Alfa");
        _factory.Cofres.Adicionar(estante.Id, 1, 0);
        var cofre = _factory.Cofres.Adicionar(estante.Id, 2, 1);

        await AssertProblema(await Enviar(HttpMethod.Put, Rota(cofre), Corpo(1, 5)), HttpStatusCode.Conflict);
        await AssertProblema(await Enviar(HttpMethod.Put, Rota(cofre), Corpo(5, 0)), HttpStatusCode.Conflict);

        Assert.Equal(2, cofre.Numero);
        Assert.Equal(1, cofre.CanalRele);
    }

    [Fact]
    public async Task Atualizar_com_corpo_invalido_devolve_400()
    {
        var cofre = _factory.Cofres.Adicionar(_factory.Estantes.Adicionar("Alfa").Id, 1, 0);

        var resposta = await Enviar(HttpMethod.Put, Rota(cofre), new { numero = 0, canalRele = 99, tamanho = "xg" });

        Assert.Equal(["numero", "canalRele", "tamanho"], Propriedades(await AssertValidacao(resposta)));
    }

    [Fact]
    public async Task Atualizar_cofre_de_outra_estante_devolve_404_sem_alterar()
    {
        var cofre = _factory.Cofres.Adicionar(_factory.Estantes.Adicionar("Alfa").Id, numero: 1, canal: 0);
        var outra = _factory.Estantes.Adicionar("Bravo");

        var detalhe = await AssertProblema(
            await Enviar(HttpMethod.Put, $"{Base(outra.Id)}/{cofre.Id}", Corpo(8, 12, "grande")), HttpStatusCode.NotFound);

        Assert.Equal("Cofre não encontrado.", detalhe);
        Assert.Equal(1, cofre.Numero);
        Assert.Equal(0, cofre.CanalRele);
        Assert.Equal(TamanhoCofre.Medio, cofre.Tamanho);
        Assert.Equal(cofre.EstanteId, _factory.Cofres.Obter(cofre.Id)!.EstanteId);
    }

    [Fact]
    public async Task Atualizar_inexistente_devolve_404()
    {
        var estante = _factory.Estantes.Adicionar("Alfa");

        await AssertProblema(await Enviar(HttpMethod.Put, $"{Base(estante.Id)}/{Guid.NewGuid()}", Corpo()), HttpStatusCode.NotFound);
    }

    // ---------- desativar / reativar ----------

    [Fact]
    public async Task Desativar_devolve_200_e_e_idempotente()
    {
        var cofre = _factory.Cofres.Adicionar(_factory.Estantes.Adicionar("Alfa").Id, 1, 0);

        foreach (var _ in Enumerable.Range(0, 2))
        {
            var resposta = await Enviar(HttpMethod.Post, Rota(cofre, "/desativar"));

            Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
            Assert.False((await Json(resposta)).GetProperty("ativo").GetBoolean());
        }

        Assert.False(cofre.Ativo);
    }

    [Theory]
    [InlineData(StatusCofre.Reservado)]
    [InlineData(StatusCofre.Ocupado)]
    public async Task Desativar_reservado_ou_ocupado_devolve_409(StatusCofre status)
    {
        var cofre = _factory.Cofres.Adicionar(_factory.Estantes.Adicionar("Alfa").Id, 1, 0, status);

        var detalhe = await AssertProblema(await Enviar(HttpMethod.Post, Rota(cofre, "/desativar")), HttpStatusCode.Conflict);

        Assert.Contains("não pode ser desativado", detalhe);
        Assert.True(cofre.Ativo);
    }

    [Fact]
    public async Task Reativar_devolve_200_e_e_idempotente()
    {
        var cofre = _factory.Cofres.Adicionar(_factory.Estantes.Adicionar("Alfa").Id, 1, 0, ativo: false);

        foreach (var _ in Enumerable.Range(0, 2))
        {
            var resposta = await Enviar(HttpMethod.Post, Rota(cofre, "/reativar"));

            Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
            Assert.True((await Json(resposta)).GetProperty("ativo").GetBoolean());
        }
    }

    // ---------- manutenção (T-0405) ----------

    [Fact]
    public async Task Manutencao_e_retirada_devolvem_200_com_o_status_novo()
    {
        var cofre = _factory.Cofres.Adicionar(_factory.Estantes.Adicionar("Alfa").Id, 1, 0);

        var colocar = await Enviar(HttpMethod.Post, Rota(cofre, "/manutencao"));
        Assert.Equal(HttpStatusCode.OK, colocar.StatusCode);
        Assert.Equal("manutencao", (await Json(colocar)).GetProperty("status").GetString());

        var retirar = await Enviar(HttpMethod.Post, Rota(cofre, "/retirar-manutencao"));
        Assert.Equal(HttpStatusCode.OK, retirar.StatusCode);
        Assert.Equal("livre", (await Json(retirar)).GetProperty("status").GetString());
    }

    [Theory]
    [InlineData(StatusCofre.Reservado, "reservado")]
    [InlineData(StatusCofre.Ocupado, "ocupado")]
    [InlineData(StatusCofre.Manutencao, "em manutenção")]
    public async Task Manutencao_fora_de_livre_devolve_409(StatusCofre status, string texto)
    {
        var cofre = _factory.Cofres.Adicionar(_factory.Estantes.Adicionar("Alfa").Id, 1, 0, status);

        var detalhe = await AssertProblema(await Enviar(HttpMethod.Post, Rota(cofre, "/manutencao")), HttpStatusCode.Conflict);

        Assert.Equal($"O cofre está {texto} e não pode ser colocado em manutenção.", detalhe);
        Assert.Equal(status, cofre.Status);
    }

    [Theory]
    [InlineData(StatusCofre.Livre)]
    [InlineData(StatusCofre.Reservado)]
    [InlineData(StatusCofre.Ocupado)]
    public async Task Retirar_fora_de_manutencao_devolve_409(StatusCofre status)
    {
        var cofre = _factory.Cofres.Adicionar(_factory.Estantes.Adicionar("Alfa").Id, 1, 0, status);

        var detalhe = await AssertProblema(await Enviar(HttpMethod.Post, Rota(cofre, "/retirar-manutencao")), HttpStatusCode.Conflict);

        Assert.Contains("não pode ser retirado da manutenção", detalhe);
        Assert.Equal(status, cofre.Status);
    }

    [Theory]
    [InlineData("/desativar")]
    [InlineData("/reativar")]
    [InlineData("/manutencao")]
    [InlineData("/retirar-manutencao")]
    public async Task Acoes_em_cofre_inexistente_ou_de_outra_estante_devolvem_404(string acao)
    {
        var cofre = _factory.Cofres.Adicionar(_factory.Estantes.Adicionar("Alfa").Id, 1, 0);
        var outra = _factory.Estantes.Adicionar("Bravo");

        await AssertProblema(await Enviar(HttpMethod.Post, $"{Base(outra.Id)}/{cofre.Id}{acao}"), HttpStatusCode.NotFound);
        await AssertProblema(await Enviar(HttpMethod.Post, $"{Base(cofre.EstanteId)}/{Guid.NewGuid()}{acao}"), HttpStatusCode.NotFound);
    }

    // ---------- concorrência ----------

    [Theory]
    [InlineData("PUT", "", StatusCofre.Livre, true)]
    [InlineData("POST", "/desativar", StatusCofre.Livre, true)]
    [InlineData("POST", "/reativar", StatusCofre.Livre, false)]
    [InlineData("POST", "/manutencao", StatusCofre.Livre, true)]
    [InlineData("POST", "/retirar-manutencao", StatusCofre.Manutencao, true)]
    public async Task Conflito_de_concorrencia_devolve_409_generico(string metodo, string sufixo, StatusCofre status, bool ativo)
    {
        var cofre = _factory.Cofres.Adicionar(_factory.Estantes.Adicionar("Alfa").Id, 1, 0, status, ativo);
        _factory.Cofres.ConflitoNoSalvar = true;

        var detalhe = await AssertProblema(
            await Enviar(new HttpMethod(metodo), Rota(cofre, sufixo), metodo == "PUT" ? Corpo(2, 3) : null),
            HttpStatusCode.Conflict);

        Assert.Contains("alterado por outra operação", detalhe);
    }

    // ---------- autorização em todas as rotas ----------

    public static TheoryData<string, string> Rotas()
    {
        var b = Base(Guid.NewGuid());
        var c = $"{b}/{Guid.NewGuid()}";
        return new TheoryData<string, string>
        {
            { "POST", b },
            { "GET", b },
            { "GET", c },
            { "PUT", c },
            { "POST", $"{c}/desativar" },
            { "POST", $"{c}/reativar" },
            { "POST", $"{c}/manutencao" },
            { "POST", $"{c}/retirar-manutencao" },
        };
    }

    [Theory]
    [MemberData(nameof(Rotas))]
    public async Task Sem_token_devolve_401_ProblemDetails(string metodo, string rota)
    {
        var resposta = await Enviar(new HttpMethod(metodo), rota, Corpo(), token: null);

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
        Assert.Equal("application/problem+json", resposta.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [MemberData(nameof(Rotas))]
    public async Task Token_de_Usuario_devolve_403_ProblemDetails(string metodo, string rota)
    {
        var resposta = await Enviar(new HttpMethod(metodo), rota, Corpo(), token: Token(Papeis.Usuario));

        Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
        Assert.Equal("application/problem+json", resposta.Content.Headers.ContentType?.MediaType);
        Assert.Empty(_factory.Cofres.Todos());
    }

    [Fact]
    public void Rotas_reais_de_cofres_estao_no_grupo_admin_com_a_politica()
    {
        var endpoints = _factory.Services.GetRequiredService<Microsoft.AspNetCore.Routing.EndpointDataSource>().Endpoints;
        var padroes = VerificadorRotasAdmin.RotasAdmin(endpoints).Select(r => r.RoutePattern.RawText!.TrimEnd('/')).ToHashSet();

        const string b = "/admin/estantes/{estanteId:guid}/cofres";
        Assert.Contains(b, padroes);
        Assert.Contains($"{b}/{{cofreId:guid}}", padroes);
        Assert.Contains($"{b}/{{cofreId:guid}}/desativar", padroes);
        Assert.Contains($"{b}/{{cofreId:guid}}/reativar", padroes);
        Assert.Contains($"{b}/{{cofreId:guid}}/manutencao", padroes);
        Assert.Contains($"{b}/{{cofreId:guid}}/retirar-manutencao", padroes);
        Assert.Empty(VerificadorRotasAdmin.Violacoes(endpoints));
    }
}
