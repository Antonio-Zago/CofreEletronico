using CofreEletronico.Domain.Common;

namespace CofreEletronico.Application.Estantes;

/// <summary>Montagem das credenciais devolvidas ao admin e tradução de erros de domínio, comuns aos casos de uso.</summary>
internal static class CredenciaisEstante
{
    /// <summary>Chave do erro de validação quando a regra violada vem do domínio (mensagem segura, sem segredos).</summary>
    public const string ChaveErroDominio = "estante";

    /// <summary>
    /// Monta o DTO (segredo em hex minúsculo; usuário MQTT = device_id). Não zera <paramref name="segredo"/>: quem
    /// chama zera o array num <c>finally</c> (<c>CryptographicOperations.ZeroMemory</c>), inclusive se salvar falhar.
    /// </summary>
    public static CredenciaisEstanteDto Montar(string deviceId, byte[] segredo, string senhaMqtt) =>
        new(deviceId, Convert.ToHexString(segredo).ToLowerInvariant(), deviceId, senhaMqtt);

    public static IReadOnlyDictionary<string, string[]> Erros(ExcecaoDominio ex) =>
        new Dictionary<string, string[]> { [ChaveErroDominio] = [ex.Message] };
}
