namespace CofreEletronico.Application.Mqtt.Portas;

/// <summary>
/// Conexão MQTT gerenciada do backend (uma por instância da Api, usuário <c>backend</c> no broker).
/// Implementada na Infrastructure (MQTTnet); a Application só conhece esta porta, sem tipos do MQTTnet.
/// QoS 1 fixo em publicações e assinaturas (contrato MQTT em docs/ARQUITETURA.md).
/// </summary>
public interface IClienteMqtt
{
    /// <summary><c>false</c> quando <c>Mqtt:Host</c> não está configurado: o serviço fica inativo.</summary>
    bool Habilitado { get; }

    /// <summary>
    /// Conectado ao broker e com as assinaturas registradas já refeitas nesta conexão (base do futuro <c>/health</c>, T-1201).
    /// </summary>
    bool Conectado { get; }

    /// <summary>
    /// Publica um comando em <c>cofre/{deviceId}/cmd</c> (único tópico em que o backend escreve), com QoS 1 e SEMPRE
    /// sem retenção, e aguarda o PUBACK do broker. O tópico é montado aqui por <see cref="TopicosMqtt.Comando"/>, que
    /// valida o <paramref name="deviceId"/>. Não há fila: desconectado (ou desabilitado), falha na hora com
    /// <see cref="MqttIndisponivelException"/>. Um comando de abertura entregue atrasado é perigoso; quem chama decide
    /// o que responder.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="deviceId"/> fora do formato ou pacote acima de <see cref="TopicosMqtt.TamanhoMaximoPacote"/> bytes.</exception>
    /// <exception cref="MqttIndisponivelException">Sem conexão com o broker, ou a conexão caiu durante o envio.</exception>
    /// <exception cref="MqttPublicacaoRecusadaException">O broker respondeu com código de erro (ex.: ACL).</exception>
    Task PublicarComandoAsync(string deviceId, ReadOnlyMemory<byte> payload, CancellationToken ct);

    /// <summary>
    /// Registra um tratador para o filtro (QoS 1). O registro vale entre reconexões: a cada conexão nova, todos os
    /// filtros registrados são assinados de novo. Sem conexão no momento, a assinatura acontece na próxima conexão.
    /// Um tratador que lança exceção é logado e não derruba a conexão nem impede os demais tratadores.
    /// Regras para o tratador:
    /// <list type="bullet">
    /// <item>roda no laço de recepção do cliente: deve ser rápido; trabalho pesado (banco, rede) vai para um
    /// <c>Channel</c> com limite (<c>Channel.CreateBounded</c>) consumido por outra tarefa;</item>
    /// <item>mensagem inválida (JSON malformado, campos fora do contrato) é descartada com log enxuto (tópico e motivo),
    /// sem lançar exceção;</item>
    /// <item>nunca inclua o payload em mensagens de exceção nem de log (pode conter dados do dispositivo).</item>
    /// </list>
    /// Descartar o retorno remove o tratador (a assinatura no broker só deixa de ser refeita na próxima conexão).
    /// </summary>
    /// <exception cref="ArgumentException">Filtro inválido.</exception>
    /// <exception cref="MqttAssinaturaRecusadaException">Conectado, mas o broker recusou a assinatura (ex.: ACL).</exception>
    Task<IDisposable> AssinarAsync(string filtro, Func<MensagemMqtt, CancellationToken, Task> tratador, CancellationToken ct);
}

/// <summary>Mensagem recebida do broker. O payload é uma cópia própria (pode ser guardado depois do tratador).</summary>
public sealed record MensagemMqtt(string Topico, ReadOnlyMemory<byte> Payload, bool Retida);
