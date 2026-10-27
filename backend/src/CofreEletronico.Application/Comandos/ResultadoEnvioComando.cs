namespace CofreEletronico.Application.Comandos;

/// <summary>Identificadores de um comando de abertura (sem segredo, sem assinatura). Seguro para log e auditoria.</summary>
/// <param name="Ts">Segundos Unix UTC enviados no comando.</param>
public sealed record ComandoEnviado(Guid CommandId, string DeviceId, int Cofre, int Canal, long Ts);

public enum StatusEnvioComando
{
    /// <summary>O broker confirmou o recebimento (PUBACK). Não significa que o ESP32 abriu: isso vem no ack (T-0603).</summary>
    Enviado,

    /// <summary>O cofre não existe ou não pertence à estante informada. Nada foi publicado.</summary>
    CofreNaoEncontrado,

    /// <summary>
    /// MQTT desabilitado, desconectado, queda ou timeout durante o envio. Resultado INCERTO (o comando pode ter chegado
    /// ao broker): nunca reenvie; uma nova tentativa do usuário gera um novo commandId.
    /// </summary>
    MqttIndisponivel,

    /// <summary>O broker recusou a publicação (ex.: ACL do usuário <c>backend</c> errada). Configuração a corrigir.</summary>
    PublicacaoRecusada,
}

/// <summary>Resultado de <see cref="EnviarComandoAbertura"/>.</summary>
public sealed class ResultadoEnvioComando
{
    private ResultadoEnvioComando(StatusEnvioComando status, ComandoEnviado? comando)
    {
        Status = status;
        Comando = comando;
    }

    public StatusEnvioComando Status { get; }

    /// <summary>
    /// Preenchido em todos os status, exceto <see cref="StatusEnvioComando.CofreNaoEncontrado"/>: mesmo sem PUBACK, o
    /// commandId identifica a tentativa (auditoria, T-0605).
    /// </summary>
    public ComandoEnviado? Comando { get; }

    public static ResultadoEnvioComando Enviado(ComandoEnviado comando) =>
        new(StatusEnvioComando.Enviado, comando ?? throw new ArgumentNullException(nameof(comando)));

    public static ResultadoEnvioComando CofreNaoEncontrado() => new(StatusEnvioComando.CofreNaoEncontrado, null);

    public static ResultadoEnvioComando MqttIndisponivel(ComandoEnviado comando) =>
        new(StatusEnvioComando.MqttIndisponivel, comando ?? throw new ArgumentNullException(nameof(comando)));

    public static ResultadoEnvioComando PublicacaoRecusada(ComandoEnviado comando) =>
        new(StatusEnvioComando.PublicacaoRecusada, comando ?? throw new ArgumentNullException(nameof(comando)));
}
