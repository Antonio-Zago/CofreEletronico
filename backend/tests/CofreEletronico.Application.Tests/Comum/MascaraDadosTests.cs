using CofreEletronico.Application.Comum;

namespace CofreEletronico.Application.Tests.Comum;

public class MascaraDadosTests
{
    [Theory]
    [InlineData("ana@exemplo.com", "a***@exemplo.com")]
    [InlineData("  admin@cofre.com.br ", "a***@cofre.com.br")]
    [InlineData("x@d.io", "x***@d.io")]
    [InlineData(null, "***")]
    [InlineData("", "***")]
    [InlineData("sem-arroba", "***")]
    [InlineData("@dominio.com", "***")]
    [InlineData("ana@", "***")]
    public void Mascara_o_email_mantendo_so_a_inicial_e_o_dominio(string? email, string esperado)
    {
        Assert.Equal(esperado, MascaraDados.MascararEmail(email));
    }
}
