using CofreEletronico.Domain.Estantes;
using CofreEletronico.Domain.Tarifas;
using CofreEletronico.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;

namespace CofreEletronico.Infrastructure.Tests.Persistencia;

/// <summary>
/// Modelo EF de <c>estantes</c>, <c>cofres</c> e <c>tarifas</c> contra o contrato (docs/ARQUITETURA.md), sem banco.
/// </summary>
public sealed class EstantesCofresTarifasModeloTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly IServiceScope _scope;
    private readonly IModel _modelo;

    public EstantesCofresTarifasModeloTests()
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
    [InlineData(typeof(Estante), "estantes")]
    [InlineData(typeof(Cofre), "cofres")]
    [InlineData(typeof(Tarifa), "tarifas")]
    public void Tabelas_seguem_o_contrato(Type tipo, string tabela)
    {
        Assert.Equal(tabela, Entidade(tipo).GetTableName());
    }

    [Fact]
    public void Estantes_tem_as_colunas_e_tipos_do_contrato()
    {
        var estante = Entidade(typeof(Estante));

        Assert.Equal(
            ["ativa", "criado_em", "device_id", "endereco", "id", "latitude", "longitude", "nome", "online", "segredo_hmac", "ultimo_heartbeat", "xmin"],
            Colunas(estante));

        Assert.Equal("character varying(100)", Tipo(estante, nameof(Estante.Nome)));
        Assert.Equal("character varying(200)", Tipo(estante, nameof(Estante.Endereco)));
        Assert.Equal("double precision", Tipo(estante, nameof(Estante.Latitude)));
        Assert.Equal("double precision", Tipo(estante, nameof(Estante.Longitude)));
        Assert.Equal("character varying(10)", Tipo(estante, nameof(Estante.DeviceId)));
        Assert.Equal("text", Tipo(estante, "SegredoHmac"));
        Assert.Equal("timestamp with time zone", Tipo(estante, nameof(Estante.CriadoEm)));
        Assert.Equal("timestamp with time zone", Tipo(estante, nameof(Estante.UltimoHeartbeat)));

        Assert.True(estante.FindProperty(nameof(Estante.UltimoHeartbeat))!.IsNullable);
        Assert.All(
            estante.GetProperties().Where(p => p.Name != nameof(Estante.UltimoHeartbeat)),
            p => Assert.False(p.IsNullable, $"{p.Name} deveria ser NOT NULL"));
    }

    [Fact]
    public void Cofres_tem_as_colunas_e_tipos_do_contrato()
    {
        var cofre = Entidade(typeof(Cofre));

        Assert.Equal(["ativo", "canal_rele", "estante_id", "id", "numero", "status", "tamanho"], Colunas(cofre));
        Assert.Equal("integer", Tipo(cofre, nameof(Cofre.Numero)));
        Assert.Equal("integer", Tipo(cofre, nameof(Cofre.CanalRele)));
        Assert.Equal("character varying(20)", Tipo(cofre, nameof(Cofre.Tamanho)));
        Assert.Equal("character varying(20)", Tipo(cofre, nameof(Cofre.Status)));
        Assert.All(cofre.GetProperties(), p => Assert.False(p.IsNullable, $"{p.Name} deveria ser NOT NULL"));
    }

    [Fact]
    public void Tarifas_tem_as_colunas_e_tipos_do_contrato()
    {
        var tarifa = Entidade(typeof(Tarifa));

        Assert.Equal(["id", "tamanho", "tempo_minimo", "valor_por_hora"], Colunas(tarifa));
        Assert.Equal("numeric(10,2)", Tipo(tarifa, nameof(Tarifa.ValorPorHora)));
        Assert.Equal("integer", Tipo(tarifa, nameof(Tarifa.TempoMinimo)));
        Assert.Equal("character varying(20)", Tipo(tarifa, nameof(Tarifa.Tamanho)));
        Assert.All(tarifa.GetProperties(), p => Assert.False(p.IsNullable, $"{p.Name} deveria ser NOT NULL"));
    }

    [Theory]
    [InlineData(typeof(Estante), "pk_estantes")]
    [InlineData(typeof(Cofre), "pk_cofres")]
    [InlineData(typeof(Tarifa), "pk_tarifas")]
    public void Chaves_primarias_tem_nome_explicito_e_id_gerado_no_dominio(Type tipo, string nome)
    {
        var entidade = Entidade(tipo);

        Assert.Equal(nome, entidade.FindPrimaryKey()!.GetName());
        Assert.Equal(ValueGenerated.Never, entidade.FindProperty("Id")!.ValueGenerated);
    }

    [Theory]
    [InlineData(typeof(Estante), "ix_estantes_device_id", new[] { "DeviceId" })]
    [InlineData(typeof(Cofre), "ix_cofres_estante_id_numero", new[] { "EstanteId", "Numero" })]
    [InlineData(typeof(Cofre), "ix_cofres_estante_id_canal_rele", new[] { "EstanteId", "CanalRele" })]
    [InlineData(typeof(Tarifa), "ix_tarifas_tamanho", new[] { "Tamanho" })]
    public void Indices_unicos_do_contrato(Type tipo, string nome, string[] propriedades)
    {
        var indice = Entidade(tipo).GetIndexes().Single(i => i.GetDatabaseName() == nome);

        Assert.True(indice.IsUnique);
        Assert.Equal(propriedades, indice.Properties.Select(p => p.Name));
    }

    [Theory]
    [InlineData(typeof(Estante), "ck_estantes_latitude", "latitude >= -90 AND latitude <= 90")]
    [InlineData(typeof(Estante), "ck_estantes_longitude", "longitude >= -180 AND longitude <= 180")]
    [InlineData(typeof(Estante), "ck_estantes_device_id", "device_id ~ '^est-[a-z0-9]{6}$'")]
    [InlineData(typeof(Cofre), "ck_cofres_numero", "numero > 0")]
    [InlineData(typeof(Cofre), "ck_cofres_canal_rele", "canal_rele >= 0 AND canal_rele <= 15")]
    [InlineData(typeof(Cofre), "ck_cofres_tamanho", "tamanho IN ('pequeno', 'medio', 'grande')")]
    [InlineData(typeof(Cofre), "ck_cofres_status", "status IN ('livre', 'reservado', 'ocupado', 'manutencao')")]
    [InlineData(typeof(Tarifa), "ck_tarifas_tamanho", "tamanho IN ('pequeno', 'medio', 'grande')")]
    [InlineData(typeof(Tarifa), "ck_tarifas_valor_por_hora", "valor_por_hora > 0")]
    [InlineData(typeof(Tarifa), "ck_tarifas_tempo_minimo", "tempo_minimo > 0")]
    public void Check_constraints_do_contrato(Type tipo, string nome, string sql)
    {
        var check = Entidade(tipo).GetCheckConstraints().SingleOrDefault(c => c.Name == nome);

        Assert.NotNull(check);
        Assert.Equal(sql, check.Sql);
    }

    [Fact]
    public void Cofres_referencia_estantes_com_restrict()
    {
        var fk = Entidade(typeof(Cofre)).GetForeignKeys().Single();

        Assert.Equal(typeof(Estante), fk.PrincipalEntityType.ClrType);
        Assert.Equal(nameof(Cofre.EstanteId), fk.Properties.Single().Name);
        Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior);
        Assert.Equal("fk_cofres_estantes_estante_id", fk.GetConstraintName());
    }

    [Theory]
    [InlineData(TamanhoCofre.Pequeno, "pequeno")]
    [InlineData(TamanhoCofre.Medio, "medio")]
    [InlineData(TamanhoCofre.Grande, "grande")]
    public void Tamanho_e_gravado_como_texto_minusculo(TamanhoCofre tamanho, string texto)
    {
        foreach (var tipo in new[] { typeof(Cofre), typeof(Tarifa) })
        {
            var conversor = Entidade(tipo).FindProperty("Tamanho")!.GetValueConverter()!;

            Assert.Equal(texto, conversor.ConvertToProvider(tamanho));
            Assert.Equal(tamanho, conversor.ConvertFromProvider(texto));
        }
    }

    [Theory]
    [InlineData(StatusCofre.Livre, "livre")]
    [InlineData(StatusCofre.Reservado, "reservado")]
    [InlineData(StatusCofre.Ocupado, "ocupado")]
    [InlineData(StatusCofre.Manutencao, "manutencao")]
    public void Status_e_gravado_como_texto_minusculo(StatusCofre status, string texto)
    {
        var conversor = Entidade(typeof(Cofre)).FindProperty(nameof(Cofre.Status))!.GetValueConverter()!;

        Assert.Equal(texto, conversor.ConvertToProvider(status));
        Assert.Equal(status, conversor.ConvertFromProvider(texto));
    }

    [Fact]
    public void Status_desconhecido_no_banco_falha_ao_ler()
    {
        var conversor = Entidade(typeof(Cofre)).FindProperty(nameof(Cofre.Status))!.GetValueConverter()!;

        Assert.Throws<InvalidOperationException>(() => conversor.ConvertFromProvider("quebrado"));
    }

    [Fact]
    public void Estantes_usam_xmin_como_token_de_concorrencia()
    {
        var estante = Entidade(typeof(Estante));
        var versao = Assert.Single(estante.GetProperties(), p => p.IsConcurrencyToken);

        Assert.Equal("Versao", versao.Name);
        Assert.True(versao.IsShadowProperty());
        Assert.Equal(typeof(uint), versao.ClrType);
        Assert.Equal("xmin", versao.GetColumnName());
        Assert.Equal("xid", versao.GetColumnType());
        Assert.Equal(ValueGenerated.OnAddOrUpdate, versao.ValueGenerated);
    }

    [Fact]
    public void Cofres_ainda_nao_tem_token_de_concorrencia()
    {
        // A reserva (T-0702) decide o controle de concorrência.
        Assert.DoesNotContain(Entidade(typeof(Cofre)).GetProperties(), p => p.IsConcurrencyToken);
    }

    private IEntityType Entidade(Type tipo) =>
        _modelo.FindEntityType(tipo) ?? throw new InvalidOperationException($"{tipo.Name} não está no modelo.");

    private static string[] Colunas(IEntityType entidade) =>
        entidade.GetProperties().Select(p => p.GetColumnName()).Order(StringComparer.Ordinal).ToArray();

    private static string? Tipo(IEntityType entidade, string propriedade) =>
        entidade.FindProperty(propriedade)!.GetColumnType();
}
