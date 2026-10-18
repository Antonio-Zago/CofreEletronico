using CofreEletronico.Application.Autenticacao.Portas;

namespace CofreEletronico.Application.Autenticacao;

/// <summary>
/// POST /auth/login: verifica as credenciais (com bloqueio por tentativas) e abre uma nova sessão.
/// E-mail inexistente, senha errada e conta bloqueada dão o mesmo resultado.
/// </summary>
public sealed class AutenticarUsuario
{
    private readonly IServicoIdentidade _identidade;
    private readonly EmissorTokens _emissor;

    public AutenticarUsuario(IServicoIdentidade identidade, EmissorTokens emissor)
    {
        _identidade = identidade;
        _emissor = emissor;
    }

    public async Task<ResultadoAutenticacao> ExecutarAsync(LoginComando comando, CancellationToken cancellationToken = default)
    {
        var usuario = await _identidade.VerificarCredenciaisAsync(comando.Email.Trim(), comando.Senha, cancellationToken);
        if (usuario is null)
            return ResultadoAutenticacao.Falha(StatusAutenticacao.CredenciaisInvalidas);

        return ResultadoAutenticacao.Sucesso(await _emissor.EmitirNovaSessaoAsync(usuario, cancellationToken));
    }
}
