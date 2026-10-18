namespace CofreEletronico.Application.Autenticacao.Portas;

/// <summary>
/// Gera o valor opaco do refresh token e calcula o hash que vai para o banco. Implementado na Infrastructure.
/// </summary>
public interface IGeradorRefreshToken
{
    /// <summary>Validade de um refresh token a partir da emissão (truncada no fim da sessão).</summary>
    TimeSpan Validade { get; }

    /// <summary>Duração máxima de uma sessão (família), contada do login, independente das rotações.</summary>
    TimeSpan ValidadeSessao { get; }

    RefreshTokenGerado Gerar();

    /// <summary>Hash do valor recebido do cliente, para busca no banco.</summary>
    string CalcularHash(string valor);
}

/// <summary><see cref="Valor"/> vai só para o cliente; <see cref="Hash"/> vai só para o banco.</summary>
public sealed record RefreshTokenGerado(string Valor, string Hash);
