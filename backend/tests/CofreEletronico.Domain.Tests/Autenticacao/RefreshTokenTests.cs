using CofreEletronico.Domain.Autenticacao;

namespace CofreEletronico.Domain.Tests.Autenticacao;

public class RefreshTokenTests
{
    private static readonly DateTimeOffset Agora = new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan ValidadeToken = TimeSpan.FromDays(30);
    private static readonly TimeSpan ValidadeSessao = TimeSpan.FromDays(90);
    private static readonly DateTimeOffset Expira = Agora + ValidadeToken;
    private static readonly DateTimeOffset FimSessao = Agora + ValidadeSessao;
    private static readonly Guid UsuarioId = Guid.NewGuid();

    private static RefreshToken Emitir() => RefreshToken.Emitir(UsuarioId, "hash-1", Agora, ValidadeToken, ValidadeSessao);

    [Fact]
    public void Emitir_cria_token_ativo_em_nova_familia_com_limite_de_sessao()
    {
        var token = Emitir();

        Assert.NotEqual(Guid.Empty, token.Id);
        Assert.NotEqual(Guid.Empty, token.FamiliaId);
        Assert.Equal(UsuarioId, token.UsuarioId);
        Assert.Equal("hash-1", token.TokenHash);
        Assert.Equal(Agora, token.CriadoEm);
        Assert.Equal(Expira, token.ExpiraEm);
        Assert.Equal(FimSessao, token.SessaoExpiraEm);
        Assert.Null(token.RevogadoEm);
        Assert.Null(token.SubstituidoPorId);
        Assert.False(token.FoiRotacionado);
        Assert.True(token.EstaAtivo(Agora));
    }

    [Fact]
    public void Emitir_com_sessao_mais_curta_que_o_token_trunca_a_expiracao()
    {
        var token = RefreshToken.Emitir(UsuarioId, "hash-1", Agora, ValidadeToken, TimeSpan.FromDays(10));

        Assert.Equal(Agora.AddDays(10), token.ExpiraEm);
        Assert.Equal(Agora.AddDays(10), token.SessaoExpiraEm);
    }

    [Fact]
    public void Cada_emissao_abre_familia_diferente()
    {
        Assert.NotEqual(Emitir().FamiliaId, Emitir().FamiliaId);
    }

    [Fact]
    public void Token_expira_no_instante_de_expiracao()
    {
        var token = Emitir();

        Assert.True(token.EstaAtivo(Expira.AddTicks(-1)));
        Assert.False(token.EstaAtivo(Expira));
    }

    [Fact]
    public void Revogar_desativa_e_e_idempotente()
    {
        var token = Emitir();

        token.Revogar(Agora.AddMinutes(1));
        token.Revogar(Agora.AddMinutes(5));

        Assert.False(token.EstaAtivo(Agora.AddMinutes(2)));
        Assert.Equal(Agora.AddMinutes(1), token.RevogadoEm);
        Assert.False(token.FoiRotacionado);
    }

    [Fact]
    public void Rotacionar_revoga_o_atual_e_cria_o_proximo_na_mesma_familia_e_sessao()
    {
        var atual = Emitir();
        var momento = Agora.AddDays(1);

        var novo = atual.Rotacionar("hash-2", momento, ValidadeToken);

        Assert.Equal(momento, atual.RevogadoEm);
        Assert.Equal(novo.Id, atual.SubstituidoPorId);
        Assert.True(atual.FoiRotacionado);
        Assert.False(atual.EstaAtivo(momento));

        Assert.NotEqual(atual.Id, novo.Id);
        Assert.Equal(atual.FamiliaId, novo.FamiliaId);
        Assert.Equal(UsuarioId, novo.UsuarioId);
        Assert.Equal("hash-2", novo.TokenHash);
        Assert.Equal(momento, novo.CriadoEm);
        Assert.Equal(momento + ValidadeToken, novo.ExpiraEm);
        Assert.Equal(FimSessao, novo.SessaoExpiraEm); // copiado, não renovado
        Assert.True(novo.EstaAtivo(momento));
    }

    [Fact]
    public void Rotacao_perto_do_limite_trunca_a_expiracao_no_fim_da_sessao()
    {
        // Cadeia de rotações a cada 29 dias até perto do limite de 90 dias.
        var token = Emitir();
        var momento = Agora;
        for (var i = 0; i < 3; i++)
        {
            momento = momento.AddDays(29);
            token = token.Rotacionar($"hash-{i + 2}", momento, ValidadeToken);
        }

        // Dia 87: agora + 30 dias passaria do dia 90, então expira_em = sessao_expira_em.
        Assert.Equal(Agora.AddDays(87), momento);
        Assert.Equal(FimSessao, token.ExpiraEm);
        Assert.Equal(FimSessao, token.SessaoExpiraEm);
        Assert.True(token.EstaAtivo(FimSessao.AddTicks(-1)));
        Assert.False(token.EstaAtivo(FimSessao));
    }

    [Fact]
    public void Depois_do_limite_da_sessao_nao_rotaciona()
    {
        var token = Emitir();
        var ultimo = token.Rotacionar("hash-2", Agora.AddDays(29), ValidadeToken)
            .Rotacionar("hash-3", Agora.AddDays(58), ValidadeToken)
            .Rotacionar("hash-4", Agora.AddDays(87), ValidadeToken);

        Assert.Throws<InvalidOperationException>(() => ultimo.Rotacionar("hash-5", FimSessao, ValidadeToken));
    }

    [Fact]
    public void Nao_rotaciona_token_revogado()
    {
        var token = Emitir();
        token.Revogar(Agora);

        Assert.Throws<InvalidOperationException>(() => token.Rotacionar("hash-2", Agora.AddMinutes(1), ValidadeToken));
    }

    [Fact]
    public void Nao_rotaciona_token_expirado()
    {
        var token = Emitir();

        Assert.Throws<InvalidOperationException>(() => token.Rotacionar("hash-2", Expira, ValidadeToken));
    }

    [Fact]
    public void Nao_rotaciona_duas_vezes()
    {
        var token = Emitir();
        token.Rotacionar("hash-2", Agora, ValidadeToken);

        Assert.Throws<InvalidOperationException>(() => token.Rotacionar("hash-3", Agora, ValidadeToken));
    }

    [Fact]
    public void Emitir_valida_os_dados()
    {
        Assert.Throws<ArgumentException>(() => RefreshToken.Emitir(Guid.Empty, "hash", Agora, ValidadeToken, ValidadeSessao));
        Assert.Throws<ArgumentException>(() => RefreshToken.Emitir(UsuarioId, " ", Agora, ValidadeToken, ValidadeSessao));
        Assert.Throws<ArgumentException>(() => RefreshToken.Emitir(UsuarioId, "hash", Agora, TimeSpan.Zero, ValidadeSessao));
        Assert.Throws<ArgumentException>(() => RefreshToken.Emitir(UsuarioId, "hash", Agora, ValidadeToken, TimeSpan.Zero));
    }
}
