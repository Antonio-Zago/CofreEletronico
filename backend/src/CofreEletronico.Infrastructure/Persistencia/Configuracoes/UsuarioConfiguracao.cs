using CofreEletronico.Infrastructure.Persistencia.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CofreEletronico.Infrastructure.Persistencia.Configuracoes;

/// <summary>
/// Tabela <c>usuarios</c>. Roda depois do <c>base.OnModelCreating</c> do Identity,
/// então os nomes aqui sobrescrevem os padrões (AspNetUsers, EmailIndex etc.).
/// As demais colunas técnicas do Identity ficam no snake_case automático.
/// </summary>
internal sealed class UsuarioConfiguracao : IEntityTypeConfiguration<Usuario>
{
    public const int NomeTamanhoMaximo = 150;
    public const int CpfTamanho = 11;
    public const int TelefoneTamanhoMaximo = 20;

    public void Configure(EntityTypeBuilder<Usuario> builder)
    {
        // Defesa em profundidade: o banco aceita só 11 dígitos (dígitos verificadores são validados na T-0304).
        builder.ToTable("usuarios", t =>
            t.HasCheckConstraint("ck_usuarios_cpf_digitos", "cpf ~ '^[0-9]{11}$'"));

        builder.Property(u => u.Nome).HasMaxLength(NomeTamanhoMaximo).IsRequired();
        builder.Property(u => u.Cpf).HasMaxLength(CpfTamanho);
        builder.Property(u => u.CriadoEm).IsRequired();

        // Colunas do contrato sobre propriedades herdadas do Identity.
        builder.Property(u => u.PasswordHash).HasColumnName("senha_hash");
        builder.Property(u => u.PhoneNumber).HasColumnName("telefone").HasMaxLength(TelefoneTamanhoMaximo);

        // E-mail obrigatório: no PostgreSQL um índice único aceita vários NULL.
        builder.Property(u => u.Email).IsRequired();
        builder.Property(u => u.NormalizedEmail).IsRequired();

        // Lockout ligado por padrão no banco, inclusive para usuários inseridos fora do UserManager.
        // Efeito colateral aceito: como false é o sentinela do CLR, um insert via EF com false também recebe true.
        builder.Property(u => u.LockoutEnabled).HasDefaultValue(true);

        // O Identity cria este índice (único) com nome próprio; aqui só renomeamos para snake_case.
        builder.HasIndex(u => u.NormalizedUserName).HasDatabaseName("ix_usuarios_normalized_user_name");

        // O padrão do Identity para o e-mail é NÃO único: o banco passa a garantir a unicidade.
        builder.HasIndex(u => u.NormalizedEmail)
            .HasDatabaseName("ix_usuarios_normalized_email")
            .IsUnique();

        // CPF é opcional, mas único quando informado.
        builder.HasIndex(u => u.Cpf)
            .HasDatabaseName("ix_usuarios_cpf")
            .IsUnique()
            .HasFilter("cpf IS NOT NULL");
    }
}
