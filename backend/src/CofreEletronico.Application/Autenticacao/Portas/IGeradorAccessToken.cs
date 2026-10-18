namespace CofreEletronico.Application.Autenticacao.Portas;

/// <summary>Emite o access token (JWT) do usuário. Implementado na Infrastructure.</summary>
public interface IGeradorAccessToken
{
    AccessTokenGerado Gerar(UsuarioAutenticado usuario);
}

public sealed record AccessTokenGerado(string Token, DateTimeOffset ExpiraEm);
