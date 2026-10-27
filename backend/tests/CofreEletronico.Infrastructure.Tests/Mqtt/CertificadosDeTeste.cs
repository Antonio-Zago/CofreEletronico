using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace CofreEletronico.Infrastructure.Tests.Mqtt;

/// <summary>CAs (raiz e intermediária) e certificados de servidor gerados em memória (só para os testes).</summary>
internal static class CertificadosDeTeste
{
    public const string OidServerAuth = "1.3.6.1.5.5.7.3.1";
    public const string OidClientAuth = "1.3.6.1.5.5.7.3.2";

    /// <summary>CA raiz autoassinada (com chave privada, para emitir).</summary>
    public static X509Certificate2 CriarCa(string nome)
    {
        using var chave = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var pedido = PedidoCa(nome, chave);
        return pedido.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
    }

    /// <summary>CA intermediária emitida por <paramref name="emissor"/> (com chave privada, para emitir).</summary>
    public static X509Certificate2 CriarIntermediaria(X509Certificate2 emissor, string nome)
    {
        using var chave = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var pedido = PedidoCa(nome, chave);
        using var publico = pedido.Create(emissor, DateTimeOffset.UtcNow.AddHours(-12), DateTimeOffset.UtcNow.AddMonths(6), Serial());
        return publico.CopyWithPrivateKey(chave);
    }

    public static X509Certificate2 CriarServidor(X509Certificate2 emissor, string host = "broker.local", string eku = OidServerAuth)
    {
        using var chave = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var pedido = new CertificateRequest($"CN={host}", chave, HashAlgorithmName.SHA256);
        pedido.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        pedido.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        pedido.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid(eku)], false));
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName(host);
        pedido.CertificateExtensions.Add(san.Build());

        return pedido.Create(emissor, DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddDays(30), Serial());
    }

    /// <summary>Coleção com cópias só da parte pública (como viria do arquivo da CA).</summary>
    public static X509Certificate2Collection Colecao(params X509Certificate2[] certificados)
    {
        var colecao = new X509Certificate2Collection();
        foreach (var certificado in certificados)
            colecao.Add(new X509Certificate2(certificado.RawData));
        return colecao;
    }

    /// <summary>Grava os certificados públicos em PEM (um após o outro) num arquivo temporário e devolve o caminho.</summary>
    public static string GravarPem(params X509Certificate2[] certificados)
    {
        var caminho = Path.Combine(Path.GetTempPath(), $"ca-mqtt-teste-{Guid.NewGuid():N}.crt");
        File.WriteAllText(caminho, string.Join("\n", certificados.Select(c => c.ExportCertificatePem())));
        return caminho;
    }

    private static CertificateRequest PedidoCa(string nome, ECDsa chave)
    {
        var pedido = new CertificateRequest($"CN={nome}", chave, HashAlgorithmName.SHA256);
        pedido.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        pedido.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        pedido.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(pedido.PublicKey, false));
        return pedido;
    }

    private static byte[] Serial()
    {
        var serial = new byte[16];
        RandomNumberGenerator.Fill(serial);
        serial[0] &= 0x7F; // positivo
        return serial;
    }
}
