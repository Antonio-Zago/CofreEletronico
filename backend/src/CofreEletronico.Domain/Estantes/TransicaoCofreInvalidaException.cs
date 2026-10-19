using CofreEletronico.Domain.Common;

namespace CofreEletronico.Domain.Estantes;

/// <summary>Operação não permitida no estado atual do cofre (ver máquina de estados em <see cref="Cofre"/>).</summary>
public sealed class TransicaoCofreInvalidaException : ExcecaoDominio
{
    public TransicaoCofreInvalidaException(string operacao, StatusCofre statusAtual)
        : base($"Operação '{operacao}' não permitida para cofre com status '{statusAtual}'.")
    {
        Operacao = operacao;
        StatusAtual = statusAtual;
    }

    public string Operacao { get; }

    public StatusCofre StatusAtual { get; }
}
