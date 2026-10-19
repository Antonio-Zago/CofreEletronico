using CofreEletronico.Domain.Common;
using CofreEletronico.Domain.Estantes;

namespace CofreEletronico.Domain.Tests.Estantes;

public class CofreTests
{
    private static readonly Guid EstanteId = Guid.NewGuid();

    private static Cofre Criar() => Cofre.Criar(EstanteId, 3, 2, TamanhoCofre.Medio);

    /// <summary>Leva um cofre novo (Livre) até o status pedido por um caminho válido.</summary>
    private static Cofre EmStatus(StatusCofre status)
    {
        var cofre = Criar();
        switch (status)
        {
            case StatusCofre.Livre:
                break;
            case StatusCofre.Reservado:
                cofre.Reservar();
                break;
            case StatusCofre.Ocupado:
                cofre.Reservar();
                cofre.Ocupar();
                break;
            case StatusCofre.Manutencao:
                cofre.ColocarEmManutencao();
                break;
        }

        Assert.Equal(status, cofre.Status);
        return cofre;
    }

    private static readonly Dictionary<string, Action<Cofre>> Operacoes = new()
    {
        [nameof(Cofre.Reservar)] = c => c.Reservar(),
        [nameof(Cofre.Ocupar)] = c => c.Ocupar(),
        [nameof(Cofre.CancelarReserva)] = c => c.CancelarReserva(),
        [nameof(Cofre.Liberar)] = c => c.Liberar(),
        [nameof(Cofre.ColocarEmManutencao)] = c => c.ColocarEmManutencao(),
        [nameof(Cofre.RetirarDaManutencao)] = c => c.RetirarDaManutencao(),
    };

    /// <summary>As seis transições válidas: (status de origem, operação, status de destino).</summary>
    private static readonly (StatusCofre De, string Operacao, StatusCofre Para)[] Validas =
    [
        (StatusCofre.Livre, nameof(Cofre.Reservar), StatusCofre.Reservado),
        (StatusCofre.Reservado, nameof(Cofre.Ocupar), StatusCofre.Ocupado),
        (StatusCofre.Reservado, nameof(Cofre.CancelarReserva), StatusCofre.Livre),
        (StatusCofre.Ocupado, nameof(Cofre.Liberar), StatusCofre.Livre),
        (StatusCofre.Livre, nameof(Cofre.ColocarEmManutencao), StatusCofre.Manutencao),
        (StatusCofre.Manutencao, nameof(Cofre.RetirarDaManutencao), StatusCofre.Livre),
    ];

    public static TheoryData<StatusCofre, string, StatusCofre> TransicoesValidas()
    {
        var dados = new TheoryData<StatusCofre, string, StatusCofre>();
        foreach (var (de, operacao, para) in Validas)
            dados.Add(de, operacao, para);
        return dados;
    }

    /// <summary>Todas as combinações (status, operação) que não estão em <see cref="Validas"/>.</summary>
    public static TheoryData<StatusCofre, string> TransicoesInvalidas()
    {
        var dados = new TheoryData<StatusCofre, string>();
        foreach (var status in Enum.GetValues<StatusCofre>())
        {
            foreach (var operacao in Operacoes.Keys)
            {
                if (!Validas.Any(v => v.De == status && v.Operacao == operacao))
                    dados.Add(status, operacao);
            }
        }

        return dados;
    }

    [Fact]
    public void Criar_gera_cofre_livre_e_ativo()
    {
        var cofre = Criar();

        Assert.NotEqual(Guid.Empty, cofre.Id);
        Assert.Equal(EstanteId, cofre.EstanteId);
        Assert.Equal(3, cofre.Numero);
        Assert.Equal(2, cofre.CanalRele);
        Assert.Equal(TamanhoCofre.Medio, cofre.Tamanho);
        Assert.Equal(StatusCofre.Livre, cofre.Status);
        Assert.True(cofre.Ativo);
    }

    [Fact]
    public void Canal_maximo_e_15_pela_placa_de_16_reles()
    {
        Assert.Equal(0, Cofre.CanalMinimo);
        Assert.Equal(15, Cofre.CanalMaximo);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(15)]
    public void Canal_rele_nas_bordas_e_aceito(int canal)
    {
        Assert.Equal(canal, Cofre.Criar(EstanteId, 1, canal, TamanhoCofre.Pequeno).CanalRele);
    }

    [Theory]
    [InlineData(16)]
    [InlineData(int.MaxValue)]
    public void Canal_rele_acima_de_15_falha(int canal)
    {
        var erro = Assert.Throws<ExcecaoDominio>(() => Cofre.Criar(EstanteId, 1, canal, TamanhoCofre.Pequeno));

        Assert.Contains("entre 0 e 15", erro.Message);
    }

    [Fact]
    public void Criar_sem_estante_falha()
    {
        Assert.Throws<ExcecaoDominio>(() => Cofre.Criar(Guid.Empty, 1, 0, TamanhoCofre.Pequeno));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Numero_deve_ser_positivo(int numero)
    {
        Assert.Throws<ExcecaoDominio>(() => Cofre.Criar(EstanteId, numero, 0, TamanhoCofre.Pequeno));
    }

    [Fact]
    public void Canal_rele_negativo_falha()
    {
        Assert.Throws<ExcecaoDominio>(() => Cofre.Criar(EstanteId, 1, -1, TamanhoCofre.Pequeno));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void Tamanho_fora_do_enum_falha(int tamanho)
    {
        Assert.Throws<ExcecaoDominio>(() => Cofre.Criar(EstanteId, 1, 0, (TamanhoCofre)tamanho));
    }

    [Theory]
    [MemberData(nameof(TransicoesValidas))]
    public void Transicao_valida_muda_o_status(StatusCofre de, string operacao, StatusCofre para)
    {
        var cofre = EmStatus(de);

        Operacoes[operacao](cofre);

        Assert.Equal(para, cofre.Status);
    }

    [Theory]
    [MemberData(nameof(TransicoesInvalidas))]
    public void Transicao_invalida_lanca_excecao_de_dominio_e_mantem_o_status(StatusCofre de, string operacao)
    {
        var cofre = EmStatus(de);

        var erro = Assert.Throws<TransicaoCofreInvalidaException>(() => Operacoes[operacao](cofre));

        Assert.IsAssignableFrom<ExcecaoDominio>(erro);
        Assert.Equal(operacao, erro.Operacao);
        Assert.Equal(de, erro.StatusAtual);
        Assert.Equal(de, cofre.Status);
    }

    [Theory]
    [InlineData(StatusCofre.Reservado)]
    [InlineData(StatusCofre.Ocupado)]
    public void Cofre_com_locacao_em_andamento_nao_vai_para_manutencao(StatusCofre status)
    {
        var cofre = EmStatus(status);

        Assert.Throws<TransicaoCofreInvalidaException>(cofre.ColocarEmManutencao);
    }

    [Fact]
    public void Ciclo_completo_de_locacao_volta_a_livre()
    {
        var cofre = Criar();

        cofre.Reservar();
        cofre.Ocupar();
        cofre.Liberar();
        cofre.Reservar();
        cofre.CancelarReserva();

        Assert.Equal(StatusCofre.Livre, cofre.Status);
    }

    [Theory]
    [InlineData(StatusCofre.Livre)]
    [InlineData(StatusCofre.Manutencao)]
    public void Desativar_permitido_sem_locacao_em_andamento(StatusCofre status)
    {
        var cofre = EmStatus(status);

        cofre.Desativar();
        cofre.Desativar(); // idempotente

        Assert.False(cofre.Ativo);
        Assert.Equal(status, cofre.Status);
    }

    [Theory]
    [InlineData(StatusCofre.Reservado)]
    [InlineData(StatusCofre.Ocupado)]
    public void Desativar_com_locacao_em_andamento_falha(StatusCofre status)
    {
        var cofre = EmStatus(status);

        var erro = Assert.Throws<TransicaoCofreInvalidaException>(cofre.Desativar);

        Assert.Equal(nameof(Cofre.Desativar), erro.Operacao);
        Assert.True(cofre.Ativo);
    }

    [Fact]
    public void Cofre_inativo_nao_pode_ser_reservado()
    {
        var cofre = Criar();
        cofre.Desativar();

        Assert.Throws<ExcecaoDominio>(cofre.Reservar);
        Assert.Equal(StatusCofre.Livre, cofre.Status);
    }

    [Fact]
    public void Reativar_devolve_o_cofre_ao_uso_mantendo_o_status()
    {
        var cofre = EmStatus(StatusCofre.Manutencao);
        cofre.Desativar();

        cofre.Reativar();
        cofre.Reativar(); // idempotente

        Assert.True(cofre.Ativo);
        Assert.Equal(StatusCofre.Manutencao, cofre.Status);
        cofre.RetirarDaManutencao();
        cofre.Reservar();
        Assert.Equal(StatusCofre.Reservado, cofre.Status);
    }

    [Fact]
    public void Alterar_tamanho_valida_o_enum()
    {
        var cofre = Criar();

        cofre.AlterarTamanho(TamanhoCofre.Grande);
        Assert.Equal(TamanhoCofre.Grande, cofre.Tamanho);

        Assert.Throws<ExcecaoDominio>(() => cofre.AlterarTamanho((TamanhoCofre)99));
        Assert.Equal(TamanhoCofre.Grande, cofre.Tamanho);
    }
}
