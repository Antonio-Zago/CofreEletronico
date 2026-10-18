using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using CofreEletronico.Application.Autenticacao.Portas;
using CofreEletronico.Domain.Autenticacao;
using CofreEletronico.Domain.Common;

namespace CofreEletronico.Api.Tests.Infra;

/// <summary>Usuários em memória no lugar do ASP.NET Identity (sem banco nos testes da Api).</summary>
internal sealed class ServicoIdentidadeFalso : IServicoIdentidade
{
    private readonly ConcurrentDictionary<string, (UsuarioAutenticado Usuario, string Senha)> _porEmail =
        new(StringComparer.OrdinalIgnoreCase);

    public ConcurrentDictionary<Guid, bool> Bloqueados { get; } = new();

    /// <summary>Quando preenchido, a criação devolve estes erros (ex.: senha fraca rejeitada pelo Identity).</summary>
    public IReadOnlyDictionary<string, string[]>? ErrosNaCriacao { get; set; }

    public UsuarioAutenticado Adicionar(string email, string senha, params string[] papeis)
    {
        var usuario = new UsuarioAutenticado(Guid.NewGuid(), email, papeis);
        _porEmail[email] = (usuario, senha);
        return usuario;
    }

    public Task<ResultadoCriacaoUsuario> CriarAsync(NovoUsuario dados, string papel, CancellationToken cancellationToken)
    {
        if (ErrosNaCriacao is not null)
            return Task.FromResult(ResultadoCriacaoUsuario.Invalido(ErrosNaCriacao));

        var usuario = new UsuarioAutenticado(Guid.NewGuid(), dados.Email, [papel]);
        return Task.FromResult(_porEmail.TryAdd(dados.Email, (usuario, dados.Senha))
            ? ResultadoCriacaoUsuario.Criado(usuario)
            : ResultadoCriacaoUsuario.EmailEmUso());
    }

    public Task<UsuarioAutenticado?> VerificarCredenciaisAsync(string email, string senha, CancellationToken cancellationToken)
    {
        var valido = _porEmail.TryGetValue(email, out var registro)
                     && !Bloqueados.ContainsKey(registro.Usuario.Id)
                     && registro.Senha == senha;
        return Task.FromResult(valido ? registro.Usuario : null);
    }

    public Task<UsuarioAutenticado?> ObterAsync(Guid usuarioId, CancellationToken cancellationToken) =>
        Task.FromResult(_porEmail.Values.Select(v => v.Usuario).SingleOrDefault(u => u.Id == usuarioId));
}

/// <summary>Refresh tokens em memória, com as mesmas semânticas condicionais do repositório EF.</summary>
internal sealed class RefreshTokenRepositorioFalso : IRefreshTokenRepositorio
{
    private readonly object _trava = new();
    private readonly List<RefreshToken> _tokens = [];

    public IReadOnlyList<RefreshToken> Tokens
    {
        get
        {
            lock (_trava)
                return _tokens.ToList();
        }
    }

    public Task AdicionarAsync(RefreshToken token, CancellationToken cancellationToken)
    {
        lock (_trava)
            _tokens.Add(token);
        return Task.CompletedTask;
    }

    public Task<RefreshToken?> ObterPorHashAsync(string tokenHash, CancellationToken cancellationToken)
    {
        lock (_trava)
        {
            var token = _tokens.SingleOrDefault(t => t.TokenHash == tokenHash);
            return Task.FromResult(token is null ? null : Clonar(token));
        }
    }

    public Task<bool> RotacionarAsync(RefreshToken atual, RefreshToken novo, CancellationToken cancellationToken)
    {
        lock (_trava)
        {
            var armazenado = _tokens.Single(t => t.Id == atual.Id);
            if (!armazenado.EstaAtivo(novo.CriadoEm))
                return Task.FromResult(false);

            _tokens.Remove(armazenado);
            _tokens.Add(atual);
            _tokens.Add(novo);
            return Task.FromResult(true);
        }
    }

    public Task RevogarAsync(Guid tokenId, DateTimeOffset agora, CancellationToken cancellationToken)
    {
        lock (_trava)
            _tokens.Single(t => t.Id == tokenId).Revogar(agora);
        return Task.CompletedTask;
    }

    public Task RevogarFamiliaAsync(Guid familiaId, DateTimeOffset agora, CancellationToken cancellationToken)
    {
        lock (_trava)
        {
            foreach (var token in _tokens.Where(t => t.FamiliaId == familiaId))
                token.Revogar(agora);
        }

        return Task.CompletedTask;
    }

    // Imita a leitura AsNoTracking (construtor privado: cópia por reflexão).
    private static RefreshToken Clonar(RefreshToken origem)
    {
        var copia = (RefreshToken)RuntimeHelpers.GetUninitializedObject(typeof(RefreshToken));
        foreach (var propriedade in typeof(RefreshToken).GetProperties().Where(p => p.CanWrite))
            propriedade.SetValue(copia, propriedade.GetValue(origem));
        typeof(Entity).GetProperty(nameof(Entity.Id))!.SetValue(copia, origem.Id);
        return copia;
    }
}
