using System.Net.Mail;

namespace CofreEletronico.Api.Endpoints.Auth;

// Corpos de requisição de /auth. Campos anuláveis para que um campo ausente vire 400 com mensagem
// por campo, em vez de erro de desserialização. A padronização com FluentValidation é da T-0304.

public sealed record RegistroRequisicao(string? Nome, string? Email, string? Senha, string? Telefone);

public sealed record LoginRequisicao(string? Email, string? Senha);

public sealed record RefreshTokenRequisicao(string? RefreshToken);

/// <summary>Validação mínima de entrada (obrigatórios, tamanhos, formato do e-mail). Chaves em camelCase.</summary>
internal static class ValidacaoAuth
{
    public const int NomeTamanhoMaximo = 150;
    public const int EmailTamanhoMaximo = 256; // coluna email do Identity
    public const int TelefoneTamanhoMaximo = 20;
    public const int SenhaTamanhoMaximo = 128;
    public const int RefreshTokenTamanhoMaximo = 128; // o emitido tem 43 caracteres

    public static Dictionary<string, string[]> Validar(RegistroRequisicao? r)
    {
        var erros = new Dictionary<string, string[]>();
        Obrigatorio(erros, "nome", r?.Nome, "Informe o nome.", NomeTamanhoMaximo);
        Email(erros, r?.Email);
        Obrigatorio(erros, "senha", r?.Senha, "Informe a senha.", SenhaTamanhoMaximo, aparar: false);

        if (r?.Telefone is { } telefone && telefone.Trim().Length > TelefoneTamanhoMaximo)
            erros["telefone"] = [$"O telefone deve ter no máximo {TelefoneTamanhoMaximo} caracteres."];

        return erros;
    }

    public static Dictionary<string, string[]> Validar(LoginRequisicao? r)
    {
        var erros = new Dictionary<string, string[]>();
        Obrigatorio(erros, "email", r?.Email, "Informe o e-mail.", EmailTamanhoMaximo);
        Obrigatorio(erros, "senha", r?.Senha, "Informe a senha.", SenhaTamanhoMaximo, aparar: false);
        return erros;
    }

    public static Dictionary<string, string[]> Validar(RefreshTokenRequisicao? r)
    {
        var erros = new Dictionary<string, string[]>();
        Obrigatorio(erros, "refreshToken", r?.RefreshToken, "Informe o refresh token.", RefreshTokenTamanhoMaximo, aparar: false);
        return erros;
    }

    // aparar = false para senha e token: espaços contam (são parte do valor).
    private static void Obrigatorio(
        Dictionary<string, string[]> erros, string campo, string? valor, string mensagem, int? tamanhoMaximo = null, bool aparar = true)
    {
        if (string.IsNullOrWhiteSpace(valor))
            erros[campo] = [mensagem];
        else if (tamanhoMaximo is { } max && (aparar ? valor.Trim() : valor).Length > max)
            erros[campo] = [$"Deve ter no máximo {max} caracteres."];
    }

    private static void Email(Dictionary<string, string[]> erros, string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
        {
            erros["email"] = ["Informe o e-mail."];
            return;
        }

        var email = valor.Trim();
        if (email.Length > EmailTamanhoMaximo)
            erros["email"] = [$"Deve ter no máximo {EmailTamanhoMaximo} caracteres."];
        else if (!EmailValido(email))
            erros["email"] = ["E-mail inválido."];
    }

    // MailAddress também aceita "Nome <a@b.c>": exigir Address == entrada rejeita esse formato.
    private static bool EmailValido(string email) =>
        MailAddress.TryCreate(email, out var endereco) && endereco.Address == email;
}
