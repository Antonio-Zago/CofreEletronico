using CofreEletronico.Application.Cofres.Portas;
using CofreEletronico.Domain.Common;
using CofreEletronico.Domain.Estantes;
using Microsoft.Extensions.Logging;

namespace CofreEletronico.Application.Cofres;

/// <summary>Tradução de erros e logs comuns aos casos de uso de cofres.</summary>
internal static class RegrasCofres
{
    /// <summary>Chave do erro de validação quando a regra violada vem do domínio (mensagem segura).</summary>
    public const string ChaveErroDominio = "cofre";

    public static ResultadoCofre<CofreDto> Validacao(ExcecaoDominio ex) =>
        ResultadoCofre<CofreDto>.Validacao(new Dictionary<string, string[]> { [ChaveErroDominio] = [ex.Message] });

    /// <summary>409 de estado com mensagem genérica que diz o status atual (ex.: "O cofre está ocupado e não pode ser editado.").</summary>
    public static ResultadoCofre<CofreDto> ConflitoEstado(StatusCofre statusAtual, string naoPode) =>
        ResultadoCofre<CofreDto>.ConflitoEstado($"O cofre está {TextoCofre.Descrever(statusAtual)} e não pode {naoPode}.");

    /// <summary>
    /// Consulta prévia de número e canal na estante, para devolver mensagem clara. A corrida entre duas gravações ainda
    /// é possível; ela é pega pelo índice único (<see cref="FalhaGravacao"/>), sem nova tentativa.
    /// </summary>
    public static async Task<StatusResultadoCofre?> ConferirUnicidadeAsync(
        ICofreRepositorio repositorio, Guid estanteId, int numero, int canalRele, Guid? excetoCofreId,
        CancellationToken cancellationToken)
    {
        if (await repositorio.ExisteNumeroAsync(estanteId, numero, excetoCofreId, cancellationToken))
            return StatusResultadoCofre.NumeroEmUso;
        if (await repositorio.ExisteCanalAsync(estanteId, canalRele, excetoCofreId, cancellationToken))
            return StatusResultadoCofre.CanalEmUso;
        return null;
    }

    /// <summary>Gravação recusada pelo banco: log de aviso e o status correspondente. Nada foi gravado.</summary>
    public static ResultadoCofre<CofreDto> FalhaGravacao(
        ILogger logger, ResultadoGravacaoCofre resultado, string acao, Cofre cofre, Guid adminId)
    {
        switch (resultado)
        {
            case ResultadoGravacaoCofre.ConflitoConcorrencia:
                logger.LogWarning(
                    "Conflito de concorrência ao {Acao} o cofre {CofreId} da estante {EstanteId} (admin {AdminId}): " +
                    "alterado por outra operação; nada foi gravado.",
                    acao, cofre.Id, cofre.EstanteId, adminId);
                return ResultadoCofre<CofreDto>.Falha(StatusResultadoCofre.ConflitoConcorrencia);

            case ResultadoGravacaoCofre.NumeroEmUso or ResultadoGravacaoCofre.CanalEmUso:
                logger.LogWarning(
                    "Índice único recusou {Recusa} ao {Acao} o cofre {CofreId} da estante {EstanteId} (admin {AdminId}; " +
                    "corrida com outra gravação); nada foi gravado.",
                    resultado, acao, cofre.Id, cofre.EstanteId, adminId);
                return ResultadoCofre<CofreDto>.Falha(resultado == ResultadoGravacaoCofre.NumeroEmUso
                    ? StatusResultadoCofre.NumeroEmUso
                    : StatusResultadoCofre.CanalEmUso);

            default:
                throw new InvalidOperationException($"Resultado de gravação sem tradução: {resultado}.");
        }
    }

    /// <summary>
    /// Auditoria da edição com os valores anteriores e os novos ("de X para Y"). A troca de canal é a mais sensível:
    /// muda qual relé abre o cofre.
    /// </summary>
    public static void AuditarEdicao(
        ILogger logger, Cofre cofre, int numeroAnterior, int canalAnterior, TamanhoCofre tamanhoAnterior, Guid adminId) =>
        logger.LogInformation(
            "Cofre {CofreId} da estante {EstanteId} atualizado pelo admin {AdminId}: número de {NumeroAnterior} para {Numero}, " +
            "canal de {CanalAnterior} para {CanalRele}, tamanho de {TamanhoAnterior} para {Tamanho}.",
            cofre.Id, cofre.EstanteId, adminId, numeroAnterior, cofre.Numero, canalAnterior, cofre.CanalRele,
            TextoCofre.DeTamanho(tamanhoAnterior), TextoCofre.DeTamanho(cofre.Tamanho));

    /// <summary>Auditoria de ação admin bem-sucedida: admin, estante, cofre, número e canal.</summary>
    public static void Auditar(ILogger logger, string acao, Cofre cofre, Guid adminId) =>
        logger.LogInformation(
            "Cofre {CofreId} (número {Numero}, canal {CanalRele}) da estante {EstanteId} {Acao} pelo admin {AdminId}.",
            cofre.Id, cofre.Numero, cofre.CanalRele, cofre.EstanteId, acao, adminId);
}
