namespace CofreEletronico.Application.Cofres;

public enum StatusResultadoCofre
{
    Sucesso,

    /// <summary>Dados rejeitados pela regra do domínio. Ver <see cref="ResultadoCofre{T}.Erros"/>.</summary>
    Validacao,

    /// <summary>Estante inexistente (cadastro e listagem).</summary>
    EstanteNaoEncontrada,

    /// <summary>Cofre inexistente ou de outra estante.</summary>
    CofreNaoEncontrado,

    /// <summary>Já existe outro cofre com o mesmo número na estante (consulta prévia ou índice único).</summary>
    NumeroEmUso,

    /// <summary>Já existe outro cofre com o mesmo canal do relé na estante (consulta prévia ou índice único).</summary>
    CanalEmUso,

    /// <summary>
    /// Operação não permitida no status atual do cofre (ex.: editar ocupado). <see cref="ResultadoCofre{T}.Detalhe"/>
    /// traz a mensagem para o admin, com o status atual.
    /// </summary>
    ConflitoEstado,

    /// <summary>O cofre foi alterado por outra operação entre a leitura e a gravação (xmin). Nada foi gravado.</summary>
    ConflitoConcorrencia,
}

/// <summary>Resultado tipado dos casos de uso de cofres; a Api traduz em status HTTP.</summary>
public sealed class ResultadoCofre<T>
    where T : class
{
    private static readonly IReadOnlyDictionary<string, string[]> SemErros = new Dictionary<string, string[]>();

    private ResultadoCofre(StatusResultadoCofre status, T? valor, IReadOnlyDictionary<string, string[]> erros, string? detalhe)
    {
        Status = status;
        Valor = valor;
        Erros = erros;
        Detalhe = detalhe;
    }

    public StatusResultadoCofre Status { get; }

    /// <summary>Preenchido só em <see cref="StatusResultadoCofre.Sucesso"/>.</summary>
    public T? Valor { get; }

    /// <summary>Erros por campo (camelCase), preenchido só em <see cref="StatusResultadoCofre.Validacao"/>.</summary>
    public IReadOnlyDictionary<string, string[]> Erros { get; }

    /// <summary>Mensagem segura para o cliente, preenchida só em <see cref="StatusResultadoCofre.ConflitoEstado"/>.</summary>
    public string? Detalhe { get; }

    public static ResultadoCofre<T> Sucesso(T valor) =>
        new(StatusResultadoCofre.Sucesso, valor ?? throw new ArgumentNullException(nameof(valor)), SemErros, null);

    public static ResultadoCofre<T> Validacao(IReadOnlyDictionary<string, string[]> erros) =>
        new(StatusResultadoCofre.Validacao, null, erros, null);

    public static ResultadoCofre<T> ConflitoEstado(string detalhe) =>
        new(StatusResultadoCofre.ConflitoEstado, null, SemErros, detalhe);

    public static ResultadoCofre<T> Falha(StatusResultadoCofre status)
    {
        if (status is StatusResultadoCofre.Sucesso or StatusResultadoCofre.Validacao or StatusResultadoCofre.ConflitoEstado)
            throw new ArgumentException("Use Sucesso(), Validacao() ou ConflitoEstado() para esse status.", nameof(status));

        return new(status, null, SemErros, null);
    }
}
