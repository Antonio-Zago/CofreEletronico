using CofreEletronico.Domain.Estantes;
using CofreEletronico.Infrastructure.Criptografia;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace CofreEletronico.Infrastructure.Persistencia.Configuracoes;

/// <summary>
/// Tabela <c>estantes</c> (contrato em docs/ARQUITETURA.md). Nomes de PK, índice e CHECK explícitos.
/// O segredo HMAC é configurado à parte (<see cref="ConfigurarSegredoHmac"/>), pelo <see cref="CofreDbContext"/>,
/// porque depende do cifrador da DI.
/// </summary>
internal sealed class EstanteConfiguracao : IEntityTypeConfiguration<Estante>
{
    public const string Tabela = "estantes";
    public const string ColunaSegredoHmac = "segredo_hmac";

    /// <summary>Nome da propriedade no modelo EF (use com <c>EF.Property</c> ou <c>Entry.Property</c>).</summary>
    public const string PropriedadeSegredoHmac = "SegredoHmac";

    private const string CampoSegredoHmac = "_segredoHmac";

    public void Configure(EntityTypeBuilder<Estante> builder)
    {
        builder.ToTable(Tabela, t =>
        {
            t.HasCheckConstraint("ck_estantes_latitude", "latitude >= -90 AND latitude <= 90");
            t.HasCheckConstraint("ck_estantes_longitude", "longitude >= -180 AND longitude <= 180");
            t.HasCheckConstraint("ck_estantes_device_id", "device_id ~ '^est-[a-z0-9]{6}$'");
        });

        builder.HasKey(e => e.Id).HasName("pk_estantes");
        builder.Property(e => e.Id).ValueGeneratedNever(); // gerado no domínio

        builder.Property(e => e.Nome).HasMaxLength(Estante.NomeTamanhoMaximo).IsRequired();
        builder.Property(e => e.Endereco).HasMaxLength(Estante.EnderecoTamanhoMaximo).IsRequired();
        builder.Property(e => e.Latitude).HasColumnType("double precision").IsRequired();
        builder.Property(e => e.Longitude).HasColumnType("double precision").IsRequired();
        builder.Property(e => e.DeviceId).HasMaxLength(Estante.DeviceIdTamanho).IsRequired();
        builder.Property(e => e.Online).IsRequired();
        builder.Property(e => e.UltimoHeartbeat);
        builder.Property(e => e.Ativa).IsRequired();
        builder.Property(e => e.CriadoEm).IsRequired();

        builder.HasIndex(e => e.DeviceId).IsUnique().HasDatabaseName("ix_estantes_device_id");

        // Sem índice em `ativa`: booleano de baixa cardinalidade (quase todas as estantes ativas) não ajuda o
        // planner. A busca por proximidade (T-0501) define o índice que fizer sentido para ela.
    }

    /// <summary>
    /// <c>segredo_hmac</c> (text): a entidade guarda o segredo em claro (campo <c>_segredoHmac</c>) e o banco
    /// recebe <c>v1:base64(...)</c> do <paramref name="cifrador"/>. Como o nonce é aleatório, o valor gravado muda
    /// a cada escrita: nunca filtre consultas por esta coluna.
    /// </summary>
    internal static void ConfigurarSegredoHmac(EntityTypeBuilder<Estante> builder, ICifradorSegredos cifrador)
    {
        var conversor = new ValueConverter<byte[], string>(
            claro => cifrador.Cifrar(claro),
            cifrado => cifrador.Decifrar(cifrado));

        // Comparação por conteúdo e snapshot por cópia: o change tracking não depende da referência do array.
        var comparador = new ValueComparer<byte[]>(
            (a, b) => ComparadorBytes.Iguais(a, b),
            v => ComparadorBytes.Hash(v),
            v => ComparadorBytes.Copiar(v));

        // Propriedade privada na entidade (para fora só ObterSegredoHmac()), para que serializadores não a encontrem.
        // Mapeada pelo nome, com acesso pelo campo de apoio.
        builder.Property<byte[]>(PropriedadeSegredoHmac)
            .HasField(CampoSegredoHmac)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnName(ColunaSegredoHmac)
            .HasColumnType("text")
            .IsRequired()
            .HasConversion(conversor, comparador);
    }
}

internal static class ComparadorBytes
{
    public static bool Iguais(byte[]? a, byte[]? b) =>
        ReferenceEquals(a, b) || (a is not null && b is not null && a.AsSpan().SequenceEqual(b));

    public static int Hash(byte[]? v)
    {
        if (v is null)
            return 0;

        var hash = new HashCode();
        hash.AddBytes(v);
        return hash.ToHashCode();
    }

    public static byte[] Copiar(byte[]? v) => v is null ? [] : (byte[])v.Clone();
}
