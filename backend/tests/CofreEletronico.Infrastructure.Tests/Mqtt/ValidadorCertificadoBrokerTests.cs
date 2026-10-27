using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using CofreEletronico.Infrastructure.Mqtt;
using Microsoft.Extensions.Logging.Abstractions;

namespace CofreEletronico.Infrastructure.Tests.Mqtt;

public class ValidadorCertificadoBrokerTests
{
    private const SslPolicyErrors ErroCadeia = SslPolicyErrors.RemoteCertificateChainErrors;

    private static ValidadorCertificadoBroker Criar(X509Certificate2Collection? cas, bool permitir = false, bool desenvolvimento = false) =>
        new(cas, permitir, desenvolvimento, NullLogger.Instance);

    [Fact]
    public void Sem_CA_aceita_so_o_que_o_sistema_valida()
    {
        using var ca = CertificadosDeTeste.CriarCa("CA Teste");
        using var servidor = CertificadosDeTeste.CriarServidor(ca);
        var validador = Criar(null);

        Assert.True(validador.Validar(servidor, null, SslPolicyErrors.None));
        Assert.False(validador.Validar(servidor, null, ErroCadeia));
        Assert.False(validador.Validar(servidor, null, SslPolicyErrors.RemoteCertificateNameMismatch));
        Assert.False(validador.Validar(null, null, SslPolicyErrors.RemoteCertificateNotAvailable));
    }

    [Theory]
    [InlineData(true, false)]  // opção ligada fora de Development: recusa
    [InlineData(false, true)]  // Development sem a opção: recusa
    [InlineData(false, false)]
    public void Certificado_invalido_e_recusado_sem_opcao_explicita_em_Development(bool permitir, bool desenvolvimento)
    {
        using var ca = CertificadosDeTeste.CriarCa("CA Teste");
        using var servidor = CertificadosDeTeste.CriarServidor(ca);

        Assert.False(Criar(null, permitir, desenvolvimento).Validar(servidor, null, ErroCadeia));
    }

    [Fact]
    public void Certificado_invalido_so_e_aceito_com_opcao_e_em_Development()
    {
        using var ca = CertificadosDeTeste.CriarCa("CA Teste");
        using var servidor = CertificadosDeTeste.CriarServidor(ca);

        Assert.True(Criar(null, permitir: true, desenvolvimento: true).Validar(servidor, null, ErroCadeia));
    }

    [Fact]
    public void Com_CA_aceita_cadeia_assinada_por_ela()
    {
        using var ca = CertificadosDeTeste.CriarCa("CA Teste");
        using var servidor = CertificadosDeTeste.CriarServidor(ca);

        Assert.True(Criar(CertificadosDeTeste.Colecao(ca)).Validar(servidor, null, ErroCadeia));
    }

    [Fact]
    public void Com_CA_recusa_certificado_de_outra_CA_mesmo_que_o_sistema_confie()
    {
        using var ca = CertificadosDeTeste.CriarCa("CA Teste");
        using var outraCa = CertificadosDeTeste.CriarCa("Outra CA");
        using var servidor = CertificadosDeTeste.CriarServidor(outraCa);
        var validador = Criar(CertificadosDeTeste.Colecao(ca));

        Assert.False(validador.Validar(servidor, null, ErroCadeia));
        Assert.False(validador.Validar(servidor, null, SslPolicyErrors.None));
    }

    [Fact]
    public void Com_CA_recusa_nome_divergente()
    {
        using var ca = CertificadosDeTeste.CriarCa("CA Teste");
        using var servidor = CertificadosDeTeste.CriarServidor(ca);

        Assert.False(Criar(CertificadosDeTeste.Colecao(ca))
            .Validar(servidor, null, ErroCadeia | SslPolicyErrors.RemoteCertificateNameMismatch));
    }

    [Fact]
    public void Com_CA_recusa_certificado_sem_EKU_de_servidor()
    {
        using var ca = CertificadosDeTeste.CriarCa("CA Teste");
        using var cliente = CertificadosDeTeste.CriarServidor(ca, eku: CertificadosDeTeste.OidClientAuth);

        Assert.False(Criar(CertificadosDeTeste.Colecao(ca)).Validar(cliente, null, ErroCadeia));
    }

    [Fact]
    public void Com_CA_usa_intermediario_enviado_pelo_broker()
    {
        using var raiz = CertificadosDeTeste.CriarCa("Raiz Teste");
        using var intermediaria = CertificadosDeTeste.CriarIntermediaria(raiz, "Intermediaria Teste");
        using var servidor = CertificadosDeTeste.CriarServidor(intermediaria);
        using var intermediariaPublica = new X509Certificate2(intermediaria.RawData);
        var validador = Criar(CertificadosDeTeste.Colecao(raiz));

        Assert.False(validador.Validar(servidor, null, ErroCadeia)); // sem o intermediário a cadeia não fecha
        Assert.True(validador.Validar(servidor, [intermediariaPublica], ErroCadeia));
    }

    [Fact]
    public void Arquivo_da_CA_com_raiz_e_intermediario_e_carregado_inteiro()
    {
        using var raiz = CertificadosDeTeste.CriarCa("Raiz Teste");
        using var intermediaria = CertificadosDeTeste.CriarIntermediaria(raiz, "Intermediaria Teste");
        using var servidor = CertificadosDeTeste.CriarServidor(intermediaria);
        var caminho = CertificadosDeTeste.GravarPem(intermediaria, raiz);
        try
        {
            var cas = CertificadosCa.Carregar(caminho);

            Assert.Equal(2, cas.Count);
            Assert.Single(cas.Cast<X509Certificate2>(), CertificadosCa.EhRaiz);
            Assert.True(Criar(cas).Validar(servidor, null, ErroCadeia)); // intermediário veio do arquivo
        }
        finally
        {
            File.Delete(caminho);
        }
    }

    [Fact]
    public void Intermediario_no_arquivo_nao_vira_ancora_de_confianca()
    {
        // Só a raiz é âncora: um arquivo apenas com a intermediária não valida nada.
        using var raiz = CertificadosDeTeste.CriarCa("Raiz Teste");
        using var intermediaria = CertificadosDeTeste.CriarIntermediaria(raiz, "Intermediaria Teste");
        using var servidor = CertificadosDeTeste.CriarServidor(intermediaria);

        Assert.False(Criar(CertificadosDeTeste.Colecao(intermediaria)).Validar(servidor, null, ErroCadeia));
    }
}
