using CofreEletronico.Application.Comandos.Portas;
using CofreEletronico.Infrastructure.Persistencia.Configuracoes;
using Microsoft.EntityFrameworkCore;

namespace CofreEletronico.Infrastructure.Persistencia.Repositorios;

/// <summary>
/// Dados para assinar um comando: projeção sem rastreamento de <c>cofres</c> JOIN <c>estantes</c>, filtrada pelo id do
/// cofre e pelo id da estante (cofre de outra estante não volta). O segredo é lido pela propriedade mapeada
/// (<see cref="EstanteConfiguracao.PropriedadeSegredoHmac"/>), então o conversor AES-GCM decifra na materialização e o
/// array devolvido é novo (quem chama o zera). Nenhum filtro usa a coluna cifrada. Sem filtro de status/ativo (regra de
/// quem chama).
/// </summary>
internal sealed class DadosComandoRepositorio(CofreDbContext db) : IDadosComandoRepositorio
{
    public Task<DadosComando?> ObterAsync(Guid estanteId, Guid cofreId, CancellationToken cancellationToken) =>
        Consulta(db, estanteId, cofreId).SingleOrDefaultAsync(cancellationToken);

    /// <summary>A consulta usada por <see cref="ObterAsync"/> (exposta para os testes conferirem o SQL).</summary>
    internal static IQueryable<DadosComando?> Consulta(CofreDbContext db, Guid estanteId, Guid cofreId) =>
        from c in db.Cofres.AsNoTracking()
        join e in db.Estantes.AsNoTracking() on c.EstanteId equals e.Id
        where c.Id == cofreId && c.EstanteId == estanteId
        select new DadosComando(
            e.DeviceId, c.Numero, c.CanalRele, EF.Property<byte[]>(e, EstanteConfiguracao.PropriedadeSegredoHmac));
}
