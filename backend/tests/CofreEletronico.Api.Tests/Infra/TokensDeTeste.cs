using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace CofreEletronico.Api.Tests.Infra;

/// <summary>Tokens montados à mão para os testes de borda do JwtBearer (chave, emissor, algoritmo, validade).</summary>
internal static class TokensDeTeste
{
    public const string Emissor = "cofre-eletronico-api";
    public const string Audiencia = "cofre-eletronico-app";

    public static string Criar(
        string chave = ApiFactory.ChaveJwt,
        string emissor = Emissor,
        string audiencia = Audiencia,
        string algoritmo = SecurityAlgorithms.HmacSha256,
        DateTime? expira = null,
        params string[] papeis)
    {
        var agora = DateTime.UtcNow;
        var fim = expira ?? agora.AddMinutes(15);
        var claims = new List<Claim> { new("sub", Guid.NewGuid().ToString()) };
        claims.AddRange((papeis.Length == 0 ? ["Usuario"] : papeis).Select(p => new Claim("role", p)));

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = emissor,
            Audience = audiencia,
            Subject = new ClaimsIdentity(claims),
            IssuedAt = fim.AddMinutes(-15),
            NotBefore = fim.AddMinutes(-15),
            Expires = fim,
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(chave)), algoritmo),
        });
    }

    /// <summary>Mesmo payload de um token válido, com cabeçalho <c>alg=none</c> e sem assinatura.</summary>
    public static string SemAssinatura(params string[] papeis)
    {
        var partes = Criar(papeis: papeis).Split('.');
        return $"{Base64UrlEncoder.Encode("{\"alg\":\"none\",\"typ\":\"JWT\"}")}.{partes[1]}.";
    }
}
