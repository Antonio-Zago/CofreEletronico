using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace CofreEletronico.Infrastructure.Criptografia;

/// <summary>
/// AES-256-GCM. Formato armazenado: <c>v1:</c> + base64(nonce 12 bytes | texto cifrado | tag 16 bytes).
/// O prefixo de versão permite rotação futura (ex.: <c>v2</c> com outra chave) sem ambiguidade.
/// O prefixo também entra como dado associado (AAD), então trocar a versão invalida a tag.
/// </summary>
/// <remarks>
/// A chave é lida das opções só no primeiro uso (<see cref="IOptions{TOptions}.Value"/> valida e lança
/// <see cref="OptionsValidationException"/> se faltar). Assim, montar o host e o DbContext no <c>dotnet ef</c>
/// não exige a chave; a Api em execução já falha antes, no ValidateOnStart.
/// Uma instância de <see cref="AesGcm"/> por operação (a classe não é documentada como thread-safe).
/// A chave e os valores em claro ficam em arrays gerenciados que não são zerados depois do uso:
/// limitação aceita no .NET (o GC pode copiar arrays e não há zeragem garantida).
/// </remarks>
public sealed class CifradorSegredosAesGcm : ICifradorSegredos
{
    public const string VersaoAtual = "v1";

    private const string PrefixoV1 = VersaoAtual + ":";
    private const int NonceTamanho = 12; // AesGcm.NonceByteSizes.MaxSize
    private const int TagTamanho = 16; // AesGcm.TagByteSizes.MaxSize

    private static readonly byte[] DadoAssociadoV1 = Encoding.ASCII.GetBytes(VersaoAtual);

    private readonly Lazy<byte[]> _chave;

    public CifradorSegredosAesGcm(IOptions<CriptografiaOpcoes> opcoes)
    {
        _chave = new Lazy<byte[]>(
            () => Convert.FromBase64String(opcoes.Value.ChaveSegredos.Trim()),
            // PublicationOnly: uma falha de configuração não fica em cache; a próxima chamada tenta de novo.
            LazyThreadSafetyMode.PublicationOnly);
    }

    public string Cifrar(byte[] valorEmClaro)
    {
        ArgumentNullException.ThrowIfNull(valorEmClaro);

        var buffer = new byte[NonceTamanho + valorEmClaro.Length + TagTamanho];
        var nonce = buffer.AsSpan(0, NonceTamanho);
        var cifrado = buffer.AsSpan(NonceTamanho, valorEmClaro.Length);
        var tag = buffer.AsSpan(NonceTamanho + valorEmClaro.Length, TagTamanho);

        RandomNumberGenerator.Fill(nonce);
        using (var aes = new AesGcm(_chave.Value, TagTamanho))
        {
            aes.Encrypt(nonce, valorEmClaro, cifrado, tag, DadoAssociadoV1);
        }

        return PrefixoV1 + Convert.ToBase64String(buffer);
    }

    public byte[] Decifrar(string valorCifrado)
    {
        if (string.IsNullOrEmpty(valorCifrado) || !valorCifrado.StartsWith(PrefixoV1, StringComparison.Ordinal))
            throw new SegredoCifradoInvalidoException("Segredo cifrado com versão de formato desconhecida ou ausente.");

        byte[] buffer;
        try
        {
            buffer = Convert.FromBase64String(valorCifrado[PrefixoV1.Length..]);
        }
        catch (FormatException)
        {
            throw new SegredoCifradoInvalidoException("Segredo cifrado malformado (base64 inválido).");
        }

        if (buffer.Length < NonceTamanho + TagTamanho)
            throw new SegredoCifradoInvalidoException("Segredo cifrado malformado (tamanho insuficiente).");

        var tamanhoTexto = buffer.Length - NonceTamanho - TagTamanho;
        var nonce = buffer.AsSpan(0, NonceTamanho);
        var cifrado = buffer.AsSpan(NonceTamanho, tamanhoTexto);
        var tag = buffer.AsSpan(NonceTamanho + tamanhoTexto, TagTamanho);
        var claro = new byte[tamanhoTexto];

        try
        {
            using var aes = new AesGcm(_chave.Value, TagTamanho);
            aes.Decrypt(nonce, cifrado, tag, claro, DadoAssociadoV1);
        }
        catch (CryptographicException)
        {
            // Sem a exceção interna nem o conteúdo: a mensagem não ajuda a montar ataque nem vaza dado.
            throw new SegredoCifradoInvalidoException(
                "Falha ao decifrar o segredo: valor adulterado ou cifrado com outra chave (Criptografia:ChaveSegredos).");
        }

        return claro;
    }
}
