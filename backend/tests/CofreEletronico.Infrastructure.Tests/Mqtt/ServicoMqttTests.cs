using System.Text;
using CofreEletronico.Application.Mqtt;
using CofreEletronico.Application.Mqtt.Portas;
using CofreEletronico.Infrastructure.Mqtt;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CofreEletronico.Infrastructure.Tests.Mqtt;

public sealed class ServicoMqttTests : IAsyncDisposable
{
    private const string DeviceId = "est-7k2m9q";
    private const string Topico = "cofre/est-7k2m9q/cmd";

    private readonly AdaptadorMqttFalso _adaptador = new();
    private readonly RelogioMqtt _relogio = new();
    private readonly LoggerMqtt<ServicoMqtt> _logger = new();
    private ServicoMqtt? _servico;

    public async ValueTask DisposeAsync()
    {
        if (_servico is not null)
        {
            await _servico.StopAsync(CancellationToken.None);
            _servico.Dispose();
        }
    }

    private ServicoMqtt Criar(string? host = "broker.local", double aleatorio = 0.5, bool permitirCertificadoInvalido = false)
    {
        var opcoes = Options.Create(new MqttOpcoes
        {
            Host = host,
            PermitirCertificadoInvalidoEmDesenvolvimento = permitirCertificadoInvalido,
            Senha = "senha-ficticia-de-teste",
            ReconexaoInicialSegundos = 1,
            ReconexaoMaximaSegundos = 60,
        });
        _servico = new ServicoMqtt(opcoes, _adaptador, _relogio, _logger, () => aleatorio);
        return _servico;
    }

    private async Task<ServicoMqtt> CriarConectadoAsync()
    {
        var servico = Criar();
        await servico.StartAsync(CancellationToken.None);
        await Esperar.AteAsync(() => servico.Conectado, "conectar");
        return servico;
    }

    /// <summary>Queda de uma conexão que já durou mais que o limite de estabilidade (30 s): reconecta na hora.</summary>
    private Task QuedaAposConexaoEstavelAsync(string motivo = "queda simulada")
    {
        _relogio.Advance(TimeSpan.FromSeconds(31));
        return _adaptador.SimularQuedaAsync(motivo);
    }

    [Fact]
    public async Task Sem_host_fica_inativo_com_um_log_informativo()
    {
        var servico = Criar(host: "");

        await servico.StartAsync(CancellationToken.None);
        await servico.StopAsync(CancellationToken.None);

        Assert.False(servico.Habilitado);
        Assert.False(servico.Conectado);
        Assert.Equal(0, _adaptador.TentativasConexao);
        Assert.Equal(1, _logger.Contar(LogLevel.Information, "MQTT desabilitado"));
        Assert.Equal(0, _adaptador.Desconexoes);
    }

    [Fact]
    public async Task Sem_host_publicar_falha_e_assinar_so_registra()
    {
        var servico = Criar(host: null);
        await servico.StartAsync(CancellationToken.None);

        await Assert.ThrowsAsync<MqttIndisponivelException>(() =>
            servico.PublicarComandoAsync(DeviceId, "{}"u8.ToArray(), CancellationToken.None));
        using var registro = await servico.AssinarAsync(TopicosMqtt.FiltroAck, (_, _) => Task.CompletedTask, CancellationToken.None);

        Assert.Empty(_adaptador.Assinaturas);
    }

    [Fact]
    public async Task Subida_nao_espera_a_conexao()
    {
        _adaptador.PortaoConexao = new TaskCompletionSource(); // conexão nunca completa
        var servico = Criar();

        var subida = servico.StartAsync(CancellationToken.None);

        await subida.WaitAsync(TimeSpan.FromSeconds(2)); // TimeoutException se a subida esperar o broker
        Assert.False(servico.Conectado);
    }

    [Fact]
    public async Task Broker_fora_tenta_de_novo_com_backoff_crescente_ate_conectar()
    {
        _adaptador.FalhasRestantes = 3;
        var servico = Criar(aleatorio: 0.5); // sem jitter efetivo: 1 s, 2 s, 4 s

        await servico.StartAsync(CancellationToken.None);

        foreach (var (tentativa, espera) in new[] { (1, 1), (2, 2), (3, 4) })
        {
            await Esperar.AteAsync(() => _relogio.TimersCriados == tentativa, $"espera da tentativa {tentativa}");
            Assert.Equal(tentativa, _adaptador.TentativasConexao);

            _relogio.Advance(TimeSpan.FromSeconds(espera) - TimeSpan.FromMilliseconds(1));
            await Task.Delay(50);
            Assert.Equal(tentativa, _adaptador.TentativasConexao); // não tenta antes do intervalo

            _relogio.Advance(TimeSpan.FromMilliseconds(1));
            await Esperar.AteAsync(() => _adaptador.TentativasConexao == tentativa + 1, $"tentativa {tentativa + 1}");
        }

        await Esperar.AteAsync(() => servico.Conectado, "conectar");
        Assert.Equal(3, _logger.Contar(LogLevel.Warning, "Falha ao conectar ao broker MQTT"));
        Assert.Contains("(tentativa 3)", _logger.Tudo);
        Assert.Equal(1, _logger.Contar(LogLevel.Information, "Conectado ao broker MQTT"));
        Assert.DoesNotContain("senha-ficticia-de-teste", _logger.Tudo);
    }

    [Fact]
    public async Task Reconecta_apos_queda_e_refaz_todas_as_assinaturas()
    {
        var servico = Criar();
        using var a = await servico.AssinarAsync(TopicosMqtt.FiltroAck, (_, _) => Task.CompletedTask, CancellationToken.None);
        using var b = await servico.AssinarAsync(TopicosMqtt.FiltroEstado, (_, _) => Task.CompletedTask, CancellationToken.None);
        using var c = await servico.AssinarAsync(TopicosMqtt.FiltroAck, (_, _) => Task.CompletedTask, CancellationToken.None);
        Assert.Empty(_adaptador.Assinaturas); // ainda desconectado

        await servico.StartAsync(CancellationToken.None);
        await Esperar.AteAsync(() => servico.Conectado, "conectar");
        Assert.Equal([TopicosMqtt.FiltroAck, TopicosMqtt.FiltroEstado], _adaptador.FiltrosDaConexao(1));

        // Assinatura feita com a conexão ativa também vale para as próximas conexões.
        using var d = await servico.AssinarAsync(TopicosMqtt.FiltroOnline, (_, _) => Task.CompletedTask, CancellationToken.None);
        Assert.Contains(TopicosMqtt.FiltroOnline, _adaptador.FiltrosDaConexao(1));

        await QuedaAposConexaoEstavelAsync("KeepAliveTimeout");
        await Esperar.AteAsync(() => _adaptador.Conexoes == 2, "reconectar");

        Assert.True(servico.Conectado);
        Assert.Equal([TopicosMqtt.FiltroAck, TopicosMqtt.FiltroEstado, TopicosMqtt.FiltroOnline], _adaptador.FiltrosDaConexao(2));
        Assert.Equal(1, _logger.Contar(LogLevel.Warning, "Conexão com o broker MQTT perdida: KeepAliveTimeout"));
    }

    [Fact]
    public async Task Queda_seguida_de_falhas_volta_ao_backoff()
    {
        var servico = await CriarConectadoAsync();
        _adaptador.FalhasRestantes = 1;

        await QuedaAposConexaoEstavelAsync();
        await Esperar.AteAsync(() => _relogio.TimersCriados == 1, "espera após falha");
        Assert.False(servico.Conectado);

        _relogio.Advance(TimeSpan.FromSeconds(1));
        await Esperar.AteAsync(() => servico.Conectado, "reconectar");
        Assert.Equal(2, _adaptador.Conexoes);
    }

    [Fact]
    public async Task Nunca_faz_duas_tentativas_ao_mesmo_tempo()
    {
        var servico = await CriarConectadoAsync();
        _adaptador.PortaoConexao = new TaskCompletionSource();

        await QuedaAposConexaoEstavelAsync();
        await Esperar.AteAsync(() => _adaptador.TentativasConexao == 2, "tentativa após queda");
        for (var i = 0; i < 5; i++)
            await _adaptador.SimularQuedaAsync(); // eventos repetidos durante a tentativa

        _adaptador.PortaoConexao.SetResult();
        await Esperar.AteAsync(() => servico.Conectado, "reconectar");
        await Task.Delay(100);

        Assert.Equal(1, _adaptador.MaximoConcorrente);
        Assert.True(_adaptador.TentativasConexao <= 4, $"tentativas demais: {_adaptador.TentativasConexao}");
    }

    [Fact]
    public async Task Assinatura_recusada_com_conexao_ativa_lanca_e_nao_fica_registrada()
    {
        var servico = await CriarConectadoAsync();
        _adaptador.FiltrosRecusados.Add("cofre/#");

        await Assert.ThrowsAsync<MqttAssinaturaRecusadaException>(() =>
            servico.AssinarAsync("cofre/#", (_, _) => Task.CompletedTask, CancellationToken.None));

        Assert.DoesNotContain("cofre/#", servico.FiltrosRegistrados);
    }

    [Fact]
    public async Task Assinatura_descartada_nao_e_refeita_nem_recebe_mensagens()
    {
        var servico = await CriarConectadoAsync();
        var recebidas = 0;
        var registro = await servico.AssinarAsync(TopicosMqtt.FiltroAck, (_, _) => { recebidas++; return Task.CompletedTask; }, CancellationToken.None);

        registro.Dispose();
        await _adaptador.EntregarAsync(new MensagemMqtt("cofre/est-7k2m9q/ack", "{}"u8.ToArray(), false));
        await QuedaAposConexaoEstavelAsync();
        await Esperar.AteAsync(() => _adaptador.Conexoes == 2, "reconectar");

        Assert.Equal(0, recebidas);
        Assert.Empty(_adaptador.FiltrosDaConexao(2));
    }

    [Fact]
    public async Task Publicar_desconectado_falha_na_hora_sem_fila()
    {
        _adaptador.FalhasRestantes = int.MaxValue;
        var servico = Criar();
        await servico.StartAsync(CancellationToken.None);
        await Esperar.AteAsync(() => _adaptador.TentativasConexao >= 1, "primeira tentativa");

        await Assert.ThrowsAsync<MqttIndisponivelException>(() =>
            servico.PublicarComandoAsync(DeviceId, "{}"u8.ToArray(), CancellationToken.None));

        Assert.Empty(_adaptador.Publicacoes);
    }

    [Fact]
    public async Task Publica_no_topico_de_comando_do_deviceId()
    {
        var servico = await CriarConectadoAsync();

        await servico.PublicarComandoAsync(DeviceId, Encoding.UTF8.GetBytes("{\"a\":1}"), CancellationToken.None);

        var publicacao = Assert.Single(_adaptador.Publicacoes);
        Assert.Equal(Topico, publicacao.Topico);
        Assert.Equal("{\"a\":1}", Encoding.UTF8.GetString(publicacao.Payload));
    }

    [Fact]
    public async Task Payload_acima_do_limite_do_broker_e_recusado()
    {
        var servico = await CriarConectadoAsync();
        // Maior payload que cabe (o tamanho restante ocupa 2 bytes nessa faixa).
        var limite = Enumerable.Range(0, TopicosMqtt.TamanhoMaximoPacote).Last(n => TopicosMqtt.TamanhoPacotePublicacao(Topico, n) <= TopicosMqtt.TamanhoMaximoPacote);

        await servico.PublicarComandoAsync(DeviceId, new byte[limite], CancellationToken.None); // cabe exatamente
        var erro = await Assert.ThrowsAsync<ArgumentException>(() =>
            servico.PublicarComandoAsync(DeviceId, new byte[limite + 1], CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            servico.PublicarComandoAsync(DeviceId, new byte[4097], CancellationToken.None));

        Assert.Contains("4096", erro.Message);
        Assert.Single(_adaptador.Publicacoes);
    }

    [Theory]
    [InlineData("est-+")]
    [InlineData("+")]
    [InlineData("#")]
    [InlineData("est-7k2m9q/../x")]
    [InlineData("est-12345/")]
    [InlineData("backend")]
    public async Task DeviceId_invalido_e_recusado_sem_publicar(string deviceId)
    {
        var servico = await CriarConectadoAsync();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            servico.PublicarComandoAsync(deviceId, "{}"u8.ToArray(), CancellationToken.None));

        Assert.Empty(_adaptador.Publicacoes);
    }

    [Fact]
    public void Nenhuma_porta_permite_escolher_topico_nem_retencao()
    {
        // A porta só publica comando (tópico montado a partir do deviceId) e nem a porta nem o adaptador aceitam
        // flag de retenção: o adaptador real fixa retain = false.
        var publicacoesPorta = typeof(IClienteMqtt).GetMethods().Where(m => m.Name.StartsWith("Publicar", StringComparison.Ordinal)).ToList();
        var publicar = Assert.Single(publicacoesPorta);
        Assert.Equal(nameof(IClienteMqtt.PublicarComandoAsync), publicar.Name);
        Assert.Equal("deviceId", publicar.GetParameters()[0].Name);

        var metodos = typeof(IClienteMqtt).GetMethods().Concat([typeof(IAdaptadorMqtt).GetMethod(nameof(IAdaptadorMqtt.PublicarAsync))!]);
        Assert.DoesNotContain(metodos.SelectMany(m => m.GetParameters()), p => p.ParameterType == typeof(bool));
    }

    [Fact]
    public async Task Falha_de_comunicacao_ao_publicar_vira_indisponivel_e_recusa_do_broker_passa_adiante()
    {
        var servico = await CriarConectadoAsync();

        _adaptador.ErroPublicacao = new TimeoutException("sem PUBACK");
        var indisponivel = await Assert.ThrowsAsync<MqttIndisponivelException>(() =>
            servico.PublicarComandoAsync(DeviceId, "{}"u8.ToArray(), CancellationToken.None));
        Assert.IsType<TimeoutException>(indisponivel.InnerException);

        _adaptador.ErroPublicacao = new MqttPublicacaoRecusadaException(Topico, "NotAuthorized");
        await Assert.ThrowsAsync<MqttPublicacaoRecusadaException>(() =>
            servico.PublicarComandoAsync(DeviceId, "{}"u8.ToArray(), CancellationToken.None));
    }

    [Fact]
    public async Task Excecao_no_tratador_e_logada_e_isolada()
    {
        var servico = await CriarConectadoAsync();
        var recebidasPeloSegundo = new List<MensagemMqtt>();
        var recebidasPorOutroFiltro = 0;
        using var a = await servico.AssinarAsync(TopicosMqtt.FiltroAck,
            (_, _) => throw new InvalidOperationException("falha no tratador"), CancellationToken.None);
        using var b = await servico.AssinarAsync(TopicosMqtt.FiltroAck,
            (m, _) => { recebidasPeloSegundo.Add(m); return Task.CompletedTask; }, CancellationToken.None);
        using var c = await servico.AssinarAsync(TopicosMqtt.FiltroEstado,
            (_, _) => { recebidasPorOutroFiltro++; return Task.CompletedTask; }, CancellationToken.None);

        var mensagem = new MensagemMqtt("cofre/est-7k2m9q/ack", "{\"segredo\":\"payload\"}"u8.ToArray(), false);
        await _adaptador.EntregarAsync(mensagem); // não lança

        Assert.Same(mensagem, Assert.Single(recebidasPeloSegundo));
        Assert.Equal(0, recebidasPorOutroFiltro);
        Assert.True(servico.Conectado);
        Assert.Equal(1, _logger.Contar(LogLevel.Error, "Tratador da assinatura cofre/+/ack falhou"));
        Assert.DoesNotContain("payload", _logger.Tudo);
    }

    [Fact]
    public async Task Encerramento_desconecta_limpo_sem_warning_de_queda()
    {
        var servico = await CriarConectadoAsync();

        await servico.StopAsync(CancellationToken.None);

        Assert.Equal(1, _adaptador.Desconexoes);
        Assert.False(servico.Conectado);
        Assert.Equal(0, _logger.Contar(LogLevel.Warning, "perdida"));
        Assert.Equal(1, _logger.Contar(LogLevel.Information, "Desconectado do broker MQTT"));
    }

    [Fact]
    public async Task Quedas_logo_apos_conectar_esperam_cada_vez_mais()
    {
        // Ex.: duas instâncias com o usuário "backend" se derrubando. Sem jitter efetivo: 1 s, 2 s, 4 s, 8 s.
        var servico = await CriarConectadoAsync();

        var esperas = new[] { 1, 2, 4, 8 };
        for (var i = 0; i < esperas.Length; i++)
        {
            await _adaptador.SimularQuedaAsync("SessionTakenOver"); // durou 0 s
            await Esperar.AteAsync(() => _relogio.TimersCriados == i + 1, $"espera após a queda {i + 1}");

            _relogio.Advance(TimeSpan.FromSeconds(esperas[i]) - TimeSpan.FromMilliseconds(1));
            await Task.Delay(50);
            Assert.Equal(i + 1, _adaptador.Conexoes); // não reconecta antes do intervalo

            _relogio.Advance(TimeSpan.FromMilliseconds(1));
            await Esperar.AteAsync(() => _adaptador.Conexoes == i + 2 && servico.Conectado, $"reconexão {i + 2}");
        }

        Assert.Equal(esperas.Length, _logger.Contar(LogLevel.Warning, "A conexão com o broker MQTT durou só"));

        // Depois de uma conexão estável, a contagem zera: a próxima queda reconecta na hora, sem espera.
        await QuedaAposConexaoEstavelAsync();
        await Esperar.AteAsync(() => _adaptador.Conexoes == esperas.Length + 2, "reconexão imediata");
        Assert.Equal(esperas.Length, _relogio.TimersCriados);
    }

    [Fact]
    public async Task Sessao_assumida_por_outra_instancia_gera_erro_proprio()
    {
        await CriarConectadoAsync();

        await _adaptador.SimularQuedaAsync("SessionTakenOver", sessaoAssumida: true);

        Assert.Equal(1, _logger.Contar(LogLevel.Error, "Outra instância está usando o usuário MQTT backend; só pode haver uma conectada"));
        Assert.Equal(0, _logger.Contar(LogLevel.Warning, "Conexão com o broker MQTT perdida"));
    }

    [Fact]
    public async Task Credenciais_recusadas_logam_erro_espacado_e_usam_o_backoff_maximo()
    {
        _adaptador.FalhasRestantes = 12;
        _adaptador.ErroConexao = () => new FalhaCredenciaisMqttException("BadUserNameOrPassword");
        var servico = Criar(aleatorio: 0.5); // backoff máximo sem jitter efetivo: 60 s

        await servico.StartAsync(CancellationToken.None);

        for (var tentativa = 1; tentativa <= 12; tentativa++)
        {
            await Esperar.AteAsync(() => _relogio.TimersCriados == tentativa, $"espera da tentativa {tentativa}");

            _relogio.Advance(TimeSpan.FromSeconds(60) - TimeSpan.FromMilliseconds(1));
            await Task.Delay(20);
            Assert.Equal(tentativa, _adaptador.TentativasConexao); // já na 1ª falha, espera o máximo

            _relogio.Advance(TimeSpan.FromMilliseconds(1));
            await Esperar.AteAsync(() => _adaptador.TentativasConexao == tentativa + 1, $"tentativa {tentativa + 1}");
        }

        await Esperar.AteAsync(() => servico.Conectado, "conectar");
        // Erro na 1ª recusa e na 10ª (a cada IntervaloLogCredenciais); as demais só em Debug.
        Assert.Equal(2, _logger.Contar(LogLevel.Error, "recusou o usuário backend (BadUserNameOrPassword"));
        Assert.Equal(0, _logger.Contar(LogLevel.Warning, "Falha ao conectar"));
        Assert.DoesNotContain("senha-ficticia-de-teste", _logger.Tudo);
    }

    [Theory]
    [InlineData("broker.local")]
    [InlineData("")]
    public async Task Opcao_de_certificado_invalido_ligada_gera_warning_na_subida(string host)
    {
        var servico = Criar(host: host, permitirCertificadoInvalido: true);

        await servico.StartAsync(CancellationToken.None);

        await Esperar.AteAsync(
            () => _logger.Contar(LogLevel.Warning, "Mqtt:PermitirCertificadoInvalidoEmDesenvolvimento está ligada") == 1,
            "warning da opção de certificado inválido");
    }

    [Fact]
    public async Task Encerramento_durante_espera_do_backoff_termina_rapido()
    {
        _adaptador.FalhasRestantes = int.MaxValue;
        var servico = Criar();
        await servico.StartAsync(CancellationToken.None);
        await Esperar.AteAsync(() => _relogio.TimersCriados == 1, "espera do backoff");

        var parada = servico.StopAsync(CancellationToken.None);

        await parada.WaitAsync(TimeSpan.FromSeconds(2)); // TimeoutException se não terminar
        Assert.Equal(0, _adaptador.Desconexoes);
    }
}
