using CofreEletronico.Domain.Common;

namespace CofreEletronico.Domain.Tests.Common;

public class EntityTests
{
    private sealed class EntidadeA(Guid id) : Entity(id);

    private sealed class EntidadeB(Guid id) : Entity(id);

    [Fact]
    public void Entidades_do_mesmo_tipo_com_mesmo_id_sao_iguais()
    {
        var id = Guid.NewGuid();

        Assert.Equal(new EntidadeA(id), new EntidadeA(id));
        Assert.True(new EntidadeA(id) == new EntidadeA(id));
    }

    [Fact]
    public void Entidades_de_tipos_diferentes_com_mesmo_id_sao_diferentes()
    {
        var id = Guid.NewGuid();

        Assert.NotEqual<Entity>(new EntidadeA(id), new EntidadeB(id));
    }

    [Fact]
    public void Id_vazio_e_rejeitado()
    {
        Assert.Throws<ArgumentException>(() => new EntidadeA(Guid.Empty));
    }
}
