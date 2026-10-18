using CofreEletronico.Application.Autenticacao;

namespace CofreEletronico.Application.Tests.Autenticacao;

public class AutenticarUsuarioTests
{
    private readonly AutenticacaoCenario _cenario = new();

    [Fact]
    public async Task Login_valido_devolve_tokens_e_abre_nova_familia()
    {
        var usuario = _cenario.Identidade.Adicionar("ana@exemplo.com", "Senha123", "Usuario", "Admin");

        var primeiro = await _cenario.Login.ExecutarAsync(new LoginComando(" ana@exemplo.com ", "Senha123"));
        var segundo = await _cenario.Login.ExecutarAsync(new LoginComando("ana@exemplo.com", "Senha123"));

        Assert.Equal(StatusAutenticacao.Sucesso, primeiro.Status);
        Assert.Equal(StatusAutenticacao.Sucesso, segundo.Status);
        Assert.NotEqual(primeiro.Tokens!.RefreshToken, segundo.Tokens!.RefreshToken);

        // Cada login é uma sessão independente.
        var familias = _cenario.Repositorio.Tokens.Select(t => t.FamiliaId).Distinct();
        Assert.Equal(2, familias.Count());
        Assert.All(_cenario.AccessTokens.Emitidos, u => Assert.Equal(usuario, u));

        // Cada login abre uma sessão de 90 dias.
        Assert.All(_cenario.Repositorio.Tokens, t => Assert.Equal(AutenticacaoCenario.Inicio.AddDays(90), t.SessaoExpiraEm));
    }

    [Fact]
    public async Task Senha_correta_com_conta_bloqueada_durante_a_verificacao_da_o_mesmo_401_sem_tokens()
    {
        var usuario = _cenario.Identidade.Adicionar("ana@exemplo.com", "Senha123", "Usuario");
        _cenario.Identidade.BloqueadosDuranteVerificacao.Add(usuario.Id);

        var corrida = await _cenario.Login.ExecutarAsync(new LoginComando("ana@exemplo.com", "Senha123"));
        var senhaErrada = await _cenario.Login.ExecutarAsync(new LoginComando("ana@exemplo.com", "Errada123"));

        Assert.Equal(1, _cenario.Identidade.SenhasCorretasRecusadas);
        Assert.Equal(StatusAutenticacao.CredenciaisInvalidas, corrida.Status);
        Assert.Null(corrida.Tokens);
        Assert.Empty(corrida.Erros);
        Assert.Equal(senhaErrada.Status, corrida.Status);

        // Nenhum token emitido: nem refresh persistido, nem access token gerado.
        Assert.Empty(_cenario.Repositorio.Tokens);
        Assert.Empty(_cenario.AccessTokens.Emitidos);
    }

    [Theory]
    [InlineData("ana@exemplo.com", "SenhaErrada1", false)] // senha errada
    [InlineData("naoexiste@exemplo.com", "Senha123", false)] // e-mail inexistente
    [InlineData("ana@exemplo.com", "Senha123", true)] // conta bloqueada, mesmo com a senha certa
    public async Task Falhas_de_login_tem_o_mesmo_resultado(string email, string senha, bool bloquear)
    {
        var usuario = _cenario.Identidade.Adicionar("ana@exemplo.com", "Senha123", "Usuario");
        if (bloquear)
            _cenario.Identidade.Bloqueados.Add(usuario.Id);

        var resultado = await _cenario.Login.ExecutarAsync(new LoginComando(email, senha));

        Assert.Equal(StatusAutenticacao.CredenciaisInvalidas, resultado.Status);
        Assert.Null(resultado.Tokens);
        Assert.Empty(resultado.Erros);
        Assert.Empty(_cenario.Repositorio.Tokens);
    }
}
