using CofreEletronico.Application.Autenticacao;

namespace CofreEletronico.Application.Tests.Autenticacao;

public class EncerrarSessaoTests
{
    private readonly AutenticacaoCenario _cenario = new();

    [Fact]
    public async Task Logout_revoga_o_refresh_token()
    {
        var sessao = await _cenario.LogarAsync();

        await _cenario.Logout.ExecutarAsync(new LogoutComando(sessao.RefreshToken));

        var token = Assert.Single(_cenario.Repositorio.Tokens);
        Assert.Equal(AutenticacaoCenario.Inicio, token.RevogadoEm);
        Assert.False(token.FoiRotacionado);
    }

    [Fact]
    public async Task Logout_repetido_e_idempotente_e_mantem_o_instante_original()
    {
        var sessao = await _cenario.LogarAsync();
        await _cenario.Logout.ExecutarAsync(new LogoutComando(sessao.RefreshToken));
        _cenario.Relogio.Avancar(TimeSpan.FromHours(1));

        await _cenario.Logout.ExecutarAsync(new LogoutComando(sessao.RefreshToken));

        Assert.Equal(AutenticacaoCenario.Inicio, _cenario.Repositorio.Tokens.Single().RevogadoEm);
    }

    [Theory]
    [InlineData("")]
    [InlineData("nao-existe")]
    public async Task Logout_de_token_inexistente_nao_falha(string valor)
    {
        await _cenario.Logout.ExecutarAsync(new LogoutComando(valor));

        Assert.Empty(_cenario.Repositorio.Tokens);
    }
}
