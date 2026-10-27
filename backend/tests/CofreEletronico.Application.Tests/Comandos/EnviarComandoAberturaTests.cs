using System.Text.Json;
using CofreEletronico.Application.Comandos;
using CofreEletronico.Application.Mqtt.Portas;
using CofreEletronico.Application.Tests.Autenticacao;
using CofreEletronico.Application.Tests.Comum;
using Microsoft.Extensions.Logging;

namespace CofreEletronico.Application.Tests.Comandos;

public class EnviarComandoAberturaTests
{
    private static readonly DateTimeOffset Agora = new(2026, 10, 27, 15, 30, 45, 987, TimeSpan.Zero);
    private static readonly byte[] Segredo = Enumerable.Range(100, 32).Select(i => (byte)i).ToArray();
    private const string DeviceId = "est-7k2m9q";

    private readonly Guid _estanteId = Guid.NewGuid();
    private readonly Guid _cofreId = Guid.NewGuid();
    private readonly DadosComandoRepositorioFalso _repositorio = new();
    private readonly ClienteMqttFalso _mqtt = new();
    private readonly RelogioFalso _relogio = new(Agora);
    private readonly LoggerCapturado<EnviarComandoAbertura> _log = new();
    private readonly EnviarComandoAbertura _casoDeUso;

    public EnviarComandoAberturaTests()
    {
        _repositorio.Adicionar(_estanteId, _cofreId, DeviceId, 3, 5, Segredo);
        _casoDeUso = new EnviarComandoAbertura(_repositorio, _mqtt, _relogio, _log);
    }

    private JsonElement UltimoPayload() => JsonDocument.Parse(_mqtt.Publicados[^1].Payload).RootElement;

    [Fact]
    public async Task Publica_para_o_deviceId_da_estante_com_numero_canal_e_assinatura_corretos()
    {
        var resultado = await _casoDeUso.ExecutarAsync(_estanteId, _cofreId);

        Assert.Equal(StatusEnvioComando.Enviado, resultado.Status);
        var (deviceId, _) = Assert.Single(_mqtt.Publicados);
        Assert.Equal(DeviceId, deviceId);

        var json = UltimoPayload();
        var comando = resultado.Comando!;
        Assert.Equal(comando.CommandId.ToString("D"), json.GetProperty("commandId").GetString());
        Assert.Equal(3, json.GetProperty("cofre").GetInt32());
        Assert.Equal(5, json.GetProperty("canal").GetInt32());
        Assert.Equal("abrir", json.GetProperty("acao").GetString());
        Assert.Equal(new ComandoEnviado(comando.CommandId, DeviceId, 3, 5, Agora.ToUnixTimeSeconds()), comando);

        var esperado = MontadorComando.Montar(DeviceId, comando.CommandId, 3, 5, AcaoComando.Abrir, comando.Ts, Segredo);
        Assert.Equal(esperado.Assinatura, json.GetProperty("assinatura").GetString());
    }

    [Fact]
    public async Task Ts_e_o_instante_do_TimeProvider_em_segundos_Unix()
    {
        var resultado = await _casoDeUso.ExecutarAsync(_estanteId, _cofreId);

        Assert.Equal(Agora.ToUnixTimeSeconds(), resultado.Comando!.Ts);
        Assert.Equal(Agora.ToUnixTimeSeconds(), UltimoPayload().GetProperty("ts").GetInt64());
    }

    [Fact]
    public async Task CommandId_no_formato_D_minusculo_e_novo_a_cada_chamada()
    {
        await _casoDeUso.ExecutarAsync(_estanteId, _cofreId);
        await _casoDeUso.ExecutarAsync(_estanteId, _cofreId);

        var ids = _mqtt.Publicados
            .Select(p => JsonDocument.Parse(p.Payload).RootElement.GetProperty("commandId").GetString()!)
            .ToList();

        Assert.Equal(2, ids.Count);
        Assert.All(ids, id => Assert.Matches("^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$", id));
        Assert.NotEqual(ids[0], ids[1]);
        Assert.NotEqual(_mqtt.Publicados[0].Payload, _mqtt.Publicados[1].Payload);
    }

    [Fact]
    public async Task Payload_publicado_fica_abaixo_de_4096_bytes()
    {
        await _casoDeUso.ExecutarAsync(_estanteId, _cofreId);

        Assert.True(_mqtt.Publicados[0].Payload.Length < 4096);
    }

    [Fact]
    public async Task Cofre_de_outra_estante_da_CofreNaoEncontrado_sem_publicar()
    {
        var outraEstante = Guid.NewGuid();
        _repositorio.Adicionar(outraEstante, Guid.NewGuid(), "est-outra1", 1, 0, Segredo);

        var resultado = await _casoDeUso.ExecutarAsync(outraEstante, _cofreId);

        Assert.Equal(StatusEnvioComando.CofreNaoEncontrado, resultado.Status);
        Assert.Null(resultado.Comando);
        Assert.Equal(0, _mqtt.Chamadas);
    }

    [Fact]
    public async Task Cofre_inexistente_da_CofreNaoEncontrado()
    {
        var resultado = await _casoDeUso.ExecutarAsync(_estanteId, Guid.NewGuid());

        Assert.Equal(StatusEnvioComando.CofreNaoEncontrado, resultado.Status);
        Assert.Equal(0, _mqtt.Chamadas);
    }

    [Fact]
    public async Task MqttIndisponivel_vira_status_sem_reenvio_e_com_warning()
    {
        _mqtt.FalhaAoPublicar = new MqttIndisponivelException("Sem conexão com o broker MQTT.");

        var resultado = await _casoDeUso.ExecutarAsync(_estanteId, _cofreId);

        Assert.Equal(StatusEnvioComando.MqttIndisponivel, resultado.Status);
        Assert.NotNull(resultado.Comando);
        Assert.Equal(1, _mqtt.Chamadas);
        var aviso = Assert.Single(_log.Registros, r => r.Nivel == LogLevel.Warning);
        Assert.Contains(resultado.Comando!.CommandId.ToString(), aviso.Mensagem);
        Assert.DoesNotContain(_log.Registros, r => r.Nivel == LogLevel.Information && r.Mensagem.Contains("publicado"));
    }

    [Fact]
    public async Task Publicacao_recusada_vira_status_com_log_de_erro()
    {
        _mqtt.FalhaAoPublicar = new MqttPublicacaoRecusadaException("cofre/est-7k2m9q/cmd", "NotAuthorized");

        var resultado = await _casoDeUso.ExecutarAsync(_estanteId, _cofreId);

        Assert.Equal(StatusEnvioComando.PublicacaoRecusada, resultado.Status);
        Assert.Equal(1, _mqtt.Chamadas);
        var erro = Assert.Single(_log.Registros, r => r.Nivel == LogLevel.Error);
        Assert.Contains("NotAuthorized", erro.Mensagem);
        Assert.Contains("ACL", erro.Mensagem);
    }

    [Fact]
    public async Task Outra_excecao_da_publicacao_propaga()
    {
        _mqtt.FalhaAoPublicar = new ArgumentException("pacote grande demais");

        await Assert.ThrowsAsync<ArgumentException>(() => _casoDeUso.ExecutarAsync(_estanteId, _cofreId));
        Assert.Equal(1, _mqtt.Chamadas);
    }

    [Fact]
    public async Task Segredo_e_zerado_antes_de_publicar_no_sucesso()
    {
        var zeradoAoPublicar = false;
        _mqtt.AoPublicar = () => zeradoAoPublicar = _repositorio.SegredosEntregues[^1].All(b => b == 0);

        await _casoDeUso.ExecutarAsync(_estanteId, _cofreId);

        Assert.True(zeradoAoPublicar);
        Assert.All(_repositorio.SegredosEntregues, s => Assert.All(s, b => Assert.Equal(0, b)));
    }

    [Theory]
    [InlineData(typeof(MqttIndisponivelException))]
    [InlineData(typeof(MqttPublicacaoRecusadaException))]
    [InlineData(typeof(InvalidOperationException))]
    public async Task Segredo_e_zerado_mesmo_quando_a_publicacao_falha(Type tipoFalha)
    {
        _mqtt.FalhaAoPublicar = tipoFalha == typeof(MqttPublicacaoRecusadaException)
            ? new MqttPublicacaoRecusadaException("t", "c")
            : (Exception)Activator.CreateInstance(tipoFalha, "falha")!;

        try
        {
            await _casoDeUso.ExecutarAsync(_estanteId, _cofreId);
        }
        catch (InvalidOperationException)
        {
            // propagada de propósito
        }

        var segredo = Assert.Single(_repositorio.SegredosEntregues);
        Assert.All(segredo, b => Assert.Equal(0, b));
    }

    [Fact]
    public async Task Segredo_e_zerado_quando_a_montagem_lanca()
    {
        _repositorio.Adicionar(_estanteId, _cofreId, DeviceId, 3, 99, Segredo); // canal fora do contrato

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _casoDeUso.ExecutarAsync(_estanteId, _cofreId));

        Assert.All(Assert.Single(_repositorio.SegredosEntregues), b => Assert.Equal(0, b));
        Assert.Equal(0, _mqtt.Chamadas);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(typeof(MqttIndisponivelException))]
    [InlineData(typeof(MqttPublicacaoRecusadaException))]
    public async Task Logs_nao_contem_segredo_mensagem_canonica_nem_assinatura(Type? tipoFalha)
    {
        _mqtt.FalhaAoPublicar = tipoFalha switch
        {
            null => null,
            _ when tipoFalha == typeof(MqttPublicacaoRecusadaException) => new MqttPublicacaoRecusadaException("cofre/est-7k2m9q/cmd", "NotAuthorized"),
            _ => new MqttIndisponivelException("Sem conexão com o broker MQTT."),
        };

        var resultado = await _casoDeUso.ExecutarAsync(_estanteId, _cofreId);

        var comando = resultado.Comando!;
        var assinado = MontadorComando.Montar(DeviceId, comando.CommandId, 3, 5, AcaoComando.Abrir, comando.Ts, Segredo);
        var logs = _log.Tudo;
        Assert.NotEmpty(_log.Registros);
        Assert.Contains(comando.CommandId.ToString(), logs);
        Assert.Contains(DeviceId, logs);
        Assert.DoesNotContain(Convert.ToHexString(Segredo), logs, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Convert.ToBase64String(Segredo), logs);
        Assert.DoesNotContain(assinado.Assinatura, logs);
        Assert.DoesNotContain(assinado.MensagemCanonica, logs);
        Assert.DoesNotContain("\"assinatura\"", logs);
    }

    [Fact]
    public async Task Sucesso_gera_log_informativo_com_commandId_deviceId_cofre_e_canal()
    {
        var resultado = await _casoDeUso.ExecutarAsync(_estanteId, _cofreId);

        var info = Assert.Single(_log.Registros, r => r.Nivel == LogLevel.Information);
        Assert.Contains(resultado.Comando!.CommandId.ToString(), info.Mensagem);
        Assert.Contains(DeviceId, info.Mensagem);
        Assert.Contains("cofre 3", info.Mensagem);
        Assert.Contains("canal 5", info.Mensagem);
    }

    [Fact]
    public async Task Preparar_devolve_o_commandId_sem_publicar_e_publicar_usa_o_mesmo_comando()
    {
        // Ponto de extensão da T-0603: registrar o pendente entre preparar e publicar.
        var preparado = await _casoDeUso.PrepararAsync(_estanteId, _cofreId);

        Assert.NotNull(preparado);
        Assert.Equal(0, _mqtt.Chamadas);
        Assert.All(Assert.Single(_repositorio.SegredosEntregues), b => Assert.Equal(0, b));

        var resultado = await _casoDeUso.PublicarAsync(preparado!);

        Assert.Equal(StatusEnvioComando.Enviado, resultado.Status);
        Assert.Same(preparado!.Comando, resultado.Comando);
        Assert.Equal(preparado.Comando.CommandId.ToString("D"), UltimoPayload().GetProperty("commandId").GetString());
    }

    [Fact]
    public async Task Preparar_cofre_de_outra_estante_devolve_null()
    {
        Assert.Null(await _casoDeUso.PrepararAsync(Guid.NewGuid(), _cofreId));
    }

    [Fact]
    public async Task Mesmo_comando_preparado_nao_pode_ser_publicado_duas_vezes()
    {
        _mqtt.FalhaAoPublicar = new MqttIndisponivelException("Sem conexão com o broker MQTT.");
        var preparado = (await _casoDeUso.PrepararAsync(_estanteId, _cofreId))!;
        await _casoDeUso.PublicarAsync(preparado);
        _mqtt.FalhaAoPublicar = null;

        await Assert.ThrowsAsync<InvalidOperationException>(() => _casoDeUso.PublicarAsync(preparado));

        Assert.Equal(1, _mqtt.Chamadas);
    }

    // ---------- Cancelamento (revisão R1) ----------

    [Fact]
    public async Task Cancelar_a_requisicao_durante_o_envio_nao_interrompe_a_publicacao()
    {
        using var requisicao = new CancellationTokenSource();
        var tokenAindaValidoAposCancelar = false;
        _mqtt.DuranteEnvio = async ct =>
        {
            await requisicao.CancelAsync(); // o cliente fecha o app com o PUBLISH em andamento
            tokenAindaValidoAposCancelar = !ct.IsCancellationRequested;
            await Task.Delay(10, ct); // continua o envio; com o token da requisição, lançaria
        };

        var resultado = await _casoDeUso.ExecutarAsync(_estanteId, _cofreId, requisicao.Token);

        Assert.Equal(StatusEnvioComando.Enviado, resultado.Status);
        Assert.True(tokenAindaValidoAposCancelar);
        Assert.False(_mqtt.TokenRecebido.CanBeCanceled);
        Assert.Single(_mqtt.Publicados);
        Assert.Single(_log.Registros, r => r.Nivel == LogLevel.Information && r.Mensagem.Contains("publicado"));
    }

    [Fact]
    public async Task OperationCanceledException_durante_o_envio_vira_MqttIndisponivel_com_warning()
    {
        _mqtt.FalhaAoPublicar = new OperationCanceledException("interrompido");

        var resultado = await _casoDeUso.ExecutarAsync(_estanteId, _cofreId);

        Assert.Equal(StatusEnvioComando.MqttIndisponivel, resultado.Status);
        Assert.Equal(1, _mqtt.Chamadas);
        var aviso = Assert.Single(_log.Registros, r => r.Nivel == LogLevel.Warning);
        Assert.Contains(resultado.Comando!.CommandId.ToString(), aviso.Mensagem);
        Assert.Contains(DeviceId, aviso.Mensagem);
    }

    [Fact]
    public async Task Requisicao_cancelada_antes_do_envio_nao_publica_e_consome_o_comando()
    {
        using var requisicao = new CancellationTokenSource();
        await requisicao.CancelAsync();
        var preparado = (await _casoDeUso.PrepararAsync(_estanteId, _cofreId))!;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _casoDeUso.PublicarAsync(preparado, requisicao.Token));

        Assert.Equal(0, _mqtt.Chamadas);
        // O comando fica consumido: nunca é publicado depois, nem em outra tentativa.
        await Assert.ThrowsAsync<InvalidOperationException>(() => _casoDeUso.PublicarAsync(preparado));
        Assert.Equal(0, _mqtt.Chamadas);
    }

    [Fact]
    public async Task ToString_dos_resultados_nao_expoe_o_payload()
    {
        var preparado = (await _casoDeUso.PrepararAsync(_estanteId, _cofreId))!;

        Assert.DoesNotContain("assinatura", preparado.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains(preparado.Comando.CommandId.ToString(), preparado.ToString());
    }

    [Fact]
    public void DadosComando_ToString_mascara_o_segredo()
    {
        var dados = new Application.Comandos.Portas.DadosComando(DeviceId, 3, 5, Segredo);

        var texto = dados.ToString();

        Assert.Contains("***", texto);
        Assert.DoesNotContain("System.Byte", texto);
        Assert.Contains(DeviceId, texto);
    }
}
