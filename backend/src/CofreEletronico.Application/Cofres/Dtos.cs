using CofreEletronico.Domain.Estantes;

namespace CofreEletronico.Application.Cofres;

/// <summary>
/// Cofre como sai da Api (contrato em docs/ARQUITETURA.md, "Contrato REST: administração", subseção "Cofres").
/// <see cref="Tamanho"/> e <see cref="Status"/> em texto minúsculo e sem acento (<c>pequeno</c>, <c>manutencao</c>...),
/// os mesmos valores gravados no banco.
/// </summary>
public sealed record CofreDto(
    Guid Id,
    Guid EstanteId,
    int Numero,
    int CanalRele,
    string Tamanho,
    string Status,
    bool Ativo)
{
    public static CofreDto De(Cofre cofre) =>
        Montar(cofre.Id, cofre.EstanteId, cofre.Numero, cofre.CanalRele, cofre.Tamanho, cofre.Status, cofre.Ativo);

    /// <summary>
    /// Também usado nas projeções do repositório (avaliado em memória, na projeção final): a conversão do enum em
    /// texto não vai para o SQL.
    /// </summary>
    public static CofreDto Montar(
        Guid id, Guid estanteId, int numero, int canalRele, TamanhoCofre tamanho, StatusCofre status, bool ativo) =>
        new(id, estanteId, numero, canalRele, TextoCofre.DeTamanho(tamanho), TextoCofre.DeStatus(status), ativo);
}

/// <summary>Texto de tamanho e status do cofre no contrato REST (minúsculo, sem acento).</summary>
public static class TextoCofre
{
    private static readonly Dictionary<string, TamanhoCofre> Tamanhos =
        Enum.GetValues<TamanhoCofre>().ToDictionary(t => t.ToString().ToLowerInvariant(), StringComparer.Ordinal);

    /// <summary>Valores aceitos na entrada (<c>pequeno</c>, <c>medio</c>, <c>grande</c>), na ordem do enum.</summary>
    public static IReadOnlyList<string> TamanhosAceitos { get; } =
        Enum.GetValues<TamanhoCofre>().Select(t => t.ToString().ToLowerInvariant()).ToArray();

    public static string DeTamanho(TamanhoCofre tamanho) => tamanho.ToString().ToLowerInvariant();

    public static string DeStatus(StatusCofre status) => status.ToString().ToLowerInvariant();

    /// <summary>
    /// Converte o texto do contrato só com correspondência exata (minúsculo, sem acento). Números (<c>"1"</c>), nomes
    /// com maiúscula ou com espaços são recusados.
    /// </summary>
    public static bool TentarTamanho(string? texto, out TamanhoCofre tamanho)
    {
        if (texto is not null && Tamanhos.TryGetValue(texto, out tamanho))
            return true;

        tamanho = default;
        return false;
    }

    /// <summary>Status para mensagens ao admin ("O cofre está ocupado e ...").</summary>
    internal static string Descrever(StatusCofre status) => status switch
    {
        StatusCofre.Livre => "livre",
        StatusCofre.Reservado => "reservado",
        StatusCofre.Ocupado => "ocupado",
        StatusCofre.Manutencao => "em manutenção",
        _ => "em um estado desconhecido",
    };
}
