using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CofreEletronico.Infrastructure.Mqtt;

/// <summary>
/// Seção <c>Mqtt</c>: conexão do backend com o broker. <see cref="Senha"/> é segredo: vem só de user-secrets ou da
/// variável <c>Mqtt__Senha</c>, nunca de appsettings. Sem <see cref="Host"/>, o MQTT fica desabilitado.
/// </summary>
public sealed class MqttOpcoes
{
    public const string Secao = "Mqtt";

    /// <summary>Limite do broker (<c>max_keepalive 120</c>).</summary>
    public const int KeepAliveMaximoSegundos = 120;

    /// <summary>Host do broker. Vazio = MQTT desabilitado (a Api sobe sem broker).</summary>
    public string? Host { get; set; }

    public int Porta { get; set; } = 1883;

    /// <summary>
    /// Usuário no broker (reservado: <c>backend</c>). Também é o ClientId: o broker usa
    /// <c>use_username_as_clientid</c>, então só pode haver UMA conexão por usuário.
    /// </summary>
    public string Usuario { get; set; } = "backend";

    /// <summary>Segredo: só user-secrets ou <c>Mqtt__Senha</c>.</summary>
    public string? Senha { get; set; }

    public bool UsarTls { get; set; }

    /// <summary>CA (PEM ou DER) que assina o certificado do broker. Vazio = repositório de confiança do sistema.</summary>
    public string? CaminhoCertificadoCa { get; set; }

    /// <summary>Só em Development (a validação recusa nos demais ambientes). Aceita certificado inválido do broker.</summary>
    public bool PermitirCertificadoInvalidoEmDesenvolvimento { get; set; }

    public int KeepAliveSegundos { get; set; } = 30;

    /// <summary>Primeiro intervalo de reconexão; dobra a cada tentativa (com jitter) até <see cref="ReconexaoMaximaSegundos"/>.</summary>
    public int ReconexaoInicialSegundos { get; set; } = 1;

    public int ReconexaoMaximaSegundos { get; set; } = 60;

    /// <summary>Tempo máximo para conectar e para o broker responder (CONNACK, PUBACK, SUBACK).</summary>
    public int TimeoutSegundos { get; set; } = 10;

    /// <summary>
    /// Conexão que cai antes deste tempo conta como falha: a próxima tentativa espera o backoff (evita duas instâncias
    /// com o mesmo usuário se derrubando sem parar).
    /// </summary>
    public int LimiteConexaoEstavelSegundos { get; set; } = 30;

    public bool Habilitado => !string.IsNullOrWhiteSpace(Host);

    /// <summary>Nunca expõe a senha (ex.: se as opções forem logadas por engano).</summary>
    public override string ToString() =>
        $"Mqtt {{ Host = {Host}, Porta = {Porta}, Usuario = {Usuario}, UsarTls = {UsarTls}, Senha = *** }}";
}

/// <summary>
/// Validação na subida (ValidateOnStart). As mensagens nunca ecoam a senha.
/// </summary>
internal sealed class MqttOpcoesValidador(IHostEnvironment ambiente) : IValidateOptions<MqttOpcoes>
{
    public ValidateOptionsResult Validate(string? name, MqttOpcoes options)
    {
        var falhas = new List<string>();

        if (options.Porta is < 1 or > 65535)
            falhas.Add("Mqtt:Porta deve estar entre 1 e 65535.");
        if (options.KeepAliveSegundos is < 1 or > MqttOpcoes.KeepAliveMaximoSegundos)
            falhas.Add($"Mqtt:KeepAliveSegundos deve estar entre 1 e {MqttOpcoes.KeepAliveMaximoSegundos} (limite do broker).");
        if (options.ReconexaoInicialSegundos is < 1 or > 60)
            falhas.Add("Mqtt:ReconexaoInicialSegundos deve estar entre 1 e 60.");
        if (options.ReconexaoMaximaSegundos < options.ReconexaoInicialSegundos || options.ReconexaoMaximaSegundos > 3600)
            falhas.Add("Mqtt:ReconexaoMaximaSegundos deve ser maior ou igual a Mqtt:ReconexaoInicialSegundos e no máximo 3600.");
        if (options.TimeoutSegundos is < 1 or > 60)
            falhas.Add("Mqtt:TimeoutSegundos deve estar entre 1 e 60.");
        if (options.LimiteConexaoEstavelSegundos is < 1 or > 3600)
            falhas.Add("Mqtt:LimiteConexaoEstavelSegundos deve estar entre 1 e 3600.");

        if (options.PermitirCertificadoInvalidoEmDesenvolvimento && !ambiente.IsDevelopment())
            falhas.Add("Mqtt:PermitirCertificadoInvalidoEmDesenvolvimento só é aceito no ambiente Development.");

        if (options.Habilitado)
        {
            if (string.IsNullOrWhiteSpace(options.Usuario))
                falhas.Add("Mqtt:Usuario não configurado.");
            if (string.IsNullOrEmpty(options.Senha))
            {
                falhas.Add(
                    "Mqtt:Senha não configurada (obrigatória quando Mqtt:Host é informado). Em desenvolvimento use " +
                    "'dotnet user-secrets set \"Mqtt:Senha\" \"<senha>\" --project src/CofreEletronico.Api'; em servidor, " +
                    "a variável de ambiente Mqtt__Senha.");
            }
        }

        if (!string.IsNullOrWhiteSpace(options.CaminhoCertificadoCa))
        {
            if (!File.Exists(options.CaminhoCertificadoCa))
                falhas.Add("Mqtt:CaminhoCertificadoCa aponta para um arquivo que não existe.");
            else if (ProblemaCertificado(options.CaminhoCertificadoCa) is { } problema)
                falhas.Add(problema);
        }

        if (falhas.Count > 0)
            falhas.Add("Ver a seção \"MQTT\" do backend/README.md.");

        return falhas.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(falhas);
    }

    private static string? ProblemaCertificado(string caminho)
    {
        X509Certificate2Collection colecao;
        try
        {
            colecao = CertificadosCa.Carregar(caminho);
        }
        catch (CryptographicException)
        {
            return "Mqtt:CaminhoCertificadoCa não contém um certificado X.509 válido (PEM ou DER).";
        }

        try
        {
            return colecao.Cast<X509Certificate2>().Any(CertificadosCa.EhRaiz)
                ? null
                : "Mqtt:CaminhoCertificadoCa precisa conter o certificado raiz (autoassinado) da CA do broker.";
        }
        finally
        {
            foreach (var certificado in colecao)
                certificado.Dispose();
        }
    }
}
