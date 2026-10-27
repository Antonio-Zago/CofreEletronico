using CofreEletronico.Application.Mqtt.Portas;

namespace CofreEletronico.Infrastructure.Mqtt;

/// <summary>
/// Adaptador fino sobre o cliente MQTTnet (uma conexão, sem lógica de reconexão). Isola o MQTTnet para que o
/// <see cref="ServicoMqtt"/> (reconexão, assinaturas, despacho) seja testado com um fake.
/// </summary>
internal interface IAdaptadorMqtt
{
    bool Conectado { get; }

    /// <summary>A conexão estabelecida caiu (não dispara para falha ao conectar).</summary>
    event Func<DesconexaoMqtt, Task>? Desconectado;

    /// <summary>Mensagem recebida (payload já copiado).</summary>
    event Func<MensagemMqtt, Task>? MensagemRecebida;

    /// <summary>
    /// Conecta (clean start). Lança <see cref="FalhaCredenciaisMqttException"/> se o broker recusar usuário, senha,
    /// autorização ou ClientId; outras exceções para falhas de rede/protocolo.
    /// </summary>
    Task ConectarAsync(CancellationToken ct);

    /// <summary>Assina com QoS 1. Lança <see cref="MqttAssinaturaRecusadaException"/> se o broker recusar.</summary>
    Task AssinarAsync(string filtro, CancellationToken ct);

    /// <summary>
    /// Publica com QoS 1 e SEMPRE sem retenção (o backend só publica <c>cmd</c>, que nunca é retido).
    /// Lança <see cref="MqttPublicacaoRecusadaException"/> se o broker recusar.
    /// </summary>
    Task PublicarAsync(string topico, ReadOnlyMemory<byte> payload, CancellationToken ct);

    /// <summary>Desconexão limpa (DISCONNECT normal).</summary>
    Task DesconectarAsync(CancellationToken ct);
}

/// <summary>
/// Queda de uma conexão estabelecida. <see cref="SessaoAssumida"/>: o broker derrubou esta conexão porque outro
/// cliente entrou com o mesmo ClientId (= usuário, por <c>use_username_as_clientid</c>).
/// </summary>
internal sealed record DesconexaoMqtt(string Motivo, bool SessaoAssumida);

/// <summary>O broker recusou o CONNECT por credenciais/autorização/ClientId. Reconectar logo não resolve.</summary>
internal sealed class FalhaCredenciaisMqttException(string codigo)
    : Exception($"O broker MQTT recusou a conexão (código {codigo}).")
{
    public string Codigo { get; } = codigo;
}

/// <summary>O broker recusou o CONNECT por outro motivo (ex.: ServerUnavailable, QuotaExceeded). Tenta de novo com backoff.</summary>
internal sealed class MqttConexaoRecusadaException(string codigo)
    : Exception($"O broker MQTT recusou a conexão (código {codigo}).")
{
    public string Codigo { get; } = codigo;
}
