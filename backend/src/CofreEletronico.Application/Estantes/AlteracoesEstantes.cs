using System.Security.Cryptography;
using CofreEletronico.Application.Estantes.Portas;
using CofreEletronico.Domain.Common;
using Microsoft.Extensions.Logging;

namespace CofreEletronico.Application.Estantes;

/// <summary>PUT /admin/estantes/{id}: altera nome, endereço e coordenadas. device_id e segredo não mudam.</summary>
public sealed class AtualizarEstante
{
    private readonly IEstanteRepositorio _repositorio;
    private readonly ILogger<AtualizarEstante> _logger;

    public AtualizarEstante(IEstanteRepositorio repositorio, ILogger<AtualizarEstante> logger)
    {
        _repositorio = repositorio;
        _logger = logger;
    }

    public async Task<ResultadoEstante<EstanteDto>> ExecutarAsync(
        AtualizarEstanteComando comando, CancellationToken cancellationToken = default)
    {
        var estante = await _repositorio.ObterParaAlteracaoAsync(comando.EstanteId, cancellationToken);
        if (estante is null)
            return ResultadoEstante<EstanteDto>.Falha(StatusResultadoEstante.NaoEncontrada);

        try
        {
            // Valida tudo antes de alterar: se lançar, a entidade continua como estava.
            estante.Atualizar(comando.Nome, comando.Endereco, comando.Latitude, comando.Longitude);
        }
        catch (ExcecaoDominio ex)
        {
            return ResultadoEstante<EstanteDto>.Validacao(CredenciaisEstante.Erros(ex));
        }

        if (await _repositorio.SalvarAsync(cancellationToken) == ResultadoSalvamento.ConflitoConcorrencia)
            return ConflitoConcorrencia.Registrar<EstanteDto>(_logger, "atualizar", estante.Id, comando.AdminId);

        _logger.LogInformation(
            "Estante {EstanteId} ({DeviceId}) atualizada pelo admin {AdminId}.", estante.Id, estante.DeviceId, comando.AdminId);

        return ResultadoEstante<EstanteDto>.Sucesso(EstanteDto.De(estante));
    }
}

/// <summary>
/// POST /admin/estantes/{id}/desativar: exclusão lógica, idempotente. Recusada (Conflito) se algum cofre da estante
/// estiver reservado ou ocupado: a regra atravessa os agregados Estante e Cofre, por isso fica aqui, consultando
/// o repositório.
/// </summary>
/// <remarks>
/// A consulta e a gravação não são atômicas: uma reserva que comece entre as duas não é vista. A reserva (T-0702)
/// precisa conferir <c>estantes.ativa</c> na própria escrita condicional para fechar essa janela.
/// </remarks>
public sealed class DesativarEstante
{
    private readonly IEstanteRepositorio _repositorio;
    private readonly ILogger<DesativarEstante> _logger;

    public DesativarEstante(IEstanteRepositorio repositorio, ILogger<DesativarEstante> logger)
    {
        _repositorio = repositorio;
        _logger = logger;
    }

    public async Task<ResultadoEstante<EstanteDto>> ExecutarAsync(AcaoEstanteComando comando, CancellationToken cancellationToken = default)
    {
        var estante = await _repositorio.ObterParaAlteracaoAsync(comando.EstanteId, cancellationToken);
        if (estante is null)
            return ResultadoEstante<EstanteDto>.Falha(StatusResultadoEstante.NaoEncontrada);

        // Idempotente: já inativa devolve sucesso sem gravar nada.
        if (!estante.Ativa)
            return ResultadoEstante<EstanteDto>.Sucesso(EstanteDto.De(estante));

        if (await _repositorio.PossuiCofreEmUsoAsync(estante.Id, cancellationToken))
            return ResultadoEstante<EstanteDto>.Falha(StatusResultadoEstante.Conflito);

        estante.Desativar();
        if (await _repositorio.SalvarAsync(cancellationToken) == ResultadoSalvamento.ConflitoConcorrencia)
            return ConflitoConcorrencia.Registrar<EstanteDto>(_logger, "desativar", estante.Id, comando.AdminId);

        _logger.LogInformation(
            "Estante {EstanteId} ({DeviceId}) desativada pelo admin {AdminId}.", estante.Id, estante.DeviceId, comando.AdminId);

        return ResultadoEstante<EstanteDto>.Sucesso(EstanteDto.De(estante));
    }
}

/// <summary>POST /admin/estantes/{id}/reativar: desfaz a exclusão lógica. Idempotente.</summary>
public sealed class ReativarEstante
{
    private readonly IEstanteRepositorio _repositorio;
    private readonly ILogger<ReativarEstante> _logger;

    public ReativarEstante(IEstanteRepositorio repositorio, ILogger<ReativarEstante> logger)
    {
        _repositorio = repositorio;
        _logger = logger;
    }

    public async Task<ResultadoEstante<EstanteDto>> ExecutarAsync(AcaoEstanteComando comando, CancellationToken cancellationToken = default)
    {
        var estante = await _repositorio.ObterParaAlteracaoAsync(comando.EstanteId, cancellationToken);
        if (estante is null)
            return ResultadoEstante<EstanteDto>.Falha(StatusResultadoEstante.NaoEncontrada);

        if (estante.Ativa)
            return ResultadoEstante<EstanteDto>.Sucesso(EstanteDto.De(estante));

        estante.Reativar();
        if (await _repositorio.SalvarAsync(cancellationToken) == ResultadoSalvamento.ConflitoConcorrencia)
            return ConflitoConcorrencia.Registrar<EstanteDto>(_logger, "reativar", estante.Id, comando.AdminId);

        _logger.LogInformation(
            "Estante {EstanteId} ({DeviceId}) reativada pelo admin {AdminId}.", estante.Id, estante.DeviceId, comando.AdminId);

        return ResultadoEstante<EstanteDto>.Sucesso(EstanteDto.De(estante));
    }
}

/// <summary>
/// POST /admin/estantes/{id}/credenciais: gera novo segredo HMAC (salvo cifrado; o anterior deixa de valer) e nova
/// senha MQTT (devolvida, não armazenada). O device_id não muda. O admin precisa trocar a senha no Mosquitto e
/// regravar o ESP32.
/// </summary>
public sealed class RegenerarCredenciaisEstante
{
    private readonly IEstanteRepositorio _repositorio;
    private readonly IGeradorCredenciaisEstante _gerador;
    private readonly ILogger<RegenerarCredenciaisEstante> _logger;

    public RegenerarCredenciaisEstante(
        IEstanteRepositorio repositorio, IGeradorCredenciaisEstante gerador, ILogger<RegenerarCredenciaisEstante> logger)
    {
        _repositorio = repositorio;
        _gerador = gerador;
        _logger = logger;
    }

    public async Task<ResultadoEstante<CredenciaisEstanteDto>> ExecutarAsync(
        AcaoEstanteComando comando, CancellationToken cancellationToken = default)
    {
        var estante = await _repositorio.ObterParaAlteracaoAsync(comando.EstanteId, cancellationToken);
        if (estante is null)
            return ResultadoEstante<CredenciaisEstanteDto>.Falha(StatusResultadoEstante.NaoEncontrada);

        var segredo = _gerador.GerarSegredoHmac();
        try
        {
            estante.SubstituirSegredoHmac(segredo);

            // Outra regeneração (ou alteração) simultânea venceu: as credenciais desta não foram gravadas e não
            // podem ser devolvidas (o admin ficaria com um segredo que o backend não usa).
            if (await _repositorio.SalvarAsync(cancellationToken) == ResultadoSalvamento.ConflitoConcorrencia)
                return ConflitoConcorrencia.Registrar<CredenciaisEstanteDto>(_logger, "regenerar credenciais", estante.Id, comando.AdminId);

            var credenciais = CredenciaisEstante.Montar(estante.DeviceId, segredo, _gerador.GerarSenhaMqtt());

            _logger.LogInformation(
                "Credenciais da estante {EstanteId} ({DeviceId}) regeneradas pelo admin {AdminId}.",
                estante.Id, estante.DeviceId, comando.AdminId);

            return ResultadoEstante<CredenciaisEstanteDto>.Sucesso(credenciais);
        }
        finally
        {
            // Também se salvar lançar exceção. A entidade guarda a própria cópia.
            CryptographicOperations.ZeroMemory(segredo);
        }
    }
}

/// <summary>Resposta e log comuns quando o token de concorrência recusa a gravação.</summary>
internal static class ConflitoConcorrencia
{
    public static ResultadoEstante<T> Registrar<T>(ILogger logger, string acao, Guid estanteId, Guid adminId)
        where T : class
    {
        logger.LogWarning(
            "Conflito de concorrência ao {Acao} a estante {EstanteId} (admin {AdminId}): alterada por outra operação; nada foi gravado.",
            acao, estanteId, adminId);
        return ResultadoEstante<T>.Falha(StatusResultadoEstante.ConflitoConcorrencia);
    }
}
