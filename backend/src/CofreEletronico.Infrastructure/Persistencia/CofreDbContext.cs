using CofreEletronico.Domain.Autenticacao;
using CofreEletronico.Domain.Estantes;
using CofreEletronico.Domain.Tarifas;
using CofreEletronico.Infrastructure.Criptografia;
using CofreEletronico.Infrastructure.Persistencia.Configuracoes;
using CofreEletronico.Infrastructure.Persistencia.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace CofreEletronico.Infrastructure.Persistencia;

/// <summary>
/// Contexto EF Core da aplicação: tabelas do ASP.NET Identity e as entidades de domínio.
/// </summary>
/// <remarks>
/// Recebe o <see cref="ICifradorSegredos"/> singleton da DI para o conversor de <c>estantes.segredo_hmac</c>
/// (ver <see cref="EstanteConfiguracao"/>). Como o EF guarda o modelo em cache, o modelo é chaveado também
/// pela instância do cifrador (<see cref="CofreModelCacheKeyFactory"/>): em produção há um único cifrador e,
/// portanto, um único modelo; nos testes, cada container com outra chave ganha o próprio modelo, sem herdar
/// o conversor de outro container.
/// </remarks>
public class CofreDbContext(DbContextOptions<CofreDbContext> options, ICifradorSegredos cifradorSegredos)
    : IdentityDbContext<Usuario, Papel, Guid>(options)
{
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<Estante> Estantes => Set<Estante>();

    public DbSet<Cofre> Cofres => Set<Cofre>();

    public DbSet<Tarifa> Tarifas => Set<Tarifa>();

    /// <summary>Cifrador usado no modelo; também compõe a chave do cache de modelo.</summary>
    internal ICifradorSegredos CifradorSegredos { get; } = cifradorSegredos;

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Ponto de entrada das classes IEntityTypeConfiguration desta camada (todas com construtor sem parâmetros).
        builder.ApplyConfigurationsFromAssembly(typeof(CofreDbContext).Assembly);

        // Única configuração que depende de um serviço: o conversor cifra/decifra com o cifrador deste contexto.
        builder.Entity<Estante>(e => EstanteConfiguracao.ConfigurarSegredoHmac(e, CifradorSegredos));
    }
}
