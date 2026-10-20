using CofreEletronico.Application;
using CofreEletronico.Application.Autenticacao.Portas;
using CofreEletronico.Infrastructure.Autorizacao;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace CofreEletronico.Infrastructure.Tests.Autorizacao;

public class AdminInicialOpcoesTests
{
    private const string EmailValido = "admin@cofre.com.br";
    private const string SenhaValida = "SenhaForte123";

    private static AdminInicialOpcoes Ler(string? email, string? senha, string? nome = null)
    {
        var extra = new Dictionary<string, string?> { ["AdminInicial:Email"] = email, ["AdminInicial:Senha"] = senha };
        if (nome is not null)
            extra["AdminInicial:Nome"] = nome;

        using var provider = InfrastructureFixture.CriarProvider(configuracaoExtra: extra);
        return provider.GetRequiredService<IOptions<AdminInicialOpcoes>>().Value;
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("   ", null)]
    public void Sem_email_e_senha_nao_esta_configurado_e_e_valido(string? email, string? senha)
    {
        var opcoes = Ler(email, senha);

        Assert.False(opcoes.Configurado);
        Assert.Null(opcoes.CriarComando());
    }

    [Fact]
    public void Secao_ausente_nao_esta_configurada()
    {
        using var provider = InfrastructureFixture.CriarProvider();

        Assert.Null(provider.GetRequiredService<IOptions<AdminInicialOpcoes>>().Value.CriarComando());
    }

    [Fact]
    public void Email_e_senha_validos_geram_o_comando_com_nome_padrao()
    {
        var comando = Ler($"  {EmailValido} ", SenhaValida)!.CriarComando();

        Assert.NotNull(comando);
        Assert.Equal(EmailValido, comando.Email);
        Assert.Equal(SenhaValida, comando.Senha);
        Assert.Equal(AdminInicialOpcoes.NomePadrao, comando.Nome);
    }

    [Theory]
    [InlineData("  Dona Admin ", "Dona Admin")]
    [InlineData("   ", AdminInicialOpcoes.NomePadrao)]
    public void Nome_configurado_e_aparado_e_vazio_usa_o_padrao(string nome, string esperado)
    {
        Assert.Equal(esperado, Ler(EmailValido, SenhaValida, nome).CriarComando()!.Nome);
    }

    [Fact]
    public void Senha_sem_email_falha()
    {
        var erro = Assert.Throws<OptionsValidationException>(() => Ler(null, SenhaValida));

        Assert.Contains("AdminInicial:Email não configurado", erro.Message);
        Assert.DoesNotContain(SenhaValida, erro.Message);
    }

    [Fact]
    public void Email_sem_senha_falha()
    {
        var erro = Assert.Throws<OptionsValidationException>(() => Ler(EmailValido, null));

        Assert.Contains("AdminInicial:Senha não configurada", erro.Message);
        Assert.DoesNotContain(EmailValido, erro.Message);
    }

    [Theory]
    [InlineData("nao-e-email")]
    [InlineData("Admin <admin@cofre.com.br>")]
    [InlineData("admin@")]
    public void Email_invalido_falha_sem_ecoar_o_valor(string email)
    {
        var erro = Assert.Throws<OptionsValidationException>(() => Ler(email, SenhaValida));

        Assert.Contains("AdminInicial:Email não é um e-mail válido", erro.Message);
        Assert.DoesNotContain(email, erro.Message);
    }

    [Fact]
    public void Email_acima_de_256_caracteres_falha()
    {
        var email = new string('a', 250) + "@cofre.com";

        var erro = Assert.Throws<OptionsValidationException>(() => Ler(email, SenhaValida));

        Assert.Contains("no máximo 256", erro.Message);
    }

    [Fact]
    public void Senha_acima_de_128_caracteres_falha_sem_ecoar_o_valor()
    {
        var senha = "Aa1" + new string('x', 126);

        var erro = Assert.Throws<OptionsValidationException>(() => Ler(EmailValido, senha));

        Assert.Contains("AdminInicial:Senha deve ter no máximo 128", erro.Message);
        Assert.DoesNotContain(senha, erro.Message);
    }

    [Fact]
    public void Senha_com_128_caracteres_e_aceita()
    {
        var senha = "Aa1" + new string('x', 125);

        Assert.Equal(senha, Ler(EmailValido, senha).Senha);
    }

    [Fact]
    public void Nome_acima_de_150_caracteres_falha()
    {
        var erro = Assert.Throws<OptionsValidationException>(() => Ler(EmailValido, SenhaValida, new string('n', 151)));

        Assert.Contains("AdminInicial:Nome deve ter no máximo 150", erro.Message);
    }

    [Fact]
    public async Task Sem_string_de_conexao_o_seed_e_pulado_sem_erro()
    {
        using var provider = InfrastructureFixture.CriarProvider(
            connectionString: null,
            configuracaoExtra: new Dictionary<string, string?> { ["AdminInicial:Email"] = EmailValido, ["AdminInicial:Senha"] = SenhaValida });

        await provider.GarantirAdminInicialAsync(); // não resolve o caso de uso nem abre conexão
    }

    [Fact]
    public async Task Configuracao_invalida_falha_mesmo_sem_banco()
    {
        using var provider = InfrastructureFixture.CriarProvider(
            connectionString: null,
            configuracaoExtra: new Dictionary<string, string?> { ["AdminInicial:Email"] = EmailValido });

        await Assert.ThrowsAsync<OptionsValidationException>(() => provider.GarantirAdminInicialAsync());
    }

    [Fact]
    public async Task Senha_recusada_pela_politica_impede_a_subida_listando_as_regras_sem_a_senha()
    {
        const string senhaFraca = "fraquinha";
        using var provider = InfrastructureFixture.CriarProvider(
            configuracaoExtra: new Dictionary<string, string?> { ["AdminInicial:Email"] = EmailValido, ["AdminInicial:Senha"] = senhaFraca },
            configurarServicos: services =>
            {
                services.AddApplication();
                services.RemoveAll<IServicoIdentidade>();
                services.AddScoped<IServicoIdentidade, IdentidadeQueRecusaSenha>();
            });

        var erro = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GarantirAdminInicialAsync());

        Assert.Contains("AdminInicial recusado", erro.Message);
        Assert.Contains("A senha deve conter ao menos um dígito (0-9).", erro.Message);
        Assert.Contains("A senha deve conter ao menos uma letra maiúscula.", erro.Message);
        Assert.DoesNotContain(senhaFraca, erro.Message);
        Assert.DoesNotContain(EmailValido, erro.Message);
    }

    /// <summary>Simula o Identity recusando a senha pela política (sem banco).</summary>
    private sealed class IdentidadeQueRecusaSenha : IServicoIdentidade
    {
        public Task<ResultadoCriacaoUsuario> CriarUsuarioAsync(NovoUsuario dados, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ResultadoCriacaoUsuario> CriarAdministradorAsync(NovoUsuario dados, CancellationToken cancellationToken) =>
            Task.FromResult(ResultadoCriacaoUsuario.Invalido(new Dictionary<string, string[]>
            {
                ["senha"] = ["A senha deve conter ao menos um dígito (0-9).", "A senha deve conter ao menos uma letra maiúscula."],
            }));

        public Task<bool> ExisteUsuarioComPapelAsync(string papel, CancellationToken cancellationToken) => Task.FromResult(false);

        public Task<UsuarioAutenticado?> VerificarCredenciaisAsync(string email, string senha, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<UsuarioAutenticado?> ObterAsync(Guid usuarioId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
