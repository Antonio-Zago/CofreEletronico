using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CofreEletronico.Domain.Estantes;

namespace CofreEletronico.Application.Comandos;

/// <summary>
/// Monta e assina o comando MQTT de <c>cofre/{deviceId}/cmd</c> no formato canônico do contrato
/// (docs/ARQUITETURA.md, "Formato canônico da assinatura"). Função pura: sem I/O, sem relógio e sem aleatoriedade
/// (commandId e ts chegam prontos), para que o vetor de teste do contrato seja reproduzível.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Mensagem assinada: <c>deviceId|commandId|cofre|canal|acao|ts</c> em UTF-8; o <c>deviceId</c> não vai no
/// payload.</item>
/// <item>Chave: os bytes do segredo da estante (nunca o texto hex).</item>
/// <item>Assinatura: HMAC-SHA256 em hex minúsculo (64 caracteres).</item>
/// <item>Números sempre com <see cref="CultureInfo.InvariantCulture"/> (a cultura do thread não altera os bytes).</item>
/// <item>JSON com exatamente <c>commandId</c>, <c>cofre</c>, <c>canal</c>, <c>acao</c>, <c>ts</c> e
/// <c>assinatura</c>; números como número JSON.</item>
/// </list>
/// Entradas fora do contrato lançam <see cref="ArgumentException"/>: elas vêm do banco (que já tem CHECKs), então
/// indicam erro de programação, não do usuário. As mensagens não contêm o segredo.
/// O segredo não é guardado nem copiado: quem chama continua dono do array e o zera depois.
/// </remarks>
public static partial class MontadorComando
{
    /// <summary>Separador dos campos da mensagem canônica.</summary>
    public const char Separador = '|';

    /// <summary>Tamanho da assinatura em hex (HMAC-SHA256 = 32 bytes).</summary>
    public const int TamanhoAssinaturaHex = 64;

    /// <summary>Texto do contrato para a ação.</summary>
    public static string TextoAcao(AcaoComando acao) => acao switch
    {
        AcaoComando.Abrir => "abrir",
        _ => throw new ArgumentOutOfRangeException(nameof(acao), "Ação de comando fora do contrato."),
    };

    /// <summary>Mensagem canônica <c>deviceId|commandId|cofre|canal|acao|ts</c> (valida as entradas).</summary>
    public static string MensagemCanonica(string deviceId, Guid commandId, int cofre, int canal, AcaoComando acao, long ts)
    {
        ValidarCampos(deviceId, commandId, cofre, canal, acao, ts);
        return Formatar(deviceId, commandId, cofre, canal, acao, ts);
    }

    /// <summary>
    /// Valida, assina e serializa. O <paramref name="segredo"/> (exatamente 32 bytes) só é lido; não é copiado nem guardado.
    /// </summary>
    /// <exception cref="ArgumentException">Algum campo fora do contrato.</exception>
    public static ComandoAssinado Montar(
        string deviceId, Guid commandId, int cofre, int canal, AcaoComando acao, long ts, ReadOnlySpan<byte> segredo)
    {
        ValidarCampos(deviceId, commandId, cofre, canal, acao, ts);
        if (segredo.Length != Estante.SegredoHmacTamanhoBytes)
        {
            throw new ArgumentException(
                $"O segredo HMAC deve ter exatamente {Estante.SegredoHmacTamanhoBytes} bytes.", nameof(segredo));
        }

        var mensagem = Formatar(deviceId, commandId, cofre, canal, acao, ts);
        var assinatura = Convert.ToHexString(HMACSHA256.HashData(segredo, Encoding.UTF8.GetBytes(mensagem)))
            .ToLowerInvariant();
        var payload = SerializarPayload(commandId, cofre, canal, acao, ts, assinatura);

        return new ComandoAssinado(commandId, deviceId, cofre, canal, acao, ts, mensagem, assinatura, payload);
    }

    private static string Formatar(string deviceId, Guid commandId, int cofre, int canal, AcaoComando acao, long ts) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{deviceId}{Separador}{FormatarCommandId(commandId)}{Separador}{cofre}{Separador}{canal}{Separador}{TextoAcao(acao)}{Separador}{ts}");

    /// <summary>Formato "D": 8-4-4-4-12, minúsculo, 36 caracteres (independe de cultura).</summary>
    private static string FormatarCommandId(Guid commandId) => commandId.ToString("D", CultureInfo.InvariantCulture);

    private static byte[] SerializarPayload(Guid commandId, int cofre, int canal, AcaoComando acao, long ts, string assinatura)
    {
        // Utf8JsonWriter escreve números no formato JSON (invariante), sem depender da cultura do thread.
        var buffer = new ArrayBufferWriter<byte>(256);
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteString("commandId", FormatarCommandId(commandId));
            json.WriteNumber("cofre", cofre);
            json.WriteNumber("canal", canal);
            json.WriteString("acao", TextoAcao(acao));
            json.WriteNumber("ts", ts);
            json.WriteString("assinatura", assinatura);
            json.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    private static void ValidarCampos(string deviceId, Guid commandId, int cofre, int canal, AcaoComando acao, long ts)
    {
        if (deviceId is null || !DeviceIdRegex().IsMatch(deviceId))
            throw new ArgumentException("O deviceId deve ter o formato 'est-' seguido de 6 caracteres [a-z0-9].", nameof(deviceId));
        if (commandId == Guid.Empty)
            throw new ArgumentException("O commandId não pode ser vazio.", nameof(commandId));
        if (cofre <= 0)
            throw new ArgumentOutOfRangeException(nameof(cofre), "O número do cofre deve ser maior que zero.");
        if (canal < Cofre.CanalMinimo || canal > Cofre.CanalMaximo)
            throw new ArgumentOutOfRangeException(nameof(canal), $"O canal deve estar entre {Cofre.CanalMinimo} e {Cofre.CanalMaximo}.");
        if (!Enum.IsDefined(acao))
            throw new ArgumentOutOfRangeException(nameof(acao), "Ação de comando fora do contrato.");
        if (ts <= 0)
            throw new ArgumentOutOfRangeException(nameof(ts), "O ts deve ser um instante Unix positivo, em segundos.");
    }

    [GeneratedRegex(Estante.DeviceIdPadrao, RegexOptions.CultureInvariant)]
    private static partial Regex DeviceIdRegex();
}

/// <summary>
/// Comando montado e assinado por <see cref="MontadorComando.Montar"/>. Não contém o segredo.
/// <b>Não registre em log nem desestruture este tipo</b> (ex.: <c>{@Comando}</c> no Serilog): para log, use os
/// identificadores (<see cref="CommandId"/>, <see cref="DeviceId"/>, <see cref="Cofre"/>, <see cref="Canal"/>).
/// <see cref="MensagemCanonica"/> e <see cref="Assinatura"/> são internas (visíveis só aos testes da Application), e
/// <see cref="ToString"/> mostra só os identificadores (nem a mensagem canônica, nem a assinatura, nem o payload).
/// </summary>
public sealed class ComandoAssinado
{
    private readonly byte[] _payload;

    internal ComandoAssinado(
        Guid commandId, string deviceId, int cofre, int canal, AcaoComando acao, long ts,
        string mensagemCanonica, string assinatura, byte[] payload)
    {
        CommandId = commandId;
        DeviceId = deviceId;
        Cofre = cofre;
        Canal = canal;
        Acao = acao;
        Ts = ts;
        MensagemCanonica = mensagemCanonica;
        Assinatura = assinatura;
        _payload = payload;
    }

    public Guid CommandId { get; }

    public string DeviceId { get; }

    public int Cofre { get; }

    public int Canal { get; }

    public AcaoComando Acao { get; }

    /// <summary>Segundos Unix UTC.</summary>
    public long Ts { get; }

    /// <summary><c>deviceId|commandId|cofre|canal|acao|ts</c>. Não registre em log.</summary>
    internal string MensagemCanonica { get; }

    /// <summary>HMAC-SHA256 em hex minúsculo (64 caracteres). Não registre em log.</summary>
    internal string Assinatura { get; }

    /// <summary>JSON do comando em UTF-8 (somente leitura), pronto para <c>IClienteMqtt.PublicarComandoAsync</c>.</summary>
    /// <remarks>Interno pelo mesmo motivo de <see cref="Assinatura"/>: o JSON contém a assinatura.</remarks>
    internal ReadOnlyMemory<byte> Payload => _payload;

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture,
            $"ComandoAssinado {{ CommandId = {CommandId:D}, DeviceId = {DeviceId}, Cofre = {Cofre}, Canal = {Canal}, Ts = {Ts} }}");
}
