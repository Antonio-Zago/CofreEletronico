using CofreEletronico.Application.Autenticacao;

namespace CofreEletronico.Application.Tests.Autenticacao;

public class RenovarTokensTests
{
    private readonly AutenticacaoCenario _cenario = new();

    private Task<ResultadoAutenticacao> Renovar(string refreshToken) =>
        _cenario.Refresh.ExecutarAsync(new RenovarTokensComando(refreshToken));

    [Fact]
    public async Task Refresh_valido_rotaciona_o_token()
    {
        var sessao = await _cenario.LogarAsync();
        _cenario.Relogio.Avancar(TimeSpan.FromDays(1));

        var resultado = await Renovar(sessao.RefreshToken);

        Assert.Equal(StatusAutenticacao.Sucesso, resultado.Status);
        var novos = resultado.Tokens!;
        Assert.NotEqual(sessao.RefreshToken, novos.RefreshToken);
        Assert.Equal(_cenario.Relogio.Agora.AddDays(30), novos.RefreshTokenExpiraEm);

        var antigo = _cenario.Repositorio.PorHash(_cenario.RefreshTokens.CalcularHash(sessao.RefreshToken))!;
        var novo = _cenario.Repositorio.PorHash(_cenario.RefreshTokens.CalcularHash(novos.RefreshToken))!;
        Assert.Equal(_cenario.Relogio.Agora, antigo.RevogadoEm);
        Assert.Equal(novo.Id, antigo.SubstituidoPorId);
        Assert.Equal(antigo.FamiliaId, novo.FamiliaId);
        Assert.True(novo.EstaAtivo(_cenario.Relogio.Agora));
    }

    [Fact]
    public async Task Token_novo_tambem_pode_ser_renovado()
    {
        var sessao = await _cenario.LogarAsync();
        var segundo = (await Renovar(sessao.RefreshToken)).Tokens!;

        var terceiro = await Renovar(segundo.RefreshToken);

        Assert.Equal(StatusAutenticacao.Sucesso, terceiro.Status);
    }

    [Fact]
    public async Task Token_expirado_e_rejeitado_sem_revogar_a_familia()
    {
        var sessao = await _cenario.LogarAsync();
        _cenario.Relogio.Avancar(TimeSpan.FromDays(30));

        var resultado = await Renovar(sessao.RefreshToken);

        Assert.Equal(StatusAutenticacao.TokenInvalido, resultado.Status);
        Assert.Null(resultado.Tokens);
        Assert.Equal(0, _cenario.Repositorio.FamiliasRevogadas);
        Assert.Single(_cenario.Repositorio.Tokens);
    }

    [Fact]
    public async Task Token_revogado_por_logout_e_rejeitado()
    {
        var sessao = await _cenario.LogarAsync();
        await _cenario.Logout.ExecutarAsync(new LogoutComando(sessao.RefreshToken));

        var resultado = await Renovar(sessao.RefreshToken);

        Assert.Equal(StatusAutenticacao.TokenInvalido, resultado.Status);
        Assert.Single(_cenario.Repositorio.Tokens);
    }

    [Theory]
    [InlineData("")]
    [InlineData("nao-existe")]
    public async Task Token_inexistente_e_rejeitado(string valor)
    {
        await _cenario.LogarAsync();

        var resultado = await Renovar(valor);

        Assert.Equal(StatusAutenticacao.TokenInvalido, resultado.Status);
    }

    [Fact]
    public async Task Reuso_de_token_rotacionado_revoga_toda_a_familia()
    {
        var sessao = await _cenario.LogarAsync();
        var outraSessao = (await _cenario.Login.ExecutarAsync(new LoginComando("ana@exemplo.com", "Senha123"))).Tokens!;
        var rotacionado = (await Renovar(sessao.RefreshToken)).Tokens!;

        // O token antigo (já rotacionado) volta a ser apresentado: sinal de roubo.
        var reuso = await Renovar(sessao.RefreshToken);

        Assert.Equal(StatusAutenticacao.TokenInvalido, reuso.Status);
        Assert.Equal(1, _cenario.Repositorio.FamiliasRevogadas);

        // O token legítimo mais recente da família também morreu...
        Assert.Equal(StatusAutenticacao.TokenInvalido, (await Renovar(rotacionado.RefreshToken)).Status);

        // ...mas a sessão de outro login (outra família) segue válida.
        Assert.Equal(StatusAutenticacao.Sucesso, (await Renovar(outraSessao.RefreshToken)).Status);
    }

    [Fact]
    public async Task Corrida_perdida_na_rotacao_revoga_a_familia()
    {
        var sessao = await _cenario.LogarAsync();
        _cenario.Repositorio.PerderProximaCorrida = true;

        var resultado = await Renovar(sessao.RefreshToken);

        Assert.Equal(StatusAutenticacao.TokenInvalido, resultado.Status);
        Assert.Equal(1, _cenario.Repositorio.FamiliasRevogadas);
        var token = Assert.Single(_cenario.Repositorio.Tokens);
        Assert.NotNull(token.RevogadoEm);
    }

    [Fact]
    public async Task Rotacao_perto_do_limite_da_sessao_trunca_a_expiracao()
    {
        var sessao = await _cenario.LogarAsync();
        var fimSessao = AutenticacaoCenario.Inicio.AddDays(90);

        // Renova a cada 29 dias: dias 29, 58 e 87.
        var atual = sessao.RefreshToken;
        TokensDto? ultimo = null;
        for (var i = 0; i < 3; i++)
        {
            _cenario.Relogio.Avancar(TimeSpan.FromDays(29));
            ultimo = (await Renovar(atual)).Tokens!;
            atual = ultimo.RefreshToken;
        }

        // No dia 87, agora + 30 dias passaria do limite: a expiração é a do fim da sessão.
        Assert.Equal(fimSessao, ultimo!.RefreshTokenExpiraEm);
        var token = _cenario.Repositorio.PorHash(_cenario.RefreshTokens.CalcularHash(atual))!;
        Assert.Equal(fimSessao, token.ExpiraEm);
        Assert.Equal(fimSessao, token.SessaoExpiraEm);
        Assert.All(_cenario.Repositorio.Tokens, t => Assert.Equal(fimSessao, t.SessaoExpiraEm));
    }

    [Fact]
    public async Task Depois_do_limite_da_sessao_refresh_e_rejeitado_sem_revogar_a_familia()
    {
        var sessao = await _cenario.LogarAsync();
        var atual = sessao.RefreshToken;
        for (var i = 0; i < 3; i++)
        {
            _cenario.Relogio.Avancar(TimeSpan.FromDays(29));
            atual = (await Renovar(atual)).Tokens!.RefreshToken;
        }

        _cenario.Relogio.Agora = AutenticacaoCenario.Inicio.AddDays(90);
        var resultado = await Renovar(atual);

        // Expiração, não reuso: 401 e nenhuma revogação em cadeia.
        Assert.Equal(StatusAutenticacao.TokenInvalido, resultado.Status);
        Assert.Equal(0, _cenario.Repositorio.FamiliasRevogadas);
        Assert.Null(_cenario.Repositorio.PorHash(_cenario.RefreshTokens.CalcularHash(atual))!.RevogadoEm);
    }

    [Fact]
    public async Task Usuario_removido_nao_renova()
    {
        var sessao = await _cenario.LogarAsync();
        var usuarioId = _cenario.Repositorio.Tokens.Single().UsuarioId;
        _cenario.Identidade.Remover(usuarioId);

        var resultado = await Renovar(sessao.RefreshToken);

        Assert.Equal(StatusAutenticacao.TokenInvalido, resultado.Status);
    }
}
