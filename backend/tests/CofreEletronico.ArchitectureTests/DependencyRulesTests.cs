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

    [Fact]
    public void Application_nao_depende_de_bibliotecas_de_JWT_nem_ASP_NET()
    {
        // Emissão/validação de JWT é detalhe da Infrastructure/Api; a Application só conhece IGeradorAccessToken.
        AssertSemDependencia(ApplicationAssembly,
            "Microsoft.IdentityModel", "System.IdentityModel.Tokens.Jwt", "Microsoft.AspNetCore");
    }

    [Fact]
    public void Application_nao_depende_de_MQTTnet()
    {
        // A Application só conhece a porta IClienteMqtt; o MQTTnet fica atrás do adaptador da Infrastructure.
        AssertSemDependencia(ApplicationAssembly, "MQTTnet");
    }

    [Fact]
    public void DTOs_da_Application_nao_expoem_tipos_do_Domain()
    {
        // "Nunca devolva entidade de domínio em endpoint": os DTOs (o que a Api serializa) só carregam tipos simples
        // ou outros DTOs. Uma propriedade com entidade (ou coleção de entidades) vazaria campos internos/segredos.
        var dtos = ApplicationAssembly.GetTypes()
            .Where(t => t.IsPublic && t.Name.Split('`')[0].EndsWith("Dto", StringComparison.Ordinal))
            .ToList();
        Assert.NotEmpty(dtos);

        var violacoes = dtos
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => UsaTipoDoDomain(p.PropertyType))
                .Select(p => $"{t.Name}.{p.Name}"))
            .ToList();

        Assert.True(violacoes.Count == 0, "DTOs com tipos do Domain: " + string.Join(", ", violacoes));
    }

    private static bool UsaTipoDoDomain(Type tipo) =>
        (tipo.Assembly == DomainAssembly && !tipo.IsEnum) || // enums (ex.: status) podem ser serializados
        (tipo.IsArray && UsaTipoDoDomain(tipo.GetElementType()!)) ||
        (tipo.IsGenericType && tipo.GetGenericArguments().Any(UsaTipoDoDomain));

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
