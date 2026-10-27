using CofreEletronico.Application.Comandos.Portas;
using CofreEletronico.Application.Mqtt.Portas;

namespace CofreEletronico.Application.Tests.Comandos;

/// <summary>
/// Cofres por estante em memória. Devolve um array de segredo novo a cada leitura (como a projeção EF) e guarda as
/// referências entregues, para o teste conferir que foram zeradas.
/// </summary>
internal sealed class DadosComandoRepositorioFalso : IDadosComandoRepositorio
{
    private readonly Dictionary<(Guid EstanteId, Guid CofreId), (string DeviceId, int Numero, int Canal, byte[] Segredo)> _cofres = [];

    public List<byte[]> SegredosEntregues { get; } = [];

    public void Adicionar(Guid estanteId, Guid cofreId, string deviceId, int numero, int canal, byte[] segredo) =>
        _cofres[(estanteId, cofreId)] = (deviceId, numero, canal, (byte[])segredo.Clone());

    public Task<DadosComando?> ObterAsync(Guid estanteId, Guid cofreId, CancellationToken cancellationToken)
    {
        if (!_cofres.TryGetValue((estanteId, cofreId), out var c))
            return Task.FromResult<DadosComando?>(null);

        var copia = (byte[])c.Segredo.Clone();
        SegredosEntregues.Add(copia);
        return Task.FromResult<DadosComando?>(new DadosComando(c.DeviceId, c.Numero, c.Canal, copia));
    }
}

/// <summary>Cliente MQTT falso: conta as publicações, guarda o payload e pode lançar a exceção configurada.</summary>
internal sealed class ClienteMqttFalso : IClienteMqtt
{
    public bool Habilitado { get; set; } = true;

    public bool Conectado { get; set; } = true;

    public Exception? FalhaAoPublicar { get; set; }

    /// <summary>Executado antes de "publicar" (ex.: conferir o estado do segredo nesse instante).</summary>
    public Action? AoPublicar { get; set; }

    public List<(string DeviceId, byte[] Payload)> Publicados { get; } = [];

    public int Chamadas { get; private set; }

    /// <summary>Token recebido na última publicação.</summary>
    public CancellationToken TokenRecebido { get; private set; }

    /// <summary>Simula o tempo do envio (ex.: o cliente cancela a requisição enquanto o PUBLISH está em andamento).</summary>
    public Func<CancellationToken, Task>? DuranteEnvio { get; set; }

    public async Task PublicarComandoAsync(string deviceId, ReadOnlyMemory<byte> payload, CancellationToken ct)
    {
        Chamadas++;
        TokenRecebido = ct;
        AoPublicar?.Invoke();
        if (DuranteEnvio is not null)
            await DuranteEnvio(ct);
        if (FalhaAoPublicar is not null)
            throw FalhaAoPublicar;

        Publicados.Add((deviceId, payload.ToArray()));
    }

    public Task<IDisposable> AssinarAsync(string filtro, Func<MensagemMqtt, CancellationToken, Task> tratador, CancellationToken ct) =>
        throw new NotSupportedException();
}
