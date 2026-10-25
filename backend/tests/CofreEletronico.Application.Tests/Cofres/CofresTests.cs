using CofreEletronico.Application.Cofres;
using CofreEletronico.Application.Cofres.Portas;
using CofreEletronico.Domain.Estantes;
using Microsoft.Extensions.Logging;

namespace CofreEletronico.Application.Tests.Cofres;

public class CriarCofreTests
{
    private readonly CofresCenario _cenario = new();

    private static CriarCofreComando Comando(Guid estanteId, int numero = 1, int canal = 0, TamanhoCofre tamanho = TamanhoCofre.Pequeno) =>
        new(CofresCenario.AdminId, estanteId, numero, canal, tamanho);

    [Fact]
    public async Task Cria_cofre_livre_e_ativo_e_registra_auditoria()
    {
        var estanteId = _cenario.AdicionarEstante();

        var resultado = await _cenario.Criar.ExecutarAsync(Comando(estanteId, 4, 3, TamanhoCofre.Grande));

        Assert.Equal(StatusResultadoCofre.Sucesso, resultado.Status);
        var dto = resultado.Valor!;
        Assert.Equal(estanteId, dto.EstanteId);
        Assert.Equal(4, dto.Numero);
        Assert.Equal(3, dto.CanalRele);
        Assert.Equal("grande", dto.Tamanho);
        Assert.Equal("livre", dto.Status);
        Assert.True(dto.Ativo);
        Assert.Equal(dto.Id, Assert.Single(_cenario.Repositorio.Cofres).Id);

        var log = Assert.Single(_cenario.LogCriar.Registros, r => r.Nivel == LogLevel.Information).Mensagem;
        Assert.Contains(CofresCenario.AdminId.ToString(), log);
        Assert.Contains(estanteId.ToString(), log);
        Assert.Contains(dto.Id.ToString(), log);
        Assert.Contains("número 4", log);
        Assert.Contains("canal 3", log);
    }

    [Fact]
    public async Task Estante_inexistente_devolve_EstanteNaoEncontrada()
    {
        var resultado = await _cenario.Criar.ExecutarAsync(Comando(Guid.NewGuid()));

        Assert.Equal(StatusResultadoCofre.EstanteNaoEncontrada, resultado.Status);
        Assert.Empty(_cenario.Repositorio.TentativasDeAdicao);
    }

    [Theory]
    [InlineData(0, 0, TamanhoCofre.Pequeno)]
    [InlineData(1, 16, TamanhoCofre.Pequeno)]
    [InlineData(1, -1, TamanhoCofre.Pequeno)]
    [InlineData(1, 0, (TamanhoCofre)9)]
    public async Task Dados_invalidos_viram_Validacao(int numero, int canal, TamanhoCofre tamanho)
    {
        var estanteId = _cenario.AdicionarEstante();

        var resultado = await _cenario.Criar.ExecutarAsync(Comando(estanteId, numero, canal, tamanho));

        Assert.Equal(StatusResultadoCofre.Validacao, resultado.Status);
        Assert.True(resultado.Erros.ContainsKey("cofre"));
        Assert.Empty(_cenario.Repositorio.TentativasDeAdicao);
    }

    [Fact]
    public async Task Numero_ja_usado_na_estante_devolve_NumeroEmUso()
    {
        var estanteId = _cenario.AdicionarEstante();
        _cenario.AdicionarCofre(estanteId, numero: 1, canal: 0);

        var resultado = await _cenario.Criar.ExecutarAsync(Comando(estanteId, numero: 1, canal: 5));

        Assert.Equal(StatusResultadoCofre.NumeroEmUso, resultado.Status);
        Assert.Empty(_cenario.Repositorio.TentativasDeAdicao);
    }

    [Fact]
    public async Task Canal_ja_usado_na_estante_devolve_CanalEmUso()
    {
        var estanteId = _cenario.AdicionarEstante();
        _cenario.AdicionarCofre(estanteId, numero: 1, canal: 0);

        var resultado = await _cenario.Criar.ExecutarAsync(Comando(estanteId, numero: 2, canal: 0));

        Assert.Equal(StatusResultadoCofre.CanalEmUso, resultado.Status);
        Assert.Empty(_cenario.Repositorio.TentativasDeAdicao);
    }

    [Fact]
    public async Task Mesmo_numero_e_canal_em_outra_estante_sao_permitidos()
    {
        var outra = _cenario.AdicionarEstante();
        _cenario.AdicionarCofre(outra, numero: 1, canal: 0);
        var estanteId = _cenario.AdicionarEstante();

        var resultado = await _cenario.Criar.ExecutarAsync(Comando(estanteId, numero: 1, canal: 0));

        Assert.Equal(StatusResultadoCofre.Sucesso, resultado.Status);
    }

    [Theory]
    [InlineData(ResultadoGravacaoCofre.NumeroEmUso, StatusResultadoCofre.NumeroEmUso)]
    [InlineData(ResultadoGravacaoCofre.CanalEmUso, StatusResultadoCofre.CanalEmUso)]
    public async Task Recusa_do_indice_unico_vira_conflito_sem_nova_tentativa(ResultadoGravacaoCofre recusa, StatusResultadoCofre esperado)
    {
        var estanteId = _cenario.AdicionarEstante();
        _cenario.Repositorio.RecusaNaProximaGravacao = recusa;

        var resultado = await _cenario.Criar.ExecutarAsync(Comando(estanteId));

        Assert.Equal(esperado, resultado.Status);
        Assert.Single(_cenario.Repositorio.TentativasDeAdicao);
        Assert.Empty(_cenario.Repositorio.Cofres);
        Assert.Contains(_cenario.LogCriar.Registros, r => r.Nivel == LogLevel.Warning);
        Assert.DoesNotContain(_cenario.LogCriar.Registros, r => r.Nivel == LogLevel.Information);
    }
}

public class ListarEObterCofresTests
{
    private readonly CofresCenario _cenario = new();

    [Fact]
    public async Task Lista_os_cofres_da_estante_ordenados_por_numero()
    {
        var estanteId = _cenario.AdicionarEstante();
        _cenario.AdicionarCofre(estanteId, numero: 3, canal: 2);
        _cenario.AdicionarCofre(estanteId, numero: 1, canal: 0, ativo: false);
        _cenario.AdicionarCofre(estanteId, numero: 2, canal: 1);
        _cenario.AdicionarCofre(_cenario.AdicionarEstante(), numero: 9, canal: 9);

        var resultado = await _cenario.Listar.ExecutarAsync(estanteId);

        Assert.Equal(StatusResultadoCofre.Sucesso, resultado.Status);
        Assert.Equal([1, 2, 3], resultado.Valor!.Select(c => c.Numero));
    }

    [Fact]
    public async Task Estante_sem_cofres_devolve_lista_vazia()
    {
        var resultado = await _cenario.Listar.ExecutarAsync(_cenario.AdicionarEstante());

        Assert.Equal(StatusResultadoCofre.Sucesso, resultado.Status);
        Assert.Empty(resultado.Valor!);
    }

    [Fact]
    public async Task Listar_estante_inexistente_devolve_EstanteNaoEncontrada()
    {
        var resultado = await _cenario.Listar.ExecutarAsync(Guid.NewGuid());

        Assert.Equal(StatusResultadoCofre.EstanteNaoEncontrada, resultado.Status);
    }

    [Fact]
    public async Task Obter_existente_devolve_DTO()
    {
        var cofre = _cenario.AdicionarCofre(_cenario.AdicionarEstante(), StatusCofre.Manutencao);

        var resultado = await _cenario.Obter.ExecutarAsync(cofre.EstanteId, cofre.Id);

        Assert.Equal(StatusResultadoCofre.Sucesso, resultado.Status);
        Assert.Equal(CofreDto.De(cofre), resultado.Valor);
        Assert.Equal("manutencao", resultado.Valor!.Status);
    }

    [Fact]
    public async Task Obter_cofre_de_outra_estante_devolve_CofreNaoEncontrado()
    {
        var cofre = _cenario.AdicionarCofre(_cenario.AdicionarEstante());

        var resultado = await _cenario.Obter.ExecutarAsync(_cenario.AdicionarEstante(), cofre.Id);

        Assert.Equal(StatusResultadoCofre.CofreNaoEncontrado, resultado.Status);
    }
}

public class AtualizarCofreTests
{
    private readonly CofresCenario _cenario = new();

    private static AtualizarCofreComando Comando(Cofre cofre, int numero = 7, int canal = 9, TamanhoCofre tamanho = TamanhoCofre.Grande) =>
        new(CofresCenario.AdminId, cofre.EstanteId, cofre.Id, numero, canal, tamanho);

    [Theory]
    [InlineData(StatusCofre.Livre)]
    [InlineData(StatusCofre.Manutencao)]
    public async Task Atualiza_livre_ou_em_manutencao(StatusCofre status)
    {
        var cofre = _cenario.AdicionarCofre(_cenario.AdicionarEstante(), status);

        var resultado = await _cenario.Atualizar.ExecutarAsync(Comando(cofre));

        Assert.Equal(StatusResultadoCofre.Sucesso, resultado.Status);
        Assert.Equal(7, cofre.Numero);
        Assert.Equal(9, cofre.CanalRele);
        Assert.Equal(TamanhoCofre.Grande, cofre.Tamanho);
        Assert.Equal(status, cofre.Status);
        Assert.Equal(CofreDto.De(cofre), resultado.Valor);
        Assert.Equal(1, _cenario.Repositorio.Salvamentos);
        var log = Assert.Single(_cenario.LogAtualizar.Registros, r => r.Nivel == LogLevel.Information).Mensagem;
        Assert.Contains("atualizado", log);
        Assert.Contains(CofresCenario.AdminId.ToString(), log);
        Assert.Contains(cofre.Id.ToString(), log);
        Assert.Contains(cofre.EstanteId.ToString(), log);
        // Auditoria com valores anteriores e novos (a troca de canal é a mais sensível).
        Assert.Contains("número de 1 para 7", log);
        Assert.Contains("canal de 0 para 9", log);
        Assert.Contains("tamanho de medio para grande", log);
    }

    [Theory]
    [InlineData(StatusCofre.Reservado, "O cofre está reservado e não pode ser editado.")]
    [InlineData(StatusCofre.Ocupado, "O cofre está ocupado e não pode ser editado.")]
    public async Task Reservado_ou_ocupado_devolve_ConflitoEstado_com_o_status(StatusCofre status, string mensagem)
    {
        var cofre = _cenario.AdicionarCofre(_cenario.AdicionarEstante(), status, numero: 3, canal: 2);

        var resultado = await _cenario.Atualizar.ExecutarAsync(Comando(cofre));

        Assert.Equal(StatusResultadoCofre.ConflitoEstado, resultado.Status);
        Assert.Equal(mensagem, resultado.Detalhe);
        Assert.Equal(3, cofre.Numero);
        Assert.Equal(0, _cenario.Repositorio.Salvamentos);
    }

    [Fact]
    public async Task Manter_o_proprio_numero_e_canal_nao_conflita()
    {
        var cofre = _cenario.AdicionarCofre(_cenario.AdicionarEstante(), numero: 3, canal: 2);

        var resultado = await _cenario.Atualizar.ExecutarAsync(Comando(cofre, numero: 3, canal: 2, tamanho: TamanhoCofre.Pequeno));

        Assert.Equal(StatusResultadoCofre.Sucesso, resultado.Status);
        Assert.Equal(TamanhoCofre.Pequeno, cofre.Tamanho);
    }

    [Fact]
    public async Task Numero_de_outro_cofre_da_estante_devolve_NumeroEmUso_sem_alterar()
    {
        var estanteId = _cenario.AdicionarEstante();
        _cenario.AdicionarCofre(estanteId, numero: 1, canal: 0);
        var cofre = _cenario.AdicionarCofre(estanteId, numero: 2, canal: 1);

        var resultado = await _cenario.Atualizar.ExecutarAsync(Comando(cofre, numero: 1, canal: 5));

        Assert.Equal(StatusResultadoCofre.NumeroEmUso, resultado.Status);
        Assert.Equal(2, cofre.Numero);
        Assert.Equal(1, cofre.CanalRele);
        Assert.Equal(0, _cenario.Repositorio.Salvamentos);
    }

    [Fact]
    public async Task Canal_de_outro_cofre_da_estante_devolve_CanalEmUso_sem_alterar()
    {
        var estanteId = _cenario.AdicionarEstante();
        _cenario.AdicionarCofre(estanteId, numero: 1, canal: 0);
        var cofre = _cenario.AdicionarCofre(estanteId, numero: 2, canal: 1);

        var resultado = await _cenario.Atualizar.ExecutarAsync(Comando(cofre, numero: 5, canal: 0));

        Assert.Equal(StatusResultadoCofre.CanalEmUso, resultado.Status);
        Assert.Equal(2, cofre.Numero);
        Assert.Equal(0, _cenario.Repositorio.Salvamentos);
    }

    [Fact]
    public async Task Dados_invalidos_viram_Validacao()
    {
        var cofre = _cenario.AdicionarCofre(_cenario.AdicionarEstante());

        var resultado = await _cenario.Atualizar.ExecutarAsync(Comando(cofre, canal: 16));

        Assert.Equal(StatusResultadoCofre.Validacao, resultado.Status);
        Assert.True(resultado.Erros.ContainsKey("cofre"));
        Assert.Equal(0, _cenario.Repositorio.Salvamentos);
    }

    [Fact]
    public async Task Cofre_de_outra_estante_devolve_CofreNaoEncontrado()
    {
        var cofre = _cenario.AdicionarCofre(_cenario.AdicionarEstante());

        var resultado = await _cenario.Atualizar.ExecutarAsync(
            new AtualizarCofreComando(CofresCenario.AdminId, _cenario.AdicionarEstante(), cofre.Id, 7, 9, TamanhoCofre.Grande));

        Assert.Equal(StatusResultadoCofre.CofreNaoEncontrado, resultado.Status);
        Assert.Equal(1, cofre.Numero);
    }

    [Theory]
    [InlineData(ResultadoGravacaoCofre.NumeroEmUso, StatusResultadoCofre.NumeroEmUso)]
    [InlineData(ResultadoGravacaoCofre.CanalEmUso, StatusResultadoCofre.CanalEmUso)]
    [InlineData(ResultadoGravacaoCofre.ConflitoConcorrencia, StatusResultadoCofre.ConflitoConcorrencia)]
    public async Task Recusa_no_salvar_vira_conflito(ResultadoGravacaoCofre recusa, StatusResultadoCofre esperado)
    {
        var cofre = _cenario.AdicionarCofre(_cenario.AdicionarEstante());
        if (recusa == ResultadoGravacaoCofre.ConflitoConcorrencia)
            _cenario.Repositorio.ConflitoNoSalvar = true;
        else
            _cenario.Repositorio.RecusaNaProximaGravacao = recusa;

        var resultado = await _cenario.Atualizar.ExecutarAsync(Comando(cofre));

        Assert.Equal(esperado, resultado.Status);
        Assert.Null(resultado.Valor);
        Assert.Contains(_cenario.LogAtualizar.Registros, r => r.Nivel == LogLevel.Warning);
        Assert.DoesNotContain(_cenario.LogAtualizar.Registros, r => r.Nivel == LogLevel.Information);
    }
}

public class DesativarEReativarCofreTests
{
    private readonly CofresCenario _cenario = new();

    [Theory]
    [InlineData(StatusCofre.Livre)]
    [InlineData(StatusCofre.Manutencao)]
    public async Task Desativar_sem_locacao_e_idempotente(StatusCofre status)
    {
        var cofre = _cenario.AdicionarCofre(_cenario.AdicionarEstante(), status);

        var primeira = await _cenario.Desativar.ExecutarAsync(_cenario.Acao(cofre));
        var segunda = await _cenario.Desativar.ExecutarAsync(_cenario.Acao(cofre));

        Assert.Equal(StatusResultadoCofre.Sucesso, primeira.Status);
        Assert.Equal(StatusResultadoCofre.Sucesso, segunda.Status);
        Assert.False(segunda.Valor!.Ativo);
        Assert.Equal(status, cofre.Status);
        Assert.Equal(1, _cenario.Repositorio.Salvamentos); // a segunda não grava
        Assert.Single(_cenario.LogDesativar.Registros, r => r.Nivel == LogLevel.Information && r.Mensagem.Contains("desativado"));
    }

    [Theory]
    [InlineData(StatusCofre.Reservado, "reservado")]
    [InlineData(StatusCofre.Ocupado, "ocupado")]
    public async Task Desativar_com_locacao_devolve_ConflitoEstado(StatusCofre status, string texto)
    {
        var cofre = _cenario.AdicionarCofre(_cenario.AdicionarEstante(), status);

        var resultado = await _cenario.Desativar.ExecutarAsync(_cenario.Acao(cofre));

        Assert.Equal(StatusResultadoCofre.ConflitoEstado, resultado.Status);
        Assert.Equal($"O cofre está {texto} e não pode ser desativado.", resultado.Detalhe);
        Assert.True(cofre.Ativo);
        Assert.Equal(0, _cenario.Repositorio.Salvamentos);
    }

    [Fact]
    public async Task Reativar_e_idempotente_e_mantem_o_status()
    {
        var cofre = _cenario.AdicionarCofre(_cenario.AdicionarEstante(), StatusCofre.Manutencao, ativo: false);

        var primeira = await _cenario.Reativar.ExecutarAsync(_cenario.Acao(cofre));
        var segunda = await _cenario.Reativar.ExecutarAsync(_cenario.Acao(cofre));

        Assert.Equal(StatusResultadoCofre.Sucesso, primeira.Status);
        Assert.True(segunda.Valor!.Ativo);
        Assert.Equal("manutencao", segunda.Valor.Status);
        Assert.Equal(1, _cenario.Repositorio.Salvamentos);
        Assert.Single(_cenario.LogReativar.Registros, r => r.Nivel == LogLevel.Information && r.Mensagem.Contains("reativado"));
    }

    [Fact]
    public async Task Desativar_com_conflito_de_concorrencia()
    {
        var cofre = _cenario.AdicionarCofre(_cenario.AdicionarEstante());
        _cenario.Repositorio.ConflitoNoSalvar = true;

        var resultado = await _cenario.Desativar.ExecutarAsync(_cenario.Acao(cofre));

        Assert.Equal(StatusResultadoCofre.ConflitoConcorrencia, resultado.Status);
        Assert.Contains(_cenario.LogDesativar.Registros, r => r.Nivel == LogLevel.Warning);
    }

    [Fact]
    public async Task Reativar_com_conflito_de_concorrencia()
    {
        var cofre = _cenario.AdicionarCofre(_cenario.AdicionarEstante(), ativo: false);
        _cenario.Repositorio.ConflitoNoSalvar = true;

        var resultado = await _cenario.Reativar.ExecutarAsync(_cenario.Acao(cofre));

        Assert.Equal(StatusResultadoCofre.ConflitoConcorrencia, resultado.Status);
    }
}

public class ManutencaoCofreTests
{
    private readonly CofresCenario _cenario = new();

    [Theory]
    [InlineData(StatusCofre.Livre, null)]
    [InlineData(StatusCofre.Reservado, "O cofre está reservado e não pode ser colocado em manutenção.")]
    [InlineData(StatusCofre.Ocupado, "O cofre está ocupado e não pode ser colocado em manutenção.")]
    [InlineData(StatusCofre.Manutencao, "O cofre está em manutenção e não pode ser colocado em manutenção.")]
    public async Task Colocar_em_manutencao_a_partir_de_cada_status(StatusCofre status, string? conflito)
    {
        var cofre = _cenario.AdicionarCofre(_cenario.AdicionarEstante(), status);

        var resultado = await _cenario.ColocarEmManutencao.ExecutarAsync(_cenario.Acao(cofre));

        if (conflito is null)
        {
            Assert.Equal(StatusResultadoCofre.Sucesso, resultado.Status);
            Assert.Equal("manutencao", resultado.Valor!.Status);
            Assert.Equal(1, _cenario.Repositorio.Salvamentos);
            Assert.Single(_cenario.LogManutencao.Registros, r => r.Nivel == LogLevel.Information && r.Mensagem.Contains("colocado em manutenção"));
        }
        else
        {
            Assert.Equal(StatusResultadoCofre.ConflitoEstado, resultado.Status);
            Assert.Equal(conflito, resultado.Detalhe);
            Assert.Equal(status, cofre.Status);
            Assert.Equal(0, _cenario.Repositorio.Salvamentos);
        }
    }

    [Theory]
    [InlineData(StatusCofre.Manutencao, null)]
    [InlineData(StatusCofre.Livre, "O cofre está livre e não pode ser retirado da manutenção.")]
    [InlineData(StatusCofre.Reservado, "O cofre está reservado e não pode ser retirado da manutenção.")]
    [InlineData(StatusCofre.Ocupado, "O cofre está ocupado e não pode ser retirado da manutenção.")]
    public async Task Retirar_da_manutencao_a_partir_de_cada_status(StatusCofre status, string? conflito)
    {
        var cofre = _cenario.AdicionarCofre(_cenario.AdicionarEstante(), status);

        var resultado = await _cenario.RetirarDaManutencao.ExecutarAsync(_cenario.Acao(cofre));

        if (conflito is null)
        {
            Assert.Equal(StatusResultadoCofre.Sucesso, resultado.Status);
            Assert.Equal("livre", resultado.Valor!.Status);
            Assert.Single(_cenario.LogRetirar.Registros, r => r.Nivel == LogLevel.Information && r.Mensagem.Contains("retirado da manutenção"));
        }
        else
        {
            Assert.Equal(StatusResultadoCofre.ConflitoEstado, resultado.Status);
            Assert.Equal(conflito, resultado.Detalhe);
            Assert.Equal(status, cofre.Status);
        }
    }

    [Fact]
    public async Task Cofre_inativo_pode_ir_para_manutencao_e_continua_inativo()
    {
        var cofre = _cenario.AdicionarCofre(_cenario.AdicionarEstante(), ativo: false);

        var resultado = await _cenario.ColocarEmManutencao.ExecutarAsync(_cenario.Acao(cofre));

        Assert.Equal(StatusResultadoCofre.Sucesso, resultado.Status);
        Assert.False(resultado.Valor!.Ativo);
    }

    [Fact]
    public async Task Manutencao_com_conflito_de_concorrencia()
    {
        var cofre = _cenario.AdicionarCofre(_cenario.AdicionarEstante());
        _cenario.Repositorio.ConflitoNoSalvar = true;

        var colocar = await _cenario.ColocarEmManutencao.ExecutarAsync(_cenario.Acao(cofre));

        Assert.Equal(StatusResultadoCofre.ConflitoConcorrencia, colocar.Status);
    }

    [Fact]
    public async Task Retirar_com_conflito_de_concorrencia()
    {
        var cofre = _cenario.AdicionarCofre(_cenario.AdicionarEstante(), StatusCofre.Manutencao);
        _cenario.Repositorio.ConflitoNoSalvar = true;

        var retirar = await _cenario.RetirarDaManutencao.ExecutarAsync(_cenario.Acao(cofre));

        Assert.Equal(StatusResultadoCofre.ConflitoConcorrencia, retirar.Status);
    }

    [Theory]
    [InlineData("manutencao")]
    [InlineData("retirar")]
    [InlineData("desativar")]
    [InlineData("reativar")]
    public async Task Acoes_em_cofre_de_outra_estante_devolvem_CofreNaoEncontrado(string acao)
    {
        var cofre = _cenario.AdicionarCofre(_cenario.AdicionarEstante());
        var comando = new AcaoCofreComando(CofresCenario.AdminId, _cenario.AdicionarEstante(), cofre.Id);

        var resultado = acao switch
        {
            "manutencao" => await _cenario.ColocarEmManutencao.ExecutarAsync(comando),
            "retirar" => await _cenario.RetirarDaManutencao.ExecutarAsync(comando),
            "desativar" => await _cenario.Desativar.ExecutarAsync(comando),
            _ => await _cenario.Reativar.ExecutarAsync(comando),
        };

        Assert.Equal(StatusResultadoCofre.CofreNaoEncontrado, resultado.Status);
    }
}

public class TextoCofreTests
{
    [Theory]
    [InlineData("pequeno", TamanhoCofre.Pequeno)]
    [InlineData("medio", TamanhoCofre.Medio)]
    [InlineData("grande", TamanhoCofre.Grande)]
    public void Aceita_os_textos_do_contrato(string texto, TamanhoCofre esperado)
    {
        Assert.True(TextoCofre.TentarTamanho(texto, out var tamanho));
        Assert.Equal(esperado, tamanho);
        Assert.Equal(texto, TextoCofre.DeTamanho(tamanho));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Pequeno")]
    [InlineData("médio")]
    [InlineData(" grande")]
    [InlineData("1")]
    [InlineData("enorme")]
    public void Recusa_outros_textos(string? texto)
    {
        Assert.False(TextoCofre.TentarTamanho(texto, out _));
    }

    [Theory]
    [InlineData(StatusCofre.Livre, "livre")]
    [InlineData(StatusCofre.Reservado, "reservado")]
    [InlineData(StatusCofre.Ocupado, "ocupado")]
    [InlineData(StatusCofre.Manutencao, "manutencao")]
    public void Status_em_texto_minusculo_sem_acento(StatusCofre status, string texto)
    {
        Assert.Equal(texto, TextoCofre.DeStatus(status));
    }
}
