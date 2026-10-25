using CofreEletronico.Application.Cofres.Portas;
using CofreEletronico.Domain.Common;
using CofreEletronico.Domain.Estantes;
using Microsoft.Extensions.Logging;

namespace CofreEletronico.Application.Cofres;

/// <summary>
/// POST /admin/estantes/{estanteId}/cofres: cria o cofre <c>livre</c> e ativo. A estante pode estar desativada.
/// Número ou canal já usado na estante: consulta antes (mensagem clara) e, como a corrida ainda é possível, também
/// trata a recusa dos índices únicos. Nos dois casos o resultado é Conflito (409), sem nova tentativa.
/// </summary>
public sealed class CriarCofre
{
    private readonly ICofreRepositorio _repositorio;
    private readonly ILogger<CriarCofre> _logger;

    public CriarCofre(ICofreRepositorio repositorio, ILogger<CriarCofre> logger)
    {
        _repositorio = repositorio;
        _logger = logger;
    }

    public async Task<ResultadoCofre<CofreDto>> ExecutarAsync(CriarCofreComando comando, CancellationToken cancellationToken = default)
    {
        if (!await _repositorio.ExisteEstanteAsync(comando.EstanteId, cancellationToken))
            return ResultadoCofre<CofreDto>.Falha(StatusResultadoCofre.EstanteNaoEncontrada);

        Cofre cofre;
        try
        {
            cofre = Cofre.Criar(comando.EstanteId, comando.Numero, comando.CanalRele, comando.Tamanho);
        }
        catch (ExcecaoDominio ex)
        {
            return RegrasCofres.Validacao(ex);
        }

        var duplicado = await RegrasCofres.ConferirUnicidadeAsync(
            _repositorio, comando.EstanteId, comando.Numero, comando.CanalRele, excetoCofreId: null, cancellationToken);
        if (duplicado is { } status)
            return ResultadoCofre<CofreDto>.Falha(status);

        var gravacao = await _repositorio.AdicionarAsync(cofre, cancellationToken);
        if (gravacao != ResultadoGravacaoCofre.Gravado)
            return RegrasCofres.FalhaGravacao(_logger, gravacao, "criar", cofre, comando.AdminId);

        RegrasCofres.Auditar(_logger, "criado", cofre, comando.AdminId);
        return ResultadoCofre<CofreDto>.Sucesso(CofreDto.De(cofre));
    }
}
