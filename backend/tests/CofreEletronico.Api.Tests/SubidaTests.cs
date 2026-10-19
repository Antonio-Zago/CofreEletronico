using CofreEletronico.Api.Tests.Infra;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;

namespace CofreEletronico.Api.Tests;

/// <summary>Opções obrigatórias validadas na subida (ValidateOnStart).</summary>
public class SubidaTests
{
    [Fact]
    public void Sem_chave_de_criptografia_a_Api_nao_sobe()
    {
        using var factory = new ApiFactory().WithWebHostBuilder(b => b.UseSetting("Criptografia:ChaveSegredos", ""));

        var erro = Assert.Throws<OptionsValidationException>(() => factory.CreateClient());

        Assert.Contains("Criptografia:ChaveSegredos não configurada", erro.Message);
    }

    [Fact]
    public async Task Com_chave_de_criptografia_a_Api_sobe()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var resposta = await client.GetAsync("/ping");

        Assert.True(resposta.IsSuccessStatusCode);
    }
}
