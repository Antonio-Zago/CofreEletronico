using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using CofreEletronico.Api.Tests.Infra;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace CofreEletronico.Api.Tests.Auth;

/// <summary>Validação do JWT pelo JwtBearer num endpoint protegido (que só existe nos testes).</summary>
public class EndpointProtegidoTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _cliente = factory.CreateClient();

    private async Task<HttpStatusCode> GetProtegido(string? token)
    {
        using var requisicao = new HttpRequestMessage(HttpMethod.Get, ApiFactory.RotaProtegida);
        if (token is not null)
            requisicao.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (await _cliente.SendAsync(requisicao)).StatusCode;
    }

    [Fact]
    public async Task Sem_token_devolve_401()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, await GetProtegido(null));
    }

    [Fact]
    public async Task Token_que_nao_e_jwt_devolve_401()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, await GetProtegido("isto-nao-e-um-jwt"));
    }

    [Fact]
    public async Task Token_valido_devolve_200()
    {
        Assert.Equal(HttpStatusCode.OK, await GetProtegido(TokensDeTeste.Criar()));
    }

    [Fact]
    public async Task Token_assinado_com_outra_chave_devolve_401()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, await GetProtegido(TokensDeTeste.Criar(chave: "outra-chave-qualquer-com-mais-de-32-bytes")));
    }

    [Fact]
    public async Task Token_de_outro_emissor_ou_audiencia_devolve_401()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, await GetProtegido(TokensDeTeste.Criar(emissor: "outro")));
        Assert.Equal(HttpStatusCode.Unauthorized, await GetProtegido(TokensDeTeste.Criar(audiencia: "outra")));
    }

    [Fact]
    public async Task Token_expirado_alem_da_tolerancia_devolve_401()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, await GetProtegido(TokensDeTeste.Criar(expira: DateTime.UtcNow.AddMinutes(-1))));
    }

    [Fact]
    public async Task Token_com_outro_algoritmo_devolve_401()
    {
        // EXATAMENTE a mesma chave da Api, mas HS512: a assinatura confere, então o 401 só pode vir
        // da restrição ValidAlgorithms = [HS256].
        Assert.Equal(HttpStatusCode.Unauthorized,
            await GetProtegido(TokensDeTeste.Criar(chave: ApiFactory.ChaveJwt, algoritmo: SecurityAlgorithms.HmacSha512)));
    }

    [Fact]
    public async Task Token_com_typ_diferente_de_JWT_devolve_401()
    {
        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "cofre-eletronico-api",
            Audience = "cofre-eletronico-app",
            Subject = new ClaimsIdentity([new Claim("sub", Guid.NewGuid().ToString())]),
            Expires = DateTime.UtcNow.AddMinutes(10),
            TokenType = "at+jwt",
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(ApiFactory.ChaveJwt)), SecurityAlgorithms.HmacSha256),
        });

        Assert.Equal(HttpStatusCode.Unauthorized, await GetProtegido(token));
    }

    [Fact]
    public async Task Token_sem_assinatura_devolve_401()
    {
        var semAssinatura = TokensDeTeste.SemAssinatura();

        Assert.Equal(HttpStatusCode.Unauthorized, await GetProtegido(semAssinatura));
    }
}
