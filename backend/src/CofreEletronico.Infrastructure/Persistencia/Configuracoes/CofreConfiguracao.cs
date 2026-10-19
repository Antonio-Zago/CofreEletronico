using CofreEletronico.Domain.Estantes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CofreEletronico.Infrastructure.Persistencia.Configuracoes;

/// <summary>
/// Tabela <c>cofres</c> (contrato em docs/ARQUITETURA.md). Enums como texto minúsculo com CHECK de valores.
/// Sem token de concorrência por enquanto: a reserva (T-0702) define o controle de corrida.
/// </summary>
internal sealed class CofreConfiguracao : IEntityTypeConfiguration<Cofre>
{
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
        builder.HasIndex(c => new { c.EstanteId, c.Numero }).IsUnique().HasDatabaseName("ix_cofres_estante_id_numero");
        builder.HasIndex(c => new { c.EstanteId, c.CanalRele }).IsUnique().HasDatabaseName("ix_cofres_estante_id_canal_rele");

        // Exclusão é lógica: apagar fisicamente uma estante com cofres é bloqueado no banco.
        builder.HasOne<Estante>().WithMany()
            .HasForeignKey(c => c.EstanteId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_cofres_estantes_estante_id");
    }
}
