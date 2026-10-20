namespace CofreEletronico.Api.Autorizacao;

/// <summary>
/// Nomes das políticas de autorização registradas em <c>AutenticacaoExtensions.AddAutenticacaoJwt</c>.
/// Uso: <c>grupo.RequireAuthorization(Politicas.Admin)</c> (ex.: grupo <c>/admin</c>, T-0406).
/// Endpoint sem metadado de autorização cai na FallbackPolicy (usuário autenticado); rotas públicas
/// precisam de <c>AllowAnonymous()</c> explícito.
/// </summary>
public static class Politicas
{
    /// <summary>Usuário autenticado com o papel <c>Admin</c>. Sem token: 401; sem o papel: 403.</summary>
    public const string Admin = "Admin";
}
