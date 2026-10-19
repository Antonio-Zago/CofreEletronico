using CofreEletronico.Domain.Common;
using CofreEletronico.Domain.Estantes;

namespace CofreEletronico.Domain.Tarifas;

/// <summary>
/// Tarifa por tamanho de cofre, válida para todas as estantes (contrato em docs/ARQUITETURA.md, tabela <c>tarifas</c>).
/// A locação guarda o valor cobrado, então mudar a tarifa não afeta locações existentes.
/// </summary>
public sealed class Tarifa : Entity
{
    /// <summary>Maior valor que cabe em numeric(10,2).</summary>
    public const decimal ValorPorHoraMaximo = 99_999_999.99m;

    private Tarifa(Guid id, TamanhoCofre tamanho, decimal valorPorHora, int tempoMinimo)
        : base(id)
    {
        Tamanho = tamanho;
        ValorPorHora = valorPorHora;
        TempoMinimo = tempoMinimo;
    }

    // Materialização pelo EF Core.
    private Tarifa()
    {
    }

    /// <summary>Único entre as tarifas (garantido no banco).</summary>
    public TamanhoCofre Tamanho { get; private set; }

    /// <summary>Valor por hora em BRL, positivo, com no máximo 2 casas decimais.</summary>
    public decimal ValorPorHora { get; private set; }

    /// <summary>Tempo mínimo cobrado, em minutos.</summary>
    public int TempoMinimo { get; private set; }

    public static Tarifa Criar(TamanhoCofre tamanho, decimal valorPorHora, int tempoMinimo)
    {
        if (!Enum.IsDefined(tamanho))
            throw new ExcecaoDominio("Tamanho de cofre inválido.");
        Validar(valorPorHora, tempoMinimo);

        return new Tarifa(Guid.NewGuid(), tamanho, valorPorHora, tempoMinimo);
    }

    public void Atualizar(decimal valorPorHora, int tempoMinimo)
    {
        Validar(valorPorHora, tempoMinimo);

        ValorPorHora = valorPorHora;
        TempoMinimo = tempoMinimo;
    }

    private static void Validar(decimal valorPorHora, int tempoMinimo)
    {
        if (valorPorHora <= 0)
            throw new ExcecaoDominio("O valor por hora deve ser maior que zero.");
        if (decimal.Round(valorPorHora, 2) != valorPorHora)
            throw new ExcecaoDominio("O valor por hora deve ter no máximo 2 casas decimais.");
        if (valorPorHora > ValorPorHoraMaximo)
            throw new ExcecaoDominio($"O valor por hora deve ser no máximo {ValorPorHoraMaximo}.");
        if (tempoMinimo <= 0)
            throw new ExcecaoDominio("O tempo mínimo deve ser maior que zero minuto.");
    }
}
