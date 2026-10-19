using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace CofreEletronico.Infrastructure.Persistencia;

/// <summary>
/// Chave do cache de modelo do EF = (tipo do contexto, instância do cifrador, design-time).
/// O padrão do EF usa só o tipo do contexto, o que prenderia no modelo o conversor do primeiro
/// <see cref="Criptografia.ICifradorSegredos"/> visto no processo. O cifrador é singleton, então em
/// produção continua existindo um único modelo.
/// </summary>
internal sealed class CofreModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime) =>
        context is CofreDbContext cofre
            ? (context.GetType(), cofre.CifradorSegredos, designTime)
            : (object)(context.GetType(), designTime);
}
