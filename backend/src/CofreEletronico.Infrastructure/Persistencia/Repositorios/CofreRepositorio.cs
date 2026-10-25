using CofreEletronico.Application.Cofres;
using CofreEletronico.Application.Cofres.Portas;
using CofreEletronico.Domain.Estantes;
using CofreEletronico.Infrastructure.Persistencia.Configuracoes;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CofreEletronico.Infrastructure.Persistencia.Repositorios;

/// <summary>
/// Cofres com EF Core. Leituras para exibição projetam para <see cref="CofreDto"/> (a conversão dos enums em texto é
/// feita em memória, na projeção final). Alterações usam concorrência otimista por <c>xmin</c> (ver
/// <c>CofreConfiguracao</c>). Violação dos índices únicos de número/canal (23505) é identificada pelo nome da
/// constraint; qualquer outra falha propaga. Em toda recusa, as entradas envolvidas são desanexadas.
/// </summary>
internal sealed class CofreRepositorio(CofreDbContext db) : ICofreRepositorio
{
    public Task<bool> ExisteEstanteAsync(Guid estanteId, CancellationToken cancellationToken) =>
        db.Estantes.AsNoTracking().AnyAsync(e => e.Id == estanteId, cancellationToken);

    public Task<bool> ExisteNumeroAsync(Guid estanteId, int numero, Guid? excetoCofreId, CancellationToken cancellationToken) =>
        db.Cofres.AsNoTracking().AnyAsync(
            c => c.EstanteId == estanteId && c.Numero == numero && (excetoCofreId == null || c.Id != excetoCofreId),
            cancellationToken);

    public Task<bool> ExisteCanalAsync(Guid estanteId, int canalRele, Guid? excetoCofreId, CancellationToken cancellationToken) =>
        db.Cofres.AsNoTracking().AnyAsync(
            c => c.EstanteId == estanteId && c.CanalRele == canalRele && (excetoCofreId == null || c.Id != excetoCofreId),
            cancellationToken);

    public async Task<ResultadoGravacaoCofre> AdicionarAsync(Cofre cofre, CancellationToken cancellationToken)
    {
        var entrada = db.Cofres.Add(cofre);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return ResultadoGravacaoCofre.Gravado;
        }
        catch (DbUpdateException ex) when (Classificar(ex) is { } recusa)
        {
            entrada.State = EntityState.Detached;
            return recusa;
        }
        catch
        {
            entrada.State = EntityState.Detached;
            throw;
        }
    }

    public Task<Cofre?> ObterParaAlteracaoAsync(Guid estanteId, Guid cofreId, CancellationToken cancellationToken) =>
        db.Cofres.SingleOrDefaultAsync(c => c.Id == cofreId && c.EstanteId == estanteId, cancellationToken);

    public Task<CofreDto?> ObterAsync(Guid estanteId, Guid cofreId, CancellationToken cancellationToken) =>
        db.Cofres.AsNoTracking()
            .Where(c => c.Id == cofreId && c.EstanteId == estanteId)
            .Select(c => CofreDto.Montar(c.Id, c.EstanteId, c.Numero, c.CanalRele, c.Tamanho, c.Status, c.Ativo))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<CofreDto>> ListarPorEstanteAsync(Guid estanteId, CancellationToken cancellationToken) =>
        await db.Cofres.AsNoTracking()
            .Where(c => c.EstanteId == estanteId)
            .OrderBy(c => c.Numero) // número é único na estante: ordem estável
            .Select(c => CofreDto.Montar(c.Id, c.EstanteId, c.Numero, c.CanalRele, c.Tamanho, c.Status, c.Ativo))
            .ToListAsync(cancellationToken);

    /// <summary>
    /// O UPDATE leva <c>WHERE xmin = &lt;valor lido&gt;</c>. Zero linhas afetadas: outra operação alterou o cofre depois
    /// da leitura; nada foi gravado.
    /// </summary>
    public async Task<ResultadoGravacaoCofre> SalvarAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return ResultadoGravacaoCofre.Gravado;
        }
        catch (DbUpdateConcurrencyException ex)
        {
            Desanexar(ex);
            return ResultadoGravacaoCofre.ConflitoConcorrencia;
        }
        catch (DbUpdateException ex) when (Classificar(ex) is { } recusa)
        {
            Desanexar(ex);

            // O provedor nem sempre identifica a entrada recusada (lote de comandos): os cofres pendentes também são
            // desanexados, para não serem reenviados.
            foreach (var entrada in db.ChangeTracker.Entries<Cofre>()
                         .Where(e => e.State is EntityState.Modified or EntityState.Added).ToList())
                entrada.State = EntityState.Detached;
            return recusa;
        }
    }

    /// <summary>
    /// Recusas do banco tratadas como resultado (409 na Api); qualquer outra falha devolve null e propaga:
    /// <list type="bullet">
    /// <item>23505 de um dos índices únicos de cofres, pelo nome da constraint;</item>
    /// <item>40P01 (deadlock) e 40001 (falha de serialização): ex. dois PUTs trocando canais entre si, cada um esperando
    /// o índice que o outro segura. O PostgreSQL desfaz a transação da vítima; nada foi gravado.</item>
    /// </list>
    /// </summary>
    private static ResultadoGravacaoCofre? Classificar(DbUpdateException ex) =>
        ex.InnerException switch
        {
            PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg => pg.ConstraintName switch
            {
                CofreConfiguracao.IndiceNumero => ResultadoGravacaoCofre.NumeroEmUso,
                CofreConfiguracao.IndiceCanalRele => ResultadoGravacaoCofre.CanalEmUso,
                _ => null,
            },
            PostgresException { SqlState: PostgresErrorCodes.DeadlockDetected or PostgresErrorCodes.SerializationFailure } =>
                ResultadoGravacaoCofre.ConflitoConcorrencia,
            _ => null,
        };

    /// <summary>As entradas recusadas ficam desanexadas: um SaveChanges posterior no mesmo contexto não as reenvia.</summary>
    private static void Desanexar(DbUpdateException ex)
    {
        foreach (var entrada in ex.Entries)
            entrada.State = EntityState.Detached;
    }
}
