using CofreEletronico.Infrastructure.Mqtt;

namespace CofreEletronico.Infrastructure.Tests.Mqtt;

public class BackoffExponencialTests
{
    private static BackoffExponencial Criar(double aleatorio) =>
        new(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(60), () => aleatorio);

    [Fact]
    public void Sem_jitter_dobra_ate_o_teto()
    {
        var backoff = Criar(0.5); // fator 1.0

        var segundos = Enumerable.Range(1, 9).Select(n => backoff.Proximo(n).TotalSeconds).ToArray();

        Assert.Equal([1, 2, 4, 8, 16, 32, 60, 60, 60], segundos);
    }

    [Fact]
    public void Tentativas_muito_altas_nao_estouram()
    {
        var backoff = Criar(0.5);

        Assert.Equal(TimeSpan.FromSeconds(60), backoff.Proximo(int.MaxValue));
        Assert.Equal(TimeSpan.FromSeconds(1), backoff.Proximo(0));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.25)]
    [InlineData(0.999)]
    [InlineData(1.0)]
    public void Jitter_fica_entre_80_e_120_por_cento_da_base_e_nunca_passa_do_teto(double aleatorio)
    {
        var backoff = Criar(aleatorio);

        for (var n = 1; n <= 12; n++)
        {
            var baseMs = backoff.Base(n).TotalMilliseconds;
            var ms = backoff.Proximo(n).TotalMilliseconds;
            Assert.InRange(ms, baseMs * 0.8, Math.Min(baseMs * 1.2, 60_000));
        }
    }

    [Fact]
    public void Jitter_aleatorio_real_fica_na_faixa_e_e_crescente_ate_o_teto()
    {
        var backoff = new BackoffExponencial(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(60), Random.Shared.NextDouble);

        for (var i = 0; i < 200; i++)
        {
            for (var n = 1; n <= 10; n++)
            {
                var ms = backoff.Proximo(n).TotalMilliseconds;
                Assert.InRange(ms, backoff.Base(n).TotalMilliseconds * 0.8, 60_000);
            }
        }

        // As bases são crescentes (não decrescentes) e limitadas pelo teto.
        var bases = Enumerable.Range(1, 10).Select(n => backoff.Base(n)).ToArray();
        Assert.True(bases.Zip(bases.Skip(1)).All(p => p.First <= p.Second));
        Assert.Equal(TimeSpan.FromSeconds(60), bases[^1]);
    }
}
