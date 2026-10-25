using CofreEletronico.Application.Cofres.Portas;
using CofreEletronico.Domain.Common;
using CofreEletronico.Domain.Estantes;
using Microsoft.Extensions.Logging;

namespace CofreEletronico.Application.Cofres;

/// <summary>
/// PUT /admin/estantes/{estanteId}/cofres/{cofreId}: altera número, canal do relé e tamanho. Só com o cofre
/// <c>livre</c> ou em <c>manutencao</c> (regra do domínio, <see cref="Cofre.Atualizar"/>); senão, ConflitoEstado (409).
/// </summary>
/// <remarks>
/// A unicidade é conferida ANTES de alterar a entidade (excluindo o próprio cofre), para que uma recusa não deixe o
/// cofre rastreado modificado em memória. Por isso, num cofre reservado/ocupado com número repetido, o 409 é o de
/// número em uso. A corrida com outra gravação é pega pelo índice único no salvar (409, sem nova tentativa).
/// </remarks>
public sealed class AtualizarCofre
{
    private readonly ICofreRepositorio _repositorio;
    private readonly ILogger<AtualizarCofre> _logger;

    public AtualizarCofre(ICofreRepositorio repositorio, ILogger<AtualizarCofre> logger)
    {
        _repositorio = repositorio;
        _logger = logger;
    }

    public async Task<ResultadoCofre<CofreDto>> ExecutarAsync(AtualizarCofreComando comando, CancellationToken cancellationToken = default)
    {
        var cofre = await _repositorio.ObterParaAlteracaoAsync(comando.EstanteId, comando.CofreId, cancellationToken);
        if (cofre is null)
            return ResultadoCofre<CofreDto>.Falha(StatusResultadoCofre.CofreNaoEncontrado);

        var duplicado = await RegrasCofres.ConferirUnicidadeAsync(
            _repositorio, cofre.EstanteId, comando.Numero, comando.CanalRele, cofre.Id, cancellationToken);
        if (duplicado is { } status)
            return ResultadoCofre<CofreDto>.Falha(status);

        // Valores anteriores, para a auditoria "de X para Y".
        var (numeroAnterior, canalAnterior, tamanhoAnterior) = (cofre.Numero, cofre.CanalRele, cofre.Tamanho);

        try
        {
            // Valida tudo antes de alterar: se lançar, a entidade continua como estava.
            cofre.Atualizar(comando.Numero, comando.CanalRele, comando.Tamanho);
        }
        catch (TransicaoCofreInvalidaException ex)
        {
            return RegrasCofres.ConflitoEstado(ex.StatusAtual, "ser editado");
        }
        catch (ExcecaoDominio ex)
        {
            return RegrasCofres.Validacao(ex);
        }

        var gravacao = await _repositorio.SalvarAsync(cancellationToken);
        if (gravacao != ResultadoGravacaoCofre.Gravado)
            return RegrasCofres.FalhaGravacao(_logger, gravacao, "atualizar", cofre, comando.AdminId);

        RegrasCofres.AuditarEdicao(_logger, cofre, numeroAnterior, canalAnterior, tamanhoAnterior, comando.AdminId);
        return ResultadoCofre<CofreDto>.Sucesso(CofreDto.De(cofre));
    }
}

/// <summary>
/// POST .../cofres/{cofreId}/desativar: exclusão lógica, idempotente (já inativo devolve sucesso sem gravar).
/// Reservado/ocupado: ConflitoEstado (409).
/// </summary>
public sealed class DesativarCofre
{
    private readonly ICofreRepositorio _repositorio;
    private readonly ILogger<DesativarCofre> _logger;

    public DesativarCofre(ICofreRepositorio repositorio, ILogger<DesativarCofre> logger)
    {
        _repositorio = repositorio;
        _logger = logger;
    }

    public async Task<ResultadoCofre<CofreDto>> ExecutarAsync(AcaoCofreComando comando, CancellationToken cancellationToken = default)
    {
        var cofre = await _repositorio.ObterParaAlteracaoAsync(comando.EstanteId, comando.CofreId, cancellationToken);
        if (cofre is null)
            return ResultadoCofre<CofreDto>.Falha(StatusResultadoCofre.CofreNaoEncontrado);

        if (!cofre.Ativo)
            return ResultadoCofre<CofreDto>.Sucesso(CofreDto.De(cofre));

        try
        {
            cofre.Desativar();
        }
        catch (TransicaoCofreInvalidaException ex)
        {
            return RegrasCofres.ConflitoEstado(ex.StatusAtual, "ser desativado");
        }

        var gravacao = await _repositorio.SalvarAsync(cancellationToken);
        if (gravacao != ResultadoGravacaoCofre.Gravado)
            return RegrasCofres.FalhaGravacao(_logger, gravacao, "desativar", cofre, comando.AdminId);

        RegrasCofres.Auditar(_logger, "desativado", cofre, comando.AdminId);
        return ResultadoCofre<CofreDto>.Sucesso(CofreDto.De(cofre));
    }
}

/// <summary>POST .../cofres/{cofreId}/reativar: desfaz a exclusão lógica, mantendo o status. Idempotente.</summary>
public sealed class ReativarCofre
{
    private readonly ICofreRepositorio _repositorio;
    private readonly ILogger<ReativarCofre> _logger;

    public ReativarCofre(ICofreRepositorio repositorio, ILogger<ReativarCofre> logger)
    {
        _repositorio = repositorio;
        _logger = logger;
    }

    public async Task<ResultadoCofre<CofreDto>> ExecutarAsync(AcaoCofreComando comando, CancellationToken cancellationToken = default)
    {
        var cofre = await _repositorio.ObterParaAlteracaoAsync(comando.EstanteId, comando.CofreId, cancellationToken);
        if (cofre is null)
            return ResultadoCofre<CofreDto>.Falha(StatusResultadoCofre.CofreNaoEncontrado);

        if (cofre.Ativo)
            return ResultadoCofre<CofreDto>.Sucesso(CofreDto.De(cofre));

        cofre.Reativar();

        var gravacao = await _repositorio.SalvarAsync(cancellationToken);
        if (gravacao != ResultadoGravacaoCofre.Gravado)
            return RegrasCofres.FalhaGravacao(_logger, gravacao, "reativar", cofre, comando.AdminId);

        RegrasCofres.Auditar(_logger, "reativado", cofre, comando.AdminId);
        return ResultadoCofre<CofreDto>.Sucesso(CofreDto.De(cofre));
    }
}

/// <summary>
/// POST .../cofres/{cofreId}/manutencao: <c>livre</c> → <c>manutencao</c>. Qualquer outro status (inclusive já em
/// manutenção) é ConflitoEstado (409), conforme o contrato.
/// </summary>
public sealed class ColocarCofreEmManutencao
{
    private readonly ICofreRepositorio _repositorio;
    private readonly ILogger<ColocarCofreEmManutencao> _logger;

    public ColocarCofreEmManutencao(ICofreRepositorio repositorio, ILogger<ColocarCofreEmManutencao> logger)
    {
        _repositorio = repositorio;
        _logger = logger;
    }

    public async Task<ResultadoCofre<CofreDto>> ExecutarAsync(AcaoCofreComando comando, CancellationToken cancellationToken = default)
    {
        var cofre = await _repositorio.ObterParaAlteracaoAsync(comando.EstanteId, comando.CofreId, cancellationToken);
        if (cofre is null)
            return ResultadoCofre<CofreDto>.Falha(StatusResultadoCofre.CofreNaoEncontrado);

        try
        {
            cofre.ColocarEmManutencao();
        }
        catch (TransicaoCofreInvalidaException ex)
        {
            return RegrasCofres.ConflitoEstado(ex.StatusAtual, "ser colocado em manutenção");
        }

        var gravacao = await _repositorio.SalvarAsync(cancellationToken);
        if (gravacao != ResultadoGravacaoCofre.Gravado)
            return RegrasCofres.FalhaGravacao(_logger, gravacao, "colocar em manutenção", cofre, comando.AdminId);

        RegrasCofres.Auditar(_logger, "colocado em manutenção", cofre, comando.AdminId);
        return ResultadoCofre<CofreDto>.Sucesso(CofreDto.De(cofre));
    }
}

/// <summary>
/// POST .../cofres/{cofreId}/retirar-manutencao: <c>manutencao</c> → <c>livre</c>. Qualquer outro status é
/// ConflitoEstado (409).
/// </summary>
public sealed class RetirarCofreDaManutencao
{
    private readonly ICofreRepositorio _repositorio;
    private readonly ILogger<RetirarCofreDaManutencao> _logger;

    public RetirarCofreDaManutencao(ICofreRepositorio repositorio, ILogger<RetirarCofreDaManutencao> logger)
    {
        _repositorio = repositorio;
        _logger = logger;
    }

    public async Task<ResultadoCofre<CofreDto>> ExecutarAsync(AcaoCofreComando comando, CancellationToken cancellationToken = default)
    {
        var cofre = await _repositorio.ObterParaAlteracaoAsync(comando.EstanteId, comando.CofreId, cancellationToken);
        if (cofre is null)
            return ResultadoCofre<CofreDto>.Falha(StatusResultadoCofre.CofreNaoEncontrado);

        try
        {
            cofre.RetirarDaManutencao();
        }
        catch (TransicaoCofreInvalidaException ex)
        {
            return RegrasCofres.ConflitoEstado(ex.StatusAtual, "ser retirado da manutenção");
        }

        var gravacao = await _repositorio.SalvarAsync(cancellationToken);
        if (gravacao != ResultadoGravacaoCofre.Gravado)
            return RegrasCofres.FalhaGravacao(_logger, gravacao, "retirar da manutenção", cofre, comando.AdminId);

        RegrasCofres.Auditar(_logger, "retirado da manutenção", cofre, comando.AdminId);
        return ResultadoCofre<CofreDto>.Sucesso(CofreDto.De(cofre));
    }
}
