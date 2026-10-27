using CofreEletronico.Application.Mqtt;
using CofreEletronico.Application.Mqtt.Portas;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CofreEletronico.Infrastructure.Mqtt;

/// <summary>
/// Conexão MQTT gerenciada (singleton + hosted service), implementação de <see cref="IClienteMqtt"/>.
/// <list type="bullet">
/// <item>Sem <c>Mqtt:Host</c>: inativo, com um log informativo.</item>
/// <item>Conecta em background, sem bloquear a subida da Api. Broker fora: warning e nova tentativa com backoff
/// exponencial e jitter (<see cref="BackoffExponencial"/>), para sempre.</item>
/// <item>Uma conexão que cai antes de <see cref="MqttOpcoes.LimiteConexaoEstavelSegundos"/> conta como falha: a
/// reconexão espera o backoff (duas instâncias com o mesmo usuário não ficam se derrubando sem parar).</item>
/// <item>Credenciais/autorização recusadas no CONNECT: erro no log (na 1ª e a cada
/// <see cref="IntervaloLogCredenciais"/> tentativas) e backoff no máximo.</item>
/// <item>Um único laço faz todas as tentativas (nunca duas em paralelo); a queda da conexão só o acorda.</item>
/// <item>A cada conexão, refaz todas as assinaturas registradas.</item>
/// <item>Só publica em <c>cofre/{deviceId}/cmd</c>, sem retenção. Sem conexão, falha na hora
/// (<see cref="MqttIndisponivelException"/>), sem fila.</item>
/// <item>Exceção em tratador é logada e isolada.</item>
/// </list>
/// Logs nunca contêm a senha nem o payload das mensagens.
/// </summary>
internal sealed class ServicoMqtt : BackgroundService, IClienteMqtt
{
    /// <summary>Com credenciais recusadas, o erro é logado na 1ª tentativa e a cada N tentativas.</summary>
    public const int IntervaloLogCredenciais = 10;

    private readonly MqttOpcoes _opcoes;
    private readonly IAdaptadorMqtt _adaptador;
    private readonly TimeProvider _tempo;
    private readonly ILogger<ServicoMqtt> _logger;
    private readonly BackoffExponencial _backoff;

    // Acorda o laço quando a conexão cai (no máximo um sinal pendente).
    private readonly SemaphoreSlim _sinalQueda = new(0, 1);

    private readonly object _trava = new();
    private Registro[] _registros = [];
    private volatile bool _parando;

    // Conectado E com as assinaturas refeitas (o que o health deve enxergar).
    private volatile bool _pronto;
    private CancellationToken _tokenServico;

    public ServicoMqtt(IOptions<MqttOpcoes> opcoes, IAdaptadorMqtt adaptador, TimeProvider tempo, ILogger<ServicoMqtt> logger)
        : this(opcoes, adaptador, tempo, logger, Random.Shared.NextDouble)
    {
    }

    internal ServicoMqtt(
        IOptions<MqttOpcoes> opcoes, IAdaptadorMqtt adaptador, TimeProvider tempo, ILogger<ServicoMqtt> logger, Func<double> aleatorio)
    {
        _opcoes = opcoes.Value;
        _adaptador = adaptador;
        _tempo = tempo;
        _logger = logger;
        _backoff = new BackoffExponencial(
            TimeSpan.FromSeconds(_opcoes.ReconexaoInicialSegundos), TimeSpan.FromSeconds(_opcoes.ReconexaoMaximaSegundos), aleatorio);
    }

    public bool Habilitado => _opcoes.Habilitado;

    /// <summary>Conectado ao broker e com as assinaturas registradas já refeitas nesta conexão.</summary>
    public bool Conectado => Habilitado && _pronto && _adaptador.Conectado;

    /// <summary>Filtros registrados hoje (distintos), na ordem de registro.</summary>
    internal IReadOnlyList<string> FiltrosRegistrados => Volatile.Read(ref _registros).Select(r => r.Filtro).Distinct().ToList();

    public async Task PublicarComandoAsync(string deviceId, ReadOnlyMemory<byte> payload, CancellationToken ct)
    {
        var topico = TopicosMqtt.Comando(deviceId); // valida o deviceId: nenhum outro tópico é publicável
        var tamanho = TopicosMqtt.TamanhoPacotePublicacao(topico, payload.Length);
        if (tamanho > TopicosMqtt.TamanhoMaximoPacote)
        {
            throw new ArgumentException(
                $"A mensagem MQTT tem {tamanho} bytes com o cabeçalho; o broker aceita no máximo {TopicosMqtt.TamanhoMaximoPacote}.",
                nameof(payload));
        }

        if (!Habilitado)
            throw new MqttIndisponivelException("MQTT desabilitado (Mqtt:Host não configurado).");
        if (!_adaptador.Conectado)
            throw new MqttIndisponivelException("Sem conexão com o broker MQTT.");

        try
        {
            await _adaptador.PublicarAsync(topico, payload, ct);
        }
        catch (MqttPublicacaoRecusadaException)
        {
            throw;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Queda da conexão ou timeout do PUBACK: o resultado é incerto, mas para quem chama é "indisponível".
            throw new MqttIndisponivelException("Falha ao publicar no broker MQTT (conexão indisponível ou sem resposta).", ex);
        }
    }

    public async Task<IDisposable> AssinarAsync(string filtro, Func<MensagemMqtt, CancellationToken, Task> tratador, CancellationToken ct)
    {
        TopicosMqtt.ValidarFiltro(filtro);
        ArgumentNullException.ThrowIfNull(tratador);

        var registro = new Registro(this, filtro, tratador);
        lock (_trava)
            _registros = [.. _registros, registro];

        // Estado bruto da conexão (e não Conectado): durante a reassinatura de uma conexão nova, o filtro recém-registrado
        // pode ter ficado fora da lista já lida; assinar de novo é inofensivo no MQTT.
        if (!Habilitado || !_adaptador.Conectado)
            return registro; // assinado na próxima conexão

        try
        {
            await _adaptador.AssinarAsync(filtro, ct);
        }
        catch (Exception ex) when (ex is MqttAssinaturaRecusadaException or OperationCanceledException)
        {
            registro.Dispose();
            throw;
        }
        catch (Exception ex)
        {
            // Falha de comunicação: o registro fica e a assinatura é refeita na reconexão.
            _logger.LogWarning("Falha ao assinar {Filtro} no broker MQTT ({Erro}); será refeita na próxima conexão.", filtro, Descrever(ex));
        }

        return registro;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_opcoes.PermitirCertificadoInvalidoEmDesenvolvimento)
        {
            _logger.LogWarning(
                "Mqtt:PermitirCertificadoInvalidoEmDesenvolvimento está ligada: um certificado TLS inválido do broker será aceito. " +
                "Use só na máquina de desenvolvimento.");
        }

        if (!Habilitado)
        {
            _logger.LogInformation("MQTT desabilitado: Mqtt:Host não configurado. Comandos aos ESP32 ficam indisponíveis.");
            return;
        }

        _tokenServico = stoppingToken;
        _adaptador.Desconectado += AoDesconectarAsync;
        _adaptador.MensagemRecebida += AoReceberAsync;

        // Libera a subida da Api antes de qualquer trabalho de rede.
        await Task.Yield();

        var limiteEstavel = TimeSpan.FromSeconds(_opcoes.LimiteConexaoEstavelSegundos);
        var tentativa = 0;          // tentativas desde a última conexão estável (para o log)
        var falhas = 0;             // falhas seguidas (conexão recusada ou que durou pouco): define o backoff
        var falhasCredenciais = 0;  // credenciais/autorização recusadas seguidas
        DateTimeOffset? conectadoEm = null;

        while (!stoppingToken.IsCancellationRequested)
        {
            if (_adaptador.Conectado)
            {
                if (!await AguardarQuedaAsync(stoppingToken))
                    break;
                continue;
            }

            if (conectadoEm is { } inicio)
            {
                // A conexão anterior caiu: se durou pouco, conta como falha e espera o backoff antes de reconectar.
                conectadoEm = null;
                var duracao = _tempo.GetUtcNow() - inicio;
                if (duracao >= limiteEstavel)
                {
                    falhas = 0;
                    tentativa = 0;
                }
                else
                {
                    falhas++;
                    var espera = _backoff.Proximo(falhas);
                    _logger.LogWarning(
                        "A conexão com o broker MQTT durou só {Duracao:0.0} s (estável a partir de {Limite} s). Nova tentativa em {Espera:0.0} s.",
                        duracao.TotalSeconds, _opcoes.LimiteConexaoEstavelSegundos, espera.TotalSeconds);
                    if (!await EsperarAsync(espera, stoppingToken))
                        break;
                    continue;
                }
            }

            tentativa++;
            _pronto = false;
            try
            {
                await _adaptador.ConectarAsync(stoppingToken);
                await RefazerAssinaturasAsync(stoppingToken);
                conectadoEm = _tempo.GetUtcNow();
                falhasCredenciais = 0;
                _pronto = true;
                _logger.LogInformation(
                    "Conectado ao broker MQTT {Host}:{Porta} como {Usuario} (tentativa {Tentativa}, TLS {Tls}).",
                    _opcoes.Host, _opcoes.Porta, _opcoes.Usuario, tentativa, _opcoes.UsarTls);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (FalhaCredenciaisMqttException ex)
            {
                // Reconectar logo não resolve: backoff no máximo e erro no log sem inundar.
                falhas++;
                falhasCredenciais++;
                var espera = _backoff.Proximo(int.MaxValue);
                if (falhasCredenciais == 1 || falhasCredenciais % IntervaloLogCredenciais == 0)
                {
                    _logger.LogError(
                        "O broker MQTT {Host}:{Porta} recusou o usuário {Usuario} ({Codigo}; {Falhas} recusa(s) seguida(s)). " +
                        "Confira Mqtt:Usuario, Mqtt:Senha, o arquivo de senhas e a ACL do Mosquitto. Nova tentativa em {Espera:0.0} s.",
                        _opcoes.Host, _opcoes.Porta, _opcoes.Usuario, ex.Codigo, falhasCredenciais, espera.TotalSeconds);
                }
                else
                {
                    _logger.LogDebug("Credenciais MQTT recusadas de novo ({Codigo}). Nova tentativa em {Espera:0.0} s.", ex.Codigo, espera.TotalSeconds);
                }

                await DesconectarSemFalharAsync(stoppingToken);
                if (!await EsperarAsync(espera, stoppingToken))
                    break;
            }
            catch (Exception ex)
            {
                falhas++;
                var espera = _backoff.Proximo(falhas);
                _logger.LogWarning(
                    "Falha ao conectar ao broker MQTT {Host}:{Porta} (tentativa {Tentativa}): {Erro}. Nova tentativa em {Espera:0.0} s.",
                    _opcoes.Host, _opcoes.Porta, tentativa, Descrever(ex), espera.TotalSeconds);

                await DesconectarSemFalharAsync(stoppingToken); // ex.: conectou, mas a reassinatura falhou
                if (!await EsperarAsync(espera, stoppingToken))
                    break;
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _parando = true;
        await base.StopAsync(cancellationToken);

        if (!Habilitado)
            return;

        if (_adaptador.Conectado)
        {
            try
            {
                await _adaptador.DesconectarAsync(cancellationToken);
                _logger.LogInformation("Desconectado do broker MQTT (encerramento da Api).");
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Falha ao desconectar do broker MQTT no encerramento: {Erro}.", Descrever(ex));
            }
        }

        _adaptador.Desconectado -= AoDesconectarAsync;
        _adaptador.MensagemRecebida -= AoReceberAsync;
    }

    public override void Dispose()
    {
        base.Dispose();
        _sinalQueda.Dispose();
    }

    private async Task<bool> EsperarAsync(TimeSpan espera, CancellationToken ct)
    {
        try
        {
            await Task.Delay(espera, _tempo, ct);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private async Task<bool> AguardarQuedaAsync(CancellationToken ct)
    {
        try
        {
            await _sinalQueda.WaitAsync(ct);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private async Task RefazerAssinaturasAsync(CancellationToken ct)
    {
        foreach (var filtro in FiltrosRegistrados)
        {
            try
            {
                await _adaptador.AssinarAsync(filtro, ct);
            }
            catch (MqttAssinaturaRecusadaException ex)
            {
                // Recusa (ex.: ACL) não se resolve reconectando: registra e segue com os demais filtros.
                _logger.LogError("O broker MQTT recusou a assinatura de {Filtro} (código {Codigo}).", ex.Filtro, ex.Codigo);
            }
        }
    }

    private Task AoDesconectarAsync(DesconexaoMqtt desconexao)
    {
        _pronto = false;
        if (!_parando)
        {
            if (desconexao.SessaoAssumida)
            {
                _logger.LogError(
                    "Outra instância está usando o usuário MQTT {Usuario}; só pode haver uma conectada (o broker usa o usuário como ClientId). " +
                    "Para escalar, crie um usuário MQTT por instância.",
                    _opcoes.Usuario);
            }
            else
            {
                _logger.LogWarning("Conexão com o broker MQTT perdida: {Motivo}. Reconectando.", desconexao.Motivo);
            }
        }

        Sinalizar();
        return Task.CompletedTask;
    }

    private async Task AoReceberAsync(MensagemMqtt mensagem)
    {
        foreach (var registro in Volatile.Read(ref _registros))
        {
            if (!TopicosMqtt.Casa(registro.Filtro, mensagem.Topico))
                continue;

            try
            {
                await registro.Tratador(mensagem, _tokenServico);
            }
            catch (Exception ex)
            {
                // Nunca loga o payload. A exceção não sobe para o MQTTnet (não derruba a conexão).
                _logger.LogError(ex, "Tratador da assinatura {Filtro} falhou ao processar mensagem de {Topico}.", registro.Filtro, mensagem.Topico);
            }
        }
    }

    private void Sinalizar()
    {
        try
        {
            if (_sinalQueda.CurrentCount == 0)
                _sinalQueda.Release();
        }
        catch (SemaphoreFullException)
        {
            // já havia sinal pendente
        }
        catch (ObjectDisposedException)
        {
            // encerrado
        }
    }

    private async Task DesconectarSemFalharAsync(CancellationToken ct)
    {
        if (!_adaptador.Conectado)
            return;

        try
        {
            await _adaptador.DesconectarAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Falha ao desconectar do broker MQTT: {Erro}.", Descrever(ex));
        }
    }

    private void Remover(Registro registro)
    {
        lock (_trava)
            _registros = _registros.Where(r => !ReferenceEquals(r, registro)).ToArray();
    }

    // Tipo + mensagem (sem stack trace no warning). As mensagens do MQTTnet/Socket não incluem a senha.
    private static string Descrever(Exception ex) =>
        ex.InnerException is null ? $"{ex.GetType().Name}: {ex.Message}" : $"{ex.GetType().Name}: {ex.Message} ({ex.InnerException.Message})";

    private sealed class Registro(ServicoMqtt servico, string filtro, Func<MensagemMqtt, CancellationToken, Task> tratador) : IDisposable
    {
        public string Filtro { get; } = filtro;

        public Func<MensagemMqtt, CancellationToken, Task> Tratador { get; } = tratador;

        public void Dispose() => servico.Remover(this);
    }
}
