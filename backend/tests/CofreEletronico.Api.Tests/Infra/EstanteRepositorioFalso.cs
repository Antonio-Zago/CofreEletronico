using CofreEletronico.Application.Estantes;
using CofreEletronico.Application.Estantes.Portas;
using CofreEletronico.Domain.Estantes;

namespace CofreEletronico.Api.Tests.Infra;

/// <summary>
/// Estantes em memória no lugar do repositório EF. A entidade devolvida para alteração é a própria guardada
/// (como o rastreamento do EF), então <see cref="SalvarAsync"/> não precisa fazer nada.
/// </summary>
internal sealed class EstanteRepositorioFalso : IEstanteRepositorio
{
    private readonly object _trava = new();
    private readonly List<Estante> _estantes = [];
    private readonly HashSet<Guid> _comCofreEmUso = [];

    /// <summary>Quando verdadeiro, toda inserção é recusada como device_id repetido (para o caminho de 500).</summary>
    public bool RecusarTodasAsInsercoes { get; set; }

    public void Limpar()
    {
        lock (_trava)
        {
            _estantes.Clear();
            _comCofreEmUso.Clear();
            RecusarTodasAsInsercoes = false;
            ConflitoNoSalvar = false;
        }
    }

    public Estante? Obter(Guid id)
    {
        lock (_trava)
            return _estantes.SingleOrDefault(e => e.Id == id);
    }

    public Estante Adicionar(string nome, bool ativa = true)
    {
        lock (_trava)
        {
            var estante = Estante.Criar(nome, "Rua A, 100", -23.55, -46.63, $"est-{_estantes.Count:D6}",
                Enumerable.Repeat((byte)9, 32).ToArray(), DateTimeOffset.UtcNow);
            if (!ativa)
                estante.Desativar();
            _estantes.Add(estante);
            return estante;
        }
    }

    /// <summary>Simula um cofre reservado ou ocupado na estante.</summary>
    public void MarcarCofreEmUso(Guid estanteId)
    {
        lock (_trava)
            _comCofreEmUso.Add(estanteId);
    }

    public Task<ResultadoAdicaoEstante> AdicionarAsync(Estante estante, CancellationToken cancellationToken)
    {
        lock (_trava)
        {
            if (RecusarTodasAsInsercoes || _estantes.Any(e => e.DeviceId == estante.DeviceId))
                return Task.FromResult(ResultadoAdicaoEstante.DeviceIdEmUso);

            _estantes.Add(estante);
            return Task.FromResult(ResultadoAdicaoEstante.Adicionada);
        }
    }

    public Task<Estante?> ObterParaAlteracaoAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Obter(id));

    public Task<EstanteDto?> ObterAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Obter(id) is { } e ? EstanteDto.De(e) : null);

    public Task<PaginaDto<EstanteDto>> ListarAsync(bool? ativa, int pagina, int tamanhoPagina, CancellationToken cancellationToken)
    {
        lock (_trava)
        {
            var filtradas = _estantes.Where(e => ativa is null || e.Ativa == ativa).ToList();
            var itens = filtradas
                .OrderBy(e => e.Nome, StringComparer.Ordinal).ThenBy(e => e.Id)
                .Skip((pagina - 1) * tamanhoPagina).Take(tamanhoPagina)
                .Select(EstanteDto.De).ToList();
            return Task.FromResult(new PaginaDto<EstanteDto>(itens, pagina, tamanhoPagina, filtradas.Count));
        }
    }

    public Task<bool> ExisteDeviceIdAsync(string deviceId, CancellationToken cancellationToken)
    {
        lock (_trava)
            return Task.FromResult(_estantes.Any(e => e.DeviceId == deviceId));
    }

    public Task<bool> PossuiCofreEmUsoAsync(Guid estanteId, CancellationToken cancellationToken)
    {
        lock (_trava)
            return Task.FromResult(_comCofreEmUso.Contains(estanteId));
    }

    /// <summary>Simula o token de concorrência (xmin) recusando as gravações.</summary>
    public bool ConflitoNoSalvar { get; set; }

    public Task<ResultadoSalvamento> SalvarAsync(CancellationToken cancellationToken) =>
        Task.FromResult(ConflitoNoSalvar ? ResultadoSalvamento.ConflitoConcorrencia : ResultadoSalvamento.Salvo);
}
