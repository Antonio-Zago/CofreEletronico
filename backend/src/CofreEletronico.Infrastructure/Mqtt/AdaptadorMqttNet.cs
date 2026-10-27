using System.Buffers;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using CofreEletronico.Application.Mqtt;
using CofreEletronico.Application.Mqtt.Portas;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Formatter;
using MQTTnet.Protocol;

namespace CofreEletronico.Infrastructure.Mqtt;

/// <summary>
/// Implementação de <see cref="IAdaptadorMqtt"/> com MQTTnet 5 (protocolo MQTT 5.0). Não reconecta sozinho:
/// a reconexão é do <see cref="ServicoMqtt"/>.
/// <para>
/// Sessão: clean start + expiração de sessão 0. Um <c>ack</c> antigo não serve (o backend já devolveu timeout depois
/// de 10 s), <c>estado</c>/<c>online</c> são retained (o broker reenvia o último valor na assinatura) e a assinatura
/// é refeita a cada conexão. Assim o broker não acumula fila para o backend desconectado.
/// </para>
/// <para>
/// ClientId = usuário (<c>backend</c>): o broker usa <c>use_username_as_clientid</c>, então só pode haver UMA
/// instância conectada com esse usuário (uma segunda conexão derruba a primeira).
/// </para>
/// </summary>
internal sealed class AdaptadorMqttNet : IAdaptadorMqtt, IDisposable
{
    private readonly IMqttClient _cliente;
    private readonly MqttOpcoes _opcoes;
    private readonly IHostEnvironment _ambiente;
    private readonly ILogger<AdaptadorMqttNet> _logger;
    private X509Certificate2Collection? _cas;
    private MqttClientOptions? _opcoesConexao;

    public AdaptadorMqttNet(IOptions<MqttOpcoes> opcoes, IHostEnvironment ambiente, ILogger<AdaptadorMqttNet> logger)
    {
        _opcoes = opcoes.Value;
        _ambiente = ambiente;
        _logger = logger;
        _cliente = new MqttClientFactory().CreateMqttClient();
        _cliente.DisconnectedAsync += AoDesconectarAsync;
        _cliente.ApplicationMessageReceivedAsync += AoReceberAsync;
    }

    public bool Conectado => _cliente.IsConnected;

    public event Func<DesconexaoMqtt, Task>? Desconectado;

    public event Func<MensagemMqtt, Task>? MensagemRecebida;

    public async Task ConectarAsync(CancellationToken ct)
    {
        _opcoesConexao ??= CriarOpcoesConexao();

        // MQTTnet 5 NÃO lança exceção quando o broker recusa o CONNECT: devolve o CONNACK com o código e deixa o
        // cliente desconectado. Sem esta checagem, uma recusa pareceria conexão bem-sucedida.
        var resultado = await _cliente.ConnectAsync(_opcoesConexao, ct);
        switch (resultado.ResultCode)
        {
            case MqttClientConnectResultCode.Success:
                return;
            case MqttClientConnectResultCode.BadUserNameOrPassword:
            case MqttClientConnectResultCode.NotAuthorized:
            case MqttClientConnectResultCode.ClientIdentifierNotValid:
                throw new FalhaCredenciaisMqttException(resultado.ResultCode.ToString());
            default:
                throw new MqttConexaoRecusadaException(resultado.ResultCode.ToString());
        }
    }

    public async Task AssinarAsync(string filtro, CancellationToken ct)
    {
        var opcoes = new MqttClientSubscribeOptionsBuilder()
            .WithTopicFilter(f => f.WithTopic(filtro).WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce))
            .Build();

        var resultado = await _cliente.SubscribeAsync(opcoes, ct);
        foreach (var item in resultado.Items)
        {
            // Códigos >= 0x80 são falha (ex.: NotAuthorized pela ACL).
            if ((int)item.ResultCode >= 0x80)
                throw new MqttAssinaturaRecusadaException(filtro, item.ResultCode.ToString());
        }
    }

    public async Task PublicarAsync(string topico, ReadOnlyMemory<byte> payload, CancellationToken ct)
    {
        var mensagem = new MqttApplicationMessageBuilder()
            .WithTopic(topico)
            .WithPayload(new ReadOnlySequence<byte>(payload))
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .WithRetainFlag(false) // contrato: cmd nunca é retido
            .Build();

        var resultado = await _cliente.PublishAsync(mensagem, ct);
        // NoMatchingSubscribers: entregue ao broker, só não há assinante agora (ex.: ESP32 offline). Não é erro aqui.
        if (resultado.ReasonCode is not (MqttClientPublishReasonCode.Success or MqttClientPublishReasonCode.NoMatchingSubscribers))
            throw new MqttPublicacaoRecusadaException(topico, resultado.ReasonCode.ToString());
    }

    public Task DesconectarAsync(CancellationToken ct) =>
        _cliente.DisconnectAsync(
            new MqttClientDisconnectOptionsBuilder().WithReason(MqttClientDisconnectOptionsReason.NormalDisconnection).Build(),
            ct);

    public void Dispose()
    {
        _cliente.DisconnectedAsync -= AoDesconectarAsync;
        _cliente.ApplicationMessageReceivedAsync -= AoReceberAsync;
        _cliente.Dispose();
        if (_cas is not null)
        {
            foreach (var ca in _cas)
                ca.Dispose();
        }
    }

    private MqttClientOptions CriarOpcoesConexao()
    {
        var construtor = new MqttClientOptionsBuilder()
            .WithTcpServer(_opcoes.Host, _opcoes.Porta)
            .WithClientId(_opcoes.Usuario)
            .WithCredentials(_opcoes.Usuario, _opcoes.Senha)
            .WithProtocolVersion(MqttProtocolVersion.V500)
            .WithCleanStart(true)
            .WithSessionExpiryInterval(0)
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(_opcoes.KeepAliveSegundos))
            .WithTimeout(TimeSpan.FromSeconds(_opcoes.TimeoutSegundos))
            // Declara ao broker o mesmo limite dele (max_packet_size 4096): nada maior chega até nós.
            .WithMaximumPacketSize(TopicosMqtt.TamanhoMaximoPacote);

        if (_opcoes.UsarTls)
        {
            if (!string.IsNullOrWhiteSpace(_opcoes.CaminhoCertificadoCa))
                _cas = CertificadosCa.Carregar(_opcoes.CaminhoCertificadoCa);

            var validador = new ValidadorCertificadoBroker(
                _cas, _opcoes.PermitirCertificadoInvalidoEmDesenvolvimento, _ambiente.IsDevelopment(), _logger);

            construtor.WithTlsOptions(tls => tls
                .UseTls()
                .WithTargetHost(_opcoes.Host) // nome conferido no certificado (SNI)
                .WithSslProtocols(SslProtocols.Tls12 | SslProtocols.Tls13)
                .WithCertificateValidationHandler(contexto =>
                    validador.Validar(contexto.Certificate, Intermediarios(contexto.Chain), contexto.SslPolicyErrors)));
        }

        return construtor.Build();
    }

    /// <summary>Certificados enviados pelo broker além do folha (intermediários), para montar a cadeia.</summary>
    private static List<X509Certificate2> Intermediarios(X509Chain? cadeia) =>
        cadeia is null ? [] : cadeia.ChainElements.Skip(1).Select(e => e.Certificate).ToList();

    private Task AoDesconectarAsync(MqttClientDisconnectedEventArgs args)
    {
        // Falha ao conectar também dispara o evento; isso já é tratado pelo laço de reconexão.
        if (!args.ClientWasConnected || Desconectado is not { } tratador)
            return Task.CompletedTask;

        var motivo = args.Reason.ToString();
        if (!string.IsNullOrWhiteSpace(args.ReasonString))
            motivo += $" ({args.ReasonString})";
        if (args.Exception is not null)
            motivo += $": {args.Exception.GetType().Name}: {args.Exception.Message}";

        return tratador(new DesconexaoMqtt(motivo, args.Reason == MqttClientDisconnectReason.SessionTakenOver));
    }

    private Task AoReceberAsync(MqttApplicationMessageReceivedEventArgs args)
    {
        if (MensagemRecebida is not { } tratador)
            return Task.CompletedTask;

        var mensagem = args.ApplicationMessage;
        // Cópia: o buffer do MQTTnet pode ser reaproveitado depois do evento.
        return tratador(new MensagemMqtt(mensagem.Topic, mensagem.Payload.ToArray(), mensagem.Retain));
    }
}
