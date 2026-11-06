using System.Reflection;

namespace FleetFlow.UnitTests;

public sealed class ArchitectureTests
{
    [Theory]
    [InlineData("FleetFlow.Domain", "")]
    [InlineData("FleetFlow.Application", "FleetFlow.Domain")]
    [InlineData("FleetFlow.Infrastructure", "FleetFlow.Domain,FleetFlow.Application")]
    public void Layers_do_not_reference_outer_application_layers(string assemblyName, string allowedReferences)
    {
        var allowed = allowedReferences.Split(',', StringSplitOptions.RemoveEmptyEntries);
        var references = Assembly.Load(assemblyName).GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .Where(name => name.StartsWith("FleetFlow.", StringComparison.Ordinal));

        Assert.All(references, reference => Assert.Contains(reference, allowed));
    }

    [Theory]
    [InlineData("FleetFlow.Domain")]
    [InlineData("FleetFlow.Application")]
    public void Inner_layers_do_not_depend_on_ef_or_aspnet(string assemblyName)
    {
        var references = Assembly.Load(assemblyName).GetReferencedAssemblies();
        Assert.DoesNotContain(references, reference =>
            reference.Name!.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)
            || reference.Name.StartsWith("Npgsql", StringComparison.Ordinal)
            || reference.Name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));
    }
}
