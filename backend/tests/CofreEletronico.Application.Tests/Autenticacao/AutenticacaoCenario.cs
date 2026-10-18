using CofreEletronico.Application.Autenticacao;
using Microsoft.Extensions.Logging.Abstractions;

namespace CofreEletronico.Application.Tests.Autenticacao;

/// <summary>Monta os casos de uso de autenticação com fakes compartilhados.</summary>
internal sealed class AutenticacaoCenario
{
    public static readonly DateTimeOffset Inicio = new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);

    public AutenticacaoCenario()
    {
        Relogio = new RelogioFalso(Inicio);
        AccessTokens = new GeradorAccessTokenFalso(Relogio);
        var emissor = new EmissorTokens(AccessTokens, RefreshTokens, Repositorio, Relogio);

        Registrar = new RegistrarUsuario(Identidade, emissor);
        Login = new AutenticarUsuario(Identidade, emissor);
        Refresh = new RenovarTokens(Identidade, RefreshTokens, Repositorio, emissor, Relogio, NullLogger<RenovarTokens>.Instance);
        Logout = new EncerrarSessao(RefreshTokens, Repositorio, Relogio);
    }

    public RelogioFalso Relogio { get; }

    public ServicoIdentidadeFalso Identidade { get; } = new();

    public GeradorAccessTokenFalso AccessTokens { get; }

    public GeradorRefreshTokenFalso RefreshTokens { get; } = new();

    public RefreshTokenRepositorioFalso Repositorio { get; } = new();

    public RegistrarUsuario Registrar { get; }

    public AutenticarUsuario Login { get; }

    public RenovarTokens Refresh { get; }

    public EncerrarSessao Logout { get; }

    /// <summary>Usuário cadastrado e logado; devolve os tokens da sessão.</summary>
    public async Task<TokensDto> LogarAsync(string email = "ana@exemplo.com", string senha = "Senha123")
    {
        Identidade.Adicionar(email, senha, "Usuario");
        var resultado = await Login.ExecutarAsync(new LoginComando(email, senha));
        return resultado.Tokens!;
    }
}
