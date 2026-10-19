namespace CofreEletronico.Domain.Common;

/// <summary>
/// Violação de regra de negócio (validação de entidade ou transição de estado inválida).
/// A mensagem é segura para exibir ao cliente: nunca inclui segredos.
/// </summary>
public class ExcecaoDominio : Exception
{
    public ExcecaoDominio(string mensagem)
        : base(mensagem)
    {
    }
}
