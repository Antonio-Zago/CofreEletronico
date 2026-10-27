using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;

namespace CofreEletronico.Infrastructure.Mqtt;

/// <summary>
/// Valida o certificado do broker na conexão TLS.
/// <list type="bullet">
/// <item>Sem CA configurada: só aceita o que o sistema já considera válido (<see cref="SslPolicyErrors.None"/>).</item>
/// <item>Com CA: o nome do host tem de bater, o certificado tem de ser para servidor (EKU serverAuth) e a cadeia tem
/// de terminar numa raiz do arquivo da CA (só ela é confiável; o repositório do sistema é ignorado). Intermediários
/// vêm do próprio arquivo da CA e dos enviados pelo broker.</item>
/// <item>Certificado inválido só é aceito com a opção explícita E no ambiente Development (dupla verificação:
/// a validação das opções já recusa a opção fora de Development).</item>
/// </list>
/// </summary>
internal sealed class ValidadorCertificadoBroker(
    X509Certificate2Collection? cas, bool permitirInvalido, bool ambienteDesenvolvimento, ILogger logger)
{
    /// <summary>EKU id-kp-serverAuth.</summary>
    public const string OidServerAuth = "1.3.6.1.5.5.7.3.1";

    public bool Validar(X509Certificate? certificado, IEnumerable<X509Certificate2>? intermediariosRecebidos, SslPolicyErrors erros)
    {
        // Com CA configurada, só ela vale (mesmo que o repositório do sistema confie no certificado).
        var valido = cas is null || cas.Count == 0
            ? erros == SslPolicyErrors.None
            : certificado is not null && ValidarComCa(certificado, intermediariosRecebidos, erros);
        if (valido)
            return true;

        if (permitirInvalido && ambienteDesenvolvimento)
        {
            logger.LogWarning(
                "Certificado TLS do broker MQTT inválido ({Erros}) aceito por Mqtt:PermitirCertificadoInvalidoEmDesenvolvimento (só em Development).",
                erros);
            return true;
        }

        logger.LogWarning("Certificado TLS do broker MQTT recusado: {Erros}.", erros);
        return false;
    }

    private bool ValidarComCa(X509Certificate certificado, IEnumerable<X509Certificate2>? intermediariosRecebidos, SslPolicyErrors erros)
    {
        // Nome divergente ou certificado ausente não se resolve com a CA.
        if ((erros & ~SslPolicyErrors.RemoteCertificateChainErrors) != SslPolicyErrors.None)
            return false;

        using var servidor = new X509Certificate2(certificado);
        using var cadeia = new X509Chain();
        cadeia.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        foreach (var ca in cas!)
        {
            if (CertificadosCa.EhRaiz(ca))
                cadeia.ChainPolicy.CustomTrustStore.Add(ca);
            else
                cadeia.ChainPolicy.ExtraStore.Add(ca);
        }

        foreach (var intermediario in intermediariosRecebidos ?? [])
            cadeia.ChainPolicy.ExtraStore.Add(intermediario);

        // O certificado tem de servir para autenticar servidor.
        cadeia.ChainPolicy.ApplicationPolicy.Add(new Oid(OidServerAuth));
        // Sem verificação de revogação: a CA do broker é própria e não publica CRL nem OCSP.
        // Revogar = trocar a CA/certificado do broker e redistribuir a CA.
        cadeia.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        cadeia.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
        return cadeia.Build(servidor);
    }
}

/// <summary>Carregamento do arquivo da CA do broker (PEM com um ou mais certificados, ou DER).</summary>
internal static class CertificadosCa
{
    /// <summary>
    /// Lê todos os certificados do arquivo: as raízes (autoassinadas) viram âncoras de confiança e os demais,
    /// intermediários. Lança <see cref="CryptographicException"/> se o arquivo não tiver certificado válido.
    /// </summary>
    public static X509Certificate2Collection Carregar(string caminho)
    {
        var colecao = new X509Certificate2Collection();
        colecao.ImportFromPemFile(caminho);
        if (colecao.Count == 0)
            colecao.Add(new X509Certificate2(caminho)); // DER (ou lança CryptographicException)
        return colecao;
    }

    public static bool EhRaiz(X509Certificate2 certificado) =>
        certificado.SubjectName.RawData.AsSpan().SequenceEqual(certificado.IssuerName.RawData);
}
