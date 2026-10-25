using CofreEletronico.Application.Cofres;
using CofreEletronico.Application.Cofres.Portas;
using CofreEletronico.Domain.Estantes;

namespace CofreEletronico.Api.Tests.Infra;

/// <summary>
/// Cofres em memória no lugar do repositório EF. As estantes vêm do <see cref="EstanteRepositorioFalso"/> da mesma
/// fábrica. A entidade devolvida para alteração é a própria guardada (como o rastreamento do EF).
/// </summary>
internal sealed class CofreRepositorioFalso(EstanteRepositorioFalso estantes) : ICofreRepositorio
{
    private readonly object _trava = new();
    private readonly List<Cofre> _cofres = [];

    /// <summary>Simula o token de concorrência (xmin) recusando as gravações.</summary>
    public bool ConflitoNoSalvar { get; set; }

    /// <summary>Simula o índice único recusando a próxima gravação (corrida que a consulta prévia não viu).</summary>
    public ResultadoGravacaoCofre? RecusaNaProximaGravacao { get; set; }

    public void Limpar()
    {
        lock (_trava)
        {
            _cofres.Clear();
            ConflitoNoSalvar = false;
            RecusaNaProximaGravacao = null;
        }
    }

    public Cofre? Obter(Guid id)
    {
        lock (_trava)
            return _cofres.SingleOrDefault(c => c.Id == id);
    }

    public IReadOnlyList<Cofre> Todos()
    {
        lock (_trava)
            return _cofres.ToList();
    }

    /// <summary>Cofre já cadastrado no status pedido, pelas transições válidas.</summary>
    public Cofre Adicionar(Guid estanteId, int numero, int canal, StatusCofre status = StatusCofre.Livre, bool ativo = true)
    {
        var cofre = Cofre.Criar(estanteId, numero, canal, TamanhoCofre.Medio);
        switch (status)
        {
            case StatusCofre.Reservado:
                cofre.Reservar();
                break;
            case StatusCofre.Ocupado:
                cofre.Reservar();
                cofre.Ocupar();
                break;
            case StatusCofre.Manutencao:
                cofre.ColocarEmManutencao();
                break;
        }

        if (!ativo)
            cofre.Desativar();

        lock (_trava)
            _cofres.Add(cofre);
        return cofre;
    }

    public Task<bool> ExisteEstanteAsync(Guid estanteId, CancellationToken cancellationToken) =>
        Task.FromResult(estantes.Obter(estanteId) is not null);

    public Task<bool> ExisteNumeroAsync(Guid estanteId, int numero, Guid? excetoCofreId, CancellationToken cancellationToken)
    {
        lock (_trava)
            return Task.FromResult(_cofres.Any(c => c.EstanteId == estanteId && c.Numero == numero && c.Id != excetoCofreId));
    }

    public Task<bool> ExisteCanalAsync(Guid estanteId, int canalRele, Guid? excetoCofreId, CancellationToken cancellationToken)
    {
        lock (_trava)
            return Task.FromResult(_cofres.Any(c => c.EstanteId == estanteId && c.CanalRele == canalRele && c.Id != excetoCofreId));
    }

    public Task<ResultadoGravacaoCofre> AdicionarAsync(Cofre cofre, CancellationToken cancellationToken)
    {
        lock (_trava)
        {
            if (TomarRecusa() is { } recusa)
                return Task.FromResult(recusa);

            _cofres.Add(cofre);
            return Task.FromResult(ResultadoGravacaoCofre.Gravado);
        }
    }

    public Task<Cofre?> ObterParaAlteracaoAsync(Guid estanteId, Guid cofreId, CancellationToken cancellationToken)
    {
        lock (_trava)
            return Task.FromResult(_cofres.SingleOrDefault(c => c.Id == cofreId && c.EstanteId == estanteId));
    }

    public Task<CofreDto?> ObterAsync(Guid estanteId, Guid cofreId, CancellationToken cancellationToken)
    {
        lock (_trava)
            return Task.FromResult(_cofres.Where(c => c.Id == cofreId && c.EstanteId == estanteId).Select(CofreDto.De).SingleOrDefault());
    }

    public Task<IReadOnlyList<CofreDto>> ListarPorEstanteAsync(Guid estanteId, CancellationToken cancellationToken)
    {
        lock (_trava)
            return Task.FromResult<IReadOnlyList<CofreDto>>(
                _cofres.Where(c => c.EstanteId == estanteId).OrderBy(c => c.Numero).Select(CofreDto.De).ToList());
    }

    public Task<ResultadoGravacaoCofre> SalvarAsync(CancellationToken cancellationToken)
    {
        lock (_trava)
        {
            if (ConflitoNoSalvar)
                return Task.FromResult(ResultadoGravacaoCofre.ConflitoConcorrencia);
            return Task.FromResult(TomarRecusa() ?? ResultadoGravacaoCofre.Gravado);
        }
    }

    private ResultadoGravacaoCofre? TomarRecusa()
    {
        var recusa = RecusaNaProximaGravacao;
        RecusaNaProximaGravacao = null;
        return recusa;
    }
}
