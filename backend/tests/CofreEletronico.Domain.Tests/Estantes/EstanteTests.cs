using CofreEletronico.Domain.Common;
using CofreEletronico.Domain.Estantes;

namespace CofreEletronico.Domain.Tests.Estantes;

public class EstanteTests
{
    private static readonly DateTimeOffset Agora = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);
    private static readonly byte[] Segredo = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();

    private static Estante Criar(
        string nome = "Estante Centro",
        string endereco = "Rua A, 100",
        double latitude = -23.55,
        double longitude = -46.63,
        string deviceId = "est-7k2m9q",
        byte[]? segredo = null,
        DateTimeOffset? criadoEm = null) =>
        Estante.Criar(nome, endereco, latitude, longitude, deviceId, segredo ?? Segredo, criadoEm ?? Agora);

    [Fact]
    public void Criar_gera_estante_ativa_offline_e_sem_heartbeat()
    {
        var estante = Criar();

        Assert.NotEqual(Guid.Empty, estante.Id);
        Assert.Equal("Estante Centro", estante.Nome);
        Assert.Equal("Rua A, 100", estante.Endereco);
        Assert.Equal(-23.55, estante.Latitude);
        Assert.Equal(-46.63, estante.Longitude);
        Assert.Equal("est-7k2m9q", estante.DeviceId);
        Assert.Equal(Segredo, estante.ObterSegredoHmac());
        Assert.Equal(Agora, estante.CriadoEm);
        Assert.True(estante.Ativa);
        Assert.False(estante.Online);
        Assert.Null(estante.UltimoHeartbeat);
    }

    [Fact]
    public void Criar_apara_nome_e_endereco_e_normaliza_criado_em_para_utc()
    {
        var estante = Criar(nome: "  Estante  ", endereco: " Rua B ", criadoEm: new DateTimeOffset(2026, 10, 18, 9, 0, 0, TimeSpan.FromHours(-3)));

        Assert.Equal("Estante", estante.Nome);
        Assert.Equal("Rua B", estante.Endereco);
        Assert.Equal(TimeSpan.Zero, estante.CriadoEm.Offset);
        Assert.Equal(Agora, estante.CriadoEm);
    }

    [Fact]
    public void Segredo_e_copiado_na_entrada_e_na_saida()
    {
        var segredo = (byte[])Segredo.Clone();
        var estante = Criar(segredo: segredo);

        segredo[0] = 0xFF;
        var lido = estante.ObterSegredoHmac();
        lido[1] = 0xFF;

        Assert.Equal(Segredo, estante.ObterSegredoHmac());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Nome_obrigatorio(string? nome)
    {
        Assert.Throws<ExcecaoDominio>(() => Criar(nome: nome!));
    }

    [Fact]
    public void Nome_com_mais_de_100_caracteres_falha_e_com_100_passa()
    {
        Assert.Throws<ExcecaoDominio>(() => Criar(nome: new string('a', 101)));
        Assert.Equal(100, Criar(nome: new string('a', 100)).Nome.Length);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Endereco_obrigatorio(string? endereco)
    {
        Assert.Throws<ExcecaoDominio>(() => Criar(endereco: endereco!));
    }

    [Fact]
    public void Endereco_com_mais_de_200_caracteres_falha_e_com_200_passa()
    {
        Assert.Throws<ExcecaoDominio>(() => Criar(endereco: new string('a', 201)));
        Assert.Equal(200, Criar(endereco: new string('a', 200)).Endereco.Length);
    }

    [Theory]
    [InlineData(-90.0001)]
    [InlineData(90.0001)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Latitude_fora_da_faixa_falha(double latitude)
    {
        Assert.Throws<ExcecaoDominio>(() => Criar(latitude: latitude));
    }

    [Theory]
    [InlineData(-180.0001)]
    [InlineData(180.0001)]
    [InlineData(double.NaN)]
    [InlineData(double.NegativeInfinity)]
    public void Longitude_fora_da_faixa_falha(double longitude)
    {
        Assert.Throws<ExcecaoDominio>(() => Criar(longitude: longitude));
    }

    [Theory]
    [InlineData(-90, -180)]
    [InlineData(90, 180)]
    [InlineData(0, 0)]
    public void Coordenadas_nos_limites_sao_aceitas(double latitude, double longitude)
    {
        var estante = Criar(latitude: latitude, longitude: longitude);

        Assert.Equal(latitude, estante.Latitude);
        Assert.Equal(longitude, estante.Longitude);
    }

    [Theory]
    [InlineData("est-abc123")]
    [InlineData("est-000000")]
    [InlineData("est-zzzzzz")]
    public void Device_id_valido_e_aceito(string deviceId)
    {
        Assert.Equal(deviceId, Criar(deviceId: deviceId).DeviceId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("est-abc12")] // 5 caracteres
    [InlineData("est-abc1234")] // 7 caracteres
    [InlineData("EST-abc123")]
    [InlineData("est-ABC123")]
    [InlineData("est_abc123")]
    [InlineData("xst-abc123")]
    [InlineData("est-abc12-")]
    [InlineData("est-abc12é")]
    [InlineData("est-abc123\n")] // `$` aceitaria; o padrão usa \z
    [InlineData(" est-abc123")]
    [InlineData("est-١٢٣abc")] // dígitos não ASCII
    public void Device_id_fora_do_formato_falha(string? deviceId)
    {
        Assert.Throws<ExcecaoDominio>(() => Criar(deviceId: deviceId!));
    }

    [Fact]
    public void Segredo_com_menos_de_32_bytes_falha()
    {
        Assert.Throws<ExcecaoDominio>(() => Criar(segredo: new byte[31]));
        Assert.Throws<ExcecaoDominio>(() => Criar(segredo: []));
        Assert.Throws<ExcecaoDominio>(() => Estante.Criar("n", "e", 0, 0, "est-abc123", null!, Agora));
    }

    [Fact]
    public void Segredo_com_32_bytes_e_aceito()
    {
        Assert.Equal(32, Criar(segredo: new byte[32]).ObterSegredoHmac().Length);
    }

    [Theory]
    [InlineData(33)]
    [InlineData(48)]
    [InlineData(64)]
    [InlineData(65)]
    public void Segredo_com_mais_de_32_bytes_falha(int tamanho)
    {
        // Contrato: a chave HMAC são exatamente os 32 bytes do segredo da estante.
        var erro = Assert.Throws<ExcecaoDominio>(() => Criar(segredo: new byte[tamanho]));

        Assert.Contains("exatamente 32 bytes", erro.Message);
    }

    [Fact]
    public void Segredo_nao_e_exposto_como_propriedade_nem_serializado()
    {
        // Serializadores (System.Text.Json, logs estruturados) leem propriedades públicas; o segredo só sai por método.
        Assert.DoesNotContain(typeof(Estante).GetProperties(), p => p.PropertyType == typeof(byte[]));

        var json = System.Text.Json.JsonSerializer.Serialize(Criar());
        Assert.DoesNotContain(Convert.ToBase64String(Segredo), json);
        Assert.DoesNotContain("egredo", json);
    }

    [Fact]
    public void Atualizar_altera_dados_cadastrais_e_mantem_device_id_e_segredo()
    {
        var estante = Criar();

        estante.Atualizar(" Nova ", "Rua Nova, 1", 10, 20);

        Assert.Equal("Nova", estante.Nome);
        Assert.Equal("Rua Nova, 1", estante.Endereco);
        Assert.Equal(10, estante.Latitude);
        Assert.Equal(20, estante.Longitude);
        Assert.Equal("est-7k2m9q", estante.DeviceId);
        Assert.Equal(Segredo, estante.ObterSegredoHmac());
    }

    [Fact]
    public void Atualizar_invalido_nao_altera_nada()
    {
        var estante = Criar();

        Assert.Throws<ExcecaoDominio>(() => estante.Atualizar("Nova", "Rua", 91, 0));
        Assert.Throws<ExcecaoDominio>(() => estante.Atualizar("", "Rua", 0, 0));
        Assert.Throws<ExcecaoDominio>(() => estante.Atualizar("Nova", new string('a', 201), 0, 0));
        Assert.Throws<ExcecaoDominio>(() => estante.Atualizar("Nova", "Rua", 0, -181));

        Assert.Equal("Estante Centro", estante.Nome);
        Assert.Equal("Rua A, 100", estante.Endereco);
        Assert.Equal(-23.55, estante.Latitude);
        Assert.Equal(-46.63, estante.Longitude);
    }

    [Fact]
    public void Desativar_e_reativar_sao_idempotentes()
    {
        var estante = Criar();

        estante.Desativar();
        estante.Desativar();
        Assert.False(estante.Ativa);

        estante.Reativar();
        estante.Reativar();
        Assert.True(estante.Ativa);
    }

    [Fact]
    public void Heartbeat_marca_online_e_registra_instante_em_utc()
    {
        var estante = Criar();

        estante.RegistrarHeartbeat(new DateTimeOffset(2026, 10, 18, 10, 0, 0, TimeSpan.FromHours(-3)));

        Assert.True(estante.Online);
        Assert.Equal(new DateTimeOffset(2026, 10, 18, 13, 0, 0, TimeSpan.Zero), estante.UltimoHeartbeat);
        Assert.Equal(TimeSpan.Zero, estante.UltimoHeartbeat!.Value.Offset);
    }

    [Fact]
    public void Heartbeat_atrasado_nao_faz_o_instante_voltar_mas_mantem_online()
    {
        var estante = Criar();
        estante.RegistrarHeartbeat(Agora.AddMinutes(5));
        estante.MarcarOffline();

        estante.RegistrarHeartbeat(Agora);

        Assert.True(estante.Online);
        Assert.Equal(Agora.AddMinutes(5), estante.UltimoHeartbeat);
    }

    [Fact]
    public void Marcar_offline_mantem_o_ultimo_heartbeat()
    {
        var estante = Criar();
        estante.RegistrarHeartbeat(Agora);

        estante.MarcarOffline();

        Assert.False(estante.Online);
        Assert.Equal(Agora, estante.UltimoHeartbeat);
    }

    // ---------- SubstituirSegredoHmac (T-0402) ----------

    [Fact]
    public void SubstituirSegredoHmac_troca_o_segredo_e_mantem_o_device_id()
    {
        var estante = Criar();
        var novo = Enumerable.Range(100, 32).Select(i => (byte)i).ToArray();

        estante.SubstituirSegredoHmac(novo);

        Assert.Equal(novo, estante.ObterSegredoHmac());
        Assert.Equal("est-7k2m9q", estante.DeviceId);
    }

    [Fact]
    public void SubstituirSegredoHmac_guarda_copia_do_array_recebido()
    {
        var estante = Criar();
        var novo = Enumerable.Range(100, 32).Select(i => (byte)i).ToArray();
        var esperado = (byte[])novo.Clone();

        estante.SubstituirSegredoHmac(novo);
        Array.Clear(novo);

        Assert.Equal(esperado, estante.ObterSegredoHmac());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(16)]
    [InlineData(31)]
    [InlineData(33)]
    [InlineData(64)]
    [InlineData(65)]
    public void SubstituirSegredoHmac_com_tamanho_invalido_lanca_e_mantem_o_anterior(int tamanho)
    {
        var estante = Criar();

        var ex = Assert.Throws<ExcecaoDominio>(() => estante.SubstituirSegredoHmac(new byte[tamanho]));

        Assert.Contains("segredo HMAC", ex.Message);
        Assert.Equal(Segredo, estante.ObterSegredoHmac());
    }

    [Fact]
    public void SubstituirSegredoHmac_nulo_lanca_ExcecaoDominio()
    {
        var estante = Criar();

        Assert.Throws<ExcecaoDominio>(() => estante.SubstituirSegredoHmac(null!));
        Assert.Equal(Segredo, estante.ObterSegredoHmac());
    }
}
