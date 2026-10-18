using CofreEletronico.Application.Autenticacao.Portas;
using CofreEletronico.Application.Autorizacao;

namespace CofreEletronico.Application.Autenticacao;

/// <summary>
/// POST /auth/registro: cria o usuário com o papel <see cref="Papeis.Usuario"/> e já devolve os tokens
/// (login automático). Formato e tamanho dos campos são validados antes, na borda (Api).
/// </summary>
public sealed class RegistrarUsuario
{
    private readonly IServicoIdentidade _identidade;
    private readonly EmissorTokens _emissor;

    public RegistrarUsuario(IServicoIdentidade identidade, EmissorTokens emissor)
    {
        _identidade = identidade;
        _emissor = emissor;
    }

    public async Task<ResultadoAutenticacao> ExecutarAsync(RegistrarUsuarioComando comando, CancellationToken cancellationToken = default)
    {
        var telefone = string.IsNullOrWhiteSpace(comando.Telefone) ? null : comando.Telefone.Trim();
        var dados = new NovoUsuario(comando.Nome.Trim(), comando.Email.Trim(), comando.Senha, telefone);

        var criacao = await _identidade.CriarAsync(dados, Papeis.Usuario, cancellationToken);

        return criacao.Status switch
        {
            StatusCriacaoUsuario.Criado =>
                ResultadoAutenticacao.Sucesso(await _emissor.EmitirNovaSessaoAsync(criacao.Usuario!, cancellationToken)),
            StatusCriacaoUsuario.EmailEmUso => ResultadoAutenticacao.Falha(StatusAutenticacao.EmailEmUso),
            StatusCriacaoUsuario.Invalido => ResultadoAutenticacao.Validacao(criacao.Erros),
            _ => throw new InvalidOperationException($"Status de criação inesperado: {criacao.Status}."),
        };
    }
}
