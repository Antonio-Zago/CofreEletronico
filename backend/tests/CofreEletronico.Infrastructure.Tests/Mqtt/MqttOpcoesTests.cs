using CofreEletronico.Application.Mqtt.Portas;
using CofreEletronico.Infrastructure.Mqtt;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CofreEletronico.Infrastructure.Tests.Mqtt;

public class MqttOpcoesTests
{
    private const string SenhaFicticia = "senha-mqtt-que-nao-pode-aparecer";

    private static MqttOpcoes Ler(Dictionary<string, string?> valores, string ambiente = "Testing")
    {
        using var provider = InfrastructureFixture.CriarProvider(configuracaoExtra: valores, ambiente: ambiente);
        return provider.GetRequiredService<IOptions<MqttOpcoes>>().Value;
    }

    private static string Falha(Dictionary<string, string?> valores, string ambiente = "Testing") =>
        Assert.Throws<OptionsValidationException>(() => Ler(valores, ambiente)).Message;

    [Fact]
    public void Sem_host_fica_desabilitado_com_os_padroes()
    {
        var opcoes = Ler([]);

        Assert.False(opcoes.Habilitado);
        Assert.Equal(1883, opcoes.Porta);
        Assert.Equal("backend", opcoes.Usuario);
        Assert.False(opcoes.UsarTls);
        Assert.Equal(30, opcoes.KeepAliveSegundos);
        Assert.Equal(1, opcoes.ReconexaoInicialSegundos);
        Assert.Equal(60, opcoes.ReconexaoMaximaSegundos);
        Assert.Equal(10, opcoes.TimeoutSegundos);
        Assert.Equal(30, opcoes.LimiteConexaoEstavelSegundos);
    }

    [Fact]
    public void Com_host_e_senha_e_valida()
    {
        var opcoes = Ler(new() { ["Mqtt:Host"] = "127.0.0.1", ["Mqtt:Porta"] = "1884", ["Mqtt:Senha"] = SenhaFicticia });

        Assert.True(opcoes.Habilitado);
        Assert.Equal(1884, opcoes.Porta);
        Assert.DoesNotContain(SenhaFicticia, opcoes.ToString());
    }

    [Fact]
    public void Com_host_sem_senha_falha()
    {
        var mensagem = Falha(new() { ["Mqtt:Host"] = "127.0.0.1" });

        Assert.Contains("Mqtt:Senha não configurada", mensagem);
        Assert.Contains("Mqtt__Senha", mensagem);
    }

    [Theory]
    [InlineData("Mqtt:Porta", "0", "Mqtt:Porta")]
    [InlineData("Mqtt:Porta", "65536", "Mqtt:Porta")]
    [InlineData("Mqtt:KeepAliveSegundos", "121", "Mqtt:KeepAliveSegundos")]
    [InlineData("Mqtt:KeepAliveSegundos", "0", "Mqtt:KeepAliveSegundos")]
    [InlineData("Mqtt:ReconexaoInicialSegundos", "0", "Mqtt:ReconexaoInicialSegundos")]
    [InlineData("Mqtt:ReconexaoMaximaSegundos", "0", "Mqtt:ReconexaoMaximaSegundos")]
    [InlineData("Mqtt:TimeoutSegundos", "0", "Mqtt:TimeoutSegundos")]
    [InlineData("Mqtt:LimiteConexaoEstavelSegundos", "0", "Mqtt:LimiteConexaoEstavelSegundos")]
    [InlineData("Mqtt:Usuario", " ", "Mqtt:Usuario")]
    public void Valores_fora_da_faixa_falham_sem_ecoar_a_senha(string chave, string valor, string esperado)
    {
        var mensagem = Falha(new() { ["Mqtt:Host"] = "127.0.0.1", ["Mqtt:Senha"] = SenhaFicticia, [chave] = valor });

        Assert.Contains(esperado, mensagem);
        Assert.DoesNotContain(SenhaFicticia, mensagem);
    }

    [Fact]
    public void CA_inexistente_falha()
    {
        var mensagem = Falha(new() { ["Mqtt:CaminhoCertificadoCa"] = Path.Combine(Path.GetTempPath(), $"nao-existe-{Guid.NewGuid():N}.crt") });

        Assert.Contains("Mqtt:CaminhoCertificadoCa aponta para um arquivo que não existe", mensagem);
    }

    [Fact]
    public void CA_que_nao_e_certificado_falha()
    {
        var caminho = Path.GetTempFileName();
        try
        {
            File.WriteAllText(caminho, "isto não é um certificado");

            var mensagem = Falha(new() { ["Mqtt:CaminhoCertificadoCa"] = caminho });

            Assert.Contains("não contém um certificado X.509 válido", mensagem);
        }
        finally
        {
            File.Delete(caminho);
        }
    }

    [Fact]
    public void CA_valida_e_aceita()
    {
        using var ca = CertificadosDeTeste.CriarCa("CA Teste");
        var caminho = CertificadosDeTeste.GravarPem(ca);
        try
        {
            var opcoes = Ler(new()
            {
                ["Mqtt:Host"] = "broker.local", ["Mqtt:Senha"] = SenhaFicticia, ["Mqtt:UsarTls"] = "true", ["Mqtt:CaminhoCertificadoCa"] = caminho,
            });

            Assert.True(opcoes.UsarTls);
        }
        finally
        {
            File.Delete(caminho);
        }
    }

    [Fact]
    public void CA_sem_certificado_raiz_falha()
    {
        using var raiz = CertificadosDeTeste.CriarCa("Raiz Teste");
        using var intermediaria = CertificadosDeTeste.CriarIntermediaria(raiz, "Intermediaria Teste");
        var caminho = CertificadosDeTeste.GravarPem(intermediaria);
        try
        {
            var mensagem = Falha(new() { ["Mqtt:CaminhoCertificadoCa"] = caminho });

            Assert.Contains("precisa conter o certificado raiz", mensagem);
        }
        finally
        {
            File.Delete(caminho);
        }
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Testing")]
    public void Permitir_certificado_invalido_fora_de_Development_falha(string ambiente)
    {
        var mensagem = Falha(new() { ["Mqtt:PermitirCertificadoInvalidoEmDesenvolvimento"] = "true" }, ambiente);

        Assert.Contains("só é aceito no ambiente Development", mensagem);
    }

    [Fact]
    public void Permitir_certificado_invalido_em_Development_e_aceito()
    {
        var opcoes = Ler(new() { ["Mqtt:PermitirCertificadoInvalidoEmDesenvolvimento"] = "true" }, Environments.Development);

        Assert.True(opcoes.PermitirCertificadoInvalidoEmDesenvolvimento);
    }

    [Fact]
    public void Cliente_e_hosted_service_sao_o_mesmo_singleton()
    {
        using var provider = InfrastructureFixture.CriarProvider();

        var cliente = provider.GetRequiredService<IClienteMqtt>();
        var hospedados = provider.GetServices<IHostedService>();

        Assert.Same(cliente, provider.GetRequiredService<IClienteMqtt>());
        Assert.Contains(hospedados, h => ReferenceEquals(h, cliente));
        Assert.False(cliente.Habilitado);
    }
}
