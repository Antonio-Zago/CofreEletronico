using CofreEletronico.Domain.Estantes;

namespace CofreEletronico.Application.Cofres.Portas;

/// <summary>Resultado de inserir ou salvar cofres.</summary>
public enum ResultadoGravacaoCofre
{
    Gravado,

    /// <summary>O banco recusou o número (índice único <c>ix_cofres_estante_id_numero</c>). Nada foi gravado.</summary>
    NumeroEmUso,

    /// <summary>O banco recusou o canal (índice único <c>ix_cofres_estante_id_canal_rele</c>). Nada foi gravado.</summary>
    CanalEmUso,

    /// <summary>Outra operação alterou o cofre entre a leitura e a gravação (token xmin). Nada foi gravado.</summary>
    ConflitoConcorrencia,
}

/// <summary>Persistência dos cofres para os casos de uso de administração.</summary>
/// <remarks>
/// Em qualquer resultado de gravação diferente de <see cref="ResultadoGravacaoCofre.Gravado"/>, as entidades recusadas
/// são desanexadas: um salvamento posterior no mesmo contexto não as reenvia.
/// </remarks>
public interface ICofreRepositorio
{
    /// <summary>A estante existe (ativa ou não)?</summary>
    Task<bool> ExisteEstanteAsync(Guid estanteId, CancellationToken cancellationToken);

    /// <summary>Outro cofre da estante já usa o número? <paramref name="excetoCofreId"/> exclui o próprio cofre na edição.</summary>
    Task<bool> ExisteNumeroAsync(Guid estanteId, int numero, Guid? excetoCofreId, CancellationToken cancellationToken);

    /// <summary>Outro cofre da estante já usa o canal? <paramref name="excetoCofreId"/> exclui o próprio cofre na edição.</summary>
    Task<bool> ExisteCanalAsync(Guid estanteId, int canalRele, Guid? excetoCofreId, CancellationToken cancellationToken);

    /// <summary>Insere e salva. Violação dos índices únicos de número/canal vira o resultado correspondente.</summary>
    Task<ResultadoGravacaoCofre> AdicionarAsync(Cofre cofre, CancellationToken cancellationToken);

    /// <summary>Carrega o cofre rastreado, só se pertencer à estante, para alterar e depois chamar <see cref="SalvarAsync"/>.</summary>
    Task<Cofre?> ObterParaAlteracaoAsync(Guid estanteId, Guid cofreId, CancellationToken cancellationToken);

    /// <summary>Leitura sem rastreamento, só se o cofre pertencer à estante.</summary>
    Task<CofreDto?> ObterAsync(Guid estanteId, Guid cofreId, CancellationToken cancellationToken);

    /// <summary>Cofres da estante (ativos e inativos), ordenados por número.</summary>
    Task<IReadOnlyList<CofreDto>> ListarPorEstanteAsync(Guid estanteId, CancellationToken cancellationToken);

    /// <summary>
    /// Grava as alterações dos cofres obtidos por <see cref="ObterParaAlteracaoAsync"/>, com concorrência otimista
    /// (xmin) e tradução dos índices únicos de número/canal.
    /// </summary>
    Task<ResultadoGravacaoCofre> SalvarAsync(CancellationToken cancellationToken);
}
