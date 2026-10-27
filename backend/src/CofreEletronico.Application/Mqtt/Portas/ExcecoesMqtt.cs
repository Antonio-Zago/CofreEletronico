namespace CofreEletronico.Application.Mqtt.Portas;

/// <summary>
/// Sem conexão com o broker (desconectado, desabilitado ou a conexão caiu durante o envio).
/// A mensagem é segura para log; não contém credenciais.
/// </summary>
public sealed class MqttIndisponivelException : Exception
{
    public MqttIndisponivelException(string mensagem) : base(mensagem)
    {
    }

    public MqttIndisponivelException(string mensagem, Exception interna) : base(mensagem, interna)
    {
    }
}

/// <summary>O broker recebeu a publicação e respondeu com código de erro (ex.: não autorizado pela ACL).</summary>
public sealed class MqttPublicacaoRecusadaException(string topico, string codigo)
    : Exception($"O broker recusou a publicação em '{topico}' (código {codigo}).")
{
    public string Topico { get; } = topico;

    public string Codigo { get; } = codigo;
}

/// <summary>O broker recusou a assinatura do filtro (ex.: não autorizado pela ACL).</summary>
public sealed class MqttAssinaturaRecusadaException(string filtro, string codigo)
    : Exception($"O broker recusou a assinatura de '{filtro}' (código {codigo}).")
{
    public string Filtro { get; } = filtro;

    public string Codigo { get; } = codigo;
}
