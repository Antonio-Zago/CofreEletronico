using CofreEletronico.Application.Autenticacao.Portas;
using CofreEletronico.Application.Autorizacao;
using CofreEletronico.Infrastructure.Persistencia;
using CofreEletronico.Infrastructure.Persistencia.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace CofreEletronico.Infrastructure.Autenticacao;

/// <summary>
/// <see cref="IServicoIdentidade"/> com ASP.NET Identity (só <see cref="UserManager{TUser}"/>, sem SignInManager).
/// </summary>
internal sealed class ServicoIdentidade(
    UserManager<Usuario> userManager,
    CofreDbContext db,
    TimeProvider relogio,
    VerificacaoSenhaFicticia senhaFicticia,
    ILogger<ServicoIdentidade> logger) : IServicoIdentidade
{
    private static readonly string[] PapeisUsuario = [Papeis.Usuario];
    private static readonly string[] PapeisAdministrador = [Papeis.Admin, Papeis.Usuario];

    public Task<ResultadoCriacaoUsuario> CriarUsuarioAsync(NovoUsuario dados, CancellationToken cancellationToken) =>
        CriarComPapeisAsync(dados, PapeisUsuario, cancellationToken);

    public Task<ResultadoCriacaoUsuario> CriarAdministradorAsync(NovoUsuario dados, CancellationToken cancellationToken) =>
        CriarComPapeisAsync(dados, PapeisAdministrador, cancellationToken);

    /// <summary>Papéis sempre vêm das constantes acima, nunca de quem chama a porta.</summary>
    private async Task<ResultadoCriacaoUsuario> CriarComPapeisAsync(NovoUsuario dados, string[] papeis, CancellationToken cancellationToken)
    {
        var usuario = new Usuario
        {
            UserName = dados.Email,
            Email = dados.Email,
            Nome = dados.Nome,
            PhoneNumber = dados.Telefone,
            CriadoEm = relogio.GetUtcNow(),
        };

        try
        {
            // Usuário e papéis na mesma transação: não pode existir usuário sem os papéis pedidos.
            await using var transacao = await db.Database.BeginTransactionAsync(cancellationToken);

            var criacao = await userManager.CreateAsync(usuario, dados.Senha);
            if (!criacao.Succeeded)
                return Mapear(criacao); // o dispose da transação faz o rollback

            var atribuicao = await userManager.AddToRolesAsync(usuario, papeis);
            if (!atribuicao.Succeeded)
            {
                // Papel inexistente ou erro inesperado: é falha do servidor, não do cliente.
                throw new InvalidOperationException(
                    $"Falha ao atribuir os papéis '{string.Join(", ", papeis)}' ao novo usuário: " +
                    $"{string.Join(", ", atribuicao.Errors.Select(e => e.Code))}.");
            }

            await transacao.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Corrida entre dois cadastros com o mesmo e-mail: o índice único do banco decide. 409 genérico.
            return ResultadoCriacaoUsuario.EmailEmUso();
        }

        return ResultadoCriacaoUsuario.Criado(new UsuarioAutenticado(usuario.Id, usuario.Email, papeis.ToArray()));
    }

    /// <summary>Consulta direta (sem carregar usuários), pelo nome normalizado do papel.</summary>
    public Task<bool> ExisteUsuarioComPapelAsync(string papel, CancellationToken cancellationToken)
    {
        var normalizado = userManager.NormalizeName(papel);
        return db.UserRoles.AsNoTracking()
            .AnyAsync(ur => db.Roles.Any(r => r.Id == ur.RoleId && r.NormalizedName == normalizado), cancellationToken);
    }

    /// <remarks>
    /// Lockout manual, sem SignInManager e sem <c>AccessFailedAsync</c>/<c>ResetAccessFailedCountAsync</c>:
    /// esses métodos fazem ler-alterar-salvar com ConcurrencyStamp e, sob tentativas paralelas, só uma
    /// gravação vence (50 erros em paralelo contariam como 1). Aqui o contador é mantido por UPDATEs
    /// atômicos no banco e a entidade carregada nunca é salva por este método (ver finally).
    /// A checagem de bloqueio em memória só evita o PBKDF2 em conta já bloqueada; quem decide o sucesso
    /// é o UPDATE condicional de <see cref="ConfirmarSucessoAsync"/>, executado depois da senha conferir.
    /// </remarks>
    public async Task<UsuarioAutenticado?> VerificarCredenciaisAsync(string email, string senha, CancellationToken cancellationToken)
    {
        var usuario = await userManager.FindByEmailAsync(email);
        if (usuario is null)
        {
            senhaFicticia.Verificar(senha);
            return null;
        }

        try
        {
            var agora = relogio.GetUtcNow();

            // Mesma regra do WHERE de RegistrarFalhaAsync: bloqueada enquanto lockout_end > agora.
            if (EstaBloqueado(usuario, agora))
            {
                senhaFicticia.Verificar(senha);
                logger.LogInformation("Login recusado: conta {UsuarioId} bloqueada.", usuario.Id);
                return null;
            }

            // Se o hash precisar de rehash, o próprio Identity salva (operação dele, com o stamp atual).
            if (!await userManager.CheckPasswordAsync(usuario, senha))
            {
                await RegistrarFalhaAsync(usuario.Id, agora, cancellationToken);
                return null;
            }

            // A checagem acima é só em memória e o PBKDF2 leva dezenas de ms: palpites paralelos podem ter
            // bloqueado a conta nesse meio-tempo. O sucesso só vale se o banco confirmar que não está
            // bloqueada AGORA; senão, mesmo resultado de credenciais inválidas e nenhum token.
            if (!await ConfirmarSucessoAsync(usuario.Id, cancellationToken))
            {
                logger.LogInformation("Login com senha correta recusado: conta {UsuarioId} bloqueada durante a verificação.", usuario.Id);
                return null;
            }

            return await MontarAsync(usuario);
        }
        finally
        {
            // A entidade em memória ficou desatualizada (os UPDATEs foram direto no banco). Desanexá-la
            // garante que o SaveChanges seguinte no mesmo DbContext (refresh token) não tente gravá-la
            // e não dispare DbUpdateConcurrencyException.
            db.Entry(usuario).State = EntityState.Detached;
        }
    }

    private static bool EstaBloqueado(Usuario usuario, DateTimeOffset agora) =>
        usuario.LockoutEnabled && usuario.LockoutEnd is { } fim && fim > agora;

    /// <summary>
    /// Incrementa as falhas num único UPDATE atômico. No PostgreSQL todas as expressões do SET leem o
    /// valor antigo da linha, e o lock da linha serializa as requisições concorrentes: nenhum incremento
    /// se perde. Ao atingir o máximo, bloqueia por <see cref="PoliticaIdentity.LockoutDuracao"/> e zera
    /// o contador (mesmo comportamento do Identity). Tentativas durante o bloqueio não contam.
    /// O concurrency_stamp é trocado para que gravações do UserManager com dados antigos falhem
    /// em vez de sobrescrever o contador ou o bloqueio.
    /// </summary>
    private async Task RegistrarFalhaAsync(Guid usuarioId, DateTimeOffset agora, CancellationToken cancellationToken)
    {
        const int maximo = PoliticaIdentity.LockoutMaximoTentativas;
        DateTimeOffset? fimBloqueio = TruncarParaMicrossegundos(agora + PoliticaIdentity.LockoutDuracao);
        var novoStamp = Guid.NewGuid().ToString();

        await db.Users
            .Where(u => u.Id == usuarioId && u.LockoutEnabled && (u.LockoutEnd == null || u.LockoutEnd <= agora))
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.LockoutEnd, u => u.AccessFailedCount + 1 >= maximo ? fimBloqueio : u.LockoutEnd)
                .SetProperty(u => u.AccessFailedCount, u => u.AccessFailedCount + 1 >= maximo ? 0 : u.AccessFailedCount + 1)
                .SetProperty(u => u.ConcurrencyStamp, novoStamp),
                cancellationToken);

        var bloqueadoAte = await db.Users.AsNoTracking()
            .Where(u => u.Id == usuarioId)
            .Select(u => u.LockoutEnd)
            .SingleOrDefaultAsync(cancellationToken);
        // Igualdade exata (fimBloqueio truncado a µs): foi ESTA falha que bloqueou a conta.
        if (bloqueadoAte != fimBloqueio)
            return;

        // Caminho raro (só quando ESTA falha bloqueou): vale a consulta extra para destacar o bloqueio de um
        // administrador, possível sinal de bloqueio dirigido (ver README, "Administrador inicial"). Sem e-mail.
        if (await TemPapelAsync(usuarioId, Papeis.Admin, cancellationToken))
            logger.LogWarning("Conta administradora {UsuarioId} bloqueada por tentativas de login com senha errada.", usuarioId);
        else
            logger.LogWarning("Conta {UsuarioId} bloqueada após tentativas de login com senha errada.", usuarioId);
    }

    private Task<bool> TemPapelAsync(Guid usuarioId, string papel, CancellationToken cancellationToken)
    {
        var normalizado = userManager.NormalizeName(papel);
        return db.UserRoles.AsNoTracking()
            .AnyAsync(ur => ur.UserId == usuarioId && db.Roles.Any(r => r.Id == ur.RoleId && r.NormalizedName == normalizado),
                cancellationToken);
    }

    /// <summary>
    /// Confirma o login no banco: UPDATE condicional SEMPRE executado, que zera o contador só se a conta
    /// não estiver bloqueada neste instante. Sem filtro pelo contador, 0 linhas significa apenas
    /// "bloqueada" (o usuário existe: foi carregado acima, e apagá-lo em paralelo também deve negar).
    /// O lock da linha serializa com o UPDATE de falha: se a 5ª falha bloquear antes, este afeta 0 linhas.
    /// </summary>
    private async Task<bool> ConfirmarSucessoAsync(Guid usuarioId, CancellationToken cancellationToken)
    {
        var agora = relogio.GetUtcNow(); // relido depois do PBKDF2
        var novoStamp = Guid.NewGuid().ToString();

        var linhas = await db.Users
            .Where(u => u.Id == usuarioId && !(u.LockoutEnabled && u.LockoutEnd != null && u.LockoutEnd > agora))
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.AccessFailedCount, 0)
                .SetProperty(u => u.ConcurrencyStamp, novoStamp),
                cancellationToken);

        return linhas == 1;
    }

    /// <summary>
    /// O timestamptz do PostgreSQL tem resolução de microssegundos e o .NET de 100 ns; truncar antes de
    /// gravar permite comparar o valor relido com o gravado.
    /// </summary>
    private static DateTimeOffset TruncarParaMicrossegundos(DateTimeOffset valor) =>
        new(valor.UtcTicks - valor.UtcTicks % 10, TimeSpan.Zero);

    public async Task<UsuarioAutenticado?> ObterAsync(Guid usuarioId, CancellationToken cancellationToken)
    {
        var usuario = await userManager.FindByIdAsync(usuarioId.ToString());
        return usuario is null ? null : await MontarAsync(usuario);
    }

    private async Task<UsuarioAutenticado> MontarAsync(Usuario usuario)
    {
        var papeis = await userManager.GetRolesAsync(usuario);
        return new UsuarioAutenticado(usuario.Id, usuario.Email!, papeis.ToArray());
    }

    /// <summary>Converte os erros do Identity: duplicidade vira conflito; o resto, erros por campo (camelCase).</summary>
    private static ResultadoCriacaoUsuario Mapear(IdentityResult resultado)
    {
        var codigos = resultado.Errors.Select(e => e.Code).ToHashSet(StringComparer.Ordinal);
        if (codigos.Contains(nameof(IdentityErrorDescriber.DuplicateEmail)) ||
            codigos.Contains(nameof(IdentityErrorDescriber.DuplicateUserName)))
        {
            return ResultadoCriacaoUsuario.EmailEmUso();
        }

        var erros = resultado.Errors
            .GroupBy(e => Campo(e.Code))
            .ToDictionary(g => g.Key, g => g.Select(e => e.Description).Distinct().ToArray());

        return ResultadoCriacaoUsuario.Invalido(erros);
    }

    private static string Campo(string codigo) => codigo switch
    {
        _ when codigo.StartsWith("Password", StringComparison.Ordinal) => "senha",
        nameof(IdentityErrorDescriber.InvalidEmail) or nameof(IdentityErrorDescriber.InvalidUserName) => "email",
        _ => "geral",
    };
}
