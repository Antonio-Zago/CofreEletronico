using CofreEletronico.Application.Autenticacao.Portas;
using CofreEletronico.Domain.Autenticacao;

namespace CofreEletronico.Application.Tests.Autenticacao;

internal sealed class RelogioFalso(DateTimeOffset agora) : TimeProvider
{
    public DateTimeOffset Agora { get; set; } = agora;

    public override DateTimeOffset GetUtcNow() => Agora;

    public void Avancar(TimeSpan tempo) => Agora += tempo;
}

/// <summary>Usuários em memória. Lockout simplificado: o teste marca a conta como bloqueada.</summary>
internal sealed class ServicoIdentidadeFalso : IServicoIdentidade
{
    private readonly Dictionary<string, (UsuarioAutenticado Usuario, string Senha)> _porEmail = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<Guid> Bloqueados { get; } = [];

    public IReadOnlyDictionary<string, string[]>? ErrosNaCriacao { get; set; }

    public List<(NovoUsuario Dados, string Papel)> Criados { get; } = [];

    public UsuarioAutenticado Adicionar(string email, string senha, params string[] papeis)
    {
        var usuario = new UsuarioAutenticado(Guid.NewGuid(), email, papeis);
        _porEmail[email] = (usuario, senha);
        return usuario;
    }

    public void Remover(Guid usuarioId)
    {
        var email = _porEmail.Single(p => p.Value.Usuario.Id == usuarioId).Key;
        _porEmail.Remove(email);
    }

    public Task<ResultadoCriacaoUsuario> CriarAsync(NovoUsuario dados, string papel, CancellationToken cancellationToken)
    {
        if (ErrosNaCriacao is not null)
            return Task.FromResult(ResultadoCriacaoUsuario.Invalido(ErrosNaCriacao));

        if (_porEmail.ContainsKey(dados.Email))
            return Task.FromResult(ResultadoCriacaoUsuario.EmailEmUso());

        Criados.Add((dados, papel));
        return Task.FromResult(ResultadoCriacaoUsuario.Criado(Adicionar(dados.Email, dados.Senha, papel)));
    }

    /// <summary>
    /// Simula a corrida do lockout: a senha confere, mas palpites paralelos bloquearam a conta antes da
    /// confirmação no banco (o UPDATE de sucesso afeta 0 linhas).
    /// </summary>
    public HashSet<Guid> BloqueadosDuranteVerificacao { get; } = [];

    public int SenhasCorretasRecusadas { get; private set; }

    public Task<UsuarioAutenticado?> VerificarCredenciaisAsync(string email, string senha, CancellationToken cancellationToken)
    {
        if (!_porEmail.TryGetValue(email, out var registro) || Bloqueados.Contains(registro.Usuario.Id) || registro.Senha != senha)
            return Task.FromResult<UsuarioAutenticado?>(null);

        if (BloqueadosDuranteVerificacao.Contains(registro.Usuario.Id))
        {
            SenhasCorretasRecusadas++;
            return Task.FromResult<UsuarioAutenticado?>(null);
        }

        return Task.FromResult<UsuarioAutenticado?>(registro.Usuario);
    }

    public Task<UsuarioAutenticado?> ObterAsync(Guid usuarioId, CancellationToken cancellationToken) =>
        Task.FromResult(_porEmail.Values.Select(v => v.Usuario).SingleOrDefault(u => u.Id == usuarioId));
}

internal sealed class GeradorAccessTokenFalso(TimeProvider relogio) : IGeradorAccessToken
{
    public List<UsuarioAutenticado> Emitidos { get; } = [];

    public AccessTokenGerado Gerar(UsuarioAutenticado usuario)
    {
        Emitidos.Add(usuario);
        return new AccessTokenGerado($"jwt-{usuario.Id}-{Emitidos.Count}", relogio.GetUtcNow().AddMinutes(15));
    }
}

internal sealed class GeradorRefreshTokenFalso : IGeradorRefreshToken
{
    private int _contador;

    public TimeSpan Validade => TimeSpan.FromDays(30);

    public TimeSpan ValidadeSessao => TimeSpan.FromDays(90);

    public RefreshTokenGerado Gerar()
    {
        var valor = $"refresh-{Interlocked.Increment(ref _contador)}";
        return new RefreshTokenGerado(valor, CalcularHash(valor));
    }

    public string CalcularHash(string valor) => $"hash({valor})";
}

/// <summary>
/// Repositório em memória que imita o banco: devolve cópias (não rastreadas) e faz as escritas
/// condicionais como o UPDATE ... WHERE revogado_em IS NULL.
/// </summary>
internal sealed class RefreshTokenRepositorioFalso : IRefreshTokenRepositorio
{
    private readonly List<RefreshToken> _tokens = [];

    public IReadOnlyList<RefreshToken> Tokens => _tokens;

    /// <summary>Simula outra requisição que rotacionou o token entre a leitura e a escrita.</summary>
    public bool PerderProximaCorrida { get; set; }

    public int FamiliasRevogadas { get; private set; }

    public RefreshToken? PorHash(string hash) => _tokens.SingleOrDefault(t => t.TokenHash == hash);

    public Task AdicionarAsync(RefreshToken token, CancellationToken cancellationToken)
    {
        _tokens.Add(token);
        return Task.CompletedTask;
    }

    public Task<RefreshToken?> ObterPorHashAsync(string tokenHash, CancellationToken cancellationToken)
    {
        var token = PorHash(tokenHash);
        return Task.FromResult(token is null ? null : Clonar(token));
    }

    public Task<bool> RotacionarAsync(RefreshToken atual, RefreshToken novo, CancellationToken cancellationToken)
    {
        var armazenado = _tokens.Single(t => t.Id == atual.Id);
        if (PerderProximaCorrida || !armazenado.EstaAtivo(novo.CriadoEm))
        {
            PerderProximaCorrida = false;
            return Task.FromResult(false);
        }

        _tokens.Remove(armazenado);
        _tokens.Add(atual); // já marcado como rotacionado pelo domínio
        _tokens.Add(novo);
        return Task.FromResult(true);
    }

    public Task RevogarAsync(Guid tokenId, DateTimeOffset agora, CancellationToken cancellationToken)
    {
        _tokens.Single(t => t.Id == tokenId).Revogar(agora);
        return Task.CompletedTask;
    }

    public Task RevogarFamiliaAsync(Guid familiaId, DateTimeOffset agora, CancellationToken cancellationToken)
    {
        FamiliasRevogadas++;
        foreach (var token in _tokens.Where(t => t.FamiliaId == familiaId))
            token.Revogar(agora);
        return Task.CompletedTask;
    }

    // Imita a leitura AsNoTracking: o caso de uso recebe uma cópia, alterá-la não muda o "banco".
    // O construtor é privado, então as propriedades são copiadas por reflexão.
    private static RefreshToken Clonar(RefreshToken origem)
    {
        var copia = (RefreshToken)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(RefreshToken));
        foreach (var propriedade in typeof(RefreshToken).GetProperties())
        {
            if (propriedade.CanWrite)
                propriedade.SetValue(copia, propriedade.GetValue(origem));
        }

        typeof(Domain.Common.Entity).GetProperty(nameof(RefreshToken.Id))!.SetValue(copia, origem.Id);
        return copia;
    }
}
