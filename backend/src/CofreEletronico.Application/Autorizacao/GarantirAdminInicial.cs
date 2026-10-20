using CofreEletronico.Application.Autenticacao.Portas;
using CofreEletronico.Application.Comum;
using Microsoft.Extensions.Logging;

namespace CofreEletronico.Application.Autorizacao;

/// <summary>Dados do administrador inicial vindos da configuração (seção <c>AdminInicial</c>).</summary>
public sealed record AdminInicialComando(string Email, string Senha, string Nome);

public enum StatusAdminInicial
{
    /// <summary>Seção <c>AdminInicial</c> ausente: nada a fazer.</summary>
    NaoConfigurado,

    /// <summary>Já existe ao menos um usuário com o papel Admin: nada é criado nem alterado.</summary>
    AdminJaExiste,

    /// <summary>Administrador criado com os papéis Admin e Usuario.</summary>
    Criado,

    /// <summary>
    /// O e-mail configurado já pertence a uma conta que não é Admin. Ela NÃO é promovida: promover conta
    /// existente por configuração seria escalação de privilégio.
    /// </summary>
    EmailEmUsoPorOutraConta,

    /// <summary>Dados recusados pela política do Identity (ex.: senha fraca). Ver <see cref="ResultadoAdminInicial.Erros"/>.</summary>
    Invalido,
}

/// <summary>Resultado de <see cref="GarantirAdminInicial"/>. <see cref="Erros"/> só em <see cref="StatusAdminInicial.Invalido"/>.</summary>
public sealed record ResultadoAdminInicial(StatusAdminInicial Status, IReadOnlyDictionary<string, string[]> Erros)
{
    private static readonly IReadOnlyDictionary<string, string[]> SemErros = new Dictionary<string, string[]>();

    public static ResultadoAdminInicial De(StatusAdminInicial status) => new(status, SemErros);
}

/// <summary>
/// Garante que exista um administrador, criando-o a partir da configuração na subida da Api.
/// Idempotente: se já houver qualquer Admin, não faz nada (nem troca a senha). Nunca promove conta existente.
/// Nunca registra a senha nem o e-mail completo.
/// </summary>
public sealed class GarantirAdminInicial
{
    private readonly IServicoIdentidade _identidade;
    private readonly ILogger<GarantirAdminInicial> _logger;

    public GarantirAdminInicial(IServicoIdentidade identidade, ILogger<GarantirAdminInicial> logger)
    {
        _identidade = identidade;
        _logger = logger;
    }

    public async Task<ResultadoAdminInicial> ExecutarAsync(AdminInicialComando? comando, CancellationToken cancellationToken = default)
    {
        if (comando is null)
        {
            _logger.LogInformation("AdminInicial não configurado: nenhum administrador inicial será criado.");
            return ResultadoAdminInicial.De(StatusAdminInicial.NaoConfigurado);
        }

        // Limitação conhecida: duas instâncias subindo ao mesmo tempo com e-mails DIFERENTES em AdminInicial
        // podem passar ambas por esta checagem e criar dois administradores (com o mesmo e-mail, o índice único
        // impede, ver o tratamento de EmailEmUso abaixo). Aceitável: a configuração é do operador.
        if (await _identidade.ExisteUsuarioComPapelAsync(Papeis.Admin, cancellationToken))
        {
            // Warning (e não Information) para aparecer também em produção: a senha continua configurada sem uso.
            _logger.LogWarning(
                "Já existe administrador cadastrado: AdminInicial ignorado. AdminInicial:Senha continua configurada; " +
                "remova-a (user-secrets, variável de ambiente ou secret montado).");
            return ResultadoAdminInicial.De(StatusAdminInicial.AdminJaExiste);
        }

        var email = comando.Email.Trim();
        var dados = new NovoUsuario(comando.Nome.Trim(), email, comando.Senha, Telefone: null);
        var criacao = await _identidade.CriarAdministradorAsync(dados, cancellationToken);

        switch (criacao.Status)
        {
            case StatusCriacaoUsuario.Criado:
                _logger.LogInformation(
                    "Administrador inicial {UsuarioId} criado ({Email}). Remova AdminInicial:Senha da configuração " +
                    "(user-secrets ou variável de ambiente) e troque a senha se ela foi compartilhada.",
                    criacao.Usuario!.Id, MascaraDados.MascararEmail(email));
                return ResultadoAdminInicial.De(StatusAdminInicial.Criado);

            case StatusCriacaoUsuario.EmailEmUso:
                // Outra instância da Api pode ter criado o Admin em paralelo com o mesmo e-mail.
                if (await _identidade.ExisteUsuarioComPapelAsync(Papeis.Admin, cancellationToken))
                {
                    _logger.LogInformation("Administrador criado em paralelo por outra instância: AdminInicial ignorado.");
                    return ResultadoAdminInicial.De(StatusAdminInicial.AdminJaExiste);
                }

                _logger.LogWarning(
                    "AdminInicial:Email ({Email}) já pertence a uma conta que não é administradora. A conta NÃO foi promovida " +
                    "e nenhum administrador foi criado. Configure outro e-mail em AdminInicial:Email.",
                    MascaraDados.MascararEmail(email));
                return ResultadoAdminInicial.De(StatusAdminInicial.EmailEmUsoPorOutraConta);

            case StatusCriacaoUsuario.Invalido:
                return new ResultadoAdminInicial(StatusAdminInicial.Invalido, criacao.Erros);

            default:
                throw new InvalidOperationException($"Status de criação inesperado: {criacao.Status}.");
        }
    }
}
