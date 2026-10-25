using CofreEletronico.Domain.Estantes;

namespace CofreEletronico.Application.Cofres;

// Entradas dos casos de uso de /admin/estantes/{estanteId}/cofres. AdminId (claim "sub" do token) entra só nos logs de
// auditoria. Formato e faixas são validados antes, na borda (Api); o domínio valida de novo.

public sealed record CriarCofreComando(Guid AdminId, Guid EstanteId, int Numero, int CanalRele, TamanhoCofre Tamanho);

public sealed record AtualizarCofreComando(
    Guid AdminId, Guid EstanteId, Guid CofreId, int Numero, int CanalRele, TamanhoCofre Tamanho);

/// <summary>Desativar, reativar, colocar em manutenção e retirar da manutenção: só o cofre e o admin que pediu.</summary>
public sealed record AcaoCofreComando(Guid AdminId, Guid EstanteId, Guid CofreId);
