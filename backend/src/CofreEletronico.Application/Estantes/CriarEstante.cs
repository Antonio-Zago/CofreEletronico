using System.Security.Cryptography;
using CofreEletronico.Application.Estantes.Portas;
using CofreEletronico.Domain.Common;
using CofreEletronico.Domain.Estantes;
using Microsoft.Extensions.Logging;

namespace CofreEletronico.Application.Estantes;

/// <summary>
/// POST /admin/estantes: cria a estante (ativa, offline) com device_id e segredo HMAC novos e devolve, uma única vez,
/// as credenciais de provisionamento (segredo em hex e senha MQTT, que não é armazenada no backend).
/// </summary>
/// <remarks>
/// Colisão de device_id: consulta antes de inserir e, como a corrida entre dois cadastros ainda é possível, também
/// trata a recusa do índice único (<see cref="ResultadoAdicaoEstante.DeviceIdEmUso"/>), tentando com outro device_id
/// até <see cref="MaximoTentativas"/> vezes. Esgotadas, devolve <see cref="StatusResultadoEstante.DeviceIdIndisponivel"/>
/// (500) e registra erro. Com 36^6 (~2,2 bilhões) combinações, isso só acontece se o gerador estiver quebrado.
/// O array do segredo em claro é zerado ao fim de cada tentativa, inclusive se a gravação lançar exceção
/// (a entidade guarda a própria cópia).
/// </remarks>
public sealed class CriarEstante
{
    public const int MaximoTentativas = 5;

    private readonly IEstanteRepositorio _repositorio;
    private readonly IGeradorCredenciaisEstante _gerador;
    private readonly TimeProvider _relogio;
    private readonly ILogger<CriarEstante> _logger;

    public CriarEstante(
        IEstanteRepositorio repositorio, IGeradorCredenciaisEstante gerador, TimeProvider relogio, ILogger<CriarEstante> logger)
    {
        _repositorio = repositorio;
        _gerador = gerador;
        _relogio = relogio;
        _logger = logger;
    }

    public async Task<ResultadoEstante<EstanteCriadaDto>> ExecutarAsync(
        CriarEstanteComando comando, CancellationToken cancellationToken = default)
    {
        for (var tentativa = 1; tentativa <= MaximoTentativas; tentativa++)
        {
            var deviceId = _gerador.GerarDeviceId();
            if (await _repositorio.ExisteDeviceIdAsync(deviceId, cancellationToken))
            {
                _logger.LogWarning("device_id gerado já existe (tentativa {Tentativa} de {Maximo}).", tentativa, MaximoTentativas);
                continue;
            }

            var segredo = _gerador.GerarSegredoHmac();
            try
            {
                Estante estante;
                try
                {
                    estante = Estante.Criar(
                        comando.Nome, comando.Endereco, comando.Latitude, comando.Longitude,
                        deviceId, segredo, _relogio.GetUtcNow());
                }
                catch (ExcecaoDominio ex)
                {
                    return ResultadoEstante<EstanteCriadaDto>.Validacao(CredenciaisEstante.Erros(ex));
                }

                if (await _repositorio.AdicionarAsync(estante, cancellationToken) == ResultadoAdicaoEstante.DeviceIdEmUso)
                {
                    _logger.LogWarning(
                        "device_id recusado pelo índice único (corrida; tentativa {Tentativa} de {Maximo}).", tentativa, MaximoTentativas);
                    continue;
                }

                var credenciais = CredenciaisEstante.Montar(estante.DeviceId, segredo, _gerador.GerarSenhaMqtt());

                _logger.LogInformation(
                    "Estante {EstanteId} ({DeviceId}) criada pelo admin {AdminId}.", estante.Id, estante.DeviceId, comando.AdminId);

                return ResultadoEstante<EstanteCriadaDto>.Sucesso(EstanteCriadaDto.De(EstanteDto.De(estante), credenciais));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(segredo);
            }
        }

        _logger.LogError(
            "Não foi possível gerar um device_id livre em {Maximo} tentativas (cadastro pedido pelo admin {AdminId}). " +
            "Verifique o gerador de credenciais.", MaximoTentativas, comando.AdminId);

        return ResultadoEstante<EstanteCriadaDto>.Falha(StatusResultadoEstante.DeviceIdIndisponivel);
    }
}
