using CofreEletronico.Domain.Common;

namespace CofreEletronico.Domain.Autenticacao;

/// <summary>
/// Refresh token rotativo (contrato em docs/ARQUITETURA.md, tabela <c>refresh_tokens</c>).
/// Guarda só o hash SHA-256 do valor entregue ao cliente. Cada login abre uma família
/// (<see cref="FamiliaId"/>); cada rotação revoga o token atual e cria o próximo na mesma família.
/// A sessão (família) tem um limite absoluto (<see cref="SessaoExpiraEm"/>), fixado no login e copiado
/// em cada rotação: nenhum token da família vale além dele.
/// O usuário é referenciado só pelo Id: a entidade do Identity vive na Infrastructure.
/// </summary>
public sealed class RefreshToken : Entity
{
    private RefreshToken(
        Guid id, Guid usuarioId, string tokenHash, Guid familiaId,
        DateTimeOffset criadoEm, DateTimeOffset expiraEm, DateTimeOffset sessaoExpiraEm)
        : base(id)
    {
        UsuarioId = usuarioId;
        TokenHash = tokenHash;
        FamiliaId = familiaId;
        CriadoEm = criadoEm;
        ExpiraEm = expiraEm;
        SessaoExpiraEm = sessaoExpiraEm;
    }

    // Materialização pelo EF Core.
    private RefreshToken()
    {
    }

    public Guid UsuarioId { get; private set; }

    /// <summary>Hash SHA-256 (hex minúsculo) do valor opaco entregue ao cliente.</summary>
    public string TokenHash { get; private set; } = string.Empty;

    /// <summary>Agrupa as rotações de um mesmo login, para revogação em cadeia em caso de reuso.</summary>
    public Guid FamiliaId { get; private set; }

    public DateTimeOffset CriadoEm { get; private set; }

    /// <summary>Expiração deste token: <c>min(criação + validade do token, SessaoExpiraEm)</c>.</summary>
    public DateTimeOffset ExpiraEm { get; private set; }

    /// <summary>Limite absoluto da sessão (login + validade da sessão), igual em todos os tokens da família.</summary>
    public DateTimeOffset SessaoExpiraEm { get; private set; }

    public DateTimeOffset? RevogadoEm { get; private set; }

    /// <summary>Token gerado na rotação deste (nulo se nunca foi rotacionado).</summary>
    public Guid? SubstituidoPorId { get; private set; }

    /// <summary>Já foi trocado por outro na rotação: apresentá-lo de novo é reuso.</summary>
    public bool FoiRotacionado => SubstituidoPorId is not null;

    /// <summary>
    /// Emite o primeiro token de uma nova família (login ou registro). A sessão vai até
    /// <c>agora + validadeSessao</c> e o token até <c>min(agora + validadeToken, fim da sessão)</c>.
    /// </summary>
    public static RefreshToken Emitir(Guid usuarioId, string tokenHash, DateTimeOffset agora, TimeSpan validadeToken, TimeSpan validadeSessao)
    {
        if (validadeSessao <= TimeSpan.Zero)
            throw new ArgumentException("A validade da sessão deve ser positiva.", nameof(validadeSessao));

        return Criar(usuarioId, tokenHash, Guid.NewGuid(), agora, validadeToken, agora + validadeSessao);
    }

    /// <summary>Válido para uso: não revogado, não expirado e dentro do limite da sessão.</summary>
    public bool EstaAtivo(DateTimeOffset agora) => RevogadoEm is null && agora < ExpiraEm && agora < SessaoExpiraEm;

    /// <summary>Revoga o token. Idempotente: se já estiver revogado, mantém o instante original.</summary>
    public void Revogar(DateTimeOffset agora)
    {
        RevogadoEm ??= agora;
    }

    /// <summary>
    /// Troca este token por um novo na mesma família: revoga este e aponta <see cref="SubstituidoPorId"/>
    /// para o novo. O novo herda <see cref="SessaoExpiraEm"/> e expira em
    /// <c>min(agora + validadeToken, SessaoExpiraEm)</c>. Só um token ativo pode ser rotacionado.
    /// </summary>
    public RefreshToken Rotacionar(string novoTokenHash, DateTimeOffset agora, TimeSpan validadeToken)
    {
        if (!EstaAtivo(agora))
            throw new InvalidOperationException("Só um refresh token ativo pode ser rotacionado.");

        var novo = Criar(UsuarioId, novoTokenHash, FamiliaId, agora, validadeToken, SessaoExpiraEm);
        RevogadoEm = agora;
        SubstituidoPorId = novo.Id;
        return novo;
    }

    private static RefreshToken Criar(
        Guid usuarioId, string tokenHash, Guid familiaId, DateTimeOffset agora, TimeSpan validadeToken, DateTimeOffset sessaoExpiraEm)
    {
        if (usuarioId == Guid.Empty)
            throw new ArgumentException("O usuário do refresh token é obrigatório.", nameof(usuarioId));
        if (string.IsNullOrWhiteSpace(tokenHash))
            throw new ArgumentException("O hash do refresh token é obrigatório.", nameof(tokenHash));
        if (validadeToken <= TimeSpan.Zero)
            throw new ArgumentException("A validade do refresh token deve ser positiva.", nameof(validadeToken));
        if (sessaoExpiraEm <= agora)
            throw new ArgumentException("O fim da sessão deve ser posterior à criação.", nameof(sessaoExpiraEm));

        var expiraEm = Min(agora + validadeToken, sessaoExpiraEm);
        return new RefreshToken(Guid.NewGuid(), usuarioId, tokenHash, familiaId, agora, expiraEm, sessaoExpiraEm);
    }

    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a <= b ? a : b;
}
