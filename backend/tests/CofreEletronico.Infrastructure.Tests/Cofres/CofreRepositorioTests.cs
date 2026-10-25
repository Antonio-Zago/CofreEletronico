using CofreEletronico.Application.Cofres.Portas;
using CofreEletronico.Domain.Estantes;
using CofreEletronico.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Update;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace CofreEletronico.Infrastructure.Tests.Cofres;

/// <summary>
/// Repositório de cofres sem banco: só o tratamento de falha do SaveChanges (simulada por um interceptor, que roda
/// antes de qualquer acesso ao banco). Consultas e o 23505 real ficam para a T-0307 (Testcontainers).
/// </summary>
public class CofreRepositorioTests
{
    private static Cofre NovoCofre() => Cofre.Criar(Guid.NewGuid(), 1, 0, TamanhoCofre.Medio);

    private static PostgresException ViolacaoUnica(string restricao) =>
        new("duplicate key value violates unique constraint", "ERROR", "ERROR", PostgresErrorCodes.UniqueViolation,
            constraintName: restricao);

    private static PostgresException ErroPostgres(string sqlState) =>
        new("erro de concorrência", "ERROR", "ERROR", sqlState);

    private static ServiceProvider ProviderComFalha(Exception excecao) => ProviderComFalha(_ => excecao);

    [Theory]
    [InlineData(PostgresErrorCodes.DeadlockDetected)] // 40P01: ex. dois PUTs trocando canais entre si
    [InlineData(PostgresErrorCodes.SerializationFailure)] // 40001
    public async Task Deadlock_ou_falha_de_serializacao_ao_salvar_vira_ConflitoConcorrencia_e_desanexa(string sqlState)
    {
        using var provider = ProviderComFalha(new DbUpdateException("falha", ErroPostgres(sqlState)));
        using var scope = provider.CreateScope();
        var repositorio = scope.ServiceProvider.GetRequiredService<ICofreRepositorio>();
        var contexto = scope.ServiceProvider.GetRequiredService<CofreDbContext>();
        var cofre = NovoCofre();
        contexto.Attach(cofre);
        cofre.Atualizar(1, 5, TamanhoCofre.Medio);

        var resultado = await repositorio.SalvarAsync(CancellationToken.None);

        Assert.Equal(ResultadoGravacaoCofre.ConflitoConcorrencia, resultado);
        Assert.Empty(contexto.ChangeTracker.Entries());
    }

    [Theory]
    [InlineData(PostgresErrorCodes.DeadlockDetected)]
    [InlineData(PostgresErrorCodes.SerializationFailure)]
    public async Task Deadlock_ou_falha_de_serializacao_ao_adicionar_vira_ConflitoConcorrencia_e_desanexa(string sqlState)
    {
        using var provider = ProviderComFalha(new DbUpdateException("falha", ErroPostgres(sqlState)));
        using var scope = provider.CreateScope();
        var repositorio = scope.ServiceProvider.GetRequiredService<ICofreRepositorio>();
        var contexto = scope.ServiceProvider.GetRequiredService<CofreDbContext>();

        var resultado = await repositorio.AdicionarAsync(NovoCofre(), CancellationToken.None);

        Assert.Equal(ResultadoGravacaoCofre.ConflitoConcorrencia, resultado);
        Assert.Empty(contexto.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Outro_erro_do_Postgres_no_salvar_propaga()
    {
        using var provider = ProviderComFalha(new DbUpdateException("falha", ErroPostgres(PostgresErrorCodes.CheckViolation)));
        using var scope = provider.CreateScope();
        var repositorio = scope.ServiceProvider.GetRequiredService<ICofreRepositorio>();
        var contexto = scope.ServiceProvider.GetRequiredService<CofreDbContext>();
        var cofre = NovoCofre();
        contexto.Attach(cofre);
        cofre.Desativar();

        await Assert.ThrowsAsync<DbUpdateException>(() => repositorio.SalvarAsync(CancellationToken.None));
    }

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

        var r1 = scope1.ServiceProvider.GetRequiredService<ICofreRepositorio>();
        Assert.Same(r1, scope1.ServiceProvider.GetRequiredService<ICofreRepositorio>());
        Assert.NotSame(r1, scope2.ServiceProvider.GetRequiredService<ICofreRepositorio>());
    }

    [Theory]
    [InlineData("ix_cofres_estante_id_numero", ResultadoGravacaoCofre.NumeroEmUso)]
    [InlineData("ix_cofres_estante_id_canal_rele", ResultadoGravacaoCofre.CanalEmUso)]
    public async Task Violacao_de_indice_unico_ao_adicionar_vira_resultado_e_desanexa(string indice, ResultadoGravacaoCofre esperado)
    {
        using var provider = ProviderComFalha(new DbUpdateException("falha", ViolacaoUnica(indice)));
        using var scope = provider.CreateScope();
        var repositorio = scope.ServiceProvider.GetRequiredService<ICofreRepositorio>();
        var contexto = scope.ServiceProvider.GetRequiredService<CofreDbContext>();

        var resultado = await repositorio.AdicionarAsync(NovoCofre(), CancellationToken.None);

        Assert.Equal(esperado, resultado);
        Assert.Empty(contexto.ChangeTracker.Entries());
    }

    [Theory]
    [InlineData("pk_cofres")]
    [InlineData("ix_estantes_device_id")]
    public async Task Violacao_de_outra_restricao_ao_adicionar_propaga_e_desanexa(string indice)
    {
        using var provider = ProviderComFalha(new DbUpdateException("falha", ViolacaoUnica(indice)));
        using var scope = provider.CreateScope();
        var repositorio = scope.ServiceProvider.GetRequiredService<ICofreRepositorio>();
        var contexto = scope.ServiceProvider.GetRequiredService<CofreDbContext>();

        await Assert.ThrowsAsync<DbUpdateException>(() => repositorio.AdicionarAsync(NovoCofre(), CancellationToken.None));

        Assert.Empty(contexto.ChangeTracker.Entries());
    }

    [Theory]
    [InlineData("ix_cofres_estante_id_numero", ResultadoGravacaoCofre.NumeroEmUso)]
    [InlineData("ix_cofres_estante_id_canal_rele", ResultadoGravacaoCofre.CanalEmUso)]
    public async Task Violacao_de_indice_unico_ao_salvar_vira_resultado_e_desanexa(string indice, ResultadoGravacaoCofre esperado)
    {
        // Sem entradas na exceção (o provedor nem sempre as identifica): o repositório desanexa os cofres pendentes.
        using var provider = ProviderComFalha(new DbUpdateException("falha", ViolacaoUnica(indice)));
        using var scope = provider.CreateScope();
        var repositorio = scope.ServiceProvider.GetRequiredService<ICofreRepositorio>();
        var contexto = scope.ServiceProvider.GetRequiredService<CofreDbContext>();
        var cofre = NovoCofre();
        contexto.Attach(cofre);
        cofre.Atualizar(2, 3, TamanhoCofre.Grande);
        Assert.Equal(EntityState.Modified, contexto.Entry(cofre).State);

        var resultado = await repositorio.SalvarAsync(CancellationToken.None);

        Assert.Equal(esperado, resultado);
        Assert.Empty(contexto.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Conflito_de_concorrencia_no_salvar_devolve_ConflitoConcorrencia_e_desanexa()
    {
#pragma warning disable EF1001 // InternalEntityEntry (IUpdateEntry) é API interna do EF; só para montar a exceção no teste
        using var provider = ProviderComFalha(contexto => new DbUpdateConcurrencyException(
            "0 linhas afetadas",
            contexto.ChangeTracker.Entries().Select(e => (IUpdateEntry)e.GetInfrastructure()).ToList()));
#pragma warning restore EF1001
        using var scope = provider.CreateScope();
        var repositorio = scope.ServiceProvider.GetRequiredService<ICofreRepositorio>();
        var contexto = scope.ServiceProvider.GetRequiredService<CofreDbContext>();
        var cofre = NovoCofre();
        contexto.Attach(cofre);
        cofre.ColocarEmManutencao();

        var resultado = await repositorio.SalvarAsync(CancellationToken.None);

        Assert.Equal(ResultadoGravacaoCofre.ConflitoConcorrencia, resultado);
        Assert.Empty(contexto.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Outra_falha_no_salvar_propaga()
    {
        using var provider = ProviderComFalha(new DbUpdateException("falha", new InvalidOperationException("outra")));
        using var scope = provider.CreateScope();
        var repositorio = scope.ServiceProvider.GetRequiredService<ICofreRepositorio>();
        var contexto = scope.ServiceProvider.GetRequiredService<CofreDbContext>();
        var cofre = NovoCofre();
        contexto.Attach(cofre);
        cofre.Desativar();

        await Assert.ThrowsAsync<DbUpdateException>(() => repositorio.SalvarAsync(CancellationToken.None));
    }

    [Fact]
    public void Update_do_cofre_leva_o_xmin_lido_como_condicao()
    {
        using var provider = InfrastructureFixture.CriarProvider();
        using var scope = provider.CreateScope();
        var contexto = scope.ServiceProvider.GetRequiredService<CofreDbContext>();
        var cofre = NovoCofre();
        var entrada = contexto.Attach(cofre);
        entrada.Property<uint>("Versao").OriginalValue = 42;

        cofre.ColocarEmManutencao();

        var versao = entrada.Property<uint>("Versao");
        Assert.True(versao.Metadata.IsConcurrencyToken);
        Assert.Equal(42u, versao.OriginalValue);
        Assert.False(versao.IsModified); // nunca é enviado no SET; o banco o atualiza sozinho
    }

    [Fact]
    public void Projecao_para_o_DTO_e_traduzivel_e_converte_os_enums_em_memoria()
    {
        // Mesma forma da projeção do repositório: só colunas no SQL; CofreDto.Montar roda na projeção final.
        using var provider = InfrastructureFixture.CriarProvider();
        using var scope = provider.CreateScope();
        var contexto = scope.ServiceProvider.GetRequiredService<CofreDbContext>();
        var estanteId = Guid.NewGuid();

        var sql = contexto.Cofres.AsNoTracking()
            .Where(c => c.EstanteId == estanteId)
            .OrderBy(c => c.Numero)
            .Select(c => Application.Cofres.CofreDto.Montar(c.Id, c.EstanteId, c.Numero, c.CanalRele, c.Tamanho, c.Status, c.Ativo))
            .ToQueryString();

        Assert.Contains("FROM cofres", sql);
        Assert.Contains("ORDER BY c.numero", sql);
        Assert.DoesNotContain("lower", sql, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FalhaNoSaveChanges(Func<DbContext, Exception> excecao) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            throw excecao(eventData.Context!);
    }
}
