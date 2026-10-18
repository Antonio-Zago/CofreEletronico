using CofreEletronico.Infrastructure.Autenticacao;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CofreEletronico.Infrastructure.Tests.Autenticacao;

public class JwtOpcoesTests
{
    private static JwtOpcoes Ler(string? chave)
    {
        using var provider = InfrastructureFixture.CriarProvider(
            configuracaoExtra: new Dictionary<string, string?> { ["Jwt:ChaveAssinatura"] = chave });
        return provider.GetRequiredService<IOptions<JwtOpcoes>>().Value;
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Chave_ausente_falha_com_mensagem_clara(string? chave)
    {
        var erro = Assert.Throws<OptionsValidationException>(() => Ler(chave));

        Assert.Contains("Jwt:ChaveAssinatura não configurada", erro.Message);
        Assert.Contains("user-secrets", erro.Message);
        Assert.Contains("Jwt__ChaveAssinatura", erro.Message);
    }

    [Fact]
    public void Chave_com_menos_de_32_bytes_falha()
    {
        var erro = Assert.Throws<OptionsValidationException>(() => Ler(new string('a', 31)));

        Assert.Contains("no mínimo 32 bytes", erro.Message);
    }

    [Fact]
    public void Chave_com_32_bytes_e_aceita_e_validades_seguem_o_contrato()
    {
        var opcoes = Ler(new string('a', 32));

        Assert.Equal(TimeSpan.FromMinutes(15), opcoes.ValidadeAccessToken);
        Assert.Equal(TimeSpan.FromDays(30), opcoes.ValidadeRefreshToken);
        Assert.Equal(TimeSpan.FromDays(90), opcoes.ValidadeSessao);
        Assert.Equal(InfrastructureFixture.Emissor, opcoes.Emissor);
        Assert.Equal(InfrastructureFixture.Audiencia, opcoes.Audiencia);
    }

    [Theory]
    [InlineData("Jwt:ValidadeAccessToken", "00:00:59", "1 a 15 minutos")]
    [InlineData("Jwt:ValidadeAccessToken", "00:15:01", "1 a 15 minutos")]
    [InlineData("Jwt:ValidadeAccessToken", "-00:05:00", "1 a 15 minutos")]
    [InlineData("Jwt:ValidadeRefreshToken", "23:59:59", "1 a 30 dias")]
    [InlineData("Jwt:ValidadeRefreshToken", "30.00:00:01", "1 a 30 dias")]
    [InlineData("Jwt:ValidadeSessao", "23:59:59", "1 a 90 dias")]
    [InlineData("Jwt:ValidadeSessao", "90.00:00:01", "1 a 90 dias")]
    public void Validades_fora_dos_limites_do_contrato_falham(string chave, string valor, string mensagem)
    {
        using var provider = InfrastructureFixture.CriarProvider(
            configuracaoExtra: new Dictionary<string, string?> { [chave] = valor });

        var erro = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<JwtOpcoes>>().Value);

        Assert.Contains(chave, erro.Message);
        Assert.Contains(mensagem, erro.Message);
    }

    [Theory]
    [InlineData("00:01:00", "1.00:00:00")]
    [InlineData("00:15:00", "30.00:00:00")]
    public void Validades_nos_limites_sao_aceitas(string access, string refresh)
    {
        using var provider = InfrastructureFixture.CriarProvider(configuracaoExtra: new Dictionary<string, string?>
        {
            ["Jwt:ValidadeAccessToken"] = access,
            ["Jwt:ValidadeRefreshToken"] = refresh,
        });

        var opcoes = provider.GetRequiredService<IOptions<JwtOpcoes>>().Value;

        Assert.Equal(TimeSpan.Parse(access), opcoes.ValidadeAccessToken);
        Assert.Equal(TimeSpan.Parse(refresh), opcoes.ValidadeRefreshToken);
    }

    [Fact]
    public void Emissor_e_audiencia_sao_obrigatorios()
    {
        using var provider = InfrastructureFixture.CriarProvider(
            configuracaoExtra: new Dictionary<string, string?> { ["Jwt:Emissor"] = "", ["Jwt:Audiencia"] = "" });

        var erro = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<JwtOpcoes>>().Value);

        Assert.Contains("Jwt:Emissor", erro.Message);
        Assert.Contains("Jwt:Audiencia", erro.Message);
    }
}
