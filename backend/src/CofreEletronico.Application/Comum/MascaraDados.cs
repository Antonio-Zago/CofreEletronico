namespace CofreEletronico.Application.Comum;

/// <summary>Mascaramento de dados pessoais para logs (nunca registre o valor completo).</summary>
public static class MascaraDados
{
    /// <summary>
    /// Mantém só o primeiro caractere da parte local e o domínio: <c>ana@exemplo.com</c> vira
    /// <c>a***@exemplo.com</c>. Valor vazio ou sem <c>@</c> vira <c>***</c>.
    /// </summary>
    public static string MascararEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return "***";

        var valor = email.Trim();
        var arroba = valor.LastIndexOf('@');
        if (arroba <= 0 || arroba == valor.Length - 1)
            return "***";

        return $"{valor[0]}***{valor[arroba..]}";
    }
}
