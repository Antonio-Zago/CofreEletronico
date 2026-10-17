using Microsoft.AspNetCore.Identity;

namespace CofreEletronico.Infrastructure.Persistencia.Identity;

/// <summary>
/// Papel de autorização (ASP.NET Identity, chave Guid). Sem campos próprios.
/// Os papéis fixos (<see cref="Application.Autorizacao.Papeis"/>) são semeados em <c>Configuracoes/PapelConfiguracao</c>.
/// </summary>
public class Papel : IdentityRole<Guid>
{
}
