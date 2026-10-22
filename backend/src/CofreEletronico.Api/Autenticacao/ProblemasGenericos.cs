namespace CofreEletronico.Api.Autenticacao;

/// <summary>
/// ProblemDetails das exceções não tratadas (<c>UseExceptionHandler</c> + <c>AddProblemDetails</c>): 500 com título e
/// detalhe genéricos em português. Nunca leva mensagem, tipo ou stack trace da exceção (a exceção vai só para o log,
/// pelo próprio middleware). Só age quando há exceção: os ProblemDetails escritos pelos endpoints ficam como estão.
/// </summary>
public static class ProblemasGenericos
{
    public const string Titulo = "Erro interno";

    public const string Detalhe = "Ocorreu um erro inesperado. Tente novamente mais tarde.";

    public static void Personalizar(ProblemDetailsContext contexto)
    {
        if (contexto.Exception is null)
            return;

        contexto.ProblemDetails.Status = StatusCodes.Status500InternalServerError;
        contexto.ProblemDetails.Title = Titulo;
        contexto.ProblemDetails.Detail = Detalhe;
        contexto.ProblemDetails.Instance = null;

        // Só o traceId (correlação com o log) sobrevive entre as extensões.
        foreach (var chave in contexto.ProblemDetails.Extensions.Keys.Where(k => k != "traceId").ToList())
            contexto.ProblemDetails.Extensions.Remove(chave);
    }
}
