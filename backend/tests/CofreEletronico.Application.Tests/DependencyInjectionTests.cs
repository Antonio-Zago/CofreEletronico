using CofreEletronico.Application.Autenticacao;
using CofreEletronico.Application.Autenticacao.Portas;
using CofreEletronico.Application.Tests.Autenticacao;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CofreEletronico.Application.Tests;

public class DependencyInjectionTests
{
    [Fact]
    public void AddApplication_monta_o_container_sem_erros()
    {
        // As portas são implementadas na Infrastructure; aqui entram fakes.
        var services = new ServiceCollection().AddApplication();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<TimeProvider>(new RelogioFalso(DateTimeOffset.UnixEpoch));
        services.AddSingleton<IServicoIdentidade, ServicoIdentidadeFalso>();
        services.AddSingleton<IGeradorAccessToken>(sp => new GeradorAccessTokenFalso(sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton<IGeradorRefreshToken, GeradorRefreshTokenFalso>();
        services.AddSingleton<IRefreshTokenRepositorio, RefreshTokenRepositorioFalso>();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<RegistrarUsuario>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<AutenticarUsuario>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<RenovarTokens>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<EncerrarSessao>());
    }
}
