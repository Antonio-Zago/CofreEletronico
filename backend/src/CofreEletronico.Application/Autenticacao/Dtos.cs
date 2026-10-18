namespace CofreEletronico.Application.Autenticacao;

/// <summary>Corpo de resposta com tokens (registro, login e refresh). Datas em UTC.</summary>
public sealed record TokensDto(
    string AccessToken,
    DateTimeOffset AccessTokenExpiraEm,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiraEm);

public sealed record RegistrarUsuarioComando(string Nome, string Email, string Senha, string? Telefone);

public sealed record LoginComando(string Email, string Senha);

public sealed record RenovarTokensComando(string RefreshToken);

public sealed record LogoutComando(string RefreshToken);
