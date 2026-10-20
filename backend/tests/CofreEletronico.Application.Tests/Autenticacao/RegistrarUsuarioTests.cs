using CofreEletronico.Application.Autenticacao;
using CofreEletronico.Application.Autorizacao;

namespace CofreEletronico.Application.Tests.Autenticacao;

public class RegistrarUsuarioTests
{
    private readonly AutenticacaoCenario _cenario = new();

    [Fact]
    public async Task Registro_cria_usuario_com_papel_Usuario_e_devolve_tokens()
    {
        var resultado = await _cenario.Registrar.ExecutarAsync(
            new RegistrarUsuarioComando("  Ana Souza ", " ana@exemplo.com ", "Senha123", "  "));

        Assert.Equal(StatusAutenticacao.Sucesso, resultado.Status);
        var tokens = resultado.Tokens!;
        Assert.False(string.IsNullOrEmpty(tokens.AccessToken));
        Assert.Equal(AutenticacaoCenario.Inicio.AddMinutes(15), tokens.AccessTokenExpiraEm);
        Assert.Equal(AutenticacaoCenario.Inicio.AddDays(30), tokens.RefreshTokenExpiraEm);

        // Dados normalizados e papel do cadastro.
        var (dados, papeis) = Assert.Single(_cenario.Identidade.Criados);
        Assert.Equal("Ana Souza", dados.Nome);
        Assert.Equal("ana@exemplo.com", dados.Email);
        Assert.Null(dados.Telefone);
        Assert.Equal([Papeis.Usuario], papeis);

        // Refresh persistido só como hash, numa família nova.
        var refresh = Assert.Single(_cenario.Repositorio.Tokens);
        Assert.Equal(_cenario.RefreshTokens.CalcularHash(tokens.RefreshToken), refresh.TokenHash);
        Assert.NotEqual(tokens.RefreshToken, refresh.TokenHash);

        // Sessão de 90 dias a partir do registro (login automático); token de 30 dias.
        Assert.Equal(AutenticacaoCenario.Inicio.AddDays(90), refresh.SessaoExpiraEm);
        Assert.Equal(AutenticacaoCenario.Inicio.AddDays(30), refresh.ExpiraEm);
    }

    [Fact]
    public async Task Email_duplicado_resulta_em_conflito_sem_tokens()
    {
        _cenario.Identidade.Adicionar("ana@exemplo.com", "Outra123", Papeis.Usuario);

        var resultado = await _cenario.Registrar.ExecutarAsync(
            new RegistrarUsuarioComando("Ana", "ana@exemplo.com", "Senha123", null));

        Assert.Equal(StatusAutenticacao.EmailEmUso, resultado.Status);
        Assert.Null(resultado.Tokens);
        Assert.Empty(_cenario.Repositorio.Tokens);
    }

    [Fact]
    public async Task Senha_rejeitada_pelo_identity_vira_erro_de_validacao()
    {
        _cenario.Identidade.ErrosNaCriacao = new Dictionary<string, string[]> { ["senha"] = ["fraca"] };

        var resultado = await _cenario.Registrar.ExecutarAsync(new RegistrarUsuarioComando("Ana", "ana@exemplo.com", "123", null));

        Assert.Equal(StatusAutenticacao.Validacao, resultado.Status);
        Assert.Equal(["fraca"], resultado.Erros["senha"]);
        Assert.Empty(_cenario.Repositorio.Tokens);
    }
}
