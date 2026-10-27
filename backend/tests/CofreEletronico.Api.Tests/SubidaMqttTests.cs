using System.Net;
using System.Net.Sockets;
using CofreEletronico.Api.Tests.Infra;
using CofreEletronico.Application.Mqtt.Portas;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace CofreEletronico.Api.Tests;

/// <summary>A Api sobe e responde com o MQTT desabilitado ou com o broker fora do ar.</summary>
public class SubidaMqttTests
{
    [Fact]
    public async Task Sem_Mqtt_Host_a_Api_sobe_com_o_MQTT_desabilitado()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var resposta = await client.GetAsync("/ping");

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var mqtt = factory.Services.GetRequiredService<IClienteMqtt>();
        Assert.False(mqtt.Habilitado);
        Assert.False(mqtt.Conectado);
    }

    [Fact]
    public async Task Com_broker_fora_do_ar_a_Api_sobe_e_responde()
    {
        var portaFechada = PortaLivre();
        using var factory = new ApiFactory().WithWebHostBuilder(b => b
            .UseSetting("Mqtt:Host", "127.0.0.1")
            .UseSetting("Mqtt:Porta", portaFechada.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .UseSetting("Mqtt:Senha", "senha-ficticia-de-teste"));
        using var client = factory.CreateClient();

        var resposta = await client.GetAsync("/ping");

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var mqtt = factory.Services.GetRequiredService<IClienteMqtt>();
        Assert.True(mqtt.Habilitado);
        Assert.False(mqtt.Conectado);
        await Assert.ThrowsAsync<MqttIndisponivelException>(() =>
            mqtt.PublicarComandoAsync("est-7k2m9q", "{}"u8.ToArray(), CancellationToken.None));
    }

    /// <summary>Porta TCP livre em 127.0.0.1 (aberta e fechada em seguida: ninguém escuta nela).</summary>
    private static int PortaLivre()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var porta = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return porta;
    }
}
