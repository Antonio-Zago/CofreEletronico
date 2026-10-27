using System.Text;
using System.Text.RegularExpressions;
using CofreEletronico.Domain.Estantes;

namespace CofreEletronico.Application.Mqtt;

/// <summary>
/// Tópicos do contrato MQTT (docs/ARQUITETURA.md, "Contrato MQTT"). O <c>deviceId</c> é validado com o mesmo padrão
/// do domínio (<see cref="Estante.DeviceIdPadrao"/>), o que impede injetar <c>+</c>, <c>#</c> ou <c>/</c> no tópico.
/// </summary>
public static partial class TopicosMqtt
{
    /// <summary>Limite de pacote do broker (<c>max_packet_size 4096</c> no mosquitto.conf).</summary>
    public const int TamanhoMaximoPacote = 4096;

    public const string Prefixo = "cofre";

    /// <summary>Todos os <c>ack</c> (ESP32 → Backend).</summary>
    public const string FiltroAck = Prefixo + "/+/ack";

    /// <summary>Todos os <c>estado</c> (ESP32 → Backend, retained).</summary>
    public const string FiltroEstado = Prefixo + "/+/estado";

    /// <summary>Todos os <c>online</c> (LWT + heartbeat, retained).</summary>
    public const string FiltroOnline = Prefixo + "/+/online";

    /// <summary><c>cofre/{deviceId}/cmd</c> (Backend → ESP32, nunca retained).</summary>
    public static string Comando(string deviceId) => Montar(deviceId, "cmd");

    /// <summary><c>cofre/{deviceId}/ack</c>.</summary>
    public static string Ack(string deviceId) => Montar(deviceId, "ack");

    /// <summary><c>cofre/{deviceId}/estado</c>.</summary>
    public static string Estado(string deviceId) => Montar(deviceId, "estado");

    /// <summary><c>cofre/{deviceId}/online</c>.</summary>
    public static string Online(string deviceId) => Montar(deviceId, "online");

    /// <summary>
    /// Filtro de assinatura válido: não vazio, <c>+</c> ocupando um nível inteiro, <c>#</c> só como último nível
    /// inteiro, sem caractere nulo.
    /// </summary>
    public static void ValidarFiltro(string filtro)
    {
        if (string.IsNullOrEmpty(filtro))
            throw new ArgumentException("O filtro MQTT não pode ser vazio.", nameof(filtro));
        if (filtro.Contains('\0') || Encoding.UTF8.GetByteCount(filtro) > ushort.MaxValue)
            throw new ArgumentException("O filtro MQTT é inválido.", nameof(filtro));

        var niveis = filtro.Split('/');
        for (var i = 0; i < niveis.Length; i++)
        {
            var nivel = niveis[i];
            if (nivel.Contains('#') && (nivel != "#" || i != niveis.Length - 1))
                throw new ArgumentException("No filtro MQTT, '#' só pode ser o último nível, sozinho.", nameof(filtro));
            if (nivel.Contains('+') && nivel != "+")
                throw new ArgumentException("No filtro MQTT, '+' precisa ocupar um nível inteiro.", nameof(filtro));
        }
    }

    /// <summary>
    /// <c>true</c> se o tópico casa com o filtro (regras do MQTT: <c>+</c> = um nível, <c>#</c> = este nível e os
    /// seguintes). Tópicos começando com <c>$</c> não casam com curinga no primeiro nível.
    /// </summary>
    public static bool Casa(string filtro, string topico)
    {
        var f = filtro.Split('/');
        var t = topico.Split('/');

        if (t.Length > 0 && t[0].StartsWith('$') && (f[0] == "+" || f[0] == "#"))
            return false;

        for (var i = 0; i < f.Length; i++)
        {
            if (f[i] == "#")
                return true;
            if (i >= t.Length)
                return false;
            if (f[i] != "+" && !string.Equals(f[i], t[i], StringComparison.Ordinal))
                return false;
        }

        return f.Length == t.Length;
    }

    /// <summary>
    /// Tamanho do pacote PUBLISH (MQTT 5, QoS 1, sem propriedades): cabeçalho fixo + tópico + id do pacote +
    /// tamanho das propriedades + payload. Comparado com <see cref="TamanhoMaximoPacote"/> antes de publicar.
    /// </summary>
    public static int TamanhoPacotePublicacao(string topico, int tamanhoPayload)
    {
        const int idPacote = 2;
        const int tamanhoPropriedades = 1; // varint 0: sem propriedades
        var restante = 2 + Encoding.UTF8.GetByteCount(topico) + idPacote + tamanhoPropriedades + tamanhoPayload;
        var bytesTamanho = restante < 128 ? 1 : restante < 16_384 ? 2 : restante < 2_097_152 ? 3 : 4;
        return 1 + bytesTamanho + restante;
    }

    private static string Montar(string deviceId, string sufixo)
    {
        if (deviceId is null || !DeviceIdRegex().IsMatch(deviceId))
            throw new ArgumentException("O deviceId deve ter o formato 'est-' seguido de 6 caracteres [a-z0-9].", nameof(deviceId));
        return $"{Prefixo}/{deviceId}/{sufixo}";
    }

    [GeneratedRegex(Estante.DeviceIdPadrao, RegexOptions.CultureInvariant)]
    private static partial Regex DeviceIdRegex();
}
