using CofreEletronico.Application.Estantes;
using CofreEletronico.Application.Estantes.Portas;
using CofreEletronico.Domain.Estantes;

namespace CofreEletronico.Application.Tests.Estantes;

/// <summary>Estantes e cofres em memória. A entidade devolvida para alteração é a própria guardada (como o rastreamento do EF).</summary>
internal sealed class EstanteRepositorioFalso : IEstanteRepositorio
{
    public List<Estante> Estantes { get; } = [];

    public List<Cofre> Cofres { get; } = [];

    /// <summary>device_ids que a consulta prévia enxerga como ocupados (além dos das estantes guardadas).</summary>
    public HashSet<string> DeviceIdsOcupados { get; } = [];

    /// <summary>Quantas próximas inserções o "índice único" recusa (corrida que a consulta prévia não viu).</summary>
    public int RecusasDoIndice { get; set; }

    public List<string> DeviceIdsConsultados { get; } = [];

    public List<Estante> TentativasDeAdicao { get; } = [];

    public int Salvamentos { get; private set; }

    /// <summary>Simula falha de banco ao inserir.</summary>
    public Exception? ExcecaoNoAdicionar { get; set; }

    public Task<ResultadoAdicaoEstante> AdicionarAsync(Estante estante, CancellationToken cancellationToken)
    {
        TentativasDeAdicao.Add(estante);
        if (ExcecaoNoAdicionar is not null)
            throw ExcecaoNoAdicionar;
        if (RecusasDoIndice > 0)
        {
            RecusasDoIndice--;
            return Task.FromResult(ResultadoAdicaoEstante.DeviceIdEmUso);
        }

        if (Estantes.Any(e => e.DeviceId == estante.DeviceId))
            return Task.FromResult(ResultadoAdicaoEstante.DeviceIdEmUso);

        Estantes.Add(estante);
        return Task.FromResult(ResultadoAdicaoEstante.Adicionada);
    }

    public Task<Estante?> ObterParaAlteracaoAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Estantes.SingleOrDefault(e => e.Id == id));

    public Task<EstanteDto?> ObterAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Estantes.Where(e => e.Id == id).Select(EstanteDto.De).SingleOrDefault());

    public Task<PaginaDto<EstanteDto>> ListarAsync(bool? ativa, int pagina, int tamanhoPagina, CancellationToken cancellationToken)
    {
        var filtradas = Estantes.Where(e => ativa is null || e.Ativa == ativa).ToList();
        var itens = filtradas
            .OrderBy(e => e.Nome, StringComparer.Ordinal).ThenBy(e => e.Id)
            .Skip((pagina - 1) * tamanhoPagina).Take(tamanhoPagina)
            .Select(EstanteDto.De).ToList();
        return Task.FromResult(new PaginaDto<EstanteDto>(itens, pagina, tamanhoPagina, filtradas.Count));
    }

    public Task<bool> ExisteDeviceIdAsync(string deviceId, CancellationToken cancellationToken)
    {
        DeviceIdsConsultados.Add(deviceId);
        return Task.FromResult(DeviceIdsOcupados.Contains(deviceId) || Estantes.Any(e => e.DeviceId == deviceId));
    }

    public Task<bool> PossuiCofreEmUsoAsync(Guid estanteId, CancellationToken cancellationToken) =>
        Task.FromResult(Cofres.Any(c =>
            c.EstanteId == estanteId && c.Status is StatusCofre.Reservado or StatusCofre.Ocupado));

    /// <summary>Simula o token de concorrência recusando a próxima gravação.</summary>
    public bool ConflitoNoSalvar { get; set; }

    /// <summary>Simula falha de banco ao salvar.</summary>
    public Exception? ExcecaoNoSalvar { get; set; }

    public Task<ResultadoSalvamento> SalvarAsync(CancellationToken cancellationToken)
    {
        if (ExcecaoNoSalvar is not null)
            throw ExcecaoNoSalvar;
        if (ConflitoNoSalvar)
            return Task.FromResult(ResultadoSalvamento.ConflitoConcorrencia);

        Salvamentos++;
        return Task.FromResult(ResultadoSalvamento.Salvo);
    }
}

/// <summary>Gerador determinístico: device_ids da fila (ou sequenciais), segredos e senhas numerados.</summary>
internal sealed class GeradorCredenciaisFalso : IGeradorCredenciaisEstante
{
    private int _deviceIds;
    private int _segredos;
    private int _senhas;

    public Queue<string> ProximosDeviceIds { get; } = new();

    /// <summary>Cópias de todos os segredos entregues, na ordem.</summary>
    public List<byte[]> SegredosGerados { get; } = [];

    public List<string> SenhasGeradas { get; } = [];

    /// <summary>Os próprios arrays entregues (não cópias), para conferir que o caso de uso os zera.</summary>
    public List<byte[]> ArraysEntregues { get; } = [];

    public string GerarDeviceId() =>
        ProximosDeviceIds.Count > 0 ? ProximosDeviceIds.Dequeue() : $"est-{++_deviceIds:D6}";

    public byte[] GerarSegredoHmac()
    {
        _segredos++;
        var segredo = Enumerable.Range(0, 32).Select(i => (byte)(i + _segredos)).ToArray();
        SegredosGerados.Add((byte[])segredo.Clone());
        ArraysEntregues.Add(segredo);
        return segredo; // array novo a cada chamada: o caso de uso zera o que recebe
    }

    public string GerarSenhaMqtt()
    {
        var senha = $"Senha{++_senhas}".PadRight(24, 'x');
        SenhasGeradas.Add(senha);
        return senha;
    }
}
