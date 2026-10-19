using System.Security.Cryptography;
using CofreEletronico.Domain.Estantes;
using CofreEletronico.Infrastructure.Criptografia;
using CofreEletronico.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;

namespace CofreEletronico.Infrastructure.Tests.Persistencia;

/// <summary>
/// O conversor de <c>estantes.segredo_hmac</c>: o banco recebe o valor cifrado e a entidade, o valor em claro.
/// Sem banco: usa o conversor e o change tracker do modelo de execução.
/// </summary>
public class SegredoHmacConversorTests
{
    private static readonly byte[] Segredo = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();

    private static Estante NovaEstante() =>
        Estante.Criar("Estante", "Rua A", 0, 0, "est-abc123", Segredo, DateTimeOffset.UnixEpoch);

    private static IProperty PropriedadeSegredo(CofreDbContext context) =>
        context.Model.FindEntityType(typeof(Estante))!.FindProperty("SegredoHmac")!;

    [Fact]
    public void Conversor_grava_o_segredo_cifrado_e_le_em_claro()
    {
        using var provider = InfrastructureFixture.CriarProvider();
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CofreDbContext>();
        var cifrador = provider.GetRequiredService<ICifradorSegredos>();
        var conversor = PropriedadeSegredo(context).GetValueConverter()!;

        var gravado = Assert.IsType<string>(conversor.ConvertToProvider(Segredo));

        Assert.StartsWith("v1:", gravado);
        Assert.DoesNotContain(Convert.ToBase64String(Segredo), gravado);
        Assert.Equal(Segredo, cifrador.Decifrar(gravado));
        Assert.Equal(Segredo, conversor.ConvertFromProvider(gravado));
    }

    [Fact]
    public void Valor_enviado_ao_banco_pela_entidade_rastreada_e_cifrado()
    {
        using var provider = InfrastructureFixture.CriarProvider();
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CofreDbContext>();
        var cifrador = provider.GetRequiredService<ICifradorSegredos>();

        var entrada = context.Add(NovaEstante());
        var propriedade = entrada.Property<byte[]>("SegredoHmac");
        var valorProvider = propriedade.Metadata.GetTypeMapping().Converter!.ConvertToProvider(propriedade.CurrentValue);

        Assert.Equal(Segredo, propriedade.CurrentValue);
        Assert.StartsWith("v1:", Assert.IsType<string>(valorProvider));
        Assert.Equal(Segredo, cifrador.Decifrar((string)valorProvider!));
    }

    [Fact]
    public void Segredo_inalterado_nao_marca_a_propriedade_como_modificada()
    {
        using var provider = InfrastructureFixture.CriarProvider();
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CofreDbContext>();

        var entrada = context.Attach(NovaEstante());
        entrada.Entity.RegistrarHeartbeat(DateTimeOffset.UnixEpoch.AddMinutes(1));
        context.ChangeTracker.DetectChanges();

        Assert.False(entrada.Property<byte[]>("SegredoHmac").IsModified);
        Assert.True(entrada.Property(e => e.Online).IsModified);
    }

    [Fact]
    public void Cada_container_usa_o_proprio_cifrador_mesmo_com_o_modelo_em_cache()
    {
        var outraChave = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        using var providerA = InfrastructureFixture.CriarProvider();
        using var providerB = InfrastructureFixture.CriarProvider(
            configuracaoExtra: new Dictionary<string, string?> { ["Criptografia:ChaveSegredos"] = outraChave });
        using var scopeA = providerA.CreateScope();
        using var scopeB = providerB.CreateScope();
        var contextA = scopeA.ServiceProvider.GetRequiredService<CofreDbContext>();
        var contextB = scopeB.ServiceProvider.GetRequiredService<CofreDbContext>();

        var gravadoB = (string)PropriedadeSegredo(contextB).GetValueConverter()!.ConvertToProvider(Segredo)!;

        Assert.Equal(Segredo, providerB.GetRequiredService<ICifradorSegredos>().Decifrar(gravadoB));
        Assert.Throws<SegredoCifradoInvalidoException>(() => providerA.GetRequiredService<ICifradorSegredos>().Decifrar(gravadoB));
        Assert.NotSame(contextA.Model, contextB.Model);
    }

    [Fact]
    public void Mesmo_container_reaproveita_o_modelo_entre_contextos()
    {
        using var provider = InfrastructureFixture.CriarProvider();
        using var scope1 = provider.CreateScope();
        using var scope2 = provider.CreateScope();

        Assert.Same(
            scope1.ServiceProvider.GetRequiredService<CofreDbContext>().Model,
            scope2.ServiceProvider.GetRequiredService<CofreDbContext>().Model);
    }
}
