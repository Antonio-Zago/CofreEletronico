using System.Net;
using System.Net.Sockets;

namespace CofreEletronico.Api.Autenticacao;

/// <summary>Chave de partição do rate limit a partir do IP do cliente.</summary>
public static class ChaveRateLimit
{
    public const string Desconhecido = "desconhecido";

    /// <summary>
    /// IPv4 (inclusive IPv4 mapeado em IPv6, ex.: <c>::ffff:10.0.0.1</c>) → o próprio endereço.
    /// IPv6 → o prefixo /64: um único cliente costuma receber um /64 inteiro e trocaria de endereço
    /// à vontade para escapar de um limite por endereço. Escopo (<c>%eth0</c>) é descartado.
    /// </summary>
    public static string Gerar(IPAddress? ip)
    {
        if (ip is null)
            return Desconhecido;

        if (ip.IsIPv4MappedToIPv6)
            ip = ip.MapToIPv4();

        if (ip.AddressFamily != AddressFamily.InterNetworkV6)
            return ip.ToString();

        var bytes = ip.GetAddressBytes();
        Array.Clear(bytes, 8, 8);
        return $"{new IPAddress(bytes)}/64";
    }
}
