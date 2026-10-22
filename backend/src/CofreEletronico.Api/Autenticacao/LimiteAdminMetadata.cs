namespace CofreEletronico.Api.Autenticacao;

/// <summary>
/// Marca os endpoints do grupo <c>/admin</c> para o limite geral por administrador (<see cref="RateLimitOpcoes.Admin"/>),
/// aplicado pelo GlobalLimiter. Fica como metadado, e não como política nomeada, porque o ASP.NET Core aplica uma
/// única política nomeada por endpoint: assim as rotas sensíveis somam o limite geral E o da política
/// <see cref="RateLimitOpcoes.PoliticaAdminSensivel"/>.
/// </summary>
public sealed class LimiteAdminMetadata
{
    public static readonly LimiteAdminMetadata Instancia = new();

    private LimiteAdminMetadata()
    {
    }
}
