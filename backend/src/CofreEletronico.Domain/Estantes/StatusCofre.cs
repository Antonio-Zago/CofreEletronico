namespace CofreEletronico.Domain.Estantes;

/// <summary>
/// Estado operacional do cofre. No banco: <c>livre</c>, <c>reservado</c>, <c>ocupado</c>, <c>manutencao</c>.
/// Transições permitidas em <see cref="Cofre"/>.
/// </summary>
public enum StatusCofre
{
    Livre = 1,
    Reservado = 2,
    Ocupado = 3,
    Manutencao = 4,
}
