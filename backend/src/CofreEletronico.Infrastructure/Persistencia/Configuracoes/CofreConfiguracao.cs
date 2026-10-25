using CofreEletronico.Domain.Estantes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CofreEletronico.Infrastructure.Persistencia.Configuracoes;

/// <summary>
/// Tabela <c>cofres</c> (contrato em docs/ARQUITETURA.md). Enums como texto minúsculo com CHECK de valores.
/// Token de concorrência: coluna de sistema <c>xmin</c> (shadow property <see cref="PropriedadeVersao"/>).
/// </summary>
internal sealed class CofreConfiguracao : IEntityTypeConfiguration<Cofre>
{
    /// <summary>Nome dos índices únicos (o repositório traduz o 23505 de cada um).</summary>
    public const string IndiceNumero = "ix_cofres_estante_id_numero";

    public const string IndiceCanalRele = "ix_cofres_estante_id_canal_rele";

    /// <summary>Shadow property do token de concorrência (coluna de sistema <c>xmin</c>).</summary>
    public const string PropriedadeVersao = "Versao";

    public void Configure(EntityTypeBuilder<Cofre> builder)
    {
        builder.ToTable("cofres", t =>
        {
            t.HasCheckConstraint("ck_cofres_numero", "numero > 0");
            t.HasCheckConstraint(
                "ck_cofres_canal_rele",
                $"canal_rele >= {Cofre.CanalMinimo} AND canal_rele <= {Cofre.CanalMaximo}");
            t.HasCheckConstraint("ck_cofres_tamanho", EnumTexto.CheckValores<TamanhoCofre>("tamanho"));
            t.HasCheckConstraint("ck_cofres_status", EnumTexto.CheckValores<StatusCofre>("status"));
        });

        builder.HasKey(c => c.Id).HasName("pk_cofres");
        builder.Property(c => c.Id).ValueGeneratedNever(); // gerado no domínio

        builder.Property(c => c.Numero).IsRequired();
        builder.Property(c => c.CanalRele).IsRequired();
        builder.Property(c => c.Tamanho)
            .HasConversion(EnumTexto.Conversor<TamanhoCofre>())
            .HasMaxLength(EnumTexto.TamanhoMaximo)
            .IsRequired();
        builder.Property(c => c.Status)
            .HasConversion(EnumTexto.Conversor<StatusCofre>())
            .HasMaxLength(EnumTexto.TamanhoMaximo)
            .IsRequired();
        builder.Property(c => c.Ativo).IsRequired();

        // Os dois índices começam por estante_id, então também servem às buscas por estante e à FK.
        builder.HasIndex(c => new { c.EstanteId, c.Numero }).IsUnique().HasDatabaseName(IndiceNumero);
        builder.HasIndex(c => new { c.EstanteId, c.CanalRele }).IsUnique().HasDatabaseName(IndiceCanalRele);

        // Concorrência otimista pela coluna de sistema xmin (igual a estantes): duas alterações simultâneas do mesmo
        // cofre não se sobrescrevem; a segunda recebe DbUpdateConcurrencyException (409 na Api). Um UPDATE condicional
        // (ExecuteUpdate, ex.: reserva na T-0702) também muda o xmin, então a edição do admin que leu antes falha.
        // xmin já existe em toda tabela: a migration não cria coluna.
        builder.Property<uint>(PropriedadeVersao)
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        // Exclusão é lógica: apagar fisicamente uma estante com cofres é bloqueado no banco.
        builder.HasOne<Estante>().WithMany()
            .HasForeignKey(c => c.EstanteId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_cofres_estantes_estante_id");
    }
}
