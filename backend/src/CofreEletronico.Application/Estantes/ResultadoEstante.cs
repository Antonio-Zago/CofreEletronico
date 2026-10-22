namespace CofreEletronico.Application.Estantes;

public enum StatusResultadoEstante
{
    Sucesso,

    /// <summary>Estante inexistente.</summary>
    NaoEncontrada,

    /// <summary>Dados rejeitados (pela regra do domínio ou pela paginação). Ver <see cref="ResultadoEstante{T}.Erros"/>.</summary>
    Validacao,

    /// <summary>Conflito de regra de negócio (ex.: desativar com cofre reservado ou ocupado).</summary>
    Conflito,

    /// <summary>A estante foi alterada por outra operação entre a leitura e a gravação (concorrência otimista). Nada foi gravado.</summary>
    ConflitoConcorrencia,

    /// <summary>Falha do servidor: não foi possível gerar um device_id livre depois de todas as tentativas.</summary>
    DeviceIdIndisponivel,
}

/// <summary>Resultado tipado dos casos de uso de estantes; a Api traduz em status HTTP.</summary>
public sealed class ResultadoEstante<T>
    where T : class
{
    private static readonly IReadOnlyDictionary<string, string[]> SemErros = new Dictionary<string, string[]>();

    private ResultadoEstante(StatusResultadoEstante status, T? valor, IReadOnlyDictionary<string, string[]> erros)
    {
        Status = status;
        Valor = valor;
        Erros = erros;
    }

    public StatusResultadoEstante Status { get; }

    /// <summary>Preenchido só em <see cref="StatusResultadoEstante.Sucesso"/>.</summary>
    public T? Valor { get; }

    /// <summary>Erros por campo (camelCase), preenchido só em <see cref="StatusResultadoEstante.Validacao"/>.</summary>
    public IReadOnlyDictionary<string, string[]> Erros { get; }

    public static ResultadoEstante<T> Sucesso(T valor) =>
        new(StatusResultadoEstante.Sucesso, valor ?? throw new ArgumentNullException(nameof(valor)), SemErros);

    public static ResultadoEstante<T> Validacao(IReadOnlyDictionary<string, string[]> erros) =>
        new(StatusResultadoEstante.Validacao, null, erros);

    public static ResultadoEstante<T> Falha(StatusResultadoEstante status)
    {
        if (status is StatusResultadoEstante.Sucesso or StatusResultadoEstante.Validacao)
            throw new ArgumentException("Use Sucesso() ou Validacao() para esse status.", nameof(status));

        return new(status, null, SemErros);
    }
}
