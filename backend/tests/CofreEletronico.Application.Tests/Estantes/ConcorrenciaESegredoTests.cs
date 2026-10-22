using CofreEletronico.Application.Estantes;
using Microsoft.Extensions.Logging;

namespace CofreEletronico.Application.Tests.Estantes;

/// <summary>Conflito de concorrência ao salvar (409) e zeragem do segredo em claro (inclusive em falha).</summary>
public class ConcorrenciaESegredoTests
{
    private readonly EstantesCenario _cenario = new();

    private AcaoEstanteComando Acao(Guid id) => new(EstantesCenario.AdminId, id);

    [Fact]
    public async Task Atualizar_com_conflito_de_concorrencia_devolve_ConflitoConcorrencia()
    {
        var estante = _cenario.AdicionarEstante();
        _cenario.Repositorio.ConflitoNoSalvar = true;

        var resultado = await _cenario.Atualizar.ExecutarAsync(
            new AtualizarEstanteComando(EstantesCenario.AdminId, estante.Id, "Nova", "Rua", 0, 0));

        Assert.Equal(StatusResultadoEstante.ConflitoConcorrencia, resultado.Status);
        Assert.Null(resultado.Valor);
        Assert.Contains(_cenario.LogAtualizar.Registros, r => r.Nivel == LogLevel.Warning);
        Assert.DoesNotContain(_cenario.LogAtualizar.Registros, r => r.Nivel == LogLevel.Information);
    }

    [Fact]
    public async Task Desativar_com_conflito_de_concorrencia_devolve_ConflitoConcorrencia()
    {
        var estante = _cenario.AdicionarEstante();
        _cenario.Repositorio.ConflitoNoSalvar = true;

        var resultado = await _cenario.Desativar.ExecutarAsync(Acao(estante.Id));

        Assert.Equal(StatusResultadoEstante.ConflitoConcorrencia, resultado.Status);
    }

    [Fact]
    public async Task Reativar_com_conflito_de_concorrencia_devolve_ConflitoConcorrencia()
    {
        var estante = _cenario.AdicionarEstante(ativa: false);
        _cenario.Repositorio.ConflitoNoSalvar = true;

        var resultado = await _cenario.Reativar.ExecutarAsync(Acao(estante.Id));

        Assert.Equal(StatusResultadoEstante.ConflitoConcorrencia, resultado.Status);
    }

    [Fact]
    public async Task Regenerar_com_conflito_nao_devolve_credenciais_nem_gera_senha_e_zera_o_segredo()
    {
        var estante = _cenario.AdicionarEstante();
        _cenario.Repositorio.ConflitoNoSalvar = true;

        var resultado = await _cenario.Regenerar.ExecutarAsync(Acao(estante.Id));

        Assert.Equal(StatusResultadoEstante.ConflitoConcorrencia, resultado.Status);
        Assert.Null(resultado.Valor);
        Assert.Empty(_cenario.Gerador.SenhasGeradas);
        Assert.All(_cenario.Gerador.ArraysEntregues, a => Assert.All(a, b => Assert.Equal(0, b)));
        Assert.DoesNotContain(_cenario.LogRegenerar.Registros, r => r.Nivel == LogLevel.Information);
    }

    [Fact]
    public async Task Regenerar_zera_o_array_do_segredo_no_sucesso_sem_afetar_a_entidade()
    {
        var estante = _cenario.AdicionarEstante();

        var resultado = await _cenario.Regenerar.ExecutarAsync(Acao(estante.Id));

        var entregue = Assert.Single(_cenario.Gerador.ArraysEntregues);
        Assert.All(entregue, b => Assert.Equal(0, b));
        Assert.Equal(_cenario.Gerador.SegredosGerados.Single(), estante.ObterSegredoHmac());
        Assert.Equal(Convert.ToHexString(estante.ObterSegredoHmac()).ToLowerInvariant(), resultado.Valor!.SegredoHmac);
    }

    [Fact]
    public async Task Regenerar_zera_o_segredo_mesmo_quando_salvar_lanca()
    {
        var estante = _cenario.AdicionarEstante();
        _cenario.Repositorio.ExcecaoNoSalvar = new InvalidOperationException("banco fora");

        await Assert.ThrowsAsync<InvalidOperationException>(() => _cenario.Regenerar.ExecutarAsync(Acao(estante.Id)));

        var entregue = Assert.Single(_cenario.Gerador.ArraysEntregues);
        Assert.All(entregue, b => Assert.Equal(0, b));
    }

    [Fact]
    public async Task Criar_zera_os_arrays_de_segredo_de_todas_as_tentativas()
    {
        _cenario.Repositorio.RecusasDoIndice = 1;

        var resultado = await _cenario.Criar.ExecutarAsync(new CriarEstanteComando(EstantesCenario.AdminId, "E", "Rua", 0, 0));

        Assert.Equal(StatusResultadoEstante.Sucesso, resultado.Status);
        Assert.Equal(2, _cenario.Gerador.ArraysEntregues.Count);
        Assert.All(_cenario.Gerador.ArraysEntregues, a => Assert.All(a, b => Assert.Equal(0, b)));
        Assert.Equal(_cenario.Gerador.SegredosGerados[1], _cenario.Repositorio.Estantes.Single().ObterSegredoHmac());
    }

    [Fact]
    public async Task Criar_zera_o_segredo_mesmo_quando_inserir_lanca()
    {
        _cenario.Repositorio.ExcecaoNoAdicionar = new InvalidOperationException("banco fora");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _cenario.Criar.ExecutarAsync(new CriarEstanteComando(EstantesCenario.AdminId, "E", "Rua", 0, 0)));

        var entregue = Assert.Single(_cenario.Gerador.ArraysEntregues);
        Assert.All(entregue, b => Assert.Equal(0, b));
    }

    [Fact]
    public async Task Criar_com_dados_invalidos_zera_o_segredo()
    {
        await _cenario.Criar.ExecutarAsync(new CriarEstanteComando(EstantesCenario.AdminId, "E", "Rua", 95, 0));

        var entregue = Assert.Single(_cenario.Gerador.ArraysEntregues);
        Assert.All(entregue, b => Assert.Equal(0, b));
    }
}
