using CofreEletronico.Application.Estantes;
using CofreEletronico.Domain.Estantes;
using Microsoft.Extensions.Logging;

namespace CofreEletronico.Application.Tests.Estantes;

public class ListarEObterEstantesTests
{
    private readonly EstantesCenario _cenario = new();

    [Fact]
    public async Task Lista_paginada_ordenada_por_nome_com_total()
    {
        var nomes = new[] { "Delta", "Alfa", "Charlie", "Bravo", "Eco" };
        for (var i = 0; i < nomes.Length; i++)
            _cenario.AdicionarEstante(nomes[i], $"est-00000{i}");

        var resultado = await _cenario.Listar.ExecutarAsync(new ListarEstantesConsulta(null, 2, 2));

        Assert.Equal(StatusResultadoEstante.Sucesso, resultado.Status);
        var pagina = resultado.Valor!;
        Assert.Equal(["Charlie", "Delta"], pagina.Itens.Select(e => e.Nome));
        Assert.Equal(2, pagina.Pagina);
        Assert.Equal(2, pagina.TamanhoPagina);
        Assert.Equal(5, pagina.Total);
    }

    [Theory]
    [InlineData(true, new[] { "Alfa", "Charlie" })]
    [InlineData(false, new[] { "Bravo" })]
    [InlineData(null, new[] { "Alfa", "Bravo", "Charlie" })]
    public async Task Filtro_por_ativa(bool? ativa, string[] esperados)
    {
        _cenario.AdicionarEstante("Alfa", "est-000001");
        _cenario.AdicionarEstante("Bravo", "est-000002", ativa: false);
        _cenario.AdicionarEstante("Charlie", "est-000003");

        var resultado = await _cenario.Listar.ExecutarAsync(new ListarEstantesConsulta(ativa, 1, 20));

        Assert.Equal(esperados, resultado.Valor!.Itens.Select(e => e.Nome));
        Assert.Equal(esperados.Length, resultado.Valor.Total);
    }

    [Theory]
    [InlineData(0, 20, "pagina")]
    [InlineData(1, 0, "tamanhoPagina")]
    [InlineData(1, 101, "tamanhoPagina")]
    public async Task Paginacao_invalida_vira_Validacao(int pagina, int tamanho, string campo)
    {
        var resultado = await _cenario.Listar.ExecutarAsync(new ListarEstantesConsulta(null, pagina, tamanho));

        Assert.Equal(StatusResultadoEstante.Validacao, resultado.Status);
        Assert.True(resultado.Erros.ContainsKey(campo));
    }

    [Fact]
    public async Task Obter_existente_devolve_DTO()
    {
        var estante = _cenario.AdicionarEstante();

        var resultado = await _cenario.Obter.ExecutarAsync(estante.Id);

        Assert.Equal(StatusResultadoEstante.Sucesso, resultado.Status);
        Assert.Equal(EstanteDto.De(estante), resultado.Valor);
    }

    [Fact]
    public async Task Obter_inexistente_devolve_NaoEncontrada()
    {
        var resultado = await _cenario.Obter.ExecutarAsync(Guid.NewGuid());

        Assert.Equal(StatusResultadoEstante.NaoEncontrada, resultado.Status);
        Assert.Null(resultado.Valor);
    }
}

public class AtualizarEstanteTests
{
    private readonly EstantesCenario _cenario = new();

    [Fact]
    public async Task Atualiza_dados_cadastrais_salva_e_mantem_device_id_e_segredo()
    {
        var estante = _cenario.AdicionarEstante();
        var segredoAntes = estante.ObterSegredoHmac();

        var resultado = await _cenario.Atualizar.ExecutarAsync(
            new AtualizarEstanteComando(EstantesCenario.AdminId, estante.Id, " Nova ", "Rua B, 200", 10, 20));

        Assert.Equal(StatusResultadoEstante.Sucesso, resultado.Status);
        Assert.Equal("Nova", resultado.Valor!.Nome);
        Assert.Equal("Rua B, 200", resultado.Valor.Endereco);
        Assert.Equal(10, resultado.Valor.Latitude);
        Assert.Equal(20, resultado.Valor.Longitude);
        Assert.Equal("est-abc123", resultado.Valor.DeviceId);
        Assert.Equal(segredoAntes, estante.ObterSegredoHmac());
        Assert.Equal(1, _cenario.Repositorio.Salvamentos);
        Assert.Contains(_cenario.LogAtualizar.Registros, r =>
            r.Nivel == LogLevel.Information && r.Mensagem.Contains(estante.Id.ToString()) &&
            r.Mensagem.Contains(EstantesCenario.AdminId.ToString()));
    }

    [Fact]
    public async Task Dados_invalidos_viram_Validacao_sem_alterar_nem_salvar()
    {
        var estante = _cenario.AdicionarEstante();

        var resultado = await _cenario.Atualizar.ExecutarAsync(
            new AtualizarEstanteComando(EstantesCenario.AdminId, estante.Id, "Nova", "Rua B", 0, 181));

        Assert.Equal(StatusResultadoEstante.Validacao, resultado.Status);
        Assert.Equal("Estante Centro", estante.Nome);
        Assert.Equal(0, _cenario.Repositorio.Salvamentos);
    }

    [Fact]
    public async Task Inexistente_devolve_NaoEncontrada()
    {
        var resultado = await _cenario.Atualizar.ExecutarAsync(
            new AtualizarEstanteComando(EstantesCenario.AdminId, Guid.NewGuid(), "Nova", "Rua B", 0, 0));

        Assert.Equal(StatusResultadoEstante.NaoEncontrada, resultado.Status);
        Assert.Equal(0, _cenario.Repositorio.Salvamentos);
    }
}

public class DesativarEReativarEstanteTests
{
    private readonly EstantesCenario _cenario = new();

    private Task<ResultadoEstante<EstanteDto>> Desativar(Guid id) =>
        _cenario.Desativar.ExecutarAsync(new AcaoEstanteComando(EstantesCenario.AdminId, id));

    private Task<ResultadoEstante<EstanteDto>> Reativar(Guid id) =>
        _cenario.Reativar.ExecutarAsync(new AcaoEstanteComando(EstantesCenario.AdminId, id));

    [Theory]
    [InlineData(StatusCofre.Reservado)]
    [InlineData(StatusCofre.Ocupado)]
    public async Task Desativar_com_cofre_em_uso_devolve_Conflito_e_nao_altera(StatusCofre status)
    {
        var estante = _cenario.AdicionarEstante();
        _cenario.AdicionarCofre(estante.Id, StatusCofre.Livre, numero: 1);
        _cenario.AdicionarCofre(estante.Id, status, numero: 2);

        var resultado = await Desativar(estante.Id);

        Assert.Equal(StatusResultadoEstante.Conflito, resultado.Status);
        Assert.True(estante.Ativa);
        Assert.Equal(0, _cenario.Repositorio.Salvamentos);
    }

    [Theory]
    [InlineData(StatusCofre.Livre)]
    [InlineData(StatusCofre.Manutencao)]
    public async Task Desativar_com_cofres_livres_ou_em_manutencao_desativa(StatusCofre status)
    {
        var estante = _cenario.AdicionarEstante();
        _cenario.AdicionarCofre(estante.Id, status);

        var resultado = await Desativar(estante.Id);

        Assert.Equal(StatusResultadoEstante.Sucesso, resultado.Status);
        Assert.False(resultado.Valor!.Ativa);
        Assert.False(estante.Ativa);
        Assert.Equal(1, _cenario.Repositorio.Salvamentos);
    }

    [Fact]
    public async Task Desativar_sem_cofres_desativa_e_loga_auditoria()
    {
        var estante = _cenario.AdicionarEstante();

        var resultado = await Desativar(estante.Id);

        Assert.Equal(StatusResultadoEstante.Sucesso, resultado.Status);
        Assert.False(estante.Ativa);
        var log = Assert.Single(_cenario.LogDesativar.Registros).Mensagem;
        Assert.Contains(estante.Id.ToString(), log);
        Assert.Contains(estante.DeviceId, log);
        Assert.Contains(EstantesCenario.AdminId.ToString(), log);
    }

    [Fact]
    public async Task Cofre_em_uso_de_outra_estante_nao_impede()
    {
        var estante = _cenario.AdicionarEstante();
        var outra = _cenario.AdicionarEstante("Outra", "est-zzz999");
        _cenario.AdicionarCofre(outra.Id, StatusCofre.Ocupado);

        var resultado = await Desativar(estante.Id);

        Assert.Equal(StatusResultadoEstante.Sucesso, resultado.Status);
    }

    [Fact]
    public async Task Desativar_ja_inativa_e_idempotente_sem_salvar()
    {
        var estante = _cenario.AdicionarEstante(ativa: false);

        var resultado = await Desativar(estante.Id);

        Assert.Equal(StatusResultadoEstante.Sucesso, resultado.Status);
        Assert.False(resultado.Valor!.Ativa);
        Assert.Equal(0, _cenario.Repositorio.Salvamentos);
    }

    [Fact]
    public async Task Reativar_inativa_reativa_e_salva()
    {
        var estante = _cenario.AdicionarEstante(ativa: false);

        var resultado = await Reativar(estante.Id);

        Assert.Equal(StatusResultadoEstante.Sucesso, resultado.Status);
        Assert.True(resultado.Valor!.Ativa);
        Assert.Equal(1, _cenario.Repositorio.Salvamentos);
        Assert.Single(_cenario.LogReativar.Registros);
    }

    [Fact]
    public async Task Reativar_ja_ativa_e_idempotente_sem_salvar()
    {
        var estante = _cenario.AdicionarEstante();

        var resultado = await Reativar(estante.Id);

        Assert.Equal(StatusResultadoEstante.Sucesso, resultado.Status);
        Assert.True(resultado.Valor!.Ativa);
        Assert.Equal(0, _cenario.Repositorio.Salvamentos);
    }

    [Fact]
    public async Task Inexistente_devolve_NaoEncontrada_nas_duas_acoes()
    {
        Assert.Equal(StatusResultadoEstante.NaoEncontrada, (await Desativar(Guid.NewGuid())).Status);
        Assert.Equal(StatusResultadoEstante.NaoEncontrada, (await Reativar(Guid.NewGuid())).Status);
    }
}

public class RegenerarCredenciaisEstanteTests
{
    private readonly EstantesCenario _cenario = new();

    [Fact]
    public async Task Troca_o_segredo_salva_e_mantem_o_device_id()
    {
        var estante = _cenario.AdicionarEstante();
        var segredoAntigo = estante.ObterSegredoHmac();

        var resultado = await _cenario.Regenerar.ExecutarAsync(new AcaoEstanteComando(EstantesCenario.AdminId, estante.Id));

        Assert.Equal(StatusResultadoEstante.Sucesso, resultado.Status);
        var credenciais = resultado.Valor!;
        Assert.Equal("est-abc123", credenciais.DeviceId);
        Assert.Equal("est-abc123", credenciais.UsuarioMqtt);
        Assert.Equal("est-abc123", estante.DeviceId);
        Assert.Equal(_cenario.Gerador.SenhasGeradas.Single(), credenciais.SenhaMqtt);

        var segredoNovo = estante.ObterSegredoHmac();
        Assert.NotEqual(segredoAntigo, segredoNovo);
        Assert.Equal(_cenario.Gerador.SegredosGerados.Single(), segredoNovo);
        Assert.Equal(Convert.ToHexString(segredoNovo).ToLowerInvariant(), credenciais.SegredoHmac);
        Assert.Equal(1, _cenario.Repositorio.Salvamentos);
    }

    [Fact]
    public async Task Log_de_auditoria_sem_segredo_nem_senha()
    {
        var estante = _cenario.AdicionarEstante();

        var resultado = await _cenario.Regenerar.ExecutarAsync(new AcaoEstanteComando(EstantesCenario.AdminId, estante.Id));

        var log = Assert.Single(_cenario.LogRegenerar.Registros).Mensagem;
        Assert.Contains(estante.Id.ToString(), log);
        Assert.Contains(EstantesCenario.AdminId.ToString(), log);
        Assert.DoesNotContain(resultado.Valor!.SegredoHmac, log, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(resultado.Valor.SenhaMqtt, log);
    }

    [Fact]
    public async Task Inexistente_devolve_NaoEncontrada_sem_gerar_nada()
    {
        var resultado = await _cenario.Regenerar.ExecutarAsync(new AcaoEstanteComando(EstantesCenario.AdminId, Guid.NewGuid()));

        Assert.Equal(StatusResultadoEstante.NaoEncontrada, resultado.Status);
        Assert.Empty(_cenario.Gerador.SegredosGerados);
        Assert.Equal(0, _cenario.Repositorio.Salvamentos);
    }
}
