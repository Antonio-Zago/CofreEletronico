using CofreEletronico.Application.Estantes.Portas;

namespace CofreEletronico.Application.Estantes;

/// <summary>
/// GET /admin/estantes: lista paginada (padrão 1 e 20, máximo 100), ordenada por nome, com filtro opcional por
/// <c>ativa</c>. A Api já valida a paginação; a checagem aqui é defesa para outros chamadores.
/// </summary>
public sealed class ListarEstantes
{
    private readonly IEstanteRepositorio _repositorio;

    public ListarEstantes(IEstanteRepositorio repositorio) => _repositorio = repositorio;

    public async Task<ResultadoEstante<PaginaDto<EstanteDto>>> ExecutarAsync(
        ListarEstantesConsulta consulta, CancellationToken cancellationToken = default)
    {
        var erros = new Dictionary<string, string[]>();
        if (consulta.Pagina < 1)
            erros["pagina"] = ["A página deve ser maior ou igual a 1."];
        if (consulta.TamanhoPagina is < 1 or > ListarEstantesConsulta.TamanhoPaginaMaximo)
            erros["tamanhoPagina"] = [$"O tamanho da página deve estar entre 1 e {ListarEstantesConsulta.TamanhoPaginaMaximo}."];
        if (erros.Count > 0)
            return ResultadoEstante<PaginaDto<EstanteDto>>.Validacao(erros);

        var pagina = await _repositorio.ListarAsync(consulta.Ativa, consulta.Pagina, consulta.TamanhoPagina, cancellationToken);
        return ResultadoEstante<PaginaDto<EstanteDto>>.Sucesso(pagina);
    }
}

/// <summary>GET /admin/estantes/{id}.</summary>
public sealed class ObterEstante
{
    private readonly IEstanteRepositorio _repositorio;

    public ObterEstante(IEstanteRepositorio repositorio) => _repositorio = repositorio;

    public async Task<ResultadoEstante<EstanteDto>> ExecutarAsync(Guid estanteId, CancellationToken cancellationToken = default)
    {
        var estante = await _repositorio.ObterAsync(estanteId, cancellationToken);
        return estante is null
            ? ResultadoEstante<EstanteDto>.Falha(StatusResultadoEstante.NaoEncontrada)
            : ResultadoEstante<EstanteDto>.Sucesso(estante);
    }
}
