namespace CofreEletronico.Application.Autorizacao;

/// <summary>
/// Nomes dos papéis fixos do sistema (contrato em docs/ARQUITETURA.md, tabela <c>papeis</c>).
/// Fica na Application para ser usada pela Api (políticas de autorização, cadastro)
/// e pela Infrastructure (seed do Identity) sem que nenhuma delas dependa da outra.
/// </summary>
public static class Papeis
{
    /// <summary>Papel atribuído a todo usuário no cadastro.</summary>
    public const string Usuario = "Usuario";

    /// <summary>Papel de administração (rotas <c>/admin</c>).</summary>
    public const string Admin = "Admin";
}
