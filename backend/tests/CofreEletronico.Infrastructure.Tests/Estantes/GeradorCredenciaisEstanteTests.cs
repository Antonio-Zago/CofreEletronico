using System.Text.RegularExpressions;
using CofreEletronico.Application.Estantes.Portas;
using CofreEletronico.Domain.Estantes;
using Microsoft.Extensions.DependencyInjection;

namespace CofreEletronico.Infrastructure.Tests.Estantes;

/// <summary>Gerador real (RandomNumberGenerator), resolvido pela DI de AddInfrastructure.</summary>
public class GeradorCredenciaisEstanteTests
{
    private const int Amostras = 2000;

    private static IGeradorCredenciaisEstante Gerador()
    {
        var provider = InfrastructureFixture.CriarProvider();
        return provider.GetRequiredService<IGeradorCredenciaisEstante>();
    }

    [Fact]
    public void Gerador_e_singleton()
    {
        using var provider = InfrastructureFixture.CriarProvider();

        Assert.Same(provider.GetRequiredService<IGeradorCredenciaisEstante>(), provider.GetRequiredService<IGeradorCredenciaisEstante>());
    }

    [Fact]
    public void Device_id_segue_o_formato_do_contrato_e_e_aceito_pelo_dominio()
    {
        var gerador = Gerador();
        var regex = new Regex(Estante.DeviceIdPadrao);

        for (var i = 0; i < Amostras; i++)
        {
            var deviceId = gerador.GerarDeviceId();
            Assert.Matches(regex, deviceId);
            Assert.Equal(Estante.DeviceIdTamanho, deviceId.Length);
        }
    }

    [Fact]
    public void Device_id_usa_o_alfabeto_inteiro_e_nao_se_repete_em_poucas_amostras()
    {
        var gerador = Gerador();
        var ids = Enumerable.Range(0, Amostras).Select(_ => gerador.GerarDeviceId()).ToList();

        // 2000 sorteios em 36^6 combinações: colisão tem probabilidade ~0,1%. Tolera no máximo uma.
        Assert.True(ids.Distinct().Count() >= Amostras - 1);

        // 12000 caracteres de 36 símbolos: todos aparecem (a chance de faltar algum é desprezível).
        var caracteres = ids.SelectMany(id => id[4..]).ToHashSet();
        Assert.Equal(36, caracteres.Count);
    }

    [Fact]
    public void Segredo_tem_32_bytes_e_vira_hex_de_64_caracteres()
    {
        var gerador = Gerador();

        var segredos = Enumerable.Range(0, 200).Select(_ => gerador.GerarSegredoHmac()).ToList();

        Assert.All(segredos, s =>
        {
            Assert.Equal(32, s.Length);
            Assert.Matches("^[0-9a-f]{64}$", Convert.ToHexString(s).ToLowerInvariant());
        });
        Assert.Equal(segredos.Count, segredos.Select(Convert.ToHexString).Distinct().Count());
        Assert.DoesNotContain(segredos, s => s.All(b => b == 0));
    }

    [Fact]
    public void Senha_mqtt_tem_24_caracteres_alfanumericos_e_nao_se_repete()
    {
        var gerador = Gerador();

        var senhas = Enumerable.Range(0, Amostras).Select(_ => gerador.GerarSenhaMqtt()).ToList();

        Assert.All(senhas, s => Assert.Matches("^[A-Za-z0-9]{24}$", s));
        Assert.Equal(senhas.Count, senhas.Distinct().Count());

        // Os três grupos (maiúsculas, minúsculas e dígitos) aparecem no conjunto.
        var todos = string.Concat(senhas);
        Assert.Contains(todos, char.IsUpper);
        Assert.Contains(todos, char.IsLower);
        Assert.Contains(todos, char.IsDigit);
    }
}
