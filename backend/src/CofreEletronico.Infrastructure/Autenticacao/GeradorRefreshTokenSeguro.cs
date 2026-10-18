using System.Security.Cryptography;
using System.Text;
using CofreEletronico.Application.Autenticacao.Portas;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace CofreEletronico.Infrastructure.Autenticacao;

/// <summary>
/// Refresh token opaco: 32 bytes de <see cref="RandomNumberGenerator"/> em base64url (43 caracteres).
/// O banco guarda só o SHA-256 em hex minúsculo (64 caracteres). A busca é feita pelo hash; como o valor
/// tem 256 bits de entropia, não há o que adivinhar pelo tempo da consulta.
/// </summary>
internal sealed class GeradorRefreshTokenSeguro(IOptions<JwtOpcoes> opcoes) : IGeradorRefreshToken
{
    public const int TamanhoBytes = 32;

    /// <summary>Tamanho do hash em hex (SHA-256 = 32 bytes = 64 caracteres).</summary>
    public const int HashTamanho = 64;

    public TimeSpan Validade => opcoes.Value.ValidadeRefreshToken;

    public TimeSpan ValidadeSessao => opcoes.Value.ValidadeSessao;

    public RefreshTokenGerado Gerar()
    {
        var valor = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(TamanhoBytes));
        return new RefreshTokenGerado(valor, CalcularHash(valor));
    }

    public string CalcularHash(string valor) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(valor))).ToLowerInvariant();
}
