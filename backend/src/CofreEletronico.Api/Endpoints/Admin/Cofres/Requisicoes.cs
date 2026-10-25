using CofreEletronico.Application.Cofres;
using CofreEletronico.Domain.Estantes;

namespace CofreEletronico.Api.Endpoints.Admin.Cofres;

/// <summary>
/// Corpo de POST e PUT /admin/estantes/{estanteId}/cofres. Campos anuláveis para que um campo ausente vire 400 com
/// mensagem por campo. <see cref="Tamanho"/> é texto (<c>pequeno</c>, <c>medio</c>, <c>grande</c>). A padronização
/// com FluentValidation é da T-0304.
/// </summary>
public sealed record CofreRequisicao(int? Numero, int? CanalRele, string? Tamanho);

/// <summary>Dados de cofre já validados na borda.</summary>
internal readonly record struct DadosCofre(int Numero, int CanalRele, TamanhoCofre Tamanho);

/// <summary>Validação de entrada de /admin/estantes/{estanteId}/cofres. Chaves em camelCase; mensagens sem ecoar o valor recebido.</summary>
internal static class ValidacaoCofres
{
    public static Dictionary<string, string[]> Validar(CofreRequisicao? r, out DadosCofre dados)
    {
        var erros = new Dictionary<string, string[]>();

        if (r?.Numero is not { } numero)
            erros["numero"] = ["Informe o número do cofre."];
        else if (numero < 1)
            erros["numero"] = ["O número do cofre deve ser maior ou igual a 1."];

        if (r?.CanalRele is not { } canal)
            erros["canalRele"] = ["Informe o canal do relé."];
        else if (canal < Cofre.CanalMinimo || canal > Cofre.CanalMaximo)
            erros["canalRele"] = [$"O canal do relé deve estar entre {Cofre.CanalMinimo} e {Cofre.CanalMaximo}."];

        TamanhoCofre tamanho = default;
        if (string.IsNullOrWhiteSpace(r?.Tamanho))
            erros["tamanho"] = ["Informe o tamanho do cofre."];
        else if (!TextoCofre.TentarTamanho(r.Tamanho, out tamanho))
            erros["tamanho"] = [$"Tamanho inválido. Use: {string.Join(", ", TextoCofre.TamanhosAceitos)}."];

        dados = erros.Count == 0 ? new DadosCofre(r!.Numero!.Value, r.CanalRele!.Value, tamanho) : default;
        return erros;
    }
}
