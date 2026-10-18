using CofreEletronico.Application.Autenticacao.Portas;
using CofreEletronico.Domain.Autenticacao;
using Microsoft.EntityFrameworkCore;

namespace CofreEletronico.Infrastructure.Persistencia.Repositorios;

/// <summary>
/// Refresh tokens com EF Core. As revogações usam UPDATE condicional (<c>... WHERE revogado_em IS NULL</c>)
/// em vez de carregar, alterar e salvar: o banco decide quem ganha numa corrida, sem depender de memória.
/// </summary>
internal sealed class RefreshTokenRepositorio(CofreDbContext db) : IRefreshTokenRepositorio
{
    public async Task AdicionarAsync(RefreshToken token, CancellationToken cancellationToken)
    {
        db.RefreshTokens.Add(token);
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task<RefreshToken?> ObterPorHashAsync(string tokenHash, CancellationToken cancellationToken) =>
        db.RefreshTokens.AsNoTracking().SingleOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);

    public async Task<bool> RotacionarAsync(RefreshToken atual, RefreshToken novo, CancellationToken cancellationToken)
    {
        var agora = novo.CriadoEm;

        await using var transacao = await db.Database.BeginTransactionAsync(cancellationToken);

        // O novo entra primeiro porque substituido_por_id do atual é FK para ele.
        db.RefreshTokens.Add(novo);
        await db.SaveChangesAsync(cancellationToken);

        // Só revoga se ninguém revogou antes. Em READ COMMITTED, uma segunda transação com o mesmo token
        // espera o lock da linha, reavalia o WHERE depois do commit da primeira e afeta 0 linhas.
        var linhas = await db.RefreshTokens
            .Where(t => t.Id == atual.Id && t.RevogadoEm == null && t.ExpiraEm > agora)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.RevogadoEm, (DateTimeOffset?)agora)
                .SetProperty(t => t.SubstituidoPorId, (Guid?)novo.Id),
                cancellationToken);

        if (linhas != 1)
        {
            await transacao.RollbackAsync(cancellationToken);
            db.Entry(novo).State = EntityState.Detached;
            return false;
        }

        await transacao.CommitAsync(cancellationToken);
        return true;
    }

    public Task RevogarAsync(Guid tokenId, DateTimeOffset agora, CancellationToken cancellationToken) =>
        db.RefreshTokens
            .Where(t => t.Id == tokenId && t.RevogadoEm == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevogadoEm, (DateTimeOffset?)agora), cancellationToken);

    public Task RevogarFamiliaAsync(Guid familiaId, DateTimeOffset agora, CancellationToken cancellationToken) =>
        db.RefreshTokens
            .Where(t => t.FamiliaId == familiaId && t.RevogadoEm == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevogadoEm, (DateTimeOffset?)agora), cancellationToken);
}
