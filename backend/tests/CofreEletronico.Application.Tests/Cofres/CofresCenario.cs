using CofreEletronico.Application.Cofres;
using CofreEletronico.Application.Tests.Comum;
using CofreEletronico.Domain.Estantes;

namespace CofreEletronico.Application.Tests.Cofres;

/// <summary>Monta os casos de uso de cofres com o repositório em memória e loggers capturados.</summary>
internal sealed class CofresCenario
{
    public static readonly Guid AdminId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    public CofresCenario()
    {
        Criar = new CriarCofre(Repositorio, LogCriar);
        Listar = new ListarCofres(Repositorio);
        Obter = new ObterCofre(Repositorio);
        Atualizar = new AtualizarCofre(Repositorio, LogAtualizar);
        Desativar = new DesativarCofre(Repositorio, LogDesativar);
        Reativar = new ReativarCofre(Repositorio, LogReativar);
        ColocarEmManutencao = new ColocarCofreEmManutencao(Repositorio, LogManutencao);
        RetirarDaManutencao = new RetirarCofreDaManutencao(Repositorio, LogRetirar);
    }

    public CofreRepositorioFalso Repositorio { get; } = new();

    public LoggerCapturado<CriarCofre> LogCriar { get; } = new();

    public LoggerCapturado<AtualizarCofre> LogAtualizar { get; } = new();

    public LoggerCapturado<DesativarCofre> LogDesativar { get; } = new();

    public LoggerCapturado<ReativarCofre> LogReativar { get; } = new();

    public LoggerCapturado<ColocarCofreEmManutencao> LogManutencao { get; } = new();

    public LoggerCapturado<RetirarCofreDaManutencao> LogRetirar { get; } = new();

    public CriarCofre Criar { get; }

    public ListarCofres Listar { get; }

    public ObterCofre Obter { get; }

    public AtualizarCofre Atualizar { get; }

    public DesativarCofre Desativar { get; }

    public ReativarCofre Reativar { get; }

    public ColocarCofreEmManutencao ColocarEmManutencao { get; }

    public RetirarCofreDaManutencao RetirarDaManutencao { get; }

    public Guid AdicionarEstante()
    {
        var id = Guid.NewGuid();
        Repositorio.Estantes.Add(id);
        return id;
    }

    /// <summary>Cofre da estante no status pedido, pelas transições válidas da máquina de estados.</summary>
    public Cofre AdicionarCofre(Guid estanteId, StatusCofre status = StatusCofre.Livre, int numero = 1, int canal = 0, bool ativo = true)
    {
        var cofre = Cofre.Criar(estanteId, numero, canal, TamanhoCofre.Medio);
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

        if (!ativo)
            cofre.Desativar();

        Repositorio.Cofres.Add(cofre);
        return cofre;
    }

    public AcaoCofreComando Acao(Cofre cofre) => new(AdminId, cofre.EstanteId, cofre.Id);
}
