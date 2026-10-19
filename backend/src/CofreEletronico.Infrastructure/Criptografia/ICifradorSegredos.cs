namespace CofreEletronico.Infrastructure.Criptografia;

/// <summary>
/// Cifra segredos que o backend precisa recuperar em claro (ex.: segredo HMAC das estantes) antes de gravá-los no banco.
/// Implementação registrada como singleton; a chave só é exigida ao cifrar/decifrar.
/// </summary>
public interface ICifradorSegredos
{
    /// <summary>Cifra com nonce aleatório: o mesmo valor gera textos diferentes a cada chamada.</summary>
    string Cifrar(byte[] valorEmClaro);

    /// <exception cref="SegredoCifradoInvalidoException">Valor adulterado, malformado, de versão desconhecida ou de outra chave.</exception>
    byte[] Decifrar(string valorCifrado);
}

/// <summary>Falha ao decifrar um segredo. A mensagem nunca inclui o conteúdo.</summary>
public sealed class SegredoCifradoInvalidoException : Exception
{
    public SegredoCifradoInvalidoException(string mensagem, Exception? interna = null)
        : base(mensagem, interna)
    {
    }
}
