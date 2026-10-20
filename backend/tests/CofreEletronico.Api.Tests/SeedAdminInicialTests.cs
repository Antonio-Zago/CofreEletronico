using CofreEletronico.Api.Tests.Infra;
using CofreEletronico.Application.Autorizacao;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CofreEletronico.Api.Tests;

/// <summary>
/// Seed do administrador inicial executado pelo Program.cs na subida. A string de conexão é fictícia (só para
/// o seed não ser pulado): o ServicoIdentidade é o fake da ApiFactory, nenhuma conexão é aberta.
/// </summary>
public class SeedAdminInicialTests
{
    private const string Email = "admin@cofre.com.br";
    private const string Senha = "SenhaForte123";

    private static WebApplicationFactory<Program> ComAdminInicial(ApiFactory factory, string? email = Email, string? senha = Senha) =>
        factory.WithWebHostBuilder(b => b
            .UseSetting("ConnectionStrings:Cofre", "Host=localhost;Database=fake;Username=fake;Password=fake")
            .UseSetting("AdminInicial:Email", email)
            .UseSetting("AdminInicial:Senha", senha));

    [Fact]
    public async Task Cria_o_admin_na_subida_com_os_papeis_Admin_e_Usuario()
    {
        using var factory = new ApiFactory();
        using var comAdmin = ComAdminInicial(factory);
        comAdmin.CreateClient().Dispose();

        var admin = await factory.Identidade.VerificarCredenciaisAsync(Email, Senha, CancellationToken.None);
        Assert.NotNull(admin);
        Assert.Equal([Papeis.Admin, Papeis.Usuario], admin.Papeis);
    }

    [Fact]
    public async Task Com_admin_existente_sobe_sem_alterar_nada()
    {
        using var factory = new ApiFactory();
        factory.Identidade.Adicionar("outro@cofre.com.br", "SenhaAntiga1", Papeis.Admin);
        using var comAdmin = ComAdminInicial(factory);
        comAdmin.CreateClient().Dispose();

        Assert.Null(await factory.Identidade.VerificarCredenciaisAsync(Email, Senha, CancellationToken.None));
    }

    [Fact]
    public void Senha_recusada_pela_politica_impede_a_subida()
    {
        using var factory = new ApiFactory();
        factory.Identidade.ErrosNaCriacao = new Dictionary<string, string[]>
        {
            ["senha"] = ["A senha deve conter ao menos um dígito (0-9)."],
        };
        using var comAdmin = ComAdminInicial(factory, senha: "SemDigito");

        var erro = Assert.Throws<InvalidOperationException>(() => comAdmin.CreateClient());

        Assert.Contains("A senha deve conter ao menos um dígito (0-9).", erro.Message);
        Assert.DoesNotContain("SemDigito", erro.Message);
    }
}
