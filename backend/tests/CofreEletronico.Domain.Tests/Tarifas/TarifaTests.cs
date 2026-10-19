using CofreEletronico.Domain.Common;
using CofreEletronico.Domain.Estantes;
using CofreEletronico.Domain.Tarifas;

namespace CofreEletronico.Domain.Tests.Tarifas;

public class TarifaTests
{
    [Fact]
    public void Criar_guarda_os_valores()
    {
        var tarifa = Tarifa.Criar(TamanhoCofre.Grande, 12.50m, 30);

        Assert.NotEqual(Guid.Empty, tarifa.Id);
        Assert.Equal(TamanhoCofre.Grande, tarifa.Tamanho);
        Assert.Equal(12.50m, tarifa.ValorPorHora);
        Assert.Equal(30, tarifa.TempoMinimo);
    }

    [Theory]
    [InlineData("0.01")]
    [InlineData("1")]
    [InlineData("10.5")]
    [InlineData("10.500")] // escala 3, mas valor com 2 casas significativas
    [InlineData("99999999.99")]
    public void Valores_validos_sao_aceitos(string valor)
    {
        var decimalValor = decimal.Parse(valor, System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(decimalValor, Tarifa.Criar(TamanhoCofre.Pequeno, decimalValor, 1).ValorPorHora);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("0.001")]
    [InlineData("10.555")]
    [InlineData("100000000")] // não cabe em numeric(10,2)
    public void Valor_por_hora_invalido_falha(string valor)
    {
        var decimalValor = decimal.Parse(valor, System.Globalization.CultureInfo.InvariantCulture);

        Assert.Throws<ExcecaoDominio>(() => Tarifa.Criar(TamanhoCofre.Pequeno, decimalValor, 30));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Tempo_minimo_deve_ser_positivo(int tempoMinimo)
    {
        Assert.Throws<ExcecaoDominio>(() => Tarifa.Criar(TamanhoCofre.Pequeno, 5m, tempoMinimo));
    }

    [Fact]
    public void Tamanho_fora_do_enum_falha()
    {
        Assert.Throws<ExcecaoDominio>(() => Tarifa.Criar((TamanhoCofre)0, 5m, 30));
    }

    [Fact]
    public void Atualizar_altera_valor_e_tempo_e_mantem_o_tamanho()
    {
        var tarifa = Tarifa.Criar(TamanhoCofre.Medio, 5m, 30);

        tarifa.Atualizar(7.25m, 60);

        Assert.Equal(TamanhoCofre.Medio, tarifa.Tamanho);
        Assert.Equal(7.25m, tarifa.ValorPorHora);
        Assert.Equal(60, tarifa.TempoMinimo);
    }

    [Fact]
    public void Atualizar_invalido_nao_altera_nada()
    {
        var tarifa = Tarifa.Criar(TamanhoCofre.Medio, 5m, 30);

        Assert.Throws<ExcecaoDominio>(() => tarifa.Atualizar(0m, 60));
        Assert.Throws<ExcecaoDominio>(() => tarifa.Atualizar(7.255m, 60));
        Assert.Throws<ExcecaoDominio>(() => tarifa.Atualizar(7m, 0));

        Assert.Equal(5m, tarifa.ValorPorHora);
        Assert.Equal(30, tarifa.TempoMinimo);
    }
}
