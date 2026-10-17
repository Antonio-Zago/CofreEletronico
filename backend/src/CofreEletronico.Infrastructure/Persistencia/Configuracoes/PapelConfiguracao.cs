using CofreEletronico.Application.Autorizacao;
using CofreEletronico.Infrastructure.Persistencia.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CofreEletronico.Infrastructure.Persistencia.Configuracoes;

/// <summary>
/// Tabela <c>papeis</c> com o seed dos papéis fixos.
/// Ids e ConcurrencyStamp são constantes para o HasData não gerar migration nova a cada build.
/// </summary>
internal sealed class PapelConfiguracao : IEntityTypeConfiguration<Papel>
{
    public static readonly Guid UsuarioId = new("5b0f6c1e-3d7a-4c8e-9f21-0a6d2e4b7c11");
    public static readonly Guid AdminId = new("a3e9d2f4-8b1c-4e6a-b5d7-2c4f6e8a0b22");

    public void Configure(EntityTypeBuilder<Papel> builder)
    {
        builder.ToTable("papeis");

        // Coluna do contrato (papeis: id, nome); normalized_name/concurrency_stamp seguem o snake_case automático.
        builder.Property(p => p.Name).HasColumnName("nome");

        // O Identity cria este índice (único) com nome próprio; aqui só renomeamos para snake_case.
        builder.HasIndex(p => p.NormalizedName).HasDatabaseName("ix_papeis_normalized_name");

        builder.HasData(
            new Papel
            {
                Id = UsuarioId,
                Name = Papeis.Usuario,
                NormalizedName = "USUARIO",
                ConcurrencyStamp = "b7d3c1a2-6e4f-4a8b-9c0d-1e2f3a4b5c61",
            },
            new Papel
            {
                Id = AdminId,
                Name = Papeis.Admin,
                NormalizedName = "ADMIN",
                ConcurrencyStamp = "c8e4d2b3-7f5a-4b9c-8d1e-2f3a4b5c6d72",
            });
    }
}
