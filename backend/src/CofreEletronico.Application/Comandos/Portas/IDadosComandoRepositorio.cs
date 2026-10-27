using System.Text;

namespace CofreEletronico.Application.Comandos.Portas;

/// <summary>
/// Dados para montar um comando: device_id da estante, número e canal do cofre e o segredo HMAC em claro.
/// <see cref="SegredoHmac"/> é um array próprio de quem recebeu: quem chama o zera depois de assinar
/// (<c>CryptographicOperations.ZeroMemory</c>). O <c>ToString</c> mascara o segredo.
/// </summary>
public sealed record DadosComando(string DeviceId, int NumeroCofre, int CanalRele, byte[] SegredoHmac)
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("DeviceId = ").Append(DeviceId)
            .Append(", NumeroCofre = ").Append(NumeroCofre)
            .Append(", CanalRele = ").Append(CanalRele)
            .Append(", SegredoHmac = ***");
        return true;
    }
}

/// <summary>Leitura dos dados de comando (implementada na Infrastructure, projeção sem rastreamento).</summary>
public interface IDadosComandoRepositorio
{
    /// <summary>
    /// Dados do cofre <paramref name="cofreId"/>, só se ele pertencer à estante <paramref name="estanteId"/>; senão,
    /// <c>null</c>. Não filtra por status nem por ativo/inativo: essas regras são de quem chama.
    /// </summary>
    Task<DadosComando?> ObterAsync(Guid estanteId, Guid cofreId, CancellationToken cancellationToken);
}
