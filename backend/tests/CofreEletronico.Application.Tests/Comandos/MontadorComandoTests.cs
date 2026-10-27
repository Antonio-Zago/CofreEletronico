using System.Globalization;
using System.Text;
using System.Text.Json;
using CofreEletronico.Application.Comandos;
using CofreEletronico.Application.Mqtt;

namespace CofreEletronico.Application.Tests.Comandos;

/// <summary>Formato canônico do comando (docs/ARQUITETURA.md, "Formato canônico da assinatura").</summary>
public class MontadorComandoTests
{
    // Vetor de teste do contrato. SOMENTE PARA TESTES: nunca usar como segredo real de estante.
    private static readonly byte[] SegredoVetor = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
    private const string DeviceIdVetor = "est-7k2m9q";
    private static readonly Guid CommandIdVetor = Guid.Parse("3f2504e0-4f89-41d3-9a0c-0305e82c3301");
    private const int CofreVetor = 3;
    private const int CanalVetor = 5;
    private const long TsVetor = 1760000000;
    private const string MensagemVetor = "est-7k2m9q|3f2504e0-4f89-41d3-9a0c-0305e82c3301|3|5|abrir|1760000000";
    private const string AssinaturaVetor = "738bc5aa34868b4e0003d424e6a818535303396cc50f46a50279a47f1dce5f4d";

    private static ComandoAssinado MontarVetor() =>
        MontadorComando.Montar(DeviceIdVetor, CommandIdVetor, CofreVetor, CanalVetor, AcaoComando.Abrir, TsVetor, SegredoVetor);

    [Fact]
    public void Vetor_de_teste_do_contrato_produz_a_mensagem_e_a_assinatura_esperadas()
    {
        var comando = MontarVetor();

        Assert.Equal(MensagemVetor, comando.MensagemCanonica);
        Assert.Equal(AssinaturaVetor, comando.Assinatura);
    }

    [Fact]
    public void Mensagem_canonica_tem_seis_campos_separados_por_barra_sem_espacos()
    {
        var mensagem = MontadorComando.MensagemCanonica(DeviceIdVetor, CommandIdVetor, 12, 0, AcaoComando.Abrir, 42);

        Assert.Equal("est-7k2m9q|3f2504e0-4f89-41d3-9a0c-0305e82c3301|12|0|abrir|42", mensagem);
        Assert.Equal(6, mensagem.Split('|').Length);
        Assert.DoesNotContain(' ', mensagem);
    }

    [Fact]
    public void Assinatura_e_hex_minusculo_de_64_caracteres()
    {
        var comando = MontadorComando.Montar(
            "est-abc123", Guid.NewGuid(), 7, 15, AcaoComando.Abrir, 1_800_000_000, Enumerable.Repeat((byte)0xAB, 32).ToArray());

        Assert.Equal(MontadorComando.TamanhoAssinaturaHex, comando.Assinatura.Length);
        Assert.Matches("^[0-9a-f]{64}$", comando.Assinatura);
    }

    [Fact]
    public void DeviceId_diferente_gera_assinatura_diferente()
    {
        var outro = MontadorComando.Montar("est-7k2m9r", CommandIdVetor, CofreVetor, CanalVetor, AcaoComando.Abrir, TsVetor, SegredoVetor);

        Assert.NotEqual(AssinaturaVetor, outro.Assinatura);
    }

    public static TheoryData<string, Guid, int, int, long, byte[]> CamposAlterados => new()
    {
        { "est-zzzzzz", CommandIdVetor, CofreVetor, CanalVetor, TsVetor, SegredoVetor },
        { DeviceIdVetor, Guid.Parse("3f2504e0-4f89-41d3-9a0c-0305e82c3302"), CofreVetor, CanalVetor, TsVetor, SegredoVetor },
        { DeviceIdVetor, CommandIdVetor, 4, CanalVetor, TsVetor, SegredoVetor },
        { DeviceIdVetor, CommandIdVetor, CofreVetor, 6, TsVetor, SegredoVetor },
        { DeviceIdVetor, CommandIdVetor, CofreVetor, CanalVetor, TsVetor + 1, SegredoVetor },
        { DeviceIdVetor, CommandIdVetor, CofreVetor, CanalVetor, TsVetor, SegredoVetor.Select(b => (byte)(b ^ 1)).ToArray() },
    };

    [Theory]
    [MemberData(nameof(CamposAlterados))]
    public void Cada_campo_alterado_altera_a_assinatura(string deviceId, Guid commandId, int cofre, int canal, long ts, byte[] segredo)
    {
        var comando = MontadorComando.Montar(deviceId, commandId, cofre, canal, AcaoComando.Abrir, ts, segredo);

        Assert.NotEqual(AssinaturaVetor, comando.Assinatura);
    }

    [Fact]
    public void Chave_sao_os_bytes_do_segredo_e_nao_o_texto_hex()
    {
        var segredoComoTextoHex = Encoding.ASCII.GetBytes(Convert.ToHexString(SegredoVetor).ToLowerInvariant());

        // O texto hex (64 bytes) nem é aceito como chave...
        Assert.Throws<ArgumentException>(() =>
            MontadorComando.Montar(DeviceIdVetor, CommandIdVetor, CofreVetor, CanalVetor, AcaoComando.Abrir, TsVetor, segredoComoTextoHex));

        // ...e, se o firmware o usasse por engano, a assinatura não bateria com a do vetor.
        var comHex = Convert.ToHexString(
            System.Security.Cryptography.HMACSHA256.HashData(segredoComoTextoHex, Encoding.UTF8.GetBytes(MensagemVetor))).ToLowerInvariant();
        Assert.NotEqual(AssinaturaVetor, comHex);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("est-ABC123")]
    [InlineData("est-abc12")]
    [InlineData("est-abc1234")]
    [InlineData("est-abc12+")]
    [InlineData("est-abc12#")]
    [InlineData("est-ab/123")]
    [InlineData("est-abc123\n")]
    [InlineData("xyz-abc123")]
    public void DeviceId_fora_do_formato_e_recusado(string? deviceId)
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            MontadorComando.Montar(deviceId!, CommandIdVetor, CofreVetor, CanalVetor, AcaoComando.Abrir, TsVetor, SegredoVetor));
        Assert.Equal("deviceId", ex.ParamName);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(16)]
    [InlineData(int.MinValue)]
    public void Canal_fora_de_0_a_15_e_recusado(int canal)
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            MontadorComando.Montar(DeviceIdVetor, CommandIdVetor, CofreVetor, canal, AcaoComando.Abrir, TsVetor, SegredoVetor));
        Assert.Equal("canal", ex.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(15)]
    public void Canais_dos_extremos_sao_aceitos(int canal)
    {
        var comando = MontadorComando.Montar(DeviceIdVetor, CommandIdVetor, CofreVetor, canal, AcaoComando.Abrir, TsVetor, SegredoVetor);

        Assert.Equal(canal, comando.Canal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void Cofre_nao_positivo_e_recusado(int cofre)
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            MontadorComando.Montar(DeviceIdVetor, CommandIdVetor, cofre, CanalVetor, AcaoComando.Abrir, TsVetor, SegredoVetor));
        Assert.Equal("cofre", ex.ParamName);
    }

    [Fact]
    public void Acao_fora_do_enum_e_recusada()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            MontadorComando.Montar(DeviceIdVetor, CommandIdVetor, CofreVetor, CanalVetor, (AcaoComando)99, TsVetor, SegredoVetor));
        Assert.Equal("acao", ex.ParamName);
    }

    [Fact]
    public void Texto_da_acao_abrir_e_o_do_contrato()
    {
        Assert.Equal("abrir", MontadorComando.TextoAcao(AcaoComando.Abrir));
        Assert.Throws<ArgumentOutOfRangeException>(() => MontadorComando.TextoAcao((AcaoComando)99));
    }

    [Fact]
    public void CommandId_vazio_e_recusado()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            MontadorComando.Montar(DeviceIdVetor, Guid.Empty, CofreVetor, CanalVetor, AcaoComando.Abrir, TsVetor, SegredoVetor));
        Assert.Equal("commandId", ex.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Ts_nao_positivo_e_recusado(long ts)
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            MontadorComando.Montar(DeviceIdVetor, CommandIdVetor, CofreVetor, CanalVetor, AcaoComando.Abrir, ts, SegredoVetor));
        Assert.Equal("ts", ex.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(16)]
    [InlineData(31)]
    [InlineData(33)]
    [InlineData(48)]
    [InlineData(64)]
    [InlineData(65)]
    [InlineData(128)]
    public void Segredo_diferente_de_32_bytes_e_recusado(int tamanho)
    {
        var segredo = new byte[tamanho];

        var ex = Assert.Throws<ArgumentException>(() =>
            MontadorComando.Montar(DeviceIdVetor, CommandIdVetor, CofreVetor, CanalVetor, AcaoComando.Abrir, TsVetor, segredo));
        Assert.Equal("segredo", ex.ParamName);
    }

    [Fact]
    public void Segredo_de_exatamente_32_bytes_e_aceito()
    {
        var comando = MontadorComando.Montar(DeviceIdVetor, CommandIdVetor, CofreVetor, CanalVetor, AcaoComando.Abrir, TsVetor, new byte[32]);

        Assert.Matches("^[0-9a-f]{64}$", comando.Assinatura);
    }

    [Fact]
    public void Mensagem_do_segredo_invalido_diz_exatamente_32_bytes()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            MontadorComando.Montar(DeviceIdVetor, CommandIdVetor, CofreVetor, CanalVetor, AcaoComando.Abrir, TsVetor, new byte[64]));

        Assert.Contains("exatamente 32 bytes", ex.Message);
    }

    [Fact]
    public void Mensagem_de_erro_nao_contem_o_segredo()
    {
        var segredo = Enumerable.Repeat((byte)0x5A, 32).ToArray();
        var hex = Convert.ToHexString(segredo);

        var ex = Assert.ThrowsAny<ArgumentException>(() =>
            MontadorComando.Montar(DeviceIdVetor, CommandIdVetor, CofreVetor, 99, AcaoComando.Abrir, TsVetor, segredo));

        Assert.DoesNotContain(hex, ex.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Montar_nao_altera_o_array_do_segredo()
    {
        var segredo = (byte[])SegredoVetor.Clone();

        MontarVetor();
        MontadorComando.Montar(DeviceIdVetor, CommandIdVetor, CofreVetor, CanalVetor, AcaoComando.Abrir, TsVetor, segredo);

        Assert.Equal(SegredoVetor, segredo);
    }

    [Fact]
    public void Json_tem_exatamente_os_seis_campos_com_os_tipos_do_contrato()
    {
        var comando = MontarVetor();

        using var doc = JsonDocument.Parse(comando.Payload);
        var raiz = doc.RootElement;
        var nomes = raiz.EnumerateObject().Select(p => p.Name).ToList();

        Assert.Equal(["commandId", "cofre", "canal", "acao", "ts", "assinatura"], nomes);
        Assert.Equal(JsonValueKind.String, raiz.GetProperty("commandId").ValueKind);
        Assert.Equal("3f2504e0-4f89-41d3-9a0c-0305e82c3301", raiz.GetProperty("commandId").GetString());
        Assert.Equal(JsonValueKind.Number, raiz.GetProperty("cofre").ValueKind);
        Assert.Equal(3, raiz.GetProperty("cofre").GetInt32());
        Assert.Equal(JsonValueKind.Number, raiz.GetProperty("canal").ValueKind);
        Assert.Equal(5, raiz.GetProperty("canal").GetInt32());
        Assert.Equal("abrir", raiz.GetProperty("acao").GetString());
        Assert.Equal(JsonValueKind.Number, raiz.GetProperty("ts").ValueKind);
        Assert.Equal(TsVetor, raiz.GetProperty("ts").GetInt64());
        Assert.Equal(AssinaturaVetor, raiz.GetProperty("assinatura").GetString());
    }

    [Fact]
    public void Json_do_vetor_tem_os_bytes_esperados()
    {
        var json = Encoding.UTF8.GetString(MontarVetor().Payload.Span);

        Assert.Equal(
            "{\"commandId\":\"3f2504e0-4f89-41d3-9a0c-0305e82c3301\",\"cofre\":3,\"canal\":5,\"acao\":\"abrir\"," +
            "\"ts\":1760000000,\"assinatura\":\"" + AssinaturaVetor + "\"}",
            json);
    }

    [Fact]
    public void Json_nao_contem_o_deviceId_nem_o_segredo()
    {
        var json = Encoding.UTF8.GetString(MontarVetor().Payload.Span);

        Assert.DoesNotContain(DeviceIdVetor, json);
        Assert.DoesNotContain(Convert.ToHexString(SegredoVetor), json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Pior_caso_do_payload_cabe_no_pacote_do_broker()
    {
        var comando = MontadorComando.Montar(
            "est-zzzzzz", Guid.NewGuid(), int.MaxValue, 15, AcaoComando.Abrir, long.MaxValue, new byte[32]);

        var tamanho = TopicosMqtt.TamanhoPacotePublicacao(TopicosMqtt.Comando(comando.DeviceId), comando.Payload.Length);

        Assert.True(comando.Payload.Length < 4096);
        Assert.True(tamanho < TopicosMqtt.TamanhoMaximoPacote, $"Pacote com {tamanho} bytes.");
    }

    [Fact]
    public void ToString_nao_mostra_mensagem_assinatura_nem_payload()
    {
        var texto = MontarVetor().ToString();

        Assert.Contains("3f2504e0-4f89-41d3-9a0c-0305e82c3301", texto);
        Assert.DoesNotContain(AssinaturaVetor, texto);
        Assert.DoesNotContain(MensagemVetor, texto);
        Assert.DoesNotContain("assinatura", texto, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("pt-BR")]
    [InlineData("ar-SA")]
    [InlineData("fa-IR")]
    [InlineData("hi-IN")]
    public void Cultura_do_thread_nao_altera_o_resultado(string cultura)
    {
        var referencia = MontarVetor();
        var original = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultura);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(cultura);

            var comando = MontarVetor();

            Assert.Equal(MensagemVetor, comando.MensagemCanonica);
            Assert.Equal(AssinaturaVetor, comando.Assinatura);
            Assert.Equal(referencia.Payload.ToArray(), comando.Payload.ToArray());
        }
        finally
        {
            (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = original;
        }
    }

    [Fact]
    public void Cultura_com_sinal_e_separadores_estranhos_nao_altera_o_resultado()
    {
        var estranha = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        estranha.NumberFormat.NegativeSign = "~";
        estranha.NumberFormat.PositiveSign = "+";
        estranha.NumberFormat.NumberGroupSeparator = ".";
        estranha.NumberFormat.NumberDecimalSeparator = ",";
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = estranha;

            var comando = MontarVetor();

            Assert.Equal(MensagemVetor, comando.MensagemCanonica);
            Assert.Equal(AssinaturaVetor, comando.Assinatura);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
