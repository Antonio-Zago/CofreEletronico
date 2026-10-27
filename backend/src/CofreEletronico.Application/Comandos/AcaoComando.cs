namespace CofreEletronico.Application.Comandos;

/// <summary>
/// Ações aceitas no campo <c>acao</c> do comando MQTT (contrato em docs/ARQUITETURA.md, "Contrato MQTT").
/// O texto enviado sai de <see cref="MontadorComando.TextoAcao"/>, nunca de <c>ToString()</c>.
/// </summary>
public enum AcaoComando
{
    /// <summary><c>"abrir"</c>: aciona o relé do canal informado.</summary>
    Abrir,
}
