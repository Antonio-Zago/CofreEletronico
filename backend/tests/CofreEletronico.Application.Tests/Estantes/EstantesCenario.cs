using CofreEletronico.Application.Estantes;
using CofreEletronico.Application.Tests.Autenticacao;
using CofreEletronico.Application.Tests.Comum;
using CofreEletronico.Domain.Estantes;

namespace CofreEletronico.Application.Tests.Estantes;

/// <summary>Monta os casos de uso de estantes com fakes compartilhados e loggers capturados.</summary>
internal sealed class EstantesCenario
{
    public static readonly DateTimeOffset Inicio = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

    public static readonly Guid AdminId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    public EstantesCenario()
    {
        Criar = new CriarEstante(Repositorio, Gerador, Relogio, LogCriar);
        Listar = new ListarEstantes(Repositorio);
        Obter = new ObterEstante(Repositorio);
        Atualizar = new AtualizarEstante(Repositorio, LogAtualizar);
        Desativar = new DesativarEstante(Repositorio, LogDesativar);
        Reativar = new ReativarEstante(Repositorio, LogReativar);
        Regenerar = new RegenerarCredenciaisEstante(Repositorio, Gerador, LogRegenerar);
    }

    public RelogioFalso Relogio { get; } = new(Inicio);

    public EstanteRepositorioFalso Repositorio { get; } = new();

    public GeradorCredenciaisFalso Gerador { get; } = new();

    public LoggerCapturado<CriarEstante> LogCriar { get; } = new();

    public LoggerCapturado<AtualizarEstante> LogAtualizar { get; } = new();

    public LoggerCapturado<DesativarEstante> LogDesativar { get; } = new();

    public LoggerCapturado<ReativarEstante> LogReativar { get; } = new();

    public LoggerCapturado<RegenerarCredenciaisEstante> LogRegenerar { get; } = new();

    public CriarEstante Criar { get; }

    public ListarEstantes Listar { get; }

    public ObterEstante Obter { get; }

    public AtualizarEstante Atualizar { get; }

    public DesativarEstante Desativar { get; }

    public ReativarEstante Reativar { get; }

    public RegenerarCredenciaisEstante Regenerar { get; }

    public string TodosOsLogs =>
        string.Join("\n", LogCriar.Tudo, LogAtualizar.Tudo, LogDesativar.Tudo, LogReativar.Tudo, LogRegenerar.Tudo);

    /// <summary>Estante já cadastrada, direto no repositório.</summary>
    public Estante AdicionarEstante(string nome = "Estante Centro", string deviceId = "est-abc123", bool ativa = true)
    {
        var estante = Estante.Criar(nome, "Rua A, 100", -23.55, -46.63, deviceId,
            Enumerable.Repeat((byte)7, 32).ToArray(), Inicio);
        if (!ativa)
            estante.Desativar();
        Repositorio.Estantes.Add(estante);
        return estante;
    }

    /// <summary>Cofre da estante no status pedido, pelas transições válidas da máquina de estados.</summary>
    public Cofre AdicionarCofre(Guid estanteId, StatusCofre status, int numero = 1)
    {
        var cofre = Cofre.Criar(estanteId, numero, numero - 1, TamanhoCofre.Medio);
        switch (status)
        {
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

        Repositorio.Cofres.Add(cofre);
        return cofre;
    }
}
