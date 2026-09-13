using NetArchTest.Rules;
using RigMD.Api.Controllers;
using RigMD.Application.Services;
using RigMD.Domain.Entities;
using RigMD.Infrastructure.Persistence;

namespace RigMD.Tests.Architecture;

public class LayerDependencyTests
{
    [Fact]
    public void DomainLayer_ShouldNot_HaveDependenciesOn_OtherLayers()
    {
        var result = Types.InAssembly(typeof(BaseEntity).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny("RigMD.Application", "RigMD.Infrastructure", "RigMD.Api")
            .GetResult();

        Assert.True(result.IsSuccessful, "Domain layer has an invalid dependency on an outer layer.");
    }

    [Fact]
    public void ApplicationLayer_ShouldNot_HaveDependenciesOn_InfrastructureOrApi()
    {
        var result = Types.InAssembly(typeof(AutomaticDiagnosisService).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny("RigMD.Infrastructure", "RigMD.Api")
            .GetResult();

        Assert.True(result.IsSuccessful, "Application layer has an invalid dependency on Infrastructure or API.");
    }

    [Fact]
    public void Controllers_ShouldNot_DirectlyReference_Infrastructure()
    {
        var result = Types.InAssembly(typeof(DiagnosticController).Assembly)
            .That().ResideInNamespace("RigMD.Api.Controllers")
            .ShouldNot()
            .HaveDependencyOn("RigMD.Infrastructure")
            .GetResult();

        Assert.True(result.IsSuccessful, "API Controllers should not directly reference Infrastructure types.");
    }

    [Fact]
    public void InfrastructureLayer_ShouldNot_HaveDependenciesOn_Api()
    {
        var result = Types.InAssembly(typeof(RigMdDbContext).Assembly)
            .ShouldNot()
            .HaveDependencyOn("RigMD.Api")
            .GetResult();

        Assert.True(result.IsSuccessful, "Infrastructure layer has an invalid dependency on API.");
    }
}
