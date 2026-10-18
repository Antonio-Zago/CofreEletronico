using CofreEletronico.Application.Autenticacao.Portas;

namespace CofreEletronico.Application.Autenticacao;

/// <summary>
/// POST /auth/logout: revoga o refresh token informado. Idempotente: token inexistente ou já revogado
/// não é erro. O access token continua válido até expirar (contrato).
/// </summary>
public sealed class EncerrarSessao
{
    private readonly IGeradorRefreshToken _geradorRefresh;
    private readonly IRefreshTokenRepositorio _repositorio;
    private readonly TimeProvider _relogio;

    public EncerrarSessao(IGeradorRefreshToken geradorRefresh, IRefreshTokenRepositorio repositorio, TimeProvider relogio)
    {
        _geradorRefresh = geradorRefresh;
        _repositorio = repositorio;
        _relogio = relogio;
    }

    public async Task ExecutarAsync(LogoutComando comando, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(comando.RefreshToken))
            return;

        var token = await _repositorio.ObterPorHashAsync(_geradorRefresh.CalcularHash(comando.RefreshToken), cancellationToken);
        if (token is null || token.RevogadoEm is not null)
            return;

        var agora = _relogio.GetUtcNow();
        token.Revogar(agora);
        await _repositorio.RevogarAsync(token.Id, agora, cancellationToken);
    }
}
