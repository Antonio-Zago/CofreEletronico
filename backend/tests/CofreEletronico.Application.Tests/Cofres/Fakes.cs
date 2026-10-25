using CofreEletronico.Application.Cofres;
using CofreEletronico.Application.Cofres.Portas;
using CofreEletronico.Domain.Estantes;

namespace CofreEletronico.Application.Tests.Cofres;

/// <summary>
/// Cofres em memória. A entidade devolvida para alteração é a própria guardada (como o rastreamento do EF), e a
/// unicidade de número/canal é conferida como os índices únicos fariam.
/// </summary>
internal sealed class CofreRepositorioFalso : ICofreRepositorio
{
    public HashSet<Guid> Estantes { get; } = [];

    public List<Cofre> Cofres { get; } = [];

    /// <summary>Recusa que o "índice único" aplica à próxima gravação (corrida que a consulta prévia não viu).</summary>
    public ResultadoGravacaoCofre? RecusaNaProximaGravacao { get; set; }

    /// <summary>Simula o token de concorrência recusando as gravações.</summary>
    public bool ConflitoNoSalvar { get; set; }

    public int Salvamentos { get; private set; }

    public List<Cofre> TentativasDeAdicao { get; } = [];

    public Task<bool> ExisteEstanteAsync(Guid estanteId, CancellationToken cancellationToken) =>
        Task.FromResult(Estantes.Contains(estanteId));

    public Task<bool> ExisteNumeroAsync(Guid estanteId, int numero, Guid? excetoCofreId, CancellationToken cancellationToken) =>
        Task.FromResult(Cofres.Any(c => c.EstanteId == estanteId && c.Numero == numero && c.Id != excetoCofreId));

    public Task<bool> ExisteCanalAsync(Guid estanteId, int canalRele, Guid? excetoCofreId, CancellationToken cancellationToken) =>
        Task.FromResult(Cofres.Any(c => c.EstanteId == estanteId && c.CanalRele == canalRele && c.Id != excetoCofreId));

    public Task<ResultadoGravacaoCofre> AdicionarAsync(Cofre cofre, CancellationToken cancellationToken)
    {
        TentativasDeAdicao.Add(cofre);
        if (RecusaNaProximaGravacao is { } recusa)
        {
            RecusaNaProximaGravacao = null;
            return Task.FromResult(recusa);
        }

        Cofres.Add(cofre);
        return Task.FromResult(ResultadoGravacaoCofre.Gravado);
    }

    public Task<Cofre?> ObterParaAlteracaoAsync(Guid estanteId, Guid cofreId, CancellationToken cancellationToken) =>
        Task.FromResult(Cofres.SingleOrDefault(c => c.Id == cofreId && c.EstanteId == estanteId));

    public Task<CofreDto?> ObterAsync(Guid estanteId, Guid cofreId, CancellationToken cancellationToken) =>
        Task.FromResult(Cofres.Where(c => c.Id == cofreId && c.EstanteId == estanteId).Select(CofreDto.De).SingleOrDefault());

    public Task<IReadOnlyList<CofreDto>> ListarPorEstanteAsync(Guid estanteId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CofreDto>>(
            Cofres.Where(c => c.EstanteId == estanteId).OrderBy(c => c.Numero).Select(CofreDto.De).ToList());

    public Task<ResultadoGravacaoCofre> SalvarAsync(CancellationToken cancellationToken)
    {
        if (ConflitoNoSalvar)
            return Task.FromResult(ResultadoGravacaoCofre.ConflitoConcorrencia);
        if (RecusaNaProximaGravacao is { } recusa)
        {
            RecusaNaProximaGravacao = null;
            return Task.FromResult(recusa);
        }

        Salvamentos++;
        return Task.FromResult(ResultadoGravacaoCofre.Gravado);
    }
}
