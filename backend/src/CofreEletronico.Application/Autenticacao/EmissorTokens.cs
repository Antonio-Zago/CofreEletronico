using CofreEletronico.Application.Autenticacao.Portas;
using CofreEletronico.Domain.Autenticacao;

namespace CofreEletronico.Application.Autenticacao;

/// <summary>Monta o par access + refresh token (usado pelos casos de uso de registro, login e refresh).</summary>
public sealed class EmissorTokens(
    IGeradorAccessToken geradorAccessToken,
    IGeradorRefreshToken geradorRefreshToken,
    IRefreshTokenRepositorio repositorio,
    TimeProvider relogio)
{
    /// <summary>Emite tokens para uma nova sessão (nova família de refresh tokens) e persiste o refresh.</summary>
    public async Task<TokensDto> EmitirNovaSessaoAsync(UsuarioAutenticado usuario, CancellationToken cancellationToken)
    {
        var agora = relogio.GetUtcNow();
        var refresh = geradorRefreshToken.Gerar();
        var entidade = RefreshToken.Emitir(
            usuario.Id, refresh.Hash, agora, geradorRefreshToken.Validade, geradorRefreshToken.ValidadeSessao);

        await repositorio.AdicionarAsync(entidade, cancellationToken);

        return Montar(usuario, refresh.Valor, entidade);
    }

    /// <summary>
    /// Gera o próximo refresh token da família. O <paramref name="atual"/> (não rastreado) é marcado
    /// como rotacionado só em memória; quem persiste é <see cref="IRefreshTokenRepositorio.RotacionarAsync"/>.
    /// </summary>
    public (RefreshToken Novo, string Valor) PrepararRotacao(RefreshToken atual, DateTimeOffset agora)
    {
        var refresh = geradorRefreshToken.Gerar();
        var novo = atual.Rotacionar(refresh.Hash, agora, geradorRefreshToken.Validade);
        return (novo, refresh.Valor);
    }

    public TokensDto Montar(UsuarioAutenticado usuario, string refreshValor, RefreshToken refresh)
    {
        var access = geradorAccessToken.Gerar(usuario);
        return new TokensDto(access.Token, access.ExpiraEm, refreshValor, refresh.ExpiraEm);
    }
}
