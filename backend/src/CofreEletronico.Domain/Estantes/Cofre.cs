using CofreEletronico.Domain.Common;

namespace CofreEletronico.Domain.Estantes;

/// <summary>
/// Cofre de uma estante (contrato em docs/ARQUITETURA.md, tabela <c>cofres</c>).
/// <see cref="Numero"/> é o número exibido ao usuário e enviado no comando MQTT;
/// <see cref="CanalRele"/> é a saída do ESP32 que aciona a fechadura. <see cref="Ativo"/> = false é a exclusão lógica.
/// </summary>
/// <remarks>
/// Máquina de estados (qualquer outra transição lança <see cref="TransicaoCofreInvalidaException"/>):
/// <code>
/// Livre      --Reservar-------------> Reservado
/// Reservado  --Ocupar---------------> Ocupado
/// Reservado  --CancelarReserva------> Livre
/// Ocupado    --Liberar--------------> Livre
/// Livre      --ColocarEmManutencao--> Manutencao
/// Manutencao --RetirarDaManutencao--> Livre
/// </code>
/// Editar (<see cref="Atualizar"/>) e desativar (<see cref="Desativar"/>) só valem sem locação em andamento
/// (Livre ou Manutencao) e não mudam o status.
/// Decisão: Reservado e Ocupado NÃO vão direto para Manutencao. Nesses estados há uma locação em
/// andamento (Pix pendente ou itens do usuário dentro do cofre); tirar o cofre de serviço sem encerrar
/// ou cancelar a locação deixaria a locação apontando para um cofre indisponível e bloquearia a abertura
/// pelo dono. O caminho é encerrar/cancelar a locação (cofre volta a Livre) e então colocar em manutenção.
/// Uma intervenção forçada (cofre com defeito e itens dentro) seria um fluxo próprio de admin, a definir
/// junto com a política de itens não retirados (pontos em aberto da arquitetura).
/// </remarks>
public sealed class Cofre : Entity
{
    /// <summary>Primeiro canal da placa de relés da estante.</summary>
    public const int CanalMinimo = 0;

    /// <summary>Último canal da placa de 16 relés por estante (contrato: canal de 0 a 15).</summary>
    public const int CanalMaximo = 15;

    private Cofre(Guid id, Guid estanteId, int numero, int canalRele, TamanhoCofre tamanho)
        : base(id)
    {
        EstanteId = estanteId;
        Numero = numero;
        CanalRele = canalRele;
        Tamanho = tamanho;
        Status = StatusCofre.Livre;
        Ativo = true;
    }

    // Materialização pelo EF Core.
    private Cofre()
    {
    }

    public Guid EstanteId { get; private set; }

    public int Numero { get; private set; }

    public int CanalRele { get; private set; }

    public TamanhoCofre Tamanho { get; private set; }

    public StatusCofre Status { get; private set; }

    public bool Ativo { get; private set; }

    /// <summary>Livre ou Manutencao: nenhuma locação (Pix pendente ou itens dentro) depende do cofre.</summary>
    private bool SemLocacaoEmAndamento => Status is StatusCofre.Livre or StatusCofre.Manutencao;

    /// <summary>Cria um cofre Livre e ativo. A unicidade de número e canal na estante é garantida no banco.</summary>
    public static Cofre Criar(Guid estanteId, int numero, int canalRele, TamanhoCofre tamanho)
    {
        if (estanteId == Guid.Empty)
            throw new ExcecaoDominio("A estante do cofre é obrigatória.");
        ValidarDados(numero, canalRele, tamanho);

        return new Cofre(Guid.NewGuid(), estanteId, numero, canalRele, tamanho);
    }

    /// <summary>
    /// Edição pelo admin (número, canal do relé e tamanho), com a mesma validação da criação (<see cref="ExcecaoDominio"/>).
    /// Só sem locação em andamento (Livre ou Manutencao): em Reservado/Ocupado lança
    /// <see cref="TransicaoCofreInvalidaException"/>. Valida tudo antes de alterar: se lançar, o cofre continua como
    /// estava. Status e <see cref="Ativo"/> não mudam. A unicidade de número e canal na estante é conferida pelo caso de
    /// uso e garantida no banco.
    /// </summary>
    public void Atualizar(int numero, int canalRele, TamanhoCofre tamanho)
    {
        ValidarDados(numero, canalRele, tamanho);
        if (!SemLocacaoEmAndamento)
            throw new TransicaoCofreInvalidaException(nameof(Atualizar), Status);

        Numero = numero;
        CanalRele = canalRele;
        Tamanho = tamanho;
    }

    /// <summary>Livre → Reservado. Cofre inativo (excluído logicamente) não pode ser reservado.</summary>
    public void Reservar()
    {
        if (Status == StatusCofre.Livre && !Ativo)
            throw new ExcecaoDominio("Cofre inativo não pode ser reservado.");

        Transitar(nameof(Reservar), StatusCofre.Livre, StatusCofre.Reservado);
    }

    /// <summary>Reservado → Ocupado (pagamento confirmado).</summary>
    public void Ocupar() => Transitar(nameof(Ocupar), StatusCofre.Reservado, StatusCofre.Ocupado);

    /// <summary>Reservado → Livre (Pix não pago ou locação cancelada).</summary>
    public void CancelarReserva() => Transitar(nameof(CancelarReserva), StatusCofre.Reservado, StatusCofre.Livre);

    /// <summary>Ocupado → Livre (locação encerrada ou expirada).</summary>
    public void Liberar() => Transitar(nameof(Liberar), StatusCofre.Ocupado, StatusCofre.Livre);

    /// <summary>Livre → Manutencao. Reservado/Ocupado não podem (ver remarks da classe).</summary>
    public void ColocarEmManutencao() =>
        Transitar(nameof(ColocarEmManutencao), StatusCofre.Livre, StatusCofre.Manutencao);

    /// <summary>Manutencao → Livre.</summary>
    public void RetirarDaManutencao() =>
        Transitar(nameof(RetirarDaManutencao), StatusCofre.Manutencao, StatusCofre.Livre);

    /// <summary>
    /// Exclusão lógica. Só sem locação em andamento (Livre ou Manutencao). Idempotente para cofre já inativo.
    /// </summary>
    public void Desativar()
    {
        if (!SemLocacaoEmAndamento)
            throw new TransicaoCofreInvalidaException(nameof(Desativar), Status);

        Ativo = false;
    }

    /// <summary>Desfaz a exclusão lógica, mantendo o status (Livre ou Manutencao). Idempotente.</summary>
    public void Reativar() => Ativo = true;

    private void Transitar(string operacao, StatusCofre esperado, StatusCofre destino)
    {
        if (Status != esperado)
            throw new TransicaoCofreInvalidaException(operacao, Status);

        Status = destino;
    }

    private static void ValidarDados(int numero, int canalRele, TamanhoCofre tamanho)
    {
        if (numero <= 0)
            throw new ExcecaoDominio("O número do cofre deve ser maior que zero.");
        if (canalRele < CanalMinimo || canalRele > CanalMaximo)
            throw new ExcecaoDominio($"O canal do relé deve estar entre {CanalMinimo} e {CanalMaximo}.");
        ValidarTamanho(tamanho);
    }

    private static void ValidarTamanho(TamanhoCofre tamanho)
    {
        if (!Enum.IsDefined(tamanho))
            throw new ExcecaoDominio("Tamanho de cofre inválido.");
    }
}
