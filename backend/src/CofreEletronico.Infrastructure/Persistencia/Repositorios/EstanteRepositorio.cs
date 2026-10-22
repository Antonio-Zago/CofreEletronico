using CofreEletronico.Application.Estantes;
using CofreEletronico.Application.Estantes.Portas;
using CofreEletronico.Domain.Estantes;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CofreEletronico.Infrastructure.Persistencia.Repositorios;

/// <summary>
/// Estantes com EF Core. As leituras para exibição são projeções direto para <see cref="EstanteDto"/>: não leem nem
/// decifram a coluna <c>segredo_hmac</c>. Nenhuma consulta filtra pela coluna cifrada. Alterações usam concorrência
/// otimista por <c>xmin</c> (ver <c>EstanteConfiguracao</c>).
/// </summary>
internal sealed class EstanteRepositorio(CofreDbContext db) : IEstanteRepositorio
{
    /// <summary>Nome do índice único do device_id (ver <c>EstanteConfiguracao</c>).</summary>
    public const string IndiceDeviceId = "ix_estantes_device_id";

    public async Task<ResultadoAdicaoEstante> AdicionarAsync(Estante estante, CancellationToken cancellationToken)
    {
        var entrada = db.Estantes.Add(estante);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return ResultadoAdicaoEstante.Adicionada;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: IndiceDeviceId,
        })
        {
            // Corrida com outro cadastro que pegou o mesmo device_id. Desanexa para que a próxima tentativa
            // (outra entidade, no mesmo contexto) não reenvie esta no SaveChanges.
            entrada.State = EntityState.Detached;
            return ResultadoAdicaoEstante.DeviceIdEmUso;
        }
        catch
        {
            entrada.State = EntityState.Detached;
            throw;
        }
    }

    public Task<Estante?> ObterParaAlteracaoAsync(Guid id, CancellationToken cancellationToken) =>
        db.Estantes.SingleOrDefaultAsync(e => e.Id == id, cancellationToken);

    public Task<EstanteDto?> ObterAsync(Guid id, CancellationToken cancellationToken) =>
        db.Estantes.AsNoTracking()
            .Where(e => e.Id == id)
            .Select(e => new EstanteDto(
                e.Id, e.Nome, e.Endereco, e.Latitude, e.Longitude, e.DeviceId, e.Online, e.UltimoHeartbeat, e.Ativa, e.CriadoEm))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<PaginaDto<EstanteDto>> ListarAsync(
        bool? ativa, int pagina, int tamanhoPagina, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pagina, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(tamanhoPagina, 1);

        var consulta = db.Estantes.AsNoTracking();
        if (ativa is { } filtro)
            consulta = consulta.Where(e => e.Ativa == filtro);

        var total = await consulta.CountAsync(cancellationToken);

        // Página muito alta (pagina * tamanho estouraria int): não há itens, só o total.
        var deslocamento = (long)(pagina - 1) * tamanhoPagina;
        if (deslocamento >= total)
            return new PaginaDto<EstanteDto>([], pagina, tamanhoPagina, total);

        var itens = await consulta
            .OrderBy(e => e.Nome)
            .ThenBy(e => e.Id)
            .Skip((int)deslocamento)
            .Take(tamanhoPagina)
            .Select(e => new EstanteDto(
                e.Id, e.Nome, e.Endereco, e.Latitude, e.Longitude, e.DeviceId, e.Online, e.UltimoHeartbeat, e.Ativa, e.CriadoEm))
            .ToListAsync(cancellationToken);

        return new PaginaDto<EstanteDto>(itens, pagina, tamanhoPagina, total);
    }

    public Task<bool> ExisteDeviceIdAsync(string deviceId, CancellationToken cancellationToken) =>
        db.Estantes.AsNoTracking().AnyAsync(e => e.DeviceId == deviceId, cancellationToken);

    public Task<bool> PossuiCofreEmUsoAsync(Guid estanteId, CancellationToken cancellationToken) =>
        db.Cofres.AsNoTracking().AnyAsync(
            c => c.EstanteId == estanteId && (c.Status == StatusCofre.Reservado || c.Status == StatusCofre.Ocupado),
            cancellationToken);

    /// <summary>
    /// O UPDATE leva <c>WHERE xmin = &lt;valor lido&gt;</c> (token de concorrência). Zero linhas afetadas: outra operação
    /// alterou a estante depois da leitura; nada foi gravado.
    /// </summary>
    public async Task<ResultadoSalvamento> SalvarAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return ResultadoSalvamento.Salvo;
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // As entradas recusadas ficam desanexadas: um SaveChanges posterior no mesmo contexto não as reenvia.
            foreach (var entrada in ex.Entries)
                entrada.State = EntityState.Detached;
            return ResultadoSalvamento.ConflitoConcorrencia;
        }
    }
}
