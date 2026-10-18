namespace CofreEletronico.Application.Autenticacao.Portas;

/// <summary>
/// Porta para o armazenamento de usuários e credenciais (implementada com ASP.NET Identity na Infrastructure).
/// </summary>
public interface IServicoIdentidade
{
    /// <summary>
    /// Cria o usuário e atribui o papel, de forma atômica. E-mail já existente (inclusive por corrida,
    /// via índice único) resulta em <see cref="StatusCriacaoUsuario.EmailEmUso"/>.
    /// </summary>
    Task<ResultadoCriacaoUsuario> CriarAsync(NovoUsuario dados, string papel, CancellationToken cancellationToken);

    /// <summary>
    /// Verifica e-mail e senha aplicando o bloqueio por tentativas. Devolve <c>null</c> para e-mail
    /// inexistente, senha errada ou conta bloqueada, sem distinguir o motivo.
    /// </summary>
    Task<UsuarioAutenticado?> VerificarCredenciaisAsync(string email, string senha, CancellationToken cancellationToken);

    /// <summary>Dados atuais do usuário (e-mail e papéis) ou <c>null</c> se ele não existir mais.</summary>
    Task<UsuarioAutenticado?> ObterAsync(Guid usuarioId, CancellationToken cancellationToken);
}

/// <summary>Dados de cadastro já normalizados (sem espaços nas pontas, telefone vazio = nulo).</summary>
public sealed record NovoUsuario(string Nome, string Email, string Senha, string? Telefone);

/// <summary>Usuário identificado, com o necessário para emitir o access token.</summary>
public sealed record UsuarioAutenticado(Guid Id, string Email, IReadOnlyCollection<string> Papeis);

public enum StatusCriacaoUsuario
{
    Criado,
    EmailEmUso,
    Invalido,
}

/// <summary>Resultado da criação. <see cref="Erros"/> é preenchido só quando <see cref="StatusCriacaoUsuario.Invalido"/>.</summary>
public sealed record ResultadoCriacaoUsuario(
    StatusCriacaoUsuario Status,
    UsuarioAutenticado? Usuario,
    IReadOnlyDictionary<string, string[]> Erros)
{
    private static readonly IReadOnlyDictionary<string, string[]> SemErros = new Dictionary<string, string[]>();

    public static ResultadoCriacaoUsuario Criado(UsuarioAutenticado usuario) => new(StatusCriacaoUsuario.Criado, usuario, SemErros);

    public static ResultadoCriacaoUsuario EmailEmUso() => new(StatusCriacaoUsuario.EmailEmUso, null, SemErros);

    public static ResultadoCriacaoUsuario Invalido(IReadOnlyDictionary<string, string[]> erros) =>
        new(StatusCriacaoUsuario.Invalido, null, erros);
}
