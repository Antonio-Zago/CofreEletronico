using System.Security.Claims;
using CofreEletronico.Application.Autenticacao.Portas;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace CofreEletronico.Infrastructure.Autenticacao;

/// <summary>
/// Access token JWT HS256 com as claims do contrato: sub, email, role (uma por papel), jti, iss e aud.
/// </summary>
internal sealed class GeradorAccessTokenJwt(IOptions<JwtOpcoes> opcoes, TimeProvider relogio) : IGeradorAccessToken
{
    private readonly JsonWebTokenHandler _handler = new();

    public AccessTokenGerado Gerar(UsuarioAutenticado usuario)
    {
        var o = opcoes.Value;

        // iat/nbf/exp do JWT têm resolução de segundos: truncar mantém ExpiraEm igual ao "exp" do token.
        var agora = DateTimeOffset.FromUnixTimeSeconds(relogio.GetUtcNow().ToUnixTimeSeconds());
        var expiraEm = agora + o.ValidadeAccessToken;

        var claims = new List<Claim>
        {
            new(ClaimsJwt.Sub, usuario.Id.ToString()),
            new(ClaimsJwt.Email, usuario.Email),
            new(ClaimsJwt.Jti, Guid.NewGuid().ToString()),
        };
        claims.AddRange(usuario.Papeis.Select(papel => new Claim(ClaimsJwt.Role, papel)));

        var descritor = new SecurityTokenDescriptor
        {
            Issuer = o.Emissor,
            Audience = o.Audiencia,
            Subject = new ClaimsIdentity(claims),
            IssuedAt = agora.UtcDateTime,
            NotBefore = agora.UtcDateTime,
            Expires = expiraEm.UtcDateTime,
            SigningCredentials = new SigningCredentials(o.CriarChave(), JwtOpcoes.Algoritmo),
            TokenType = JwtOpcoes.TipoToken,
        };

        return new AccessTokenGerado(_handler.CreateToken(descritor), expiraEm);
    }
}
