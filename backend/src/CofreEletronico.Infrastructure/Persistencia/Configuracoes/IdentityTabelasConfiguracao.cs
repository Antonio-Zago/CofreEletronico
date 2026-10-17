using CofreEletronico.Infrastructure.Persistencia.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CofreEletronico.Infrastructure.Persistencia.Configuracoes;

// Tabelas auxiliares do ASP.NET Identity com os nomes do contrato (docs/ARQUITETURA.md).
// Só usuarios_papeis tem colunas definidas no contrato; nas demais, snake_case automático.
//
// Os nomes de FK/índice abaixo são explícitos porque o EFCore.NamingConventions calcula alguns
// deles antes do ToTable/HasColumnName (geraria "fk_usuarios_claims_asp_net_users_user_id",
// "ix_usuarios_papeis_role_id" etc.). As relações em si são as do Identity: HasOne/WithMany
// com a mesma FK reaproveita a relação existente, só renomeando a constraint.

/// <summary>Tabela <c>usuarios_papeis</c> (N:N, PK composta definida pelo Identity).</summary>
internal sealed class UsuarioPapelConfiguracao : IEntityTypeConfiguration<IdentityUserRole<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUserRole<Guid>> builder)
    {
        builder.ToTable("usuarios_papeis");
        builder.Property(up => up.UserId).HasColumnName("usuario_id");
        builder.Property(up => up.RoleId).HasColumnName("papel_id");

        builder.HasOne<Usuario>().WithMany().HasForeignKey(up => up.UserId)
            .HasConstraintName("fk_usuarios_papeis_usuarios_usuario_id");
        builder.HasOne<Papel>().WithMany().HasForeignKey(up => up.RoleId)
            .HasConstraintName("fk_usuarios_papeis_papeis_papel_id");
        builder.HasIndex(up => up.RoleId).HasDatabaseName("ix_usuarios_papeis_papel_id");
    }
}

/// <summary>Tabela <c>usuarios_claims</c>.</summary>
internal sealed class UsuarioClaimConfiguracao : IEntityTypeConfiguration<IdentityUserClaim<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUserClaim<Guid>> builder)
    {
        builder.ToTable("usuarios_claims");
        builder.HasOne<Usuario>().WithMany().HasForeignKey(uc => uc.UserId)
            .HasConstraintName("fk_usuarios_claims_usuarios_user_id");
    }
}

/// <summary>Tabela <c>usuarios_logins</c>.</summary>
internal sealed class UsuarioLoginConfiguracao : IEntityTypeConfiguration<IdentityUserLogin<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUserLogin<Guid>> builder)
    {
        builder.ToTable("usuarios_logins");
        builder.HasOne<Usuario>().WithMany().HasForeignKey(ul => ul.UserId)
            .HasConstraintName("fk_usuarios_logins_usuarios_user_id");
    }
}

/// <summary>Tabela <c>usuarios_tokens</c>.</summary>
internal sealed class UsuarioTokenConfiguracao : IEntityTypeConfiguration<IdentityUserToken<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUserToken<Guid>> builder)
    {
        builder.ToTable("usuarios_tokens");
        builder.HasOne<Usuario>().WithMany().HasForeignKey(ut => ut.UserId)
            .HasConstraintName("fk_usuarios_tokens_usuarios_user_id");
    }
}

/// <summary>Tabela <c>papeis_claims</c>.</summary>
internal sealed class PapelClaimConfiguracao : IEntityTypeConfiguration<IdentityRoleClaim<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityRoleClaim<Guid>> builder)
    {
        builder.ToTable("papeis_claims");
        builder.HasOne<Papel>().WithMany().HasForeignKey(pc => pc.RoleId)
            .HasConstraintName("fk_papeis_claims_papeis_role_id");
    }
}
