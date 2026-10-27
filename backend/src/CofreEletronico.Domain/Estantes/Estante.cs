using System.Text.RegularExpressions;
using CofreEletronico.Domain.Common;

namespace CofreEletronico.Domain.Estantes;

/// <summary>
/// Estante física com um ESP32 (contrato em docs/ARQUITETURA.md, tabela <c>estantes</c>).
/// <see cref="DeviceId"/> identifica a estante nos tópicos MQTT e é o username do ESP32.
/// O segredo HMAC (<see cref="ObterSegredoHmac"/>) assina os comandos de abertura; fica em claro só em memória (cifrado no banco).
/// O device_id e o segredo são gerados fora do domínio; aqui só se valida o que chega.
/// <see cref="Ativa"/> = false é a exclusão lógica.
/// </summary>
public sealed partial class Estante : Entity
{
    public const int NomeTamanhoMaximo = 100;
    public const int EnderecoTamanhoMaximo = 200;

    /// <summary>Tamanho fixo do device_id: <c>est-</c> + 6 caracteres.</summary>
    public const int DeviceIdTamanho = 10;

    /// <summary>
    /// Tamanho exato do segredo HMAC-SHA256, em bytes (contrato: "os 32 bytes do segredo da estante"; o firmware usa
    /// exatamente esses 32 bytes como chave).
    /// </summary>
    public const int SegredoHmacTamanhoBytes = 32;

    /// <summary>Padrão do device_id (contrato). Usa <c>\A</c> e <c>\z</c> para não aceitar quebra de linha no fim.</summary>
    public const string DeviceIdPadrao = @"\Aest-[a-z0-9]{6}\z";

    // Segredo em claro. Mapeado pelo EF como "SegredoHmac" (propriedade privada, acesso pelo campo).
    // A propriedade é privada para que serializadores (que só leem públicas) não a vejam; para fora só saem cópias.
    private byte[] _segredoHmac = [];

    private byte[] SegredoHmac
    {
        get => _segredoHmac;
        set => _segredoHmac = value;
    }

    private Estante(
        Guid id, string nome, string endereco, double latitude, double longitude,
        string deviceId, byte[] segredoHmac, DateTimeOffset criadoEm)
        : base(id)
    {
        Nome = nome;
        Endereco = endereco;
        Latitude = latitude;
        Longitude = longitude;
        DeviceId = deviceId;
        _segredoHmac = segredoHmac;
        CriadoEm = criadoEm;
        Ativa = true;
        Online = false;
    }

    // Materialização pelo EF Core.
    private Estante()
    {
    }

    public string Nome { get; private set; } = string.Empty;

    public string Endereco { get; private set; } = string.Empty;

    public double Latitude { get; private set; }

    public double Longitude { get; private set; }

    public string DeviceId { get; private set; } = string.Empty;

    /// <summary>
    /// Cópia do segredo HMAC em claro (alterar o array devolvido não afeta a entidade).
    /// É método, e não propriedade, de propósito: serializadores (System.Text.Json, logs estruturados)
    /// ignoram métodos, então o segredo não vaza ao serializar a entidade por engano.
    /// </summary>
    /// <remarks>
    /// As cópias em memória (esta, a do campo e as do EF/cifrador) não são zeradas depois do uso:
    /// limitação aceita no .NET (GC pode mover/copiar arrays e não há garantia de zeragem).
    /// </remarks>
    public byte[] ObterSegredoHmac() => (byte[])_segredoHmac.Clone();

    public bool Online { get; private set; }

    public DateTimeOffset? UltimoHeartbeat { get; private set; }

    public bool Ativa { get; private set; }

    public DateTimeOffset CriadoEm { get; private set; }

    /// <summary>Cria uma estante ativa e offline (fica online no primeiro heartbeat).</summary>
    public static Estante Criar(
        string nome, string endereco, double latitude, double longitude,
        string deviceId, byte[] segredoHmac, DateTimeOffset criadoEm)
    {
        var nomeValido = ValidarTexto(nome, NomeTamanhoMaximo, "nome");
        var enderecoValido = ValidarTexto(endereco, EnderecoTamanhoMaximo, "endereço");
        ValidarCoordenadas(latitude, longitude);

        if (deviceId is null || !DeviceIdRegex().IsMatch(deviceId))
            throw new ExcecaoDominio("O device_id deve ter o formato 'est-' seguido de 6 caracteres [a-z0-9].");

        ValidarSegredoHmac(segredoHmac);

        return new Estante(
            Guid.NewGuid(), nomeValido, enderecoValido, latitude, longitude,
            deviceId, (byte[])segredoHmac.Clone(), criadoEm.ToUniversalTime());
    }

    /// <summary>Atualiza os dados cadastrais. device_id e segredo não mudam por aqui.</summary>
    public void Atualizar(string nome, string endereco, double latitude, double longitude)
    {
        var nomeValido = ValidarTexto(nome, NomeTamanhoMaximo, "nome");
        var enderecoValido = ValidarTexto(endereco, EnderecoTamanhoMaximo, "endereço");
        ValidarCoordenadas(latitude, longitude);

        Nome = nomeValido;
        Endereco = enderecoValido;
        Latitude = latitude;
        Longitude = longitude;
    }

    /// <summary>
    /// Troca o segredo HMAC (regeneração de credenciais): o anterior deixa de valer assim que a troca for salva.
    /// Mesma validação do cadastro. Guarda uma cópia (alterar depois o array recebido não afeta a entidade).
    /// O device_id não muda.
    /// </summary>
    public void SubstituirSegredoHmac(byte[] novoSegredo)
    {
        ValidarSegredoHmac(novoSegredo);
        _segredoHmac = (byte[])novoSegredo.Clone();
    }

    /// <summary>Exclusão lógica. Idempotente.</summary>
    public void Desativar() => Ativa = false;

    /// <summary>Desfaz a exclusão lógica. Idempotente.</summary>
    public void Reativar() => Ativa = true;

    /// <summary>
    /// Heartbeat recebido do ESP32: marca online. <see cref="UltimoHeartbeat"/> só avança
    /// (um heartbeat atrasado, fora de ordem, não faz o instante voltar).
    /// </summary>
    public void RegistrarHeartbeat(DateTimeOffset agora)
    {
        var agoraUtc = agora.ToUniversalTime();
        if (UltimoHeartbeat is null || agoraUtc > UltimoHeartbeat)
            UltimoHeartbeat = agoraUtc;

        Online = true;
    }

    /// <summary>LWT "offline" ou heartbeat vencido. Mantém o último heartbeat para diagnóstico.</summary>
    public void MarcarOffline() => Online = false;

    private static string ValidarTexto(string? valor, int tamanhoMaximo, string campo)
    {
        var aparado = valor?.Trim();
        if (string.IsNullOrEmpty(aparado))
            throw new ExcecaoDominio($"O {campo} da estante é obrigatório.");
        if (aparado.Length > tamanhoMaximo)
            throw new ExcecaoDominio($"O {campo} da estante deve ter no máximo {tamanhoMaximo} caracteres.");
        return aparado;
    }

    private static void ValidarSegredoHmac(byte[]? segredoHmac)
    {
        if (segredoHmac is null || segredoHmac.Length != SegredoHmacTamanhoBytes)
            throw new ExcecaoDominio($"O segredo HMAC deve ter exatamente {SegredoHmacTamanhoBytes} bytes.");
    }

    private static void ValidarCoordenadas(double latitude, double longitude)
    {
        // Comparações no sentido positivo para que NaN também seja rejeitado.
        if (!(latitude >= -90 && latitude <= 90))
            throw new ExcecaoDominio("A latitude deve estar entre -90 e 90.");
        if (!(longitude >= -180 && longitude <= 180))
            throw new ExcecaoDominio("A longitude deve estar entre -180 e 180.");
    }

    [GeneratedRegex(DeviceIdPadrao, RegexOptions.CultureInvariant)]
    private static partial Regex DeviceIdRegex();
}
