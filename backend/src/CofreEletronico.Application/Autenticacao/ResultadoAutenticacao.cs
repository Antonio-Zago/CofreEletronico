namespace CofreEletronico.Application.Autenticacao;

public enum StatusAutenticacao
{
    Sucesso,

    /// <summary>Dados de entrada rejeitados (ex.: senha fraca). Ver <see cref="ResultadoAutenticacao.Erros"/>.</summary>
    Validacao,

    /// <summary>E-mail já cadastrado.</summary>
    EmailEmUso,

    /// <summary>E-mail inexistente, senha errada ou conta bloqueada (indistinguíveis de propósito).</summary>
    CredenciaisInvalidas,

    /// <summary>Refresh token inexistente, expirado, revogado ou reutilizado.</summary>
    TokenInvalido,
}

/// <summary>Resultado tipado dos casos de uso de autenticação; a Api traduz em status HTTP.</summary>
public sealed class ResultadoAutenticacao
{
    private static readonly IReadOnlyDictionary<string, string[]> SemErros = new Dictionary<string, string[]>();

    private ResultadoAutenticacao(StatusAutenticacao status, TokensDto? tokens, IReadOnlyDictionary<string, string[]> erros)
    {
        Status = status;
        Tokens = tokens;
        Erros = erros;
    }

    public StatusAutenticacao Status { get; }

    /// <summary>Preenchido só em <see cref="StatusAutenticacao.Sucesso"/>.</summary>
    public TokensDto? Tokens { get; }

    /// <summary>Erros por campo (camelCase), preenchido só em <see cref="StatusAutenticacao.Validacao"/>.</summary>
    public IReadOnlyDictionary<string, string[]> Erros { get; }

    public static ResultadoAutenticacao Sucesso(TokensDto tokens) => new(StatusAutenticacao.Sucesso, tokens, SemErros);

    public static ResultadoAutenticacao Validacao(IReadOnlyDictionary<string, string[]> erros) =>
        new(StatusAutenticacao.Validacao, null, erros);

    public static ResultadoAutenticacao Falha(StatusAutenticacao status)
    {
        if (status is StatusAutenticacao.Sucesso or StatusAutenticacao.Validacao)
            throw new ArgumentException("Use Sucesso() ou Validacao() para esse status.", nameof(status));

        return new(status, null, SemErros);
    }
}
