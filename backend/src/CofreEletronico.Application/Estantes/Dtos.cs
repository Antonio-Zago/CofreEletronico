using System.Text;
using CofreEletronico.Domain.Estantes;

namespace CofreEletronico.Application.Estantes;

/// <summary>
/// Estante como sai da Api (contrato em docs/ARQUITETURA.md, "Objeto estante"). Nunca contém o segredo HMAC.
/// </summary>
public sealed record EstanteDto(
    Guid Id,
    string Nome,
    string Endereco,
    double Latitude,
    double Longitude,
    string DeviceId,
    bool Online,
    DateTimeOffset? UltimoHeartbeat,
    bool Ativa,
    DateTimeOffset CriadoEm)
{
    public static EstanteDto De(Estante estante) => new(
        estante.Id, estante.Nome, estante.Endereco, estante.Latitude, estante.Longitude, estante.DeviceId,
        estante.Online, estante.UltimoHeartbeat, estante.Ativa, estante.CriadoEm);
}

/// <summary>
/// Credenciais de provisionamento do ESP32 (contrato: objeto <c>credenciais</c>). Existe só na resposta do cadastro
/// e da regeneração: <see cref="SegredoHmac"/> (hex minúsculo, 64 caracteres) e <see cref="SenhaMqtt"/> não ficam
/// guardados em lugar nenhum fora desse objeto (a senha MQTT nem é armazenada no backend).
/// </summary>
/// <remarks>
/// <see cref="ToString"/> é sobrescrito para mascarar os segredos: um log ou mensagem de exceção que formate o objeto
/// por engano não os expõe. A serialização JSON (resposta HTTP) continua com os valores reais.
/// </remarks>
public sealed record CredenciaisEstanteDto(string DeviceId, string SegredoHmac, string UsuarioMqtt, string SenhaMqtt)
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("DeviceId = ").Append(DeviceId)
            .Append(", SegredoHmac = ***, UsuarioMqtt = ").Append(UsuarioMqtt)
            .Append(", SenhaMqtt = ***");
        return true;
    }
}

/// <summary>Resposta do cadastro: os campos da estante mais o objeto <c>credenciais</c> (exibido uma única vez).</summary>
public sealed record EstanteCriadaDto(
    Guid Id,
    string Nome,
    string Endereco,
    double Latitude,
    double Longitude,
    string DeviceId,
    bool Online,
    DateTimeOffset? UltimoHeartbeat,
    bool Ativa,
    DateTimeOffset CriadoEm,
    CredenciaisEstanteDto Credenciais)
{
    public static EstanteCriadaDto De(EstanteDto e, CredenciaisEstanteDto credenciais) => new(
        e.Id, e.Nome, e.Endereco, e.Latitude, e.Longitude, e.DeviceId, e.Online, e.UltimoHeartbeat, e.Ativa, e.CriadoEm,
        credenciais);
}

/// <summary>Página de uma listagem (contrato: <c>{ itens, pagina, tamanhoPagina, total }</c>).</summary>
public sealed record PaginaDto<T>(IReadOnlyList<T> Itens, int Pagina, int TamanhoPagina, int Total);
