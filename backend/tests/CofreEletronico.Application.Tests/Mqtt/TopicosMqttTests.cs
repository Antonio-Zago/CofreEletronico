using CofreEletronico.Application.Mqtt;

namespace CofreEletronico.Application.Tests.Mqtt;

public class TopicosMqttTests
{
    [Fact]
    public void Monta_os_topicos_do_contrato()
    {
        Assert.Equal("cofre/est-7k2m9q/cmd", TopicosMqtt.Comando("est-7k2m9q"));
        Assert.Equal("cofre/est-7k2m9q/ack", TopicosMqtt.Ack("est-7k2m9q"));
        Assert.Equal("cofre/est-7k2m9q/estado", TopicosMqtt.Estado("est-7k2m9q"));
        Assert.Equal("cofre/est-7k2m9q/online", TopicosMqtt.Online("est-7k2m9q"));
        Assert.Equal("cofre/+/ack", TopicosMqtt.FiltroAck);
        Assert.Equal("cofre/+/estado", TopicosMqtt.FiltroEstado);
        Assert.Equal("cofre/+/online", TopicosMqtt.FiltroOnline);
    }

    [Theory]
    [InlineData("est-+")]
    [InlineData("est-12345+")]
    [InlineData("est-12345#")]
    [InlineData("est-1/3456")]
    [InlineData("+")]
    [InlineData("#")]
    [InlineData("est-ABCDEF")]
    [InlineData("est-abc12")]
    [InlineData("est-abc1234")]
    [InlineData("est-abc123\n")]
    [InlineData("backend")]
    [InlineData("")]
    public void Recusa_deviceId_fora_do_formato(string deviceId)
    {
        Assert.Throws<ArgumentException>(() => TopicosMqtt.Comando(deviceId));
        Assert.Throws<ArgumentException>(() => TopicosMqtt.Ack(deviceId));
        Assert.Throws<ArgumentException>(() => TopicosMqtt.Estado(deviceId));
        Assert.Throws<ArgumentException>(() => TopicosMqtt.Online(deviceId));
    }

    [Fact]
    public void Recusa_deviceId_nulo() =>
        Assert.Throws<ArgumentException>(() => TopicosMqtt.Comando(null!));

    [Theory]
    [InlineData("")]
    [InlineData("cofre/#/ack")]
    [InlineData("cofre/a#")]
    [InlineData("cofre/a+/ack")]
    public void Filtro_invalido(string filtro) =>
        Assert.Throws<ArgumentException>(() => TopicosMqtt.ValidarFiltro(filtro));

    [Theory]
    [InlineData("cofre/+/ack")]
    [InlineData("cofre/#")]
    [InlineData("#")]
    [InlineData("cofre/est-7k2m9q/ack")]
    public void Filtro_valido(string filtro) => TopicosMqtt.ValidarFiltro(filtro);

    [Theory]
    [InlineData("cofre/+/ack", "cofre/est-7k2m9q/ack", true)]
    [InlineData("cofre/+/ack", "cofre/est-7k2m9q/estado", false)]
    [InlineData("cofre/+/ack", "cofre/est-7k2m9q/ack/x", false)]
    [InlineData("cofre/#", "cofre/est-7k2m9q/ack", true)]
    [InlineData("cofre/#", "cofre", true)]
    [InlineData("#", "$SYS/broker", false)]
    [InlineData("+/x", "$SYS/x", false)]
    [InlineData("cofre/est-7k2m9q/ack", "cofre/est-7k2m9q/ack", true)]
    [InlineData("cofre/est-7k2m9q/ack", "Cofre/est-7k2m9q/ack", false)]
    public void Casa_filtro_com_topico(string filtro, string topico, bool esperado) =>
        Assert.Equal(esperado, TopicosMqtt.Casa(filtro, topico));

    [Fact]
    public void Tamanho_do_pacote_inclui_cabecalho_e_topico()
    {
        // 1 (tipo) + 1 (tamanho restante = 125) + 2 + 20 (tópico) + 2 (id) + 1 (propriedades) + 100
        Assert.Equal(127,TopicosMqtt.TamanhoPacotePublicacao("cofre/est-7k2m9q/cmd", 100));
        Assert.True(TopicosMqtt.TamanhoPacotePublicacao("cofre/est-7k2m9q/cmd", TopicosMqtt.TamanhoMaximoPacote) > TopicosMqtt.TamanhoMaximoPacote);
    }
}
