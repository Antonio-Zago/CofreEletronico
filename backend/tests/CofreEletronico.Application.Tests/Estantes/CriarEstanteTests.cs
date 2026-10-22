using CofreEletronico.Application.Estantes;
using Microsoft.Extensions.Logging;

namespace CofreEletronico.Application.Tests.Estantes;

public class CriarEstanteTests
{
    private readonly EstantesCenario _cenario = new();

    private static CriarEstanteComando Comando(string nome = "Estante Centro", double latitude = -23.55) =>
        new(EstantesCenario.AdminId, nome, "Rua A, 100", latitude, -46.63);

    [Fact]
    public async Task Cria_estante_ativa_e_offline_e_devolve_as_credenciais()
    {
        _cenario.Gerador.ProximosDeviceIds.Enqueue("est-7k2m9q");

        var resultado = await _cenario.Criar.ExecutarAsync(Comando());

        Assert.Equal(StatusResultadoEstante.Sucesso, resultado.Status);
        var criada = resultado.Valor!;
        Assert.Equal("est-7k2m9q", criada.DeviceId);
        Assert.Equal("Estante Centro", criada.Nome);
        Assert.True(criada.Ativa);
        Assert.False(criada.Online);
        Assert.Null(criada.UltimoHeartbeat);
        Assert.Equal(EstantesCenario.Inicio, criada.CriadoEm);

        var credenciais = criada.Credenciais;
        Assert.Equal("est-7k2m9q", credenciais.DeviceId);
        Assert.Equal("est-7k2m9q", credenciais.UsuarioMqtt);
        Assert.Equal(_cenario.Gerador.SenhasGeradas.Single(), credenciais.SenhaMqtt);
        Assert.Matches("^[0-9a-f]{64}$", credenciais.SegredoHmac);

        var estante = Assert.Single(_cenario.Repositorio.Estantes);
        Assert.Equal(criada.Id, estante.Id);
        Assert.Equal("est-7k2m9q", estante.DeviceId);
    }

    [Fact]
    public async Task Segredo_guardado_na_entidade_e_o_mesmo_devolvido_em_hex()
    {
        var resultado = await _cenario.Criar.ExecutarAsync(Comando());

        var estante = Assert.Single(_cenario.Repositorio.Estantes);
        var segredo = estante.ObterSegredoHmac();
        Assert.Equal(_cenario.Gerador.SegredosGerados.Single(), segredo);
        Assert.Equal(Convert.ToHexString(segredo).ToLowerInvariant(), resultado.Valor!.Credenciais.SegredoHmac);
    }

    [Fact]
    public async Task Device_id_ja_existente_na_consulta_previa_gera_outro()
    {
        _cenario.Repositorio.DeviceIdsOcupados.Add("est-aaaaaa");
        _cenario.Gerador.ProximosDeviceIds.Enqueue("est-aaaaaa");
        _cenario.Gerador.ProximosDeviceIds.Enqueue("est-bbbbbb");

        var resultado = await _cenario.Criar.ExecutarAsync(Comando());

        Assert.Equal(StatusResultadoEstante.Sucesso, resultado.Status);
        Assert.Equal("est-bbbbbb", resultado.Valor!.DeviceId);
        Assert.Equal(["est-aaaaaa", "est-bbbbbb"], _cenario.Repositorio.DeviceIdsConsultados);
        Assert.Single(_cenario.Repositorio.TentativasDeAdicao); // o repetido nem chegou a ser inserido
    }

    [Fact]
    public async Task Recusa_do_indice_unico_por_corrida_tenta_de_novo_com_outro_device_id_e_outro_segredo()
    {
        _cenario.Repositorio.RecusasDoIndice = 2;
        _cenario.Gerador.ProximosDeviceIds.Enqueue("est-aaaaaa");
        _cenario.Gerador.ProximosDeviceIds.Enqueue("est-bbbbbb");
        _cenario.Gerador.ProximosDeviceIds.Enqueue("est-cccccc");

        var resultado = await _cenario.Criar.ExecutarAsync(Comando());

        Assert.Equal(StatusResultadoEstante.Sucesso, resultado.Status);
        Assert.Equal("est-cccccc", resultado.Valor!.DeviceId);
        Assert.Equal(3, _cenario.Repositorio.TentativasDeAdicao.Count);
        Assert.Equal(3, _cenario.Gerador.SegredosGerados.Count);
        Assert.Single(_cenario.Gerador.SenhasGeradas); // senha só para a tentativa que deu certo
        var estante = Assert.Single(_cenario.Repositorio.Estantes);
        Assert.Equal(_cenario.Gerador.SegredosGerados[2], estante.ObterSegredoHmac());
        Assert.Equal(2, _cenario.LogCriar.Registros.Count(r => r.Nivel == LogLevel.Warning));
    }

    [Fact]
    public async Task Esgotadas_as_tentativas_devolve_DeviceIdIndisponivel_e_loga_erro()
    {
        _cenario.Repositorio.RecusasDoIndice = int.MaxValue;

        var resultado = await _cenario.Criar.ExecutarAsync(Comando());

        Assert.Equal(StatusResultadoEstante.DeviceIdIndisponivel, resultado.Status);
        Assert.Null(resultado.Valor);
        Assert.Equal(CriarEstante.MaximoTentativas, _cenario.Repositorio.TentativasDeAdicao.Count);
        Assert.Empty(_cenario.Repositorio.Estantes);
        Assert.Empty(_cenario.Gerador.SenhasGeradas);
        Assert.Contains(_cenario.LogCriar.Registros, r => r.Nivel == LogLevel.Error);
    }

    [Fact]
    public async Task Consulta_previa_sempre_ocupada_tambem_esgota_as_tentativas()
    {
        for (var i = 0; i < CriarEstante.MaximoTentativas; i++)
        {
            var deviceId = $"est-00000{i}";
            _cenario.Repositorio.DeviceIdsOcupados.Add(deviceId);
            _cenario.Gerador.ProximosDeviceIds.Enqueue(deviceId);
        }

        var resultado = await _cenario.Criar.ExecutarAsync(Comando());

        Assert.Equal(StatusResultadoEstante.DeviceIdIndisponivel, resultado.Status);
        Assert.Empty(_cenario.Repositorio.TentativasDeAdicao);
    }

    [Fact]
    public async Task Dados_invalidos_pelo_dominio_viram_Validacao_sem_gravar()
    {
        var resultado = await _cenario.Criar.ExecutarAsync(Comando(latitude: 91));

        Assert.Equal(StatusResultadoEstante.Validacao, resultado.Status);
        Assert.Contains("latitude", Assert.Single(resultado.Erros).Value.Single());
        Assert.Empty(_cenario.Repositorio.TentativasDeAdicao);
    }

    [Fact]
    public async Task Log_de_auditoria_tem_admin_estante_e_device_id_e_nenhum_segredo()
    {
        var resultado = await _cenario.Criar.ExecutarAsync(Comando());

        var criada = resultado.Valor!;
        var log = _cenario.LogCriar.Registros.Single(r => r.Nivel == LogLevel.Information).Mensagem;
        Assert.Contains(EstantesCenario.AdminId.ToString(), log);
        Assert.Contains(criada.Id.ToString(), log);
        Assert.Contains(criada.DeviceId, log);
        Assert.DoesNotContain(criada.Credenciais.SegredoHmac, _cenario.TodosOsLogs, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(criada.Credenciais.SenhaMqtt, _cenario.TodosOsLogs);
    }

    [Fact]
    public void ToString_das_credenciais_mascara_segredo_e_senha()
    {
        var credenciais = new CredenciaisEstanteDto("est-7k2m9q", new string('a', 64), "est-7k2m9q", "SenhaSecreta123456789012");
        var criada = new EstanteCriadaDto(Guid.NewGuid(), "n", "e", 0, 0, "est-7k2m9q", false, null, true,
            EstantesCenario.Inicio, credenciais);

        foreach (var texto in new[] { credenciais.ToString(), criada.ToString() })
        {
            Assert.Contains("est-7k2m9q", texto);
            Assert.DoesNotContain(new string('a', 64), texto);
            Assert.DoesNotContain("SenhaSecreta", texto);
        }
    }
}
