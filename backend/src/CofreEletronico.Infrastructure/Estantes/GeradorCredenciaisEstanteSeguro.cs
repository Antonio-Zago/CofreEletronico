using System.Security.Cryptography;
using CofreEletronico.Application.Estantes.Portas;

namespace CofreEletronico.Infrastructure.Estantes;

/// <summary>
/// Credenciais das estantes com <see cref="RandomNumberGenerator"/>. <see cref="RandomNumberGenerator.GetString"/>
/// sorteia cada caractere de modo uniforme (rejeição, sem viés de módulo).
/// </summary>
internal sealed class GeradorCredenciaisEstanteSeguro : IGeradorCredenciaisEstante
{
    public const string PrefixoDeviceId = "est-";
    public const int SufixoDeviceIdTamanho = 6;
    public const string AlfabetoDeviceId = "abcdefghijklmnopqrstuvwxyz0123456789";

    public const int SegredoHmacTamanhoBytes = 32;

    /// <summary>24 caracteres de 62 símbolos: ~143 bits de entropia.</summary>
    public const int SenhaMqttTamanho = 24;

    /// <summary>Só letras e dígitos: dispensa escape no shell, no <c>mosquitto_passwd</c> e no firmware.</summary>
    public const string AlfabetoSenhaMqtt = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

    public string GerarDeviceId() =>
        PrefixoDeviceId + RandomNumberGenerator.GetString(AlfabetoDeviceId, SufixoDeviceIdTamanho);

    public byte[] GerarSegredoHmac() => RandomNumberGenerator.GetBytes(SegredoHmacTamanhoBytes);

    public string GerarSenhaMqtt() => RandomNumberGenerator.GetString(AlfabetoSenhaMqtt, SenhaMqttTamanho);
}
