using CofreEletronico.Application.Cofres.Portas;

namespace CofreEletronico.Application.Cofres;

/// <summary>GET /admin/estantes/{estanteId}/cofres: todos os cofres da estante, ordenados por número, sem paginação.</summary>
public sealed class ListarCofres
{
    private readonly ICofreRepositorio _repositorio;

    public ListarCofres(ICofreRepositorio repositorio) => _repositorio = repositorio;

    public async Task<ResultadoCofre<IReadOnlyList<CofreDto>>> ExecutarAsync(Guid estanteId, CancellationToken cancellationToken = default)
    {
        if (!await _repositorio.ExisteEstanteAsync(estanteId, cancellationToken))
            return ResultadoCofre<IReadOnlyList<CofreDto>>.Falha(StatusResultadoCofre.EstanteNaoEncontrada);

        var cofres = await _repositorio.ListarPorEstanteAsync(estanteId, cancellationToken);
        return ResultadoCofre<IReadOnlyList<CofreDto>>.Sucesso(cofres);
    }
}

/// <summary>GET /admin/estantes/{estanteId}/cofres/{cofreId}. Cofre de outra estante é tratado como inexistente.</summary>
public sealed class ObterCofre
{
    private readonly ICofreRepositorio _repositorio;

    public ObterCofre(ICofreRepositorio repositorio) => _repositorio = repositorio;

    public async Task<ResultadoCofre<CofreDto>> ExecutarAsync(Guid estanteId, Guid cofreId, CancellationToken cancellationToken = default)
    {
        var cofre = await _repositorio.ObterAsync(estanteId, cofreId, cancellationToken);
        return cofre is null
            ? ResultadoCofre<CofreDto>.Falha(StatusResultadoCofre.CofreNaoEncontrado)
            : ResultadoCofre<CofreDto>.Sucesso(cofre);
    }
}
