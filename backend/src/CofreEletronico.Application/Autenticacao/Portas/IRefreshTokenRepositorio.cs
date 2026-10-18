using CofreEletronico.Domain.Autenticacao;

namespace CofreEletronico.Application.Autenticacao.Portas;

/// <summary>Persistência dos refresh tokens. As revogações são escritas condicionais (seguras contra corrida).</summary>
public interface IRefreshTokenRepositorio
{
    Task AdicionarAsync(RefreshToken token, CancellationToken cancellationToken);

    /// <summary>Busca pelo hash. O objeto devolvido não é rastreado: alterá-lo não persiste nada.</summary>
    Task<RefreshToken?> ObterPorHashAsync(string tokenHash, CancellationToken cancellationToken);

    /// <summary>
    /// Persiste a rotação de forma atômica: grava <paramref name="novo"/> e revoga <paramref name="atual"/>
    /// apenas se ele ainda estiver ativo no banco. Devolve <c>false</c> (sem gravar nada) se outra
    /// requisição já tiver rotacionado ou revogado o token.
    /// </summary>
    Task<bool> RotacionarAsync(RefreshToken atual, RefreshToken novo, CancellationToken cancellationToken);

    /// <summary>Revoga o token se ainda não estiver revogado. Idempotente.</summary>
    Task RevogarAsync(Guid tokenId, DateTimeOffset agora, CancellationToken cancellationToken);

    /// <summary>Revoga todos os tokens ainda não revogados da família.</summary>
    Task RevogarFamiliaAsync(Guid familiaId, DateTimeOffset agora, CancellationToken cancellationToken);
}
