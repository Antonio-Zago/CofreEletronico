using System.Security.Cryptography;
using CofreEletronico.Infrastructure.Criptografia;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CofreEletronico.Infrastructure.Tests.Criptografia;

public class CifradorSegredosAesGcmTests
{
    private static readonly byte[] Segredo = Enumerable.Range(100, 32).Select(i => (byte)i).ToArray();

    private static CifradorSegredosAesGcm Criar(string chave) =>
        new(Options.Create(new CriptografiaOpcoes { ChaveSegredos = chave }));

    private static CifradorSegredosAesGcm Criar() => Criar(InfrastructureFixture.ChaveSegredosFicticia);

    private static string ChaveAleatoria() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    [Fact]
    public void Ida_e_volta_devolve_o_valor_original()
    {
        var cifrador = Criar();

        var cifrado = cifrador.Cifrar(Segredo);

        Assert.Equal(Segredo, cifrador.Decifrar(cifrado));
    }

    [Fact]
    public void Formato_tem_versao_e_nonce_tag_no_payload()
    {
        var cifrado = Criar().Cifrar(Segredo);

        Assert.StartsWith("v1:", cifrado);
        var payload = Convert.FromBase64String(cifrado["v1:".Length..]);
        Assert.Equal(12 + Segredo.Length + 16, payload.Length);
        Assert.DoesNotContain(Convert.ToBase64String(Segredo), cifrado);
    }

    [Fact]
    public void Cada_cifragem_usa_um_nonce_diferente()
    {
        var cifrador = Criar();

        var a = cifrador.Cifrar(Segredo);
        var b = cifrador.Cifrar(Segredo);

        Assert.NotEqual(a, b);
        var nonceA = Convert.FromBase64String(a[3..]).AsSpan(0, 12).ToArray();
        var nonceB = Convert.FromBase64String(b[3..]).AsSpan(0, 12).ToArray();
        Assert.NotEqual(nonceA, nonceB);
        Assert.Equal(Segredo, cifrador.Decifrar(a));
        Assert.Equal(Segredo, cifrador.Decifrar(b));
    }

    [Theory]
    [InlineData(0)] // nonce
    [InlineData(20)] // texto cifrado
    [InlineData(12 + 32 + 15)] // tag
    public void Adulteracao_e_detectada(int posicao)
    {
        var cifrador = Criar();
        var payload = Convert.FromBase64String(cifrador.Cifrar(Segredo)[3..]);
        payload[posicao] ^= 0x01;

        var erro = Assert.Throws<SegredoCifradoInvalidoException>(() => cifrador.Decifrar("v1:" + Convert.ToBase64String(payload)));

        Assert.Contains("adulterado", erro.Message);
        Assert.Null(erro.InnerException);
    }

    [Theory]
    [InlineData("v2:")]
    [InlineData("V1:")]
    [InlineData("")]
    public void Versao_desconhecida_ou_ausente_falha(string prefixo)
    {
        var cifrador = Criar();
        var payload = cifrador.Cifrar(Segredo)[3..];

        var erro = Assert.Throws<SegredoCifradoInvalidoException>(() => cifrador.Decifrar(prefixo + payload));

        Assert.Contains("versão", erro.Message);
        Assert.DoesNotContain(payload, erro.Message);
    }

    [Theory]
    [InlineData("v1:não-é-base64!")]
    [InlineData("v1:AAAA")] // menor que nonce + tag
    public void Valor_malformado_falha_sem_ecoar_o_conteudo(string valor)
    {
        var erro = Assert.Throws<SegredoCifradoInvalidoException>(() => Criar().Decifrar(valor));

        Assert.Contains("malformado", erro.Message);
        Assert.DoesNotContain(valor[3..], erro.Message);
    }

    [Fact]
    public void Chave_errada_falha()
    {
        var cifrado = Criar(ChaveAleatoria()).Cifrar(Segredo);

        Assert.Throws<SegredoCifradoInvalidoException>(() => Criar(ChaveAleatoria()).Decifrar(cifrado));
    }

    [Fact]
    public void Sem_chave_a_construcao_nao_falha_e_o_uso_falha_com_mensagem_clara()
    {
        using var provider = InfrastructureFixture.CriarProvider(
            configuracaoExtra: new Dictionary<string, string?> { ["Criptografia:ChaveSegredos"] = null });

        // Construir o cifrador (e o DbContext que depende dele) não lê a chave: é o que mantém o `dotnet ef` funcionando.
        var cifrador = provider.GetRequiredService<ICifradorSegredos>();

        var erro = Assert.Throws<OptionsValidationException>(() => cifrador.Cifrar(Segredo));
        Assert.Contains("Criptografia:ChaveSegredos não configurada", erro.Message);
    }

    [Fact]
    public void Cifrador_esta_registrado_como_singleton()
    {
        // O cache de modelo do EF usa a instância do cifrador como chave: scoped/transient vazaria modelos.
        var configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);

        var registro = Assert.Single(services, d => d.ServiceType == typeof(ICifradorSegredos));
        Assert.Equal(ServiceLifetime.Singleton, registro.Lifetime);

        using var provider = InfrastructureFixture.CriarProvider();
        Assert.Same(provider.GetRequiredService<ICifradorSegredos>(), provider.GetRequiredService<ICifradorSegredos>());
    }

    [Fact]
    public void Falha_de_configuracao_nao_fica_em_cache_no_cifrador()
    {
        var opcoes = new OpcoesMutaveis(new CriptografiaOpcoes { ChaveSegredos = "não-é-base64!" });
        var cifrador = new CifradorSegredosAesGcm(opcoes);

        Assert.ThrowsAny<FormatException>(() => cifrador.Cifrar(Segredo));

        opcoes.Atual = new CriptografiaOpcoes { ChaveSegredos = InfrastructureFixture.ChaveSegredosFicticia };
        Assert.Equal(Segredo, cifrador.Decifrar(cifrador.Cifrar(Segredo)));
    }

    private sealed class OpcoesMutaveis(CriptografiaOpcoes inicial) : IOptions<CriptografiaOpcoes>
    {
        public CriptografiaOpcoes Atual { get; set; } = inicial;

        public CriptografiaOpcoes Value => Atual;
    }
}
