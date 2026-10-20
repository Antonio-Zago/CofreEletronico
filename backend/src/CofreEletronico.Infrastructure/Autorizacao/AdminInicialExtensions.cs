using CofreEletronico.Application.Autorizacao;
using CofreEletronico.Infrastructure.Persistencia;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CofreEletronico.Infrastructure.Autorizacao;

public static class AdminInicialExtensions
{
    /// <summary>
    /// Executa <see cref="GarantirAdminInicial"/> com a seção <c>AdminInicial</c>. Chamado pela Api na subida,
    /// em todos os ambientes, depois das migrations (quando elas rodam). Sem a string de conexão, só registra
    /// um aviso e retorna (a Api continua subindo sem banco). Dados recusados pela política do Identity
    /// (ex.: senha fraca) lançam <see cref="InvalidOperationException"/> com as regras violadas, sem ecoar a
    /// senha, e a Api não sobe.
    /// </summary>
    public static async Task GarantirAdminInicialAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(AdminInicialExtensions).FullName!);

        // Valida a seção (e-mail e senha juntos, formato, tamanhos) mesmo sem banco.
        var opcoes = provider.GetRequiredService<IOptions<AdminInicialOpcoes>>().Value;

        var configuration = provider.GetRequiredService<IConfiguration>();
        if (string.IsNullOrWhiteSpace(configuration.GetConnectionString(PersistenciaExtensions.ConnectionStringNome)))
        {
            logger.LogWarning(
                "String de conexão '{Nome}' não configurada: verificação do administrador inicial (AdminInicial) não executada.",
                PersistenciaExtensions.ConnectionStringNome);
            return;
        }

        var casoDeUso = provider.GetRequiredService<GarantirAdminInicial>();
        var resultado = await casoDeUso.ExecutarAsync(opcoes.CriarComando(), cancellationToken);

        if (resultado.Status == StatusAdminInicial.Invalido)
        {
            // As descrições vêm do DescritorErrosIdentityPtBr: só as regras, nunca o valor da senha ou do e-mail.
            var regras = string.Join(" ", resultado.Erros.SelectMany(e => e.Value).Distinct());
            throw new InvalidOperationException(
                "AdminInicial recusado: o administrador inicial não foi criado e a Api não vai subir. " +
                $"{regras} Corrija AdminInicial:Senha/AdminInicial:Email (user-secrets ou variáveis de ambiente) e suba novamente.");
        }
    }
}
