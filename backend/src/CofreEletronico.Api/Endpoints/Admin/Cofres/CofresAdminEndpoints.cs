using System.Security.Claims;
using CofreEletronico.Application.Cofres;
using Microsoft.AspNetCore.Mvc;

namespace CofreEletronico.Api.Endpoints.Admin.Cofres;

/// <summary>
/// /admin/estantes/{estanteId}/cofres (contrato em docs/ARQUITETURA.md, "Contrato REST: administração", subseção
/// "Cofres"). Registrado dentro do grupo de <see cref="AdminEndpoints.MapAdminEndpoints"/>, que já aplica a política
/// Admin, a exigência de <c>sub</c> válido, o limite geral por admin, as tags e os 401/403/429.
/// </summary>
/// <remarks>
/// Cofre de outra estante é tratado como inexistente (404). Editar e desativar só com o cofre <c>livre</c> ou em
/// <c>manutencao</c>; os conflitos de estado saem como 409 com o status atual na mensagem.
/// </remarks>
public static class CofresAdminEndpoints
{
    public const string Rota = "/estantes/{estanteId:guid}/cofres";

    public static RouteGroupBuilder MapCofresAdmin(this RouteGroupBuilder grupoAdmin)
    {
        var grupo = grupoAdmin.MapGroup(Rota)
            .AddEndpointFilter(FiltroExcecaoDominio.Criar(typeof(CofresAdminEndpoints)));

        grupo.MapPost("", CriarAsync)
            .WithName("CriarCofre")
            .WithSummary("Cadastra um cofre (livre e ativo) na estante, com número, canal do relé (0 a 15) e tamanho.")
            .Produces<CofreDto>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        grupo.MapGet("", ListarAsync)
            .WithName("ListarCofres")
            .WithSummary("Lista os cofres da estante, ordenados por número, sem paginação.")
            .Produces<IReadOnlyList<CofreDto>>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        grupo.MapGet("/{cofreId:guid}", ObterAsync)
            .WithName("ObterCofre")
            .WithSummary("Detalha um cofre da estante.")
            .Produces<CofreDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        grupo.MapPut("/{cofreId:guid}", AtualizarAsync)
            .WithName("AtualizarCofre")
            .WithSummary("Altera número, canal do relé e tamanho. Só com o cofre livre ou em manutenção.")
            .Produces<CofreDto>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        grupo.MapPost("/{cofreId:guid}/desativar", DesativarAsync)
            .WithName("DesativarCofre")
            .WithSummary("Exclusão lógica (idempotente). Recusada se o cofre estiver reservado ou ocupado.")
            .Produces<CofreDto>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        grupo.MapPost("/{cofreId:guid}/reativar", ReativarAsync)
            .WithName("ReativarCofre")
            .WithSummary("Desfaz a exclusão lógica (idempotente), mantendo o status.")
            .Produces<CofreDto>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        grupo.MapPost("/{cofreId:guid}/manutencao", ColocarEmManutencaoAsync)
            .WithName("ColocarCofreEmManutencao")
            .WithSummary("Coloca o cofre em manutenção. Só a partir de livre.")
            .Produces<CofreDto>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        grupo.MapPost("/{cofreId:guid}/retirar-manutencao", RetirarDaManutencaoAsync)
            .WithName("RetirarCofreDaManutencao")
            .WithSummary("Retira o cofre da manutenção (volta a livre). Só a partir de manutenção.")
            .Produces<CofreDto>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return grupoAdmin;
    }

    private static async Task<IResult> CriarAsync(
        Guid estanteId, CofreRequisicao? requisicao, ClaimsPrincipal usuario, [FromServices] CriarCofre casoDeUso,
        CancellationToken cancellationToken)
    {
        var erros = ValidacaoCofres.Validar(requisicao, out var dados);
        if (erros.Count > 0)
            return ProblemasCofres.Validacao(erros);

        var resultado = await casoDeUso.ExecutarAsync(
            new CriarCofreComando(AdminAutenticado.Id(usuario), estanteId, dados.Numero, dados.CanalRele, dados.Tamanho),
            cancellationToken);

        if (resultado.Status != StatusResultadoCofre.Sucesso)
            return ProblemasCofres.Traduzir(resultado);

        var criado = resultado.Valor!;
        return Results.Created($"{AdminEndpoints.Prefixo}/estantes/{estanteId}/cofres/{criado.Id}", criado);
    }

    private static async Task<IResult> ListarAsync(Guid estanteId, [FromServices] ListarCofres casoDeUso, CancellationToken cancellationToken)
    {
        var resultado = await casoDeUso.ExecutarAsync(estanteId, cancellationToken);
        return resultado.Status == StatusResultadoCofre.Sucesso ? Results.Ok(resultado.Valor) : ProblemasCofres.Traduzir(resultado);
    }

    private static async Task<IResult> ObterAsync(
        Guid estanteId, Guid cofreId, [FromServices] ObterCofre casoDeUso, CancellationToken cancellationToken)
    {
        var resultado = await casoDeUso.ExecutarAsync(estanteId, cofreId, cancellationToken);
        return resultado.Status == StatusResultadoCofre.Sucesso ? Results.Ok(resultado.Valor) : ProblemasCofres.Traduzir(resultado);
    }

    private static async Task<IResult> AtualizarAsync(
        Guid estanteId, Guid cofreId, CofreRequisicao? requisicao, ClaimsPrincipal usuario, [FromServices] AtualizarCofre casoDeUso,
        CancellationToken cancellationToken)
    {
        var erros = ValidacaoCofres.Validar(requisicao, out var dados);
        if (erros.Count > 0)
            return ProblemasCofres.Validacao(erros);

        var resultado = await casoDeUso.ExecutarAsync(
            new AtualizarCofreComando(AdminAutenticado.Id(usuario), estanteId, cofreId, dados.Numero, dados.CanalRele, dados.Tamanho),
            cancellationToken);

        return resultado.Status == StatusResultadoCofre.Sucesso ? Results.Ok(resultado.Valor) : ProblemasCofres.Traduzir(resultado);
    }

    private static async Task<IResult> DesativarAsync(
        Guid estanteId, Guid cofreId, ClaimsPrincipal usuario, [FromServices] DesativarCofre casoDeUso, CancellationToken cancellationToken) =>
        Responder(await casoDeUso.ExecutarAsync(Acao(usuario, estanteId, cofreId), cancellationToken));

    private static async Task<IResult> ReativarAsync(
        Guid estanteId, Guid cofreId, ClaimsPrincipal usuario, [FromServices] ReativarCofre casoDeUso, CancellationToken cancellationToken) =>
        Responder(await casoDeUso.ExecutarAsync(Acao(usuario, estanteId, cofreId), cancellationToken));

    private static async Task<IResult> ColocarEmManutencaoAsync(
        Guid estanteId, Guid cofreId, ClaimsPrincipal usuario, [FromServices] ColocarCofreEmManutencao casoDeUso,
        CancellationToken cancellationToken) =>
        Responder(await casoDeUso.ExecutarAsync(Acao(usuario, estanteId, cofreId), cancellationToken));

    private static async Task<IResult> RetirarDaManutencaoAsync(
        Guid estanteId, Guid cofreId, ClaimsPrincipal usuario, [FromServices] RetirarCofreDaManutencao casoDeUso,
        CancellationToken cancellationToken) =>
        Responder(await casoDeUso.ExecutarAsync(Acao(usuario, estanteId, cofreId), cancellationToken));

    private static AcaoCofreComando Acao(ClaimsPrincipal usuario, Guid estanteId, Guid cofreId) =>
        new(AdminAutenticado.Id(usuario), estanteId, cofreId);

    private static IResult Responder(ResultadoCofre<CofreDto> resultado) =>
        resultado.Status == StatusResultadoCofre.Sucesso ? Results.Ok(resultado.Valor) : ProblemasCofres.Traduzir(resultado);
}

/// <summary>Respostas de erro de /admin/estantes/{estanteId}/cofres como ProblemDetails, em português, sem ecoar valores.</summary>
internal static class ProblemasCofres
{
    public static IResult Validacao(IDictionary<string, string[]> erros) =>
        Results.ValidationProblem(erros, title: "Dados inválidos.");

    public static IResult Traduzir<T>(ResultadoCofre<T> resultado)
        where T : class => resultado.Status switch
        {
            StatusResultadoCofre.Validacao => Validacao(resultado.Erros.ToDictionary(e => e.Key, e => e.Value)),

            StatusResultadoCofre.EstanteNaoEncontrada => Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Não encontrada",
                detail: "Estante não encontrada."),

            StatusResultadoCofre.CofreNaoEncontrado => Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Não encontrado",
                detail: "Cofre não encontrado."),

            // Consulta prévia ou índice único (23505): mesma resposta, sem ecoar o valor.
            StatusResultadoCofre.NumeroEmUso => Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Número já usado",
                detail: "Já existe outro cofre com esse número nesta estante."),

            StatusResultadoCofre.CanalEmUso => Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Canal já usado",
                detail: "O canal do relé já é usado por outro cofre nesta estante."),

            StatusResultadoCofre.ConflitoEstado => Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Operação não permitida",
                detail: resultado.Detalhe),

            // Token de concorrência (xmin): outra operação alterou o cofre; nada foi gravado.
            StatusResultadoCofre.ConflitoConcorrencia => Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Conflito de atualização",
                detail: "O cofre foi alterado por outra operação. Recarregue os dados e tente novamente."),

            _ => throw new InvalidOperationException($"Status sem tradução HTTP: {resultado.Status}."),
        };
}
