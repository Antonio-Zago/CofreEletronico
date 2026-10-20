using System.Net.Mail;
using CofreEletronico.Application.Autorizacao;
using Microsoft.Extensions.Options;

namespace CofreEletronico.Infrastructure.Autorizacao;

/// <summary>
/// Seção <c>AdminInicial</c>: administrador criado na subida da Api se ainda não houver nenhum.
/// <see cref="Email"/> e <see cref="Senha"/> vêm só de user-secrets ou de variáveis de ambiente
/// (<c>AdminInicial__Email</c>, <c>AdminInicial__Senha</c>), nunca de appsettings. Opcional: sem os dois, nada é feito.
/// </summary>
public sealed class AdminInicialOpcoes
{
    public const string Secao = "AdminInicial";

    public const string NomePadrao = "Administrador";

    // Mesmos limites do cadastro (/auth/registro).
    public const int EmailTamanhoMaximo = 256;
    public const int SenhaTamanhoMaximo = 128;
    public const int NomeTamanhoMaximo = 150;

    public string? Email { get; set; }

    public string? Senha { get; set; }

    public string? Nome { get; set; } = NomePadrao;

    /// <summary>Configurado quando e-mail ou senha foram informados (o validador exige os dois).</summary>
    public bool Configurado => !string.IsNullOrWhiteSpace(Email) || !string.IsNullOrEmpty(Senha);

    /// <summary>Comando do caso de uso, ou <c>null</c> quando a seção não foi configurada.</summary>
    public AdminInicialComando? CriarComando() => Configurado
        ? new AdminInicialComando(Email!.Trim(), Senha!, string.IsNullOrWhiteSpace(Nome) ? NomePadrao : Nome.Trim())
        : null;
}

/// <summary>
/// Validação na subida (ValidateOnStart). As mensagens nunca ecoam os valores configurados.
/// A política de senha (tamanho mínimo, dígito, maiúscula, minúscula) é a do Identity, aplicada na criação.
/// </summary>
internal sealed class AdminInicialOpcoesValidador : IValidateOptions<AdminInicialOpcoes>
{
    public ValidateOptionsResult Validate(string? name, AdminInicialOpcoes options)
    {
        if (!options.Configurado)
            return ValidateOptionsResult.Success;

        var falhas = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Email))
        {
            falhas.Add("AdminInicial:Email não configurado (obrigatório quando AdminInicial:Senha é informada).");
        }
        else
        {
            var email = options.Email.Trim();
            if (email.Length > AdminInicialOpcoes.EmailTamanhoMaximo)
                falhas.Add($"AdminInicial:Email deve ter no máximo {AdminInicialOpcoes.EmailTamanhoMaximo} caracteres.");
            else if (!EmailValido(email))
                falhas.Add("AdminInicial:Email não é um e-mail válido.");
        }

        if (string.IsNullOrEmpty(options.Senha))
            falhas.Add("AdminInicial:Senha não configurada (obrigatória quando AdminInicial:Email é informado).");
        else if (options.Senha.Length > AdminInicialOpcoes.SenhaTamanhoMaximo)
            falhas.Add($"AdminInicial:Senha deve ter no máximo {AdminInicialOpcoes.SenhaTamanhoMaximo} caracteres.");

        if (options.Nome is { } nome && nome.Trim().Length > AdminInicialOpcoes.NomeTamanhoMaximo)
            falhas.Add($"AdminInicial:Nome deve ter no máximo {AdminInicialOpcoes.NomeTamanhoMaximo} caracteres.");

        if (falhas.Count > 0)
            falhas.Add("Ver a seção \"Administrador inicial\" do backend/README.md.");

        return falhas.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(falhas);
    }

    // Mesma regra do cadastro: MailAddress aceita "Nome <a@b.c>", exigir Address == entrada rejeita esse formato.
    private static bool EmailValido(string email) =>
        MailAddress.TryCreate(email, out var endereco) && endereco.Address == email;
}
