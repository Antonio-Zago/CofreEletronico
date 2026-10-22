namespace CofreEletronico.Application.Estantes;

// Entradas dos casos de uso de /admin/estantes. AdminId (claim "sub" do token) entra só nos logs de auditoria.
// Formato e tamanho dos campos são validados antes, na borda (Api); o domínio valida de novo.

public sealed record CriarEstanteComando(Guid AdminId, string Nome, string Endereco, double Latitude, double Longitude);

public sealed record AtualizarEstanteComando(
    Guid AdminId, Guid EstanteId, string Nome, string Endereco, double Latitude, double Longitude);

/// <summary>Desativar, reativar e regenerar credenciais: só a estante e o admin que pediu.</summary>
public sealed record AcaoEstanteComando(Guid AdminId, Guid EstanteId);

/// <summary>Filtro e paginação da listagem. <see cref="Ativa"/> nulo lista todas.</summary>
public sealed record ListarEstantesConsulta(bool? Ativa, int Pagina, int TamanhoPagina)
{
    public const int PaginaPadrao = 1;
    public const int TamanhoPaginaPadrao = 20;
    public const int TamanhoPaginaMaximo = 100;
}
