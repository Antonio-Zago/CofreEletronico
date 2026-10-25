using CofreEletronico.Domain.Common;

namespace CofreEletronico.Api.Endpoints.Admin;

/// <summary>
/// Rede de segurança dos sub-grupos admin: regra de domínio violada que não foi traduzida pelo caso de uso vira 400
/// genérico, sem a mensagem nem o stack trace (só um warning com a rota).
/// </summary>
public static class FiltroExcecaoDominio
{
    /// <summary>Endpoint filter que registra o warning na categoria de log <paramref name="categoria"/>.</summary>
    public static Func<EndpointFilterInvocationContext, EndpointFilterDelegate, ValueTask<object?>> Criar(Type categoria)
    {
        var nomeCategoria = categoria.FullName!;
        return async (contexto, proximo) =>
        {
            try
            {
                return await proximo(contexto);
            }
            catch (ExcecaoDominio)
            {
                contexto.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                    .CreateLogger(nomeCategoria)
                    .LogWarning("Regra de domínio violada em {Rota} sem tradução no caso de uso.", contexto.HttpContext.Request.Path.Value);

                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Dados inválidos.",
                    detail: "A requisição não pôde ser processada.");
            }
        };
    }
}
