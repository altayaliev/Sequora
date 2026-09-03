using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Sequora.Tests;

public sealed class SmokeTests
{
    [Fact]
    public void Assembly_IsNamedSequora()
    {
        string? name = typeof(IJobQueue).Assembly.GetName().Name;

        Assert.Equal("Sequora", name);
    }

    [Fact]
    public void Assembly_HasStableVersionMetadata()
    {
        Assembly assembly = typeof(IJobQueue).Assembly;
        AssemblyName assemblyName = assembly.GetName();

        Assert.Equal(new Version(1, 0, 0, 0), assemblyName.Version);

        string? informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        Assert.NotNull(informational);
        Assert.StartsWith("1.0.0", informational, StringComparison.Ordinal);
    }

    [Fact]
    public void Assembly_DescriptionDoesNotLeakImplementation()
    {
        string? description = typeof(IJobQueue).Assembly
            .GetCustomAttribute<AssemblyDescriptionAttribute>()
            ?.Description;

        Assert.False(string.IsNullOrWhiteSpace(description));
        Assert.DoesNotContain("Channel", description, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("BackgroundService", description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Assembly_TargetsASupportedFramework()
    {
        TargetFrameworkAttribute? attribute = typeof(IJobQueue).Assembly
            .GetCustomAttribute<TargetFrameworkAttribute>();

        Assert.NotNull(attribute);
        Assert.False(string.IsNullOrWhiteSpace(attribute.FrameworkName));

        string[] supported =
        [
            ".NETCoreApp,Version=v8.0",
            ".NETCoreApp,Version=v9.0",
            ".NETCoreApp,Version=v10.0"
        ];

        Assert.Contains(
            supported,
            tfm => attribute.FrameworkName.Contains(tfm, StringComparison.Ordinal));
    }

    [Fact]
    public void HostingAbstractions_AreAvailableToTheLibrary()
    {
        Assert.NotNull(typeof(IHostedService));
        Assert.NotNull(typeof(IServiceCollection));
        Assert.NotNull(typeof(ILogger));
    }
}
