using CofreEletronico.Application.Autorizacao;
using CofreEletronico.Application.Tests.Autenticacao;
using CofreEletronico.Application.Tests.Comum;
using Microsoft.Extensions.Logging;

namespace CofreEletronico.Application.Tests.Autorizacao;

public class GarantirAdminInicialTests
{
    private const string Email = "admin@cofre.com.br";
    private const string Senha = "SenhaForte123";

    private readonly ServicoIdentidadeFalso _identidade = new();
    private readonly LoggerCapturado<GarantirAdminInicial> _logger = new();
    private readonly GarantirAdminInicial _casoDeUso;

    public GarantirAdminInicialTests()
    {
        _casoDeUso = new GarantirAdminInicial(_identidade, _logger);
    }

    private static AdminInicialComando Comando(string email = Email, string senha = Senha, string nome = "Administrador") =>
        new(email, senha, nome);

    private void AssertSemDadosSensiveisNoLog()
    {
        Assert.DoesNotContain(Senha, _logger.Tudo);
        Assert.DoesNotContain(Email, _logger.Tudo);
    }

    [Fact]
    public async Task Sem_configuracao_nao_faz_nada()
    {
        var resultado = await _casoDeUso.ExecutarAsync(null);

        Assert.Equal(StatusAdminInicial.NaoConfigurado, resultado.Status);
        Assert.Equal(0, _identidade.ConsultasPorPapel);
        Assert.Empty(_identidade.Criados);
        var (nivel, _) = Assert.Single(_logger.Registros);
        Assert.Equal(LogLevel.Information, nivel);
    }

    [Fact]
    public async Task Sem_admin_cria_com_os_papeis_Admin_e_Usuario()
    {
        var resultado = await _casoDeUso.ExecutarAsync(Comando(email: $"  {Email} ", nome: "  Dona Admin "));

        Assert.Equal(StatusAdminInicial.Criado, resultado.Status);
        var (dados, papeis) = Assert.Single(_identidade.Criados);
        Assert.Equal(Email, dados.Email);
        Assert.Equal("Dona Admin", dados.Nome);
        Assert.Equal(Senha, dados.Senha);
        Assert.Null(dados.Telefone);
        Assert.Equal([Papeis.Admin, Papeis.Usuario], papeis);

        // Log recomenda remover a senha da configuração, com o e-mail mascarado.
        var (nivel, mensagem) = Assert.Single(_logger.Registros);
        Assert.Equal(LogLevel.Information, nivel);
        Assert.Contains("AdminInicial:Senha", mensagem);
        Assert.Contains("a***@cofre.com.br", mensagem);
        AssertSemDadosSensiveisNoLog();
    }

    [Fact]
    public async Task Com_admin_existente_nao_cria_nem_altera()
    {
        _identidade.Adicionar("outro-admin@cofre.com.br", "SenhaAntiga1", Papeis.Admin, Papeis.Usuario);

        var resultado = await _casoDeUso.ExecutarAsync(Comando());

        Assert.Equal(StatusAdminInicial.AdminJaExiste, resultado.Status);
        Assert.Empty(_identidade.Criados);

        // Warning para aparecer em produção: a senha segue configurada sem uso.
        var (nivel, mensagem) = Assert.Single(_logger.Registros);
        Assert.Equal(LogLevel.Warning, nivel);
        Assert.Contains("AdminInicial:Senha", mensagem);
        AssertSemDadosSensiveisNoLog();
    }

    [Fact]
    public async Task Executar_duas_vezes_cria_um_unico_admin()
    {
        Assert.Equal(StatusAdminInicial.Criado, (await _casoDeUso.ExecutarAsync(Comando())).Status);
        Assert.Equal(StatusAdminInicial.AdminJaExiste, (await _casoDeUso.ExecutarAsync(Comando(senha: "OutraSenha456"))).Status);

        Assert.Single(_identidade.Criados);
    }

    [Fact]
    public async Task Email_de_conta_comum_existente_nao_e_promovido()
    {
        var existente = _identidade.Adicionar(Email, "SenhaDoUsuario1", Papeis.Usuario);

        var resultado = await _casoDeUso.ExecutarAsync(Comando());

        Assert.Equal(StatusAdminInicial.EmailEmUsoPorOutraConta, resultado.Status);
        Assert.Empty(_identidade.Criados);
        var conta = await _identidade.ObterAsync(existente.Id, CancellationToken.None);
        Assert.Equal([Papeis.Usuario], conta!.Papeis);
        Assert.False(await _identidade.ExisteUsuarioComPapelAsync(Papeis.Admin, CancellationToken.None));

        var (nivel, mensagem) = Assert.Single(_logger.Registros);
        Assert.Equal(LogLevel.Warning, nivel);
        Assert.Contains("a***@cofre.com.br", mensagem);
        AssertSemDadosSensiveisNoLog();
    }

    [Fact]
    public async Task Admin_criado_em_paralelo_com_o_mesmo_email_nao_gera_aviso()
    {
        // Outra instância da Api criou o Admin entre a checagem e a criação.
        _identidade.AntesDeCriar = () => _identidade.Adicionar(Email, Senha, Papeis.Admin, Papeis.Usuario);

        var resultado = await _casoDeUso.ExecutarAsync(Comando());

        Assert.Equal(StatusAdminInicial.AdminJaExiste, resultado.Status);
        Assert.DoesNotContain(_logger.Registros, r => r.Nivel >= LogLevel.Warning);
    }

    [Fact]
    public async Task Senha_recusada_pela_politica_devolve_Invalido_com_as_regras()
    {
        _identidade.ErrosNaCriacao = new Dictionary<string, string[]>
        {
            ["senha"] = ["A senha deve ter no mínimo 8 caracteres.", "A senha deve conter ao menos um dígito (0-9)."],
        };

        var resultado = await _casoDeUso.ExecutarAsync(Comando(senha: "fraca"));

        Assert.Equal(StatusAdminInicial.Invalido, resultado.Status);
        Assert.Equal(2, resultado.Erros["senha"].Length);
        Assert.Empty(_identidade.Criados);
        Assert.DoesNotContain("fraca", _logger.Tudo);
    }
}
