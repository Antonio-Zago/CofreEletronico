using CofreEletronico.Domain.Estantes;
using CofreEletronico.Domain.Tarifas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CofreEletronico.Infrastructure.Persistencia.Configuracoes;

/// <summary>Tabela <c>tarifas</c> (contrato em docs/ARQUITETURA.md): uma por tamanho, valor em numeric(10,2).</summary>
internal sealed class TarifaConfiguracao : IEntityTypeConfiguration<Tarifa>
{
    public void Configure(EntityTypeBuilder<Tarifa> builder)
    {
        builder.ToTable("tarifas", t =>
        {
            t.HasCheckConstraint("ck_tarifas_tamanho", EnumTexto.CheckValores<TamanhoCofre>("tamanho"));
            t.HasCheckConstraint("ck_tarifas_valor_por_hora", "valor_por_hora > 0");
            t.HasCheckConstraint("ck_tarifas_tempo_minimo", "tempo_minimo > 0");
        });

        builder.HasKey(t => t.Id).HasName("pk_tarifas");
        builder.Property(t => t.Id).ValueGeneratedNever(); // gerado no domínio

        builder.Property(t => t.Tamanho)
            .HasConversion(EnumTexto.Conversor<TamanhoCofre>())
            .HasMaxLength(EnumTexto.TamanhoMaximo)
            .IsRequired();
        builder.Property(t => t.ValorPorHora).HasPrecision(10, 2).IsRequired();
        builder.Property(t => t.TempoMinimo).IsRequired();

        builder.HasIndex(t => t.Tamanho).IsUnique().HasDatabaseName("ix_tarifas_tamanho");
    }
}
