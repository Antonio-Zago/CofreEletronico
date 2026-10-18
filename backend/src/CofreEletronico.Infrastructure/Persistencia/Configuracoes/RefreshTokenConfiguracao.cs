using CofreEletronico.Domain.Autenticacao;
using CofreEletronico.Infrastructure.Autenticacao;
using CofreEletronico.Infrastructure.Persistencia.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CofreEletronico.Infrastructure.Persistencia.Configuracoes;

/// <summary>
/// Tabela <c>refresh_tokens</c> (contrato em docs/ARQUITETURA.md). Nomes de índice e FK explícitos,
/// como nas demais tabelas, para não depender da ordem em que a convenção snake_case os calcula.
/// </summary>
internal sealed class RefreshTokenConfiguracao : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever(); // gerado no domínio

        builder.Property(t => t.TokenHash).HasMaxLength(GeradorRefreshTokenSeguro.HashTamanho).IsRequired();
        builder.Property(t => t.CriadoEm).IsRequired();
        builder.Property(t => t.ExpiraEm).IsRequired();
        builder.Property(t => t.SessaoExpiraEm).IsRequired();

        builder.Ignore(t => t.FoiRotacionado);

        builder.HasIndex(t => t.TokenHash).IsUnique().HasDatabaseName("ix_refresh_tokens_token_hash");
        builder.HasIndex(t => t.FamiliaId).HasDatabaseName("ix_refresh_tokens_familia_id");
        builder.HasIndex(t => t.UsuarioId).HasDatabaseName("ix_refresh_tokens_usuario_id");
        builder.HasIndex(t => t.SubstituidoPorId).HasDatabaseName("ix_refresh_tokens_substituido_por_id");

        // Apagar o usuário apaga as sessões dele.
        builder.HasOne<Usuario>().WithMany()
            .HasForeignKey(t => t.UsuarioId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_refresh_tokens_usuarios_usuario_id");

        // Auto-FK da rotação. SET NULL permite a futura limpeza de tokens expirados apagar em qualquer ordem.
        builder.HasOne<RefreshToken>().WithMany()
            .HasForeignKey(t => t.SubstituidoPorId)
            .OnDelete(DeleteBehavior.SetNull)
            .HasConstraintName("fk_refresh_tokens_refresh_tokens_substituido_por_id");
    }
}
