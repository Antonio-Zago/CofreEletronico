using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace CofreEletronico.Infrastructure.Autenticacao;

/// <summary>
/// Seção <c>Jwt</c> da configuração. Emissor e Audiencia não são segredos (appsettings.json);
/// <see cref="ChaveAssinatura"/> é segredo e vem só de user-secrets ou da variável <c>Jwt__ChaveAssinatura</c>.
/// As validades seguem o contrato (docs/ARQUITETURA.md) e têm padrão em código.
/// </summary>
public sealed class JwtOpcoes
{
    public const string Secao = "Jwt";

    /// <summary>Tamanho mínimo da chave HS256, em bytes UTF-8 (contrato: 32 bytes ou mais).</summary>
    public const int ChaveTamanhoMinimoBytes = 32;

    /// <summary>Algoritmo único aceito na emissão e na validação.</summary>
    public const string Algoritmo = SecurityAlgorithms.HmacSha256;

    /// <summary>Tolerância de relógio na validação do access token.</summary>
    public static readonly TimeSpan ToleranciaRelogio = TimeSpan.FromSeconds(30);

    /// <summary>Valor do cabeçalho <c>typ</c> emitido e o único aceito na validação.</summary>
    public const string TipoToken = "JWT";

    public static readonly TimeSpan ValidadeAccessTokenMinima = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan ValidadeAccessTokenMaxima = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan ValidadeRefreshTokenMinima = TimeSpan.FromDays(1);
    public static readonly TimeSpan ValidadeRefreshTokenMaxima = TimeSpan.FromDays(30);
    public static readonly TimeSpan ValidadeSessaoMinima = TimeSpan.FromDays(1);
    public static readonly TimeSpan ValidadeSessaoMaxima = TimeSpan.FromDays(90);

    public string Emissor { get; set; } = string.Empty;

    public string Audiencia { get; set; } = string.Empty;

    /// <summary>Segredo HS256 (texto, interpretado como UTF-8). Nunca em appsettings.</summary>
    public string ChaveAssinatura { get; set; } = string.Empty;

    public TimeSpan ValidadeAccessToken { get; set; } = TimeSpan.FromMinutes(15);

    public TimeSpan ValidadeRefreshToken { get; set; } = TimeSpan.FromDays(30);

    /// <summary>Duração máxima da sessão desde o login (contrato: 90 dias), mesmo com rotações.</summary>
    public TimeSpan ValidadeSessao { get; set; } = TimeSpan.FromDays(90);

    public SymmetricSecurityKey CriarChave() => new(Encoding.UTF8.GetBytes(ChaveAssinatura));

    /// <summary>
    /// Parâmetros de validação do access token. Usados pelo JwtBearer da Api e pelos testes,
    /// para que emissão e validação não divirjam.
    /// </summary>
    public TokenValidationParameters CriarParametrosValidacao() => new()
    {
        ValidateIssuer = true,
        ValidIssuer = Emissor,
        ValidateAudience = true,
        ValidAudience = Audiencia,
        ValidateLifetime = true,
        RequireExpirationTime = true,
        ClockSkew = ToleranciaRelogio,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = CriarChave(),
        RequireSignedTokens = true,
        ValidAlgorithms = [Algoritmo],
        ValidTypes = [TipoToken],
        NameClaimType = ClaimsJwt.Sub,
        RoleClaimType = ClaimsJwt.Role,
    };
}

/// <summary>Nomes das claims do access token (contrato), sem o mapeamento para os tipos longos do .NET.</summary>
public static class ClaimsJwt
{
    public const string Sub = "sub";
    public const string Email = "email";
    public const string Role = "role";
    public const string Jti = "jti";
}

/// <summary>Validação das opções JWT na subida (ValidateOnStart): falha cedo com mensagem clara.</summary>
internal sealed class JwtOpcoesValidador : IValidateOptions<JwtOpcoes>
{
    public ValidateOptionsResult Validate(string? name, JwtOpcoes options)
    {
        var falhas = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Emissor))
            falhas.Add("Jwt:Emissor não configurado.");

        if (string.IsNullOrWhiteSpace(options.Audiencia))
            falhas.Add("Jwt:Audiencia não configurada.");

        if (string.IsNullOrEmpty(options.ChaveAssinatura))
        {
            falhas.Add(
                "Jwt:ChaveAssinatura não configurada. Em desenvolvimento use " +
                "'dotnet user-secrets set \"Jwt:ChaveAssinatura\" \"<chave>\" --project src/CofreEletronico.Api'; " +
                "em servidor, a variável de ambiente Jwt__ChaveAssinatura. Ver backend/README.md.");
        }
        else if (Encoding.UTF8.GetByteCount(options.ChaveAssinatura) < JwtOpcoes.ChaveTamanhoMinimoBytes)
        {
            falhas.Add($"Jwt:ChaveAssinatura deve ter no mínimo {JwtOpcoes.ChaveTamanhoMinimoBytes} bytes. Ver backend/README.md.");
        }

        // Tetos do contrato: a configuração pode encurtar as validades, nunca alongar.
        if (options.ValidadeAccessToken < JwtOpcoes.ValidadeAccessTokenMinima ||
            options.ValidadeAccessToken > JwtOpcoes.ValidadeAccessTokenMaxima)
        {
            falhas.Add($"Jwt:ValidadeAccessToken deve ficar entre {JwtOpcoes.ValidadeAccessTokenMinima} e " +
                       $"{JwtOpcoes.ValidadeAccessTokenMaxima} (1 a 15 minutos).");
        }

        if (options.ValidadeRefreshToken < JwtOpcoes.ValidadeRefreshTokenMinima ||
            options.ValidadeRefreshToken > JwtOpcoes.ValidadeRefreshTokenMaxima)
        {
            falhas.Add($"Jwt:ValidadeRefreshToken deve ficar entre {JwtOpcoes.ValidadeRefreshTokenMinima} e " +
                       $"{JwtOpcoes.ValidadeRefreshTokenMaxima} (1 a 30 dias).");
        }

        if (options.ValidadeSessao < JwtOpcoes.ValidadeSessaoMinima ||
            options.ValidadeSessao > JwtOpcoes.ValidadeSessaoMaxima)
        {
            falhas.Add($"Jwt:ValidadeSessao deve ficar entre {JwtOpcoes.ValidadeSessaoMinima} e " +
                       $"{JwtOpcoes.ValidadeSessaoMaxima} (1 a 90 dias).");
        }

        return falhas.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(falhas);
    }
}
