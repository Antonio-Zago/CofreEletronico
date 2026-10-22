using System.Globalization;
using CofreEletronico.Application.Estantes;
using CofreEletronico.Domain.Estantes;

namespace CofreEletronico.Api.Endpoints.Admin.Estantes;

/// <summary>
/// Corpo de POST e PUT /admin/estantes. Campos anuláveis para que um campo ausente vire 400 com mensagem por campo,
/// em vez de erro de desserialização. A padronização com FluentValidation é da T-0304.
/// </summary>
public sealed record EstanteRequisicao(string? Nome, string? Endereco, double? Latitude, double? Longitude);

/// <summary>Validação de entrada de /admin/estantes. Chaves em camelCase; mensagens sem ecoar o valor recebido.</summary>
internal static class ValidacaoEstantes
{
    public static Dictionary<string, string[]> Validar(EstanteRequisicao? r)
    {
        var erros = new Dictionary<string, string[]>();
        Texto(erros, "nome", r?.Nome, "Informe o nome.", Estante.NomeTamanhoMaximo);
        Texto(erros, "endereco", r?.Endereco, "Informe o endereço.", Estante.EnderecoTamanhoMaximo);
        Coordenada(erros, "latitude", r?.Latitude, "Informe a latitude.", 90);
        Coordenada(erros, "longitude", r?.Longitude, "Informe a longitude.", 180);
        return erros;
    }

    /// <summary>
    /// Query de GET /admin/estantes lida como texto, para que valor malformado também saia como ValidationProblem
    /// (o binding automático devolveria 400 sem corpo fora de Development).
    /// </summary>
    public static Dictionary<string, string[]> ValidarListagem(
        string? ativa, string? pagina, string? tamanhoPagina, out ListarEstantesConsulta consulta)
    {
        var erros = new Dictionary<string, string[]>();

        bool? filtro = null;
        if (!string.IsNullOrWhiteSpace(ativa))
        {
            if (bool.TryParse(ativa.Trim(), out var valor))
                filtro = valor;
            else
                erros["ativa"] = ["Use true ou false."];
        }

        var numeroPagina = ListarEstantesConsulta.PaginaPadrao;
        if (!string.IsNullOrWhiteSpace(pagina) &&
            (!int.TryParse(pagina.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out numeroPagina) || numeroPagina < 1))
        {
            erros["pagina"] = ["A página deve ser um número inteiro maior ou igual a 1."];
        }

        var tamanho = ListarEstantesConsulta.TamanhoPaginaPadrao;
        if (!string.IsNullOrWhiteSpace(tamanhoPagina) &&
            (!int.TryParse(tamanhoPagina.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out tamanho) ||
             tamanho < 1 || tamanho > ListarEstantesConsulta.TamanhoPaginaMaximo))
        {
            erros["tamanhoPagina"] = [$"O tamanho da página deve ser um número inteiro entre 1 e {ListarEstantesConsulta.TamanhoPaginaMaximo}."];
        }

        consulta = new ListarEstantesConsulta(filtro, numeroPagina, tamanho);
        return erros;
    }

    private static void Texto(Dictionary<string, string[]> erros, string campo, string? valor, string mensagem, int tamanhoMaximo)
    {
        if (string.IsNullOrWhiteSpace(valor))
            erros[campo] = [mensagem];
        else if (valor.Trim().Length > tamanhoMaximo)
            erros[campo] = [$"Deve ter no máximo {tamanhoMaximo} caracteres."];
    }

    private static void Coordenada(Dictionary<string, string[]> erros, string campo, double? valor, string mensagem, double limite)
    {
        if (valor is not { } v)
            erros[campo] = [mensagem];
        else if (!(v >= -limite && v <= limite)) // sentido positivo: NaN também é rejeitado
            erros[campo] = [$"Deve estar entre -{limite} e {limite}."];
    }
}
