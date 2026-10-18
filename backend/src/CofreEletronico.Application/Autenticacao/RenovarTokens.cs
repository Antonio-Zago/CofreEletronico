using CofreEletronico.Application.Autenticacao.Portas;
using Microsoft.Extensions.Logging;

namespace CofreEletronico.Application.Autenticacao;

/// <summary>
/// POST /auth/refresh: troca um refresh token ativo por um novo par de tokens (rotação).
/// Apresentar um token já rotacionado é tratado como roubo: a família inteira é revogada.
/// </summary>
public sealed class RenovarTokens
{
    private readonly IServicoIdentidade _identidade;
    private readonly IGeradorRefreshToken _geradorRefresh;
    private readonly IRefreshTokenRepositorio _repositorio;
    private readonly EmissorTokens _emissor;
    private readonly TimeProvider _relogio;
    private readonly ILogger<RenovarTokens> _logger;

    public RenovarTokens(
        IServicoIdentidade identidade,
        IGeradorRefreshToken geradorRefresh,
        IRefreshTokenRepositorio repositorio,
        EmissorTokens emissor,
        TimeProvider relogio,
        ILogger<RenovarTokens> logger)
    {
        _identidade = identidade;
        _geradorRefresh = geradorRefresh;
        _repositorio = repositorio;
        _emissor = emissor;
        _relogio = relogio;
        _logger = logger;
    }

    public async Task<ResultadoAutenticacao> ExecutarAsync(RenovarTokensComando comando, CancellationToken cancellationToken = default)
    {
        var invalido = ResultadoAutenticacao.Falha(StatusAutenticacao.TokenInvalido);

        if (string.IsNullOrWhiteSpace(comando.RefreshToken))
            return invalido;

        var atual = await _repositorio.ObterPorHashAsync(_geradorRefresh.CalcularHash(comando.RefreshToken), cancellationToken);
        if (atual is null)
            return invalido;

        var agora = _relogio.GetUtcNow();

        if (atual.FoiRotacionado)
        {
            await RevogarFamiliaPorReusoAsync(atual.FamiliaId, atual.UsuarioId, agora, cancellationToken);
            return invalido;
        }

        if (!atual.EstaAtivo(agora))
            return invalido;

        var usuario = await _identidade.ObterAsync(atual.UsuarioId, cancellationToken);
        if (usuario is null)
            return invalido;

        var (novo, valor) = _emissor.PrepararRotacao(atual, agora);

        // Escrita condicional: se outra requisição rotacionou o mesmo token ao mesmo tempo, esta perde.
        // Dois usos simultâneos do mesmo token são indistinguíveis de roubo, então também revogam a família.
        if (!await _repositorio.RotacionarAsync(atual, novo, cancellationToken))
        {
            await RevogarFamiliaPorReusoAsync(atual.FamiliaId, atual.UsuarioId, agora, cancellationToken);
            return invalido;
        }

        return ResultadoAutenticacao.Sucesso(_emissor.Montar(usuario, valor, novo));
    }

    private async Task RevogarFamiliaPorReusoAsync(Guid familiaId, Guid usuarioId, DateTimeOffset agora, CancellationToken cancellationToken)
    {
        _logger.LogWarning(
            "Reuso de refresh token detectado: família {FamiliaId} do usuário {UsuarioId} revogada.", familiaId, usuarioId);
        await _repositorio.RevogarFamiliaAsync(familiaId, agora, cancellationToken);
    }
}
