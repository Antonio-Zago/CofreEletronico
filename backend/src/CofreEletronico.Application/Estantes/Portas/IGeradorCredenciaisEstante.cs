namespace CofreEletronico.Application.Estantes.Portas;

/// <summary>
/// Gera os valores aleatórios das credenciais de uma estante (gerador criptográfico na implementação).
/// Fica atrás de uma porta para manter a aleatoriedade fora dos casos de uso e permitir testes determinísticos.
/// </summary>
public interface IGeradorCredenciaisEstante
{
    /// <summary><c>est-</c> + 6 caracteres <c>[a-z0-9]</c> (contrato).</summary>
    string GerarDeviceId();

    /// <summary>Segredo HMAC-SHA256 de 32 bytes.</summary>
    byte[] GerarSegredoHmac();

    /// <summary>Senha MQTT de 24 caracteres <c>[A-Za-z0-9]</c> (seguros para shell, mosquitto_passwd e firmware).</summary>
    string GerarSenhaMqtt();
}
