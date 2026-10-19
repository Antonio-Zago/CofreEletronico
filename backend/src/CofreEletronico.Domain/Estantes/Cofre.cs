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

    /// <summary>Cria um cofre Livre e ativo. A unicidade de número e canal na estante é garantida no banco.</summary>
    public static Cofre Criar(Guid estanteId, int numero, int canalRele, TamanhoCofre tamanho)
    {
        if (estanteId == Guid.Empty)
            throw new ExcecaoDominio("A estante do cofre é obrigatória.");
        if (numero <= 0)
            throw new ExcecaoDominio("O número do cofre deve ser maior que zero.");
        if (canalRele < CanalMinimo || canalRele > CanalMaximo)
            throw new ExcecaoDominio($"O canal do relé deve estar entre {CanalMinimo} e {CanalMaximo}.");
        ValidarTamanho(tamanho);

        return new Cofre(Guid.NewGuid(), estanteId, numero, canalRele, tamanho);
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

    public void AlterarTamanho(TamanhoCofre tamanho)
    {
        ValidarTamanho(tamanho);
        Tamanho = tamanho;
    }

    /// <summary>
    /// Exclusão lógica. Só sem locação em andamento (Livre ou Manutencao). Idempotente para cofre já inativo.
    /// </summary>
    public void Desativar()
    {
        if (Status is not (StatusCofre.Livre or StatusCofre.Manutencao))
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

    private static void ValidarTamanho(TamanhoCofre tamanho)
    {
        if (!Enum.IsDefined(tamanho))
            throw new ExcecaoDominio("Tamanho de cofre inválido.");
    }
}
