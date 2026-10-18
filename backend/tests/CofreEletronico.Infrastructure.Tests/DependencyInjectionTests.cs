using CofreEletronico.Infrastructure.Persistencia;
using CofreEletronico.Infrastructure.Persistencia.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CofreEletronico.Infrastructure.Tests;

public class DependencyInjectionTests
{
    [Fact]
    public void AddInfrastructure_resolve_UserManager_e_RoleManager()
    {
        using var provider = InfrastructureFixture.CriarProvider();
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<UserManager<Usuario>>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<RoleManager<Papel>>());
    }

    [Fact]
    public void Politica_do_Identity_segue_a_US03()
    {
        using var provider = InfrastructureFixture.CriarProvider();
        var options = provider.GetRequiredService<IOptions<IdentityOptions>>().Value;

        Assert.True(options.User.RequireUniqueEmail);

        Assert.Equal(8, options.Password.RequiredLength);
        Assert.True(options.Password.RequireDigit);
        Assert.True(options.Password.RequireLowercase);
        Assert.True(options.Password.RequireUppercase);
        Assert.False(options.Password.RequireNonAlphanumeric);

        Assert.Equal(5, options.Lockout.MaxFailedAccessAttempts);
        Assert.Equal(TimeSpan.FromMinutes(15), options.Lockout.DefaultLockoutTimeSpan);
        Assert.True(options.Lockout.AllowedForNewUsers);
    }

    [Theory]
    [InlineData("Senha123", true)]
    [InlineData("senha123", false)] // sem maiúscula
    [InlineData("SENHA123", false)] // sem minúscula
    [InlineData("SenhaForte", false)] // sem dígito
    [InlineData("Sen123", false)] // menos de 8
    public async Task Validador_de_senha_aplica_a_politica(string senha, bool valida)
    {
        using var provider = InfrastructureFixture.CriarProvider();
        using var scope = provider.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<Usuario>>();

        var resultados = new List<IdentityResult>();
        foreach (var validador in userManager.PasswordValidators)
        {
            resultados.Add(await validador.ValidateAsync(userManager, new Usuario(), senha));
        }

        Assert.Equal(valida, resultados.All(r => r.Succeeded));
    }

    [Fact]
    public void Hash_ficticio_e_preparado_por_hosted_service_na_subida()
    {
        using var provider = InfrastructureFixture.CriarProvider();

        var hostedServices = provider.GetServices<Microsoft.Extensions.Hosting.IHostedService>();

        Assert.Contains(hostedServices, s => s.GetType().Name == "PreaquecimentoVerificacaoSenha");
    }

    [Fact]
    public async Task AplicarMigrations_sem_string_de_conexao_nao_falha()
    {
        using var provider = InfrastructureFixture.CriarProvider(connectionString: null);

        await provider.AplicarMigrationsAsync();
    }
}
