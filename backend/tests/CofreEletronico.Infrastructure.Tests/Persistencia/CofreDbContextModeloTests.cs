using CofreEletronico.Infrastructure.Persistencia;
using CofreEletronico.Infrastructure.Persistencia.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;

namespace CofreEletronico.Infrastructure.Tests.Persistencia;

/// <summary>
/// Verifica o modelo EF do CofreDbContext contra o contrato (docs/ARQUITETURA.md) sem banco.
/// Usa o modelo de design (IDesignTimeModel), que preserva seed data e anotações relacionais.
/// </summary>
public sealed class CofreDbContextModeloTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly IServiceScope _scope;
    private readonly IModel _modelo;

    public CofreDbContextModeloTests()
    {
        _provider = InfrastructureFixture.CriarProvider();
        _scope = _provider.CreateScope();
        var context = _scope.ServiceProvider.GetRequiredService<CofreDbContext>();
        _modelo = context.GetService<IDesignTimeModel>().Model;
    }

    public void Dispose()
    {
        _scope.Dispose();
        _provider.Dispose();
    }

    [Theory]
    [InlineData(typeof(Usuario), "usuarios")]
    [InlineData(typeof(Papel), "papeis")]
    [InlineData(typeof(IdentityUserRole<Guid>), "usuarios_papeis")]
    [InlineData(typeof(IdentityUserClaim<Guid>), "usuarios_claims")]
    [InlineData(typeof(IdentityUserLogin<Guid>), "usuarios_logins")]
    [InlineData(typeof(IdentityUserToken<Guid>), "usuarios_tokens")]
    [InlineData(typeof(IdentityRoleClaim<Guid>), "papeis_claims")]
    public void Tabelas_seguem_o_contrato(Type tipo, string tabelaEsperada)
    {
        Assert.Equal(tabelaEsperada, Entidade(tipo).GetTableName());
    }

    [Fact]
    public void Nao_ha_tabelas_com_nomes_padrao_do_Identity()
    {
        var tabelas = _modelo.GetEntityTypes().Select(e => e.GetTableName()).ToList();

        Assert.DoesNotContain(tabelas, t => t is not null && t.StartsWith("AspNet", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(nameof(Usuario.PasswordHash), "senha_hash")]
    [InlineData(nameof(Usuario.Nome), "nome")]
    [InlineData(nameof(Usuario.Cpf), "cpf")]
    [InlineData(nameof(Usuario.PhoneNumber), "telefone")]
    [InlineData(nameof(Usuario.PhoneNumberConfirmed), "phone_number_confirmed")]
    [InlineData(nameof(Usuario.CriadoEm), "criado_em")]
    [InlineData(nameof(Usuario.NormalizedEmail), "normalized_email")]
    [InlineData(nameof(Usuario.SecurityStamp), "security_stamp")]
    public void Colunas_de_usuarios_em_snake_case(string propriedade, string colunaEsperada)
    {
        Assert.Equal(colunaEsperada, Entidade(typeof(Usuario)).FindProperty(propriedade)!.GetColumnName());
    }

    [Fact]
    public void Campos_do_usuario_tem_tamanho_e_obrigatoriedade_do_contrato()
    {
        var usuario = Entidade(typeof(Usuario));

        var nome = usuario.FindProperty(nameof(Usuario.Nome))!;
        Assert.False(nome.IsNullable);
        Assert.Equal(150, nome.GetMaxLength());

        var cpf = usuario.FindProperty(nameof(Usuario.Cpf))!;
        Assert.True(cpf.IsNullable);
        Assert.Equal(11, cpf.GetMaxLength());

        var telefone = usuario.FindProperty(nameof(Usuario.PhoneNumber))!;
        Assert.True(telefone.IsNullable);
        Assert.Equal(20, telefone.GetMaxLength());

        Assert.Equal("timestamp with time zone", usuario.FindProperty(nameof(Usuario.CriadoEm))!.GetColumnType());
    }

    [Theory]
    [InlineData(nameof(Usuario.Email))]
    [InlineData(nameof(Usuario.NormalizedEmail))]
    public void Email_e_obrigatorio(string propriedade)
    {
        // Índice único no PostgreSQL aceita vários NULL: a unicidade só vale com NOT NULL.
        Assert.False(Entidade(typeof(Usuario)).FindProperty(propriedade)!.IsNullable);
    }

    [Fact]
    public void Lockout_habilitado_por_padrao_no_banco()
    {
        Assert.Equal(true, Entidade(typeof(Usuario)).FindProperty(nameof(Usuario.LockoutEnabled))!.GetDefaultValue());
    }

    [Fact]
    public void Usuarios_papeis_usa_colunas_do_contrato()
    {
        var usuarioPapel = Entidade(typeof(IdentityUserRole<Guid>));

        Assert.Equal("usuario_id", usuarioPapel.FindProperty(nameof(IdentityUserRole<Guid>.UserId))!.GetColumnName());
        Assert.Equal("papel_id", usuarioPapel.FindProperty(nameof(IdentityUserRole<Guid>.RoleId))!.GetColumnName());
    }

    [Fact]
    public void Papeis_usa_coluna_nome()
    {
        Assert.Equal("nome", Entidade(typeof(Papel)).FindProperty(nameof(Papel.Name))!.GetColumnName());
    }

    [Fact]
    public void Email_normalizado_tem_indice_unico()
    {
        var indice = Indice(typeof(Usuario), nameof(Usuario.NormalizedEmail));

        Assert.True(indice.IsUnique);
        Assert.Equal("ix_usuarios_normalized_email", indice.GetDatabaseName());
    }

    [Fact]
    public void Cpf_tem_indice_unico_filtrado()
    {
        var indice = Indice(typeof(Usuario), nameof(Usuario.Cpf));

        Assert.True(indice.IsUnique);
        Assert.Equal("ix_usuarios_cpf", indice.GetDatabaseName());
        Assert.Equal("cpf IS NOT NULL", indice.GetFilter());
    }

    [Fact]
    public void Seed_contem_os_dois_papeis_fixos()
    {
        var seed = Entidade(typeof(Papel)).GetSeedData().ToList();

        Assert.Equal(2, seed.Count);
        Assert.All(seed, p =>
        {
            Assert.NotEqual(Guid.Empty, (Guid)p[nameof(Papel.Id)]!);
            Assert.False(string.IsNullOrEmpty((string?)p[nameof(Papel.ConcurrencyStamp)]));
        });

        var nomes = seed
            .Select(p => ((string?)p[nameof(Papel.Name)], (string?)p[nameof(Papel.NormalizedName)]))
            .OrderBy(p => p.Item1)
            .ToList();
        Assert.Equal([("Admin", "ADMIN"), ("Usuario", "USUARIO")], nomes);
    }

    private IEntityType Entidade(Type tipo) =>
        _modelo.FindEntityType(tipo) ?? throw new InvalidOperationException($"{tipo.Name} não está no modelo.");

    private IIndex Indice(Type tipo, string propriedade) =>
        Entidade(tipo).GetIndexes().Single(i => i.Properties.Count == 1 && i.Properties[0].Name == propriedade);
}
