using System.Net;
using CofreEletronico.Api.Autenticacao;

namespace CofreEletronico.Api.Tests.Auth;

public class ChaveRateLimitTests
{
    [Theory]
    [InlineData("203.0.113.7", "203.0.113.7")]
    [InlineData("::ffff:203.0.113.7", "203.0.113.7")] // IPv4 mapeado em IPv6 = o mesmo cliente IPv4
    [InlineData("127.0.0.1", "127.0.0.1")]
    [InlineData("2001:db8:abcd:12:1:2:3:4", "2001:db8:abcd:12::/64")]
    [InlineData("2001:db8:abcd:12:ffff:ffff:ffff:ffff", "2001:db8:abcd:12::/64")]
    [InlineData("fe80::1%3", "fe80::/64")] // escopo descartado
    [InlineData("::1", "::/64")]
    public void Gera_a_chave_esperada(string ip, string esperado)
    {
        Assert.Equal(esperado, ChaveRateLimit.Gerar(IPAddress.Parse(ip)));
    }

    [Fact]
    public void Enderecos_do_mesmo_prefixo_64_dividem_a_chave()
    {
        Assert.Equal(
            ChaveRateLimit.Gerar(IPAddress.Parse("2001:db8:1:2::a")),
            ChaveRateLimit.Gerar(IPAddress.Parse("2001:db8:1:2:dead:beef:0:1")));
    }

    [Fact]
    public void Prefixos_64_diferentes_tem_chaves_diferentes()
    {
        Assert.NotEqual(
            ChaveRateLimit.Gerar(IPAddress.Parse("2001:db8:1:2::a")),
            ChaveRateLimit.Gerar(IPAddress.Parse("2001:db8:1:3::a")));
    }

    [Fact]
    public void Ip_ausente_usa_chave_fixa()
    {
        Assert.Equal(ChaveRateLimit.Desconhecido, ChaveRateLimit.Gerar(null));
    }
}
