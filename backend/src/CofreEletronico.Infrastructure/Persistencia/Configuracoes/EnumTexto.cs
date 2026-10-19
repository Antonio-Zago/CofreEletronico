using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace CofreEletronico.Infrastructure.Persistencia.Configuracoes;

/// <summary>
/// Enums gravados como texto em minúsculas e sem acento (contrato: <c>pequeno</c>, <c>manutencao</c> etc.).
/// Os nomes dos membros dos enums do domínio já são sem acento, então basta o nome em minúsculas.
/// </summary>
internal static class EnumTexto
{
    /// <summary>Tamanho das colunas de enum (o maior valor hoje é <c>manutencao</c>, 10 caracteres).</summary>
    public const int TamanhoMaximo = 20;

    public static string ParaTexto<T>(T valor) where T : struct, Enum => valor.ToString().ToLowerInvariant();

    public static T DeTexto<T>(string texto) where T : struct, Enum =>
        Enum.TryParse<T>(texto, ignoreCase: true, out var valor) && Enum.IsDefined(valor)
            ? valor
            : throw new InvalidOperationException($"Valor de {typeof(T).Name} desconhecido no banco.");

    public static ValueConverter<T, string> Conversor<T>() where T : struct, Enum =>
        new(v => ParaTexto(v), t => DeTexto<T>(t));

    /// <summary>Expressão SQL da CHECK constraint: <c>coluna IN ('a', 'b', ...)</c>, gerada do enum.</summary>
    public static string CheckValores<T>(string coluna) where T : struct, Enum =>
        $"{coluna} IN ({string.Join(", ", Enum.GetValues<T>().Select(v => $"'{ParaTexto(v)}'"))})";
}
