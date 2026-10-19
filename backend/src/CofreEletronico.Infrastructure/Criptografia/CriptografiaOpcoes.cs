using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CofreEletronico.Infrastructure.Criptografia;

/// <summary>
/// Seção <c>Criptografia</c>. <see cref="ChaveSegredos"/> é segredo: vem só de user-secrets ou da variável
/// <c>Criptografia__ChaveSegredos</c>, nunca de appsettings.
/// </summary>
public sealed class CriptografiaOpcoes
{
    public const string Secao = "Criptografia";

    /// <summary>Tamanho exato da chave AES-256-GCM, em bytes.</summary>
    public const int ChaveTamanhoBytes = 32;

    /// <summary>Ambiente em que chaves triviais (inclusive a de teste versionada) são aceitas.</summary>
    public const string AmbienteTestes = "Testing";

    /// <summary>Chave AES em base64 (32 bytes depois de decodificada).</summary>
    public string ChaveSegredos { get; set; } = string.Empty;
}

/// <summary>
/// Validação na subida (ValidateOnStart) e no primeiro uso do cifrador: falha cedo com mensagem clara.
/// Fora do ambiente <see cref="CriptografiaOpcoes.AmbienteTestes"/>, recusa chaves conhecidas ou triviais.
/// </summary>
internal sealed class CriptografiaOpcoesValidador(IHostEnvironment ambiente) : IValidateOptions<CriptografiaOpcoes>
{
    /// <summary>Chave fictícia versionada nos testes (bytes 0, 1, ..., 31). Nunca vale fora de Testing.</summary>
    private static readonly byte[] ChaveDeTeste = Enumerable.Range(0, CriptografiaOpcoes.ChaveTamanhoBytes).Select(i => (byte)i).ToArray();

    public ValidateOptionsResult Validate(string? name, CriptografiaOpcoes options)
    {
        if (string.IsNullOrWhiteSpace(options.ChaveSegredos))
        {
            return ValidateOptionsResult.Fail(
                "Criptografia:ChaveSegredos não configurada (chave AES de 32 bytes em base64 que cifra o segredo HMAC das estantes). " +
                "Em desenvolvimento use 'dotnet user-secrets set \"Criptografia:ChaveSegredos\" \"<chave>\" --project src/CofreEletronico.Api'; " +
                "em servidor, a variável de ambiente Criptografia__ChaveSegredos. Ver backend/README.md.");
        }

        var bytes = new byte[options.ChaveSegredos.Length];
        if (!Convert.TryFromBase64String(options.ChaveSegredos.Trim(), bytes, out var tamanho))
            return ValidateOptionsResult.Fail("Criptografia:ChaveSegredos não é um base64 válido. Ver backend/README.md.");

        if (tamanho != CriptografiaOpcoes.ChaveTamanhoBytes)
        {
            return ValidateOptionsResult.Fail(
                $"Criptografia:ChaveSegredos deve ter exatamente {CriptografiaOpcoes.ChaveTamanhoBytes} bytes depois de decodificada " +
                $"(tem {tamanho}). Ver backend/README.md.");
        }

        var chave = bytes.AsSpan(0, tamanho);
        if (!ambiente.IsEnvironment(CriptografiaOpcoes.AmbienteTestes) && EhTrivial(chave))
        {
            return ValidateOptionsResult.Fail(
                "Criptografia:ChaveSegredos é uma chave conhecida ou trivial (a chave de teste do repositório ou bytes todos iguais). " +
                "Gere uma chave aleatória com 'openssl rand -base64 32' ou com o comando PowerShell do backend/README.md.");
        }

        return ValidateOptionsResult.Success;
    }

    private static bool EhTrivial(ReadOnlySpan<byte> chave) =>
        chave.SequenceEqual(ChaveDeTeste) || chave.IndexOfAnyExcept(chave[0]) < 0; // todos os bytes iguais (inclui zeros)
}
