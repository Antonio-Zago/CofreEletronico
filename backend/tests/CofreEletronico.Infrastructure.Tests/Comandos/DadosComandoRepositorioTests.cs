using System.Data;
using System.Data.Common;
using CofreEletronico.Application.Comandos.Portas;
using CofreEletronico.Infrastructure.Criptografia;
using CofreEletronico.Infrastructure.Persistencia;
using CofreEletronico.Infrastructure.Persistencia.Repositorios;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace CofreEletronico.Infrastructure.Tests.Comandos;

/// <summary>
/// Porta de leitura dos dados de comando, sem banco: o SQL gerado (<c>ToQueryString</c>) e a materialização da
/// projeção com um leitor falso (interceptores suprimem a abertura da conexão e a execução), para conferir que o
/// conversor AES-GCM decifra o segredo na projeção. A consulta contra o PostgreSQL real fica para a T-0307.
/// </summary>
public class DadosComandoRepositorioTests
{
    private static readonly byte[] Segredo = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();

    [Fact]
    public void Consulta_e_traduzivel_filtra_por_cofre_e_estante_e_nao_filtra_pela_coluna_cifrada()
    {
        using var provider = InfrastructureFixture.CriarProvider();
        using var scope = provider.CreateScope();
        var contexto = scope.ServiceProvider.GetRequiredService<CofreDbContext>();

        var sql = DadosComandoRepositorio.Consulta(contexto, Guid.NewGuid(), Guid.NewGuid()).ToQueryString();

        Assert.Contains("FROM cofres", sql);
        Assert.Contains("INNER JOIN estantes", sql);
        Assert.Contains("e.device_id", sql);
        Assert.Contains("c.numero", sql);
        Assert.Contains("c.canal_rele", sql);
        Assert.Contains("e.segredo_hmac", sql);
        Assert.Matches(@"c\.id = @__cofreId_\d", sql);
        Assert.Matches(@"c\.estante_id = @__estanteId_\d", sql);

        var where = sql[sql.IndexOf("WHERE", StringComparison.Ordinal)..];
        Assert.DoesNotContain("segredo_hmac", where);
        Assert.DoesNotContain("status", where);
        Assert.DoesNotContain("ativ", where);
    }

    [Fact]
    public async Task Projecao_decifra_o_segredo_e_devolve_os_dados_sem_rastrear()
    {
        var leitor = new LeitorFalso();
        using var provider = ProviderComLeitor(leitor);
        using var scope = provider.CreateScope();
        var contexto = scope.ServiceProvider.GetRequiredService<CofreDbContext>();
        var cifrador = provider.GetRequiredService<ICifradorSegredos>();
        leitor.Linha = ["est-abc123", 3, 5, cifrador.Cifrar(Segredo)];
        var repositorio = scope.ServiceProvider.GetRequiredService<IDadosComandoRepositorio>();
        var estanteId = Guid.NewGuid();
        var cofreId = Guid.NewGuid();

        var dados = await repositorio.ObterAsync(estanteId, cofreId, CancellationToken.None);

        Assert.NotNull(dados);
        Assert.Equal("est-abc123", dados!.DeviceId);
        Assert.Equal(3, dados.NumeroCofre);
        Assert.Equal(5, dados.CanalRele);
        Assert.Equal(Segredo, dados.SegredoHmac);
        Assert.Empty(contexto.ChangeTracker.Entries());

        var comando = Assert.Single(leitor.Comandos);
        Assert.Contains(comando.Parametros, p => Equals(p, cofreId));
        Assert.Contains(comando.Parametros, p => Equals(p, estanteId));
    }

    [Fact]
    public async Task Sem_linha_devolve_null()
    {
        var leitor = new LeitorFalso();
        using var provider = ProviderComLeitor(leitor);
        using var scope = provider.CreateScope();
        var repositorio = scope.ServiceProvider.GetRequiredService<IDadosComandoRepositorio>();

        Assert.Null(await repositorio.ObterAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public void Repositorio_e_scoped()
    {
        using var provider = InfrastructureFixture.CriarProvider();
        using var scope1 = provider.CreateScope();
        using var scope2 = provider.CreateScope();

        var r1 = scope1.ServiceProvider.GetRequiredService<IDadosComandoRepositorio>();
        Assert.Same(r1, scope1.ServiceProvider.GetRequiredService<IDadosComandoRepositorio>());
        Assert.NotSame(r1, scope2.ServiceProvider.GetRequiredService<IDadosComandoRepositorio>());
    }

    private static ServiceProvider ProviderComLeitor(LeitorFalso leitor) =>
        InfrastructureFixture.CriarProvider(configurarServicos: services =>
        {
            var original = services.Single(d => d.ServiceType == typeof(DbContextOptions<CofreDbContext>));
            services.Remove(original);
            services.Add(new ServiceDescriptor(typeof(DbContextOptions<CofreDbContext>), sp =>
                new DbContextOptionsBuilder<CofreDbContext>((DbContextOptions<CofreDbContext>)original.ImplementationFactory!(sp))
                    .AddInterceptors(new ConexaoSuprimida(), leitor)
                    .Options, original.Lifetime));
        });

    /// <summary>Não abre conexão com o banco.</summary>
    private sealed class ConexaoSuprimida : DbConnectionInterceptor
    {
        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
            DbConnection connection, ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(InterceptionResult.Suppress());

        public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData, InterceptionResult result) =>
            InterceptionResult.Suppress();
    }

    /// <summary>Devolve <see cref="Linha"/> (ou nenhuma) no lugar de executar o comando, e guarda os parâmetros.</summary>
    private sealed class LeitorFalso : DbCommandInterceptor
    {
        public object[]? Linha { get; set; }

        public List<(string Sql, object?[] Parametros)> Comandos { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Comandos.Add((command.CommandText, command.Parameters.Cast<DbParameter>().Select(p => p.Value).ToArray()));

            var tabela = new DataTable();
            tabela.Columns.Add("device_id", typeof(string));
            tabela.Columns.Add("numero", typeof(int));
            tabela.Columns.Add("canal_rele", typeof(int));
            tabela.Columns.Add("segredo_hmac", typeof(string));
            if (Linha is not null)
                tabela.Rows.Add(Linha);

            return ValueTask.FromResult(InterceptionResult<DbDataReader>.SuppressWithResult(tabela.CreateDataReader()));
        }
    }
}
