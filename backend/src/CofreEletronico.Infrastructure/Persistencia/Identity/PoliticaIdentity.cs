using Microsoft.AspNetCore.Identity;

namespace CofreEletronico.Infrastructure.Persistencia.Identity;

/// <summary>
/// Política do ASP.NET Identity. Valores fixos em código (não em appsettings) para que
/// não possam ser afrouxados por configuração de ambiente.
/// </summary>
public static class PoliticaIdentity
{
    /// <summary>Tamanho mínimo da senha.</summary>
    public const int SenhaTamanhoMinimo = 8;

    /// <summary>Tentativas de login erradas antes do bloqueio.</summary>
    public const int LockoutMaximoTentativas = 5;

    /// <summary>Duração do bloqueio após exceder as tentativas.</summary>
    public static readonly TimeSpan LockoutDuracao = TimeSpan.FromMinutes(15);

    public static void Configurar(IdentityOptions options)
    {
        // E-mail único (reforçado no banco pelo índice único em normalized_email).
        options.User.RequireUniqueEmail = true;

        // Senha forte: >= 8 caracteres com dígito, minúscula e maiúscula; caractere especial não é exigido.
        options.Password.RequiredLength = SenhaTamanhoMinimo;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = false;

        // Lockout: 5 tentativas -> 15 minutos, já habilitado para usuários novos.
        options.Lockout.MaxFailedAccessAttempts = LockoutMaximoTentativas;
        options.Lockout.DefaultLockoutTimeSpan = LockoutDuracao;
        options.Lockout.AllowedForNewUsers = true;
    }
}
