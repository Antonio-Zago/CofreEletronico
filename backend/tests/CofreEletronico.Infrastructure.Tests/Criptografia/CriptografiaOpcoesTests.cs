using CofreEletronico.Infrastructure.Criptografia;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CofreEletronico.Infrastructure.Tests.Criptografia;

public class CriptografiaOpcoesTests
{
    private static CriptografiaOpcoes Ler(string? chave)
    {
        using var provider = InfrastructureFixture.CriarProvider(
            configuracaoExtra: new Dictionary<string, string?> { ["Criptografia:ChaveSegredos"] = chave });
        return provider.GetRequiredService<IOptions<CriptografiaOpcoes>>().Value;
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Chave_ausente_falha_com_mensagem_clara(string? chave)
    {
        var erro = Assert.Throws<OptionsValidationException>(() => Ler(chave));

        Assert.Contains("Criptografia:ChaveSegredos não configurada", erro.Message);
        Assert.Contains("user-secrets", erro.Message);
        Assert.Contains("Criptografia__ChaveSegredos", erro.Message);
        Assert.Contains("backend/README.md", erro.Message);
    }

    [Fact]
    public void Chave_que_nao_e_base64_falha()
    {
        var erro = Assert.Throws<OptionsValidationException>(() => Ler("isto não é base64!"));

        Assert.Contains("base64", erro.Message);
        Assert.Contains("backend/README.md", erro.Message);
    }

    [Theory]
    [InlineData(16)]
    [InlineData(31)]
    [InlineData(33)]
    [InlineData(48)]
    public void Chave_com_tamanho_diferente_de_32_bytes_falha(int tamanho)
    {
        var erro = Assert.Throws<OptionsValidationException>(() => Ler(Convert.ToBase64String(new byte[tamanho])));

        Assert.Contains("exatamente 32 bytes", erro.Message);
    }

    [Fact]
    public void Chave_de_32_bytes_em_base64_e_aceita()
    {
        Assert.Equal(InfrastructureFixture.ChaveSegredosFicticia, Ler(InfrastructureFixture.ChaveSegredosFicticia).ChaveSegredos);
    }

    public static TheoryData<string> ChavesTriviais() => new()
    {
        InfrastructureFixture.ChaveSegredosFicticia, // chave de teste versionada (bytes 0..31)
        Convert.ToBase64String(new byte[32]), // toda zero
        Convert.ToBase64String(Enumerable.Repeat((byte)0xAA, 32).ToArray()), // todos os bytes iguais
        Convert.ToBase64String(Enumerable.Repeat((byte)0xFF, 32).ToArray()),
    };

    [Theory]
    [MemberData(nameof(ChavesTriviais))]
    public void Fora_de_Testing_chave_conhecida_ou_trivial_e_recusada(string chave)
    {
        foreach (var ambiente in new[] { "Production", "Development", "Staging" })
        {
            using var provider = InfrastructureFixture.CriarProvider(
                configuracaoExtra: new Dictionary<string, string?> { ["Criptografia:ChaveSegredos"] = chave },
                ambiente: ambiente);

            var erro = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<CriptografiaOpcoes>>().Value);

            Assert.Contains("chave conhecida ou trivial", erro.Message);
            Assert.Contains("openssl rand -base64 32", erro.Message);
            Assert.DoesNotContain(chave, erro.Message);
        }
    }

    [Theory]
    [MemberData(nameof(ChavesTriviais))]
    public void Em_Testing_chave_trivial_e_aceita(string chave)
    {
        Assert.Equal(chave, Ler(chave).ChaveSegredos);
    }

    [Fact]
    public void Fora_de_Testing_chave_aleatoria_e_aceita()
    {
        var chave = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        using var provider = InfrastructureFixture.CriarProvider(
            configuracaoExtra: new Dictionary<string, string?> { ["Criptografia:ChaveSegredos"] = chave },
            ambiente: "Production");

        Assert.Equal(chave, provider.GetRequiredService<IOptions<CriptografiaOpcoes>>().Value.ChaveSegredos);
    }
}
