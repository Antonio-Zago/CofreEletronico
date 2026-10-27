namespace CofreEletronico.Infrastructure.Mqtt;

/// <summary>
/// Intervalo entre tentativas de conexão: <c>inicial * 2^(n-1)</c>, limitado a <c>maximo</c>, com jitter de ±20%
/// (o resultado nunca passa de <c>maximo</c>). O jitter evita que várias instâncias/reinícios batam no broker juntos.
/// </summary>
internal sealed class BackoffExponencial(TimeSpan inicial, TimeSpan maximo, Func<double> aleatorio)
{
    /// <summary>Fração do jitter: o intervalo fica entre 80% e 120% da base (e sempre &lt;= máximo).</summary>
    public const double FracaoJitter = 0.2;

    public TimeSpan Inicial { get; } = inicial;

    public TimeSpan Maximo { get; } = maximo;

    /// <summary>Base sem jitter da tentativa <paramref name="tentativa"/> (1 = primeira falha).</summary>
    public TimeSpan Base(int tentativa)
    {
        var expoente = Math.Clamp(tentativa - 1, 0, 30);
        var ms = Math.Min(Inicial.TotalMilliseconds * Math.Pow(2, expoente), Maximo.TotalMilliseconds);
        return TimeSpan.FromMilliseconds(ms);
    }

    /// <summary>Intervalo com jitter para a tentativa <paramref name="tentativa"/> (1 = primeira falha).</summary>
    public TimeSpan Proximo(int tentativa)
    {
        var r = Math.Clamp(aleatorio(), 0d, 1d);
        var fator = 1 - FracaoJitter + (2 * FracaoJitter * r);
        var ms = Math.Min(Base(tentativa).TotalMilliseconds * fator, Maximo.TotalMilliseconds);
        return TimeSpan.FromMilliseconds(ms);
    }
}
