using CofreEletronico.Application.Estantes.Portas;
using CofreEletronico.Domain.Estantes;
using CofreEletronico.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Update;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace CofreEletronico.Infrastructure.Tests.Estantes;

/// <summary>
/// Repositório de estantes sem banco: só o tratamento de falha do SaveChanges (simulada por um interceptor, que roda
/// antes de qualquer acesso ao banco). Consultas, paginação e o 23505 real ficam para a T-0307 (Testcontainers).
/// </summary>
public class EstanteRepositorioTests
{
    private static readonly byte[] Segredo = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();

    private static Estante NovaEstante(string deviceId = "est-abc123") =>
        Estante.Criar("Estante", "Rua A", 0, 0, deviceId, Segredo, DateTimeOffset.UnixEpoch);

    private static PostgresException ViolacaoUnica(string restricao) =>
        new("duplicate key value violates unique constraint", "ERROR", "ERROR", PostgresErrorCodes.UniqueViolation,
            constraintName: restricao);

    /// <summary>Container real com o SaveChanges do CofreDbContext lançando <paramref name="excecao"/>.</summary>
    private static ServiceProvider ProviderComFalha(Exception excecao) => ProviderComFalha(_ => excecao);

    private static ServiceProvider ProviderComFalha(Func<DbContext, Exception> excecao) =>
        InfrastructureFixture.CriarProvider(configurarServicos: services =>
        {
            var original = services.Single(d => d.ServiceType == typeof(DbContextOptions<CofreDbContext>));
            services.Remove(original);
            services.Add(new ServiceDescriptor(typeof(DbContextOptions<CofreDbContext>), sp =>
                new DbContextOptionsBuilder<CofreDbContext>((DbContextOptions<CofreDbContext>)original.ImplementationFactory!(sp))
                    .AddInterceptors(new FalhaNoSaveChanges(excecao))
                    .Options, original.Lifetime));
        });

    [Fact]
    public void Repositorio_e_scoped()
    {
        using var provider = InfrastructureFixture.CriarProvider();
        using var scope1 = provider.CreateScope();
        using var scope2 = provider.CreateScope();

        var r1 = scope1.ServiceProvider.GetRequiredService<IEstanteRepositorio>();
        Assert.Same(r1, scope1.ServiceProvider.GetRequiredService<IEstanteRepositorio>());
        Assert.NotSame(r1, scope2.ServiceProvider.GetRequiredService<IEstanteRepositorio>());
    }

    [Fact]
    public async Task Violacao_do_indice_do_device_id_devolve_DeviceIdEmUso_e_desanexa_a_entidade()
    {
        using var provider = ProviderComFalha(new DbUpdateException("falha", ViolacaoUnica("ix_estantes_device_id")));
        using var scope = provider.CreateScope();
        var repositorio = scope.ServiceProvider.GetRequiredService<IEstanteRepositorio>();
        var contexto = scope.ServiceProvider.GetRequiredService<CofreDbContext>();
        var estante = NovaEstante();

        var resultado = await repositorio.AdicionarAsync(estante, CancellationToken.None);

        Assert.Equal(ResultadoAdicaoEstante.DeviceIdEmUso, resultado);
        Assert.Equal(EntityState.Detached, contexto.Entry(estante).State);
        Assert.Empty(contexto.ChangeTracker.Entries()); // a próxima tentativa não reenvia esta estante
    }

    [Fact]
    public async Task Violacao_de_outra_restricao_unica_propaga_e_desanexa()
    {
        using var provider = ProviderComFalha(new DbUpdateException("falha", ViolacaoUnica("pk_estantes")));
        using var scope = provider.CreateScope();
        var repositorio = scope.ServiceProvider.GetRequiredService<IEstanteRepositorio>();
        var contexto = scope.ServiceProvider.GetRequiredService<CofreDbContext>();

        await Assert.ThrowsAsync<DbUpdateException>(() => repositorio.AdicionarAsync(NovaEstante(), CancellationToken.None));

        Assert.Empty(contexto.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Outra_falha_de_banco_propaga_e_desanexa()
    {
        using var provider = ProviderComFalha(new DbUpdateException("falha", new InvalidOperationException("outra")));
        using var scope = provider.CreateScope();
        var repositorio = scope.ServiceProvider.GetRequiredService<IEstanteRepositorio>();
        var contexto = scope.ServiceProvider.GetRequiredService<CofreDbContext>();

        await Assert.ThrowsAsync<DbUpdateException>(() => repositorio.AdicionarAsync(NovaEstante(), CancellationToken.None));

        Assert.Empty(contexto.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Conflito_de_concorrencia_no_salvar_devolve_ConflitoConcorrencia_e_desanexa()
    {
        // Como o EF faz quando o UPDATE afeta 0 linhas: a exceção leva as entradas recusadas.
#pragma warning disable EF1001 // InternalEntityEntry (IUpdateEntry) é API interna do EF; só para montar a exceção no teste
        using var provider = ProviderComFalha(contexto => new DbUpdateConcurrencyException(
            "0 linhas afetadas",
            contexto.ChangeTracker.Entries().Select(e => (IUpdateEntry)e.GetInfrastructure()).ToList()));
#pragma warning restore EF1001
        using var scope = provider.CreateScope();
        var repositorio = scope.ServiceProvider.GetRequiredService<IEstanteRepositorio>();
        var contexto = scope.ServiceProvider.GetRequiredService<CofreDbContext>();

        // Estante "carregada" (rastreada sem alterações) e depois alterada, como no fluxo de alteração.
        var estante = NovaEstante();
        contexto.Attach(estante);
        estante.SubstituirSegredoHmac(Enumerable.Repeat((byte)5, 32).ToArray());
        Assert.Equal(EntityState.Modified, contexto.Entry(estante).State);

        var resultado = await repositorio.SalvarAsync(CancellationToken.None);

        Assert.Equal(ResultadoSalvamento.ConflitoConcorrencia, resultado);
        Assert.Empty(contexto.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Outra_falha_no_salvar_propaga()
    {
        using var provider = ProviderComFalha(new DbUpdateException("falha", new InvalidOperationException("outra")));
        using var scope = provider.CreateScope();
        var repositorio = scope.ServiceProvider.GetRequiredService<IEstanteRepositorio>();
        var contexto = scope.ServiceProvider.GetRequiredService<CofreDbContext>();
        var estante = NovaEstante();
        contexto.Attach(estante);
        estante.Desativar();

        await Assert.ThrowsAsync<DbUpdateException>(() => repositorio.SalvarAsync(CancellationToken.None));
    }

    [Fact]
    public void Update_da_estante_leva_o_xmin_lido_como_condicao()
    {
        // O token entra no WHERE do UPDATE: o valor original (lido) é o que o EF compara.
        using var provider = InfrastructureFixture.CriarProvider();
        using var scope = provider.CreateScope();
        var contexto = scope.ServiceProvider.GetRequiredService<CofreDbContext>();
        var estante = NovaEstante();
        var entrada = contexto.Attach(estante);
        entrada.Property<uint>("Versao").OriginalValue = 42;

        estante.Desativar();

        var versao = entrada.Property<uint>("Versao");
        Assert.True(versao.Metadata.IsConcurrencyToken);
        Assert.Equal(42u, versao.OriginalValue);
        Assert.False(versao.IsModified); // nunca é enviado no SET; o banco o atualiza sozinho
    }

    private sealed class FalhaNoSaveChanges(Func<DbContext, Exception> excecao) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            throw excecao(eventData.Context!);
    }
}
