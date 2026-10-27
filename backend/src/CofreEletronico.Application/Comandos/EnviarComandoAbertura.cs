using System.Security.Cryptography;
using CofreEletronico.Application.Comandos.Portas;
using CofreEletronico.Application.Mqtt.Portas;
using Microsoft.Extensions.Logging;

namespace CofreEletronico.Application.Comandos;

/// <summary>
/// Monta, assina e publica o comando <c>abrir</c> em <c>cofre/{deviceId}/cmd</c> (contrato em docs/ARQUITETURA.md,
/// "Contrato MQTT"). Cada chamada gera um commandId novo (aleatório) e um <c>ts</c> do <see cref="TimeProvider"/>.
/// </summary>
/// <remarks>
/// <para><b>Não aplica regras de status.</b> Estante inativa/offline, cofre inativo, em manutenção, livre ou ocupado: quem
/// chama decide se pode abrir (locação do usuário na T-0801, abertura de emergência do admin na T-1103, que precisa abrir
/// até cofre em manutenção). Aqui só se confere que o cofre pertence à estante.</para>
/// <para><b>Nunca reenvia.</b> <see cref="MqttIndisponivelException"/> (desconectado, queda ou timeout) vira
/// <see cref="StatusEnvioComando.MqttIndisponivel"/>: o resultado é incerto e um comando de abertura atrasado é perigoso.
/// Cada tentativa do usuário gera um novo commandId. Um mesmo <see cref="ComandoPreparado"/> só pode ser publicado uma vez.</para>
/// <para><b>Ponto de extensão para a T-0603 (correlação do ack).</b> O ack do ESP32 pode chegar antes de o PUBACK
/// retornar, então o comando precisa estar registrado como pendente ANTES de publicar. Por isso o fluxo é dividido:
/// <see cref="PrepararAsync"/> (lê, monta e assina; devolve o commandId sem publicar) e <see cref="PublicarAsync"/>.
/// <see cref="ExecutarAsync"/> só encadeia os dois. Quem precisar correlacionar chama
/// <c>PrepararAsync</c> → registra o pendente com <see cref="ComandoPreparado.Comando"/> → <c>PublicarAsync</c>.
/// Regras de remoção do pendente:
/// <list type="bullet">
/// <item>remoção imediata só em <see cref="StatusEnvioComando.PublicacaoRecusada"/> (o broker recusou: nada foi
/// entregue) ou em falha ANTES do envio (exceção de <c>PublicarAsync</c>: cancelamento antes de começar,
/// <see cref="InvalidOperationException"/> de comando já publicado, <see cref="ArgumentException"/> de pacote grande
/// demais);</item>
/// <item>em <see cref="StatusEnvioComando.MqttIndisponivel"/> (inclusive cancelamento durante o envio), o pendente
/// PERMANECE até o timeout de 10 s: o comando pode ter sido entregue e o ack pode chegar;</item>
/// <item>toda remoção fica num <c>finally</c>, e a expiração pelo timeout é garantida em qualquer caminho (nenhum
/// pendente fica para sempre).</item>
/// </list></para>
/// <para><b>Cancelamento.</b> O token de quem chama vale em <see cref="PrepararAsync"/> (leitura do banco) e até o início de
/// <see cref="PublicarAsync"/>. Depois que o envio começa, ele é ignorado (publicação com
/// <see cref="CancellationToken.None"/>; o adaptador MQTT aplica o próprio timeout): o cliente fechar o app depois do
/// PUBLISH não pode deixar o cofre abrir sem status nem log. Uma <see cref="OperationCanceledException"/> que ainda assim
/// chegue durante o envio vira <see cref="StatusEnvioComando.MqttIndisponivel"/> (resultado incerto) com warning.</para>
/// <para><b>Uso pela Api.</b> Endpoints não chamam esta classe diretamente (regra em <c>ArchitectureTests</c>): usam os
/// casos de uso com autorização (<c>AbrirCofreLocacao</c>, T-0801; <c>AbrirCofreEmergencia</c>, T-1103), que a chamam
/// por dentro.</para>
/// <para>O segredo HMAC é zerado (<see cref="CryptographicOperations.ZeroMemory"/>) logo depois de assinar, num
/// <c>finally</c>, antes de qualquer publicação. Logs: commandId, deviceId, cofre e canal; nunca o segredo, a mensagem
/// canônica, a assinatura nem o payload.</para>
/// </remarks>
public sealed class EnviarComandoAbertura
{
    private readonly IDadosComandoRepositorio _repositorio;
    private readonly IClienteMqtt _mqtt;
    private readonly TimeProvider _relogio;
    private readonly ILogger<EnviarComandoAbertura> _logger;

    public EnviarComandoAbertura(
        IDadosComandoRepositorio repositorio, IClienteMqtt mqtt, TimeProvider relogio, ILogger<EnviarComandoAbertura> logger)
    {
        _repositorio = repositorio;
        _mqtt = mqtt;
        _relogio = relogio;
        _logger = logger;
    }

    /// <summary>Prepara e publica. Sem correlação de ack (ver remarks da classe).</summary>
    public async Task<ResultadoEnvioComando> ExecutarAsync(Guid estanteId, Guid cofreId, CancellationToken cancellationToken = default)
    {
        var preparado = await PrepararAsync(estanteId, cofreId, cancellationToken);
        if (preparado is null)
            return ResultadoEnvioComando.CofreNaoEncontrado();

        return await PublicarAsync(preparado, cancellationToken);
    }

    /// <summary>
    /// Lê os dados do cofre (só se pertencer à estante), gera commandId e ts, assina e devolve o comando pronto, sem
    /// publicar. <c>null</c>: cofre não encontrado na estante.
    /// </summary>
    public async Task<ComandoPreparado?> PrepararAsync(Guid estanteId, Guid cofreId, CancellationToken cancellationToken = default)
    {
        var dados = await _repositorio.ObterAsync(estanteId, cofreId, cancellationToken);
        if (dados is null)
        {
            _logger.LogInformation("Comando não preparado: cofre {CofreId} não encontrado na estante {EstanteId}.", cofreId, estanteId);
            return null;
        }

        try
        {
            var assinado = MontadorComando.Montar(
                dados.DeviceId, Guid.NewGuid(), dados.NumeroCofre, dados.CanalRele, AcaoComando.Abrir,
                _relogio.GetUtcNow().ToUnixTimeSeconds(), dados.SegredoHmac);

            return new ComandoPreparado(
                new ComandoEnviado(assinado.CommandId, assinado.DeviceId, assinado.Cofre, assinado.Canal, assinado.Ts),
                assinado.Payload);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dados.SegredoHmac);
        }
    }

    /// <summary>
    /// Publica (QoS 1, sem retain) uma única vez. Falhas do broker viram status. <paramref name="cancellationToken"/> só
    /// é conferido antes de o envio começar (aí lança <see cref="OperationCanceledException"/> e nada foi enviado);
    /// durante o envio é ignorado (ver remarks da classe). <see cref="ArgumentException"/> (pacote grande demais,
    /// recusado antes do envio) propaga.
    /// </summary>
    /// <exception cref="InvalidOperationException">O comando já foi publicado (reenvio proibido).</exception>
    /// <exception cref="OperationCanceledException">Cancelado antes de o envio começar. O comando fica consumido.</exception>
    public async Task<ResultadoEnvioComando> PublicarAsync(ComandoPreparado preparado, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preparado);
        preparado.MarcarPublicacao(); // antes do cancelamento: um comando preparado nunca é publicado depois, nem em outra tentativa
        cancellationToken.ThrowIfCancellationRequested();
        var comando = preparado.Comando;

        try
        {
            // Token não ligado à requisição: iniciado o envio, o resultado precisa ser conhecido e registrado.
            await _mqtt.PublicarComandoAsync(comando.DeviceId, preparado.Payload, CancellationToken.None);
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogWarning(ex,
                "Comando {CommandId} para {DeviceId} (cofre {Cofre}, canal {Canal}) com envio interrompido: resultado incerto. Não será reenviado.",
                comando.CommandId, comando.DeviceId, comando.Cofre, comando.Canal);
            return ResultadoEnvioComando.MqttIndisponivel(comando);
        }
        catch (MqttIndisponivelException ex)
        {
            _logger.LogWarning(ex,
                "Comando {CommandId} para {DeviceId} (cofre {Cofre}, canal {Canal}) não confirmado: MQTT indisponível. Não será reenviado.",
                comando.CommandId, comando.DeviceId, comando.Cofre, comando.Canal);
            return ResultadoEnvioComando.MqttIndisponivel(comando);
        }
        catch (MqttPublicacaoRecusadaException ex)
        {
            _logger.LogError(ex,
                "Comando {CommandId} para {DeviceId} (cofre {Cofre}, canal {Canal}) recusado pelo broker (código {Codigo}). Verifique a ACL do usuário MQTT do backend.",
                comando.CommandId, comando.DeviceId, comando.Cofre, comando.Canal, ex.Codigo);
            return ResultadoEnvioComando.PublicacaoRecusada(comando);
        }

        _logger.LogInformation(
            "Comando {CommandId} de abertura publicado para {DeviceId} (cofre {Cofre}, canal {Canal}).",
            comando.CommandId, comando.DeviceId, comando.Cofre, comando.Canal);
        return ResultadoEnvioComando.Enviado(comando);
    }
}

/// <summary>
/// Comando montado e assinado por <see cref="EnviarComandoAbertura.PrepararAsync"/>, ainda não publicado. Não contém
/// o segredo. Só pode ser publicado uma vez.
/// </summary>
public sealed class ComandoPreparado
{
    private int _publicado;

    internal ComandoPreparado(ComandoEnviado comando, ReadOnlyMemory<byte> payload)
    {
        Comando = comando;
        Payload = payload;
    }

    /// <summary>Identificadores (commandId, deviceId, cofre, canal, ts): o que a T-0603 registra como pendente.</summary>
    public ComandoEnviado Comando { get; }

    internal ReadOnlyMemory<byte> Payload { get; }

    public override string ToString() => $"ComandoPreparado {{ {Comando} }}";

    internal void MarcarPublicacao()
    {
        if (Interlocked.Exchange(ref _publicado, 1) == 1)
        {
            throw new InvalidOperationException(
                "Este comando já foi publicado: comandos de abertura nunca são reenviados (cada tentativa gera um novo commandId).");
        }
    }
}
