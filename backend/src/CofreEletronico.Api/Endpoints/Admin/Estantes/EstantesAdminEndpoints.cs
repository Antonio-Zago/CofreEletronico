using System.Security.Claims;
using CofreEletronico.Api.Autenticacao;
using CofreEletronico.Application.Estantes;
using CofreEletronico.Domain.Common;
using Microsoft.AspNetCore.Mvc;

namespace CofreEletronico.Api.Endpoints.Admin.Estantes;

/// <summary>
/// /admin/estantes (contrato em docs/ARQUITETURA.md, "Contrato REST: administração"). Registrado dentro do grupo de
/// <see cref="AdminEndpoints.MapAdminEndpoints"/>, que já aplica a política Admin, a exigência de <c>sub</c> válido,
/// o limite geral por admin, as tags e os 401/403/429. As duas rotas que geram credenciais somam também a política
/// <see cref="RateLimitOpcoes.PoliticaAdminSensivel"/>.
/// </summary>
/// <remarks>
/// O segredo HMAC (hex) e a senha MQTT só aparecem nas respostas de criação e de regeneração, que levam
/// <c>Cache-Control: no-store</c>. Nunca entram em log, exceção ou ProblemDetails.
/// </remarks>
public static class EstantesAdminEndpoints
{
    public const string Rota = "/estantes";

    public static RouteGroupBuilder MapEstantesAdmin(this RouteGroupBuilder grupoAdmin)
    {
        var grupo = grupoAdmin.MapGroup(Rota)
            .AddEndpointFilter(TraduzirExcecaoDominioAsync);

        grupo.MapPost("", CriarAsync)
            .WithName("CriarEstante")
            .WithSummary("Cadastra a estante e devolve, uma única vez, as credenciais do ESP32 (device_id, segredo HMAC e senha MQTT).")
            .Produces<EstanteCriadaDto>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .RequireRateLimiting(RateLimitOpcoes.PoliticaAdminSensivel);

        grupo.MapGet("", ListarAsync)
            .WithName("ListarEstantes")
            .WithSummary("Lista as estantes, ordenadas por nome, com paginação (padrão 1 e 20, máximo 100) e filtro opcional por ativa.")
            .Produces<PaginaDto<EstanteDto>>()
            .ProducesValidationProblem();

        grupo.MapGet("/{id:guid}", ObterAsync)
            .WithName("ObterEstante")
            .WithSummary("Detalha uma estante.")
            .Produces<EstanteDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        grupo.MapPut("/{id:guid}", AtualizarAsync)
            .WithName("AtualizarEstante")
            .WithSummary("Altera nome, endereço e coordenadas. device_id e segredo não mudam.")
            .Produces<EstanteDto>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        grupo.MapPost("/{id:guid}/desativar", DesativarAsync)
            .WithName("DesativarEstante")
            .WithSummary("Exclusão lógica (idempotente). Recusada se algum cofre estiver reservado ou ocupado.")
            .Produces<EstanteDto>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        grupo.MapPost("/{id:guid}/reativar", ReativarAsync)
            .WithName("ReativarEstante")
            .WithSummary("Desfaz a exclusão lógica (idempotente).")
            .Produces<EstanteDto>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        grupo.MapPost("/{id:guid}/credenciais", RegenerarCredenciaisAsync)
            .WithName("RegenerarCredenciaisEstante")
            .WithSummary("Gera novo segredo HMAC e nova senha MQTT (os anteriores deixam de valer). O device_id não muda.")
            .Produces<CredenciaisEstanteDto>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireRateLimiting(RateLimitOpcoes.PoliticaAdminSensivel);

        return grupoAdmin;
    }

    private static async Task<IResult> CriarAsync(
        EstanteRequisicao? requisicao, ClaimsPrincipal usuario, HttpContext contexto, [FromServices] CriarEstante casoDeUso,
        CancellationToken cancellationToken)
    {
        var erros = ValidacaoEstantes.Validar(requisicao);
        if (erros.Count > 0)
            return ProblemasEstantes.Validacao(erros);

        var resultado = await casoDeUso.ExecutarAsync(
            new CriarEstanteComando(
                AdminAutenticado.Id(usuario), requisicao!.Nome!, requisicao.Endereco!, requisicao.Latitude!.Value, requisicao.Longitude!.Value),
            cancellationToken);

        if (resultado.Status != StatusResultadoEstante.Sucesso)
            return ProblemasEstantes.Traduzir(resultado);

        NaoArmazenar(contexto);
        var criada = resultado.Valor!;
        return Results.Created($"{AdminEndpoints.Prefixo}{Rota}/{criada.Id}", criada);
    }

    private static async Task<IResult> ListarAsync(
        [FromQuery] string? ativa, [FromQuery] string? pagina, [FromQuery] string? tamanhoPagina,
        [FromServices] ListarEstantes casoDeUso, CancellationToken cancellationToken)
    {
        var erros = ValidacaoEstantes.ValidarListagem(ativa, pagina, tamanhoPagina, out var consulta);
        if (erros.Count > 0)
            return ProblemasEstantes.Validacao(erros);

        var resultado = await casoDeUso.ExecutarAsync(consulta, cancellationToken);
        return resultado.Status == StatusResultadoEstante.Sucesso ? Results.Ok(resultado.Valor) : ProblemasEstantes.Traduzir(resultado);
    }

    private static async Task<IResult> ObterAsync(Guid id, [FromServices] ObterEstante casoDeUso, CancellationToken cancellationToken)
    {
        var resultado = await casoDeUso.ExecutarAsync(id, cancellationToken);
        return resultado.Status == StatusResultadoEstante.Sucesso ? Results.Ok(resultado.Valor) : ProblemasEstantes.Traduzir(resultado);
    }

    private static async Task<IResult> AtualizarAsync(
        Guid id, EstanteRequisicao? requisicao, ClaimsPrincipal usuario, [FromServices] AtualizarEstante casoDeUso,
        CancellationToken cancellationToken)
    {
        var erros = ValidacaoEstantes.Validar(requisicao);
        if (erros.Count > 0)
            return ProblemasEstantes.Validacao(erros);

        var resultado = await casoDeUso.ExecutarAsync(
            new AtualizarEstanteComando(
                AdminAutenticado.Id(usuario), id, requisicao!.Nome!, requisicao.Endereco!, requisicao.Latitude!.Value, requisicao.Longitude!.Value),
            cancellationToken);

        return resultado.Status == StatusResultadoEstante.Sucesso ? Results.Ok(resultado.Valor) : ProblemasEstantes.Traduzir(resultado);
    }

    private static async Task<IResult> DesativarAsync(
        Guid id, ClaimsPrincipal usuario, [FromServices] DesativarEstante casoDeUso, CancellationToken cancellationToken)
    {
        var resultado = await casoDeUso.ExecutarAsync(new AcaoEstanteComando(AdminAutenticado.Id(usuario), id), cancellationToken);
        return resultado.Status == StatusResultadoEstante.Sucesso ? Results.Ok(resultado.Valor) : ProblemasEstantes.Traduzir(resultado);
    }

    private static async Task<IResult> ReativarAsync(
        Guid id, ClaimsPrincipal usuario, [FromServices] ReativarEstante casoDeUso, CancellationToken cancellationToken)
    {
        var resultado = await casoDeUso.ExecutarAsync(new AcaoEstanteComando(AdminAutenticado.Id(usuario), id), cancellationToken);
        return resultado.Status == StatusResultadoEstante.Sucesso ? Results.Ok(resultado.Valor) : ProblemasEstantes.Traduzir(resultado);
    }

    private static async Task<IResult> RegenerarCredenciaisAsync(
        Guid id, ClaimsPrincipal usuario, HttpContext contexto, [FromServices] RegenerarCredenciaisEstante casoDeUso,
        CancellationToken cancellationToken)
    {
        var resultado = await casoDeUso.ExecutarAsync(new AcaoEstanteComando(AdminAutenticado.Id(usuario), id), cancellationToken);
        if (resultado.Status != StatusResultadoEstante.Sucesso)
            return ProblemasEstantes.Traduzir(resultado);

        NaoArmazenar(contexto);
        return Results.Ok(resultado.Valor);
    }

    /// <summary>Resposta com credenciais: nem proxy nem navegador podem guardá-la.</summary>
    private static void NaoArmazenar(HttpContext contexto)
    {
        contexto.Response.Headers.CacheControl = "no-store";
        contexto.Response.Headers.Pragma = "no-cache";
    }

    /// <summary>
    /// Rede de segurança: regra de domínio violada que não foi traduzida pelo caso de uso vira 400 genérico,
    /// sem a mensagem nem o stack trace.
    /// </summary>
    private static async ValueTask<object?> TraduzirExcecaoDominioAsync(EndpointFilterInvocationContext contexto, EndpointFilterDelegate proximo)
    {
        try
        {
            return await proximo(contexto);
        }
        catch (ExcecaoDominio)
        {
            contexto.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                .CreateLogger(typeof(EstantesAdminEndpoints).FullName!)
                .LogWarning("Regra de domínio violada em {Rota} sem tradução no caso de uso.", contexto.HttpContext.Request.Path.Value);

            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Dados inválidos.",
                detail: "A requisição não pôde ser processada.");
        }
    }
}

/// <summary>Respostas de erro de /admin/estantes como ProblemDetails, com mensagens genéricas em português.</summary>
internal static class ProblemasEstantes
{
    public static IResult Validacao(IDictionary<string, string[]> erros) =>
        Results.ValidationProblem(erros, title: "Dados inválidos.");

    public static IResult Traduzir<T>(ResultadoEstante<T> resultado)
        where T : class => resultado.Status switch
        {
            StatusResultadoEstante.Validacao => Validacao(resultado.Erros.ToDictionary(e => e.Key, e => e.Value)),

            StatusResultadoEstante.NaoEncontrada => Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Não encontrada",
                detail: "Estante não encontrada."),

            // Hoje o único conflito de regra é o de desativar com locação em andamento.
            StatusResultadoEstante.Conflito => Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Operação não permitida",
                detail: "A estante tem cofre reservado ou ocupado e não pode ser desativada agora."),

            // Token de concorrência (xmin): outra operação alterou a estante; nada foi gravado.
            StatusResultadoEstante.ConflitoConcorrencia => Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Conflito de atualização",
                detail: "A estante foi alterada por outra operação. Recarregue os dados e tente novamente."),

            StatusResultadoEstante.DeviceIdIndisponivel => Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Erro interno",
                detail: "Não foi possível concluir o cadastro. Tente novamente."),

            _ => throw new InvalidOperationException($"Status sem tradução HTTP: {resultado.Status}."),
        };
}
