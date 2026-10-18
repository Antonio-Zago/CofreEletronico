using System.Security.Cryptography;
using CofreEletronico.Infrastructure.Persistencia.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CofreEletronico.Infrastructure.Autenticacao;

/// <summary>
/// Verificação de senha contra o hash de uma senha aleatória, com o mesmo algoritmo e custo do hasher
/// do Identity (mesmas <see cref="PasswordHasherOptions"/>). Usada quando o e-mail não existe ou a conta
/// está bloqueada, para o tempo de resposta ficar parecido com o de uma senha errada e dificultar a
/// enumeração de e-mails pelo tempo. O hash é calculado no construtor (singleton criado na subida).
/// </summary>
internal sealed class VerificacaoSenhaFicticia
{
    private static readonly Usuario UsuarioFicticio = new();

    private readonly PasswordHasher<Usuario> _hasher;
    private readonly string _hash;

    public VerificacaoSenhaFicticia(IOptions<PasswordHasherOptions> opcoes)
    {
        _hasher = new PasswordHasher<Usuario>(opcoes);
        _hash = _hasher.HashPassword(UsuarioFicticio, Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
    }

    public void Verificar(string senha) => _hasher.VerifyHashedPassword(UsuarioFicticio, _hash, senha);
}

/// <summary>
/// Força a criação de <see cref="VerificacaoSenhaFicticia"/> na subida do host (eager), para que o
/// primeiro login com e-mail inexistente não pague o cálculo do hash e fique mais lento que os demais.
/// </summary>
internal sealed class PreaquecimentoVerificacaoSenha(VerificacaoSenhaFicticia verificacao) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        GC.KeepAlive(verificacao);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
