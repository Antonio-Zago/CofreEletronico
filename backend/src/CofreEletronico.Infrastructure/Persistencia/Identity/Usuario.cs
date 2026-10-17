using Microsoft.AspNetCore.Identity;

namespace CofreEletronico.Infrastructure.Persistencia.Identity;

/// <summary>
/// Usuário do sistema (ASP.NET Identity, chave Guid). Existe só na Infrastructure:
/// o domínio referencia apenas <c>usuario_id</c>. Mapeamento em <c>Configuracoes/UsuarioConfiguracao</c>.
/// O telefone é o <c>PhoneNumber</c> herdado do Identity (coluna <c>telefone</c>, opcional, até 20 caracteres).
/// </summary>
public class Usuario : IdentityUser<Guid>
{
    /// <summary>Nome completo (obrigatório, até 150 caracteres).</summary>
    [PersonalData]
    public string Nome { get; set; } = string.Empty;

    /// <summary>
    /// CPF com 11 dígitos, sem pontuação. Opcional: pedido só quando necessário (ex.: Pix), por LGPD.
    /// A validação dos dígitos verificadores é feita na T-0304.
    /// </summary>
    [PersonalData]
    public string? Cpf { get; set; }

    /// <summary>
    /// Instante de criação, sempre em UTC. DateTimeOffset mapeia para <c>timestamptz</c> no Npgsql,
    /// que rejeita offset diferente de zero na escrita: o UTC é garantido pelo próprio tipo
    /// (com DateTime dependeríamos de Kind=Utc, que se perde facilmente). Combina com TimeProvider.GetUtcNow().
    /// </summary>
    public DateTimeOffset CriadoEm { get; set; }
}
