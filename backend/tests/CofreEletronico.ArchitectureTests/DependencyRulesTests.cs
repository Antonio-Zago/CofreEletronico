using System.Reflection;
using CofreEletronico.Domain.Common;
using NetArchTest.Rules;

namespace CofreEletronico.ArchitectureTests;

/// <summary>
/// Garante a regra de dependência da Clean Architecture:
/// Api → Infrastructure → Application → Domain (as dependências só apontam para dentro).
/// </summary>
public class DependencyRulesTests
{
    private const string DomainNs = "CofreEletronico.Domain";
    private const string ApplicationNs = "CofreEletronico.Application";
    private const string InfrastructureNs = "CofreEletronico.Infrastructure";
    private const string ApiNs = "CofreEletronico.Api";

    private static readonly Assembly DomainAssembly = typeof(Entity).Assembly;
    private static readonly Assembly ApplicationAssembly = typeof(Application.DependencyInjection).Assembly;
    private static readonly Assembly InfrastructureAssembly = typeof(Infrastructure.DependencyInjection).Assembly;

    [Fact]
    public void Domain_nao_depende_de_outras_camadas()
    {
        AssertSemDependencia(DomainAssembly, ApplicationNs, InfrastructureNs, ApiNs);
    }

    [Fact]
    public void Application_nao_depende_de_Infrastructure_nem_Api()
    {
        AssertSemDependencia(ApplicationAssembly, InfrastructureNs, ApiNs);
    }

    [Fact]
    public void Infrastructure_nao_depende_de_Api()
    {
        AssertSemDependencia(InfrastructureAssembly, ApiNs);
    }

    [Fact]
    public void Domain_nao_depende_de_frameworks_de_infraestrutura()
    {
        AssertSemDependencia(DomainAssembly,
            "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "MQTTnet", "Npgsql");
    }

    [Fact]
    public void Application_nao_depende_de_EF_Core_nem_Identity()
    {
        // Persistência e ASP.NET Identity (Usuario/Papel) ficam só na Infrastructure.
        AssertSemDependencia(ApplicationAssembly,
            "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore.Identity");
    }

    private static void AssertSemDependencia(Assembly assembly, params string[] proibidas)
    {
        var resultado = Types.InAssembly(assembly)
            .ShouldNot()
            .HaveDependencyOnAny(proibidas)
            .GetResult();

        var violacoes = resultado.FailingTypeNames ?? [];
        Assert.True(resultado.IsSuccessful,
            $"{assembly.GetName().Name} depende de [{string.Join(", ", proibidas)}]: {string.Join(", ", violacoes)}");
    }
}
