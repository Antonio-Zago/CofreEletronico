using System.Text.RegularExpressions;
using CofreEletronico.Application.Autenticacao.Portas;
using Microsoft.Extensions.DependencyInjection;

namespace CofreEletronico.Infrastructure.Tests.Autenticacao;

public class GeradorRefreshTokenTests
{
    private static IGeradorRefreshToken Gerador()
    {
        var provider = InfrastructureFixture.CriarProvider();
        return provider.GetRequiredService<IGeradorRefreshToken>();
    }

    [Fact]
    public void Valor_e_base64url_de_32_bytes_e_hash_e_sha256_hex()
    {
        var gerado = Gerador().Gerar();

        Assert.Matches(new Regex("^[A-Za-z0-9_-]{43}$"), gerado.Valor); // 32 bytes sem padding
        Assert.Matches(new Regex("^[0-9a-f]{64}$"), gerado.Hash);
    }

    [Fact]
    public void Hash_e_deterministico_e_nao_contem_o_valor()
    {
        var gerador = Gerador();
        var gerado = gerador.Gerar();

        Assert.Equal(gerado.Hash, gerador.CalcularHash(gerado.Valor));
        Assert.DoesNotContain(gerado.Valor, gerado.Hash, StringComparison.Ordinal);
        // SHA-256 de "abc" (vetor de teste conhecido).
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", gerador.CalcularHash("abc"));
    }

    [Fact]
    public void Valores_sao_unicos()
    {
        var gerador = Gerador();

        var valores = Enumerable.Range(0, 100).Select(_ => gerador.Gerar().Valor).ToHashSet();

        Assert.Equal(100, valores.Count);
    }

    [Fact]
    public void Validades_padrao_sao_30_dias_por_token_e_90_por_sessao()
    {
        Assert.Equal(TimeSpan.FromDays(30), Gerador().Validade);
        Assert.Equal(TimeSpan.FromDays(90), Gerador().ValidadeSessao);
    }
}
