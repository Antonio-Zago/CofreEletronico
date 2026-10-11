using Microsoft.Extensions.DependencyInjection;

namespace CofreEletronico.Application.Tests;

public class DependencyInjectionTests
{
    [Fact]
    public void AddApplication_monta_o_container_sem_erros()
    {
        var services = new ServiceCollection().AddApplication();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });

        Assert.NotNull(provider);
    }
}
