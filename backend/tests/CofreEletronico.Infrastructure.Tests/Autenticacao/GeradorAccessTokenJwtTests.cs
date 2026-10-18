using System.Text;
using CofreEletronico.Application.Autenticacao.Portas;
using CofreEletronico.Infrastructure.Autenticacao;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace CofreEletronico.Infrastructure.Tests.Autenticacao;

public class GeradorAccessTokenJwtTests
{
    private static readonly UsuarioAutenticado Usuario =
        new(Guid.NewGuid(), "ana@exemplo.com", ["Usuario", "Admin"]);

    private static (AccessTokenGerado Token, JwtOpcoes Opcoes) Gerar(DateTimeOffset agora)
    {
        using var provider = InfrastructureFixture.CriarProvider(relogio: new RelogioFixo(agora));
        var gerador = provider.GetRequiredService<IGeradorAccessToken>();
        var opcoes = provider.GetRequiredService<IOptions<JwtOpcoes>>().Value;
        return (gerador.Gerar(Usuario), opcoes);
    }

    [Fact]
    public async Task Token_tem_as_claims_do_contrato_e_valida_com_a_mesma_chave()
    {
        var (gerado, opcoes) = Gerar(DateTimeOffset.UtcNow);

        var validacao = await new JsonWebTokenHandler().ValidateTokenAsync(gerado.Token, opcoes.CriarParametrosValidacao());

        Assert.True(validacao.IsValid, validacao.Exception?.Message);
        var jwt = (JsonWebToken)validacao.SecurityToken;

        Assert.Equal(Usuario.Id.ToString(), jwt.Subject);
        Assert.Equal("ana@exemplo.com", jwt.GetClaim("email").Value);
        Assert.Equal(["Admin", "Usuario"], jwt.Claims.Where(c => c.Type == "role").Select(c => c.Value).Order());
        Assert.True(Guid.TryParse(jwt.Id, out _));
        Assert.Equal(InfrastructureFixture.Emissor, jwt.Issuer);
        Assert.Equal([InfrastructureFixture.Audiencia], jwt.Audiences);

        // Identidade usa "sub" como nome e "role" como papel (sem mapeamento para os tipos longos do .NET).
        Assert.Equal(Usuario.Id.ToString(), validacao.ClaimsIdentity.Name);
        Assert.Equal("role", validacao.ClaimsIdentity.RoleClaimType);
    }

    [Fact]
    public void Token_usa_HS256_e_expira_em_15_minutos()
    {
        var agora = new DateTimeOffset(2026, 10, 17, 12, 0, 0, 500, TimeSpan.Zero);

        var (gerado, _) = Gerar(agora);
        var jwt = new JsonWebToken(gerado.Token);

        Assert.Equal("HS256", jwt.Alg);
        Assert.Equal("JWT", jwt.Typ);
        var emitidoEm = new DateTimeOffset(2026, 10, 17, 12, 0, 0, TimeSpan.Zero); // truncado ao segundo
        Assert.Equal(emitidoEm.UtcDateTime, jwt.IssuedAt);
        Assert.Equal(emitidoEm.AddMinutes(15).UtcDateTime, jwt.ValidTo);
        Assert.Equal(emitidoEm.AddMinutes(15), gerado.ExpiraEm);
        Assert.Equal(TimeSpan.Zero, gerado.ExpiraEm.Offset);
    }

    [Fact]
    public async Task Token_assinado_com_outra_chave_e_rejeitado()
    {
        var (gerado, opcoes) = Gerar(DateTimeOffset.UtcNow);
        var parametros = opcoes.CriarParametrosValidacao();
        parametros.IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("outra-chave-qualquer-com-mais-de-32-bytes"));

        var validacao = await new JsonWebTokenHandler().ValidateTokenAsync(gerado.Token, parametros);

        Assert.False(validacao.IsValid);
    }

    [Fact]
    public async Task Token_expirado_alem_da_tolerancia_e_rejeitado()
    {
        var (gerado, opcoes) = Gerar(DateTimeOffset.UtcNow.AddMinutes(-16));

        var validacao = await new JsonWebTokenHandler().ValidateTokenAsync(gerado.Token, opcoes.CriarParametrosValidacao());

        Assert.False(validacao.IsValid);
        Assert.IsType<SecurityTokenExpiredException>(validacao.Exception);
    }

    [Fact]
    public void Parametros_de_validacao_seguem_o_contrato()
    {
        var (_, opcoes) = Gerar(DateTimeOffset.UtcNow);

        var parametros = opcoes.CriarParametrosValidacao();

        Assert.True(parametros.ValidateIssuer);
        Assert.True(parametros.ValidateAudience);
        Assert.True(parametros.ValidateLifetime);
        Assert.True(parametros.ValidateIssuerSigningKey);
        Assert.Equal(["HS256"], parametros.ValidAlgorithms);
        Assert.Equal(["JWT"], parametros.ValidTypes);
        Assert.Equal(TimeSpan.FromSeconds(30), parametros.ClockSkew);
        Assert.Equal("sub", parametros.NameClaimType);
        Assert.Equal("role", parametros.RoleClaimType);
    }
}
