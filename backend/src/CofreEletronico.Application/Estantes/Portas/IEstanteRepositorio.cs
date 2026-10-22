using CofreEletronico.Domain.Estantes;

namespace CofreEletronico.Application.Estantes.Portas;

public enum ResultadoAdicaoEstante
{
    Adicionada,

    /// <summary>
    /// O banco recusou o device_id (índice único <c>ix_estantes_device_id</c>, corrida com outro cadastro).
    /// Nada foi gravado e a entidade não fica presa no contexto: quem chama pode tentar de novo com outro device_id.
    /// </summary>
    DeviceIdEmUso,
}

public enum ResultadoSalvamento
{
    Salvo,

    /// <summary>Outra operação alterou a estante entre a leitura e a gravação. Nada foi gravado.</summary>
    ConflitoConcorrencia,
}

/// <summary>Persistência das estantes para os casos de uso de administração.</summary>
public interface IEstanteRepositorio
{
    /// <summary>Insere e salva. Violação do índice único do device_id vira <see cref="ResultadoAdicaoEstante.DeviceIdEmUso"/>.</summary>
    Task<ResultadoAdicaoEstante> AdicionarAsync(Estante estante, CancellationToken cancellationToken);

    /// <summary>Carrega a estante rastreada, para alterar e depois chamar <see cref="SalvarAsync"/>.</summary>
    Task<Estante?> ObterParaAlteracaoAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Leitura (sem rastreamento e sem ler o segredo).</summary>
    Task<EstanteDto?> ObterAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Página ordenada por nome e depois por id (ordem estável). <paramref name="ativa"/> nulo não filtra.
    /// Paginação e contagem no banco, sem ler o segredo. <paramref name="pagina"/> começa em 1.
    /// </summary>
    Task<PaginaDto<EstanteDto>> ListarAsync(bool? ativa, int pagina, int tamanhoPagina, CancellationToken cancellationToken);

    Task<bool> ExisteDeviceIdAsync(string deviceId, CancellationToken cancellationToken);

    /// <summary>Algum cofre da estante está <c>reservado</c> ou <c>ocupado</c> (locação em andamento)?</summary>
    Task<bool> PossuiCofreEmUsoAsync(Guid estanteId, CancellationToken cancellationToken);

    /// <summary>
    /// Grava as alterações das estantes obtidas por <see cref="ObterParaAlteracaoAsync"/>. Se a estante foi alterada
    /// por outra operação depois de carregada (token de concorrência), nada é gravado e o resultado é
    /// <see cref="ResultadoSalvamento.ConflitoConcorrencia"/>.
    /// </summary>
    Task<ResultadoSalvamento> SalvarAsync(CancellationToken cancellationToken);
}
