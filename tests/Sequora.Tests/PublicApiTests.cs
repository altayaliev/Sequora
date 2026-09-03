using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;

namespace Sequora.Tests;

public sealed class PublicApiTests
{
    private static readonly Assembly Library = typeof(IJobQueue).Assembly;

    [Fact]
    public void CoreTypes_ArePublic()
    {
        Assert.True(typeof(IJobHandler<>).IsPublic);
        Assert.True(typeof(IJobQueue).IsPublic);
        Assert.True(typeof(ISequoraBuilder).IsPublic);
        Assert.True(typeof(SequoraOptions).IsPublic);
        Assert.True(typeof(EnqueueOptions).IsPublic);
        Assert.True(typeof(QueueFullBehavior).IsPublic);
        Assert.True(typeof(ShutdownBehavior).IsPublic);
        Assert.True(typeof(RetryBackoffStrategy).IsPublic);
        Assert.True(typeof(SequoraQueueFullException).IsPublic);
        Assert.True(typeof(SequoraStoppedException).IsPublic);
        Assert.True(typeof(SequoraHandlerNotFoundException).IsPublic);
        Assert.True(typeof(SequoraHandlerAlreadyRegisteredException).IsPublic);
        Assert.True(typeof(SequoraDuplicateJobException).IsPublic);
        Assert.True(typeof(SequoraServiceCollectionExtensions).IsPublic);
    }

    [Fact]
    public void PublicSurface_ContainsOnlyTheStableTypes()
    {
        string[] exported = [.. Library.GetExportedTypes()
            .Select(type => type.FullName)
            .Where(name => name is not null)
            .Select(name => name!)
            .OrderBy(name => name, StringComparer.Ordinal)];

        Assert.Equal(
            [
                "Sequora.EnqueueOptions",
                "Sequora.IJobHandler`1",
                "Sequora.IJobQueue",
                "Sequora.ISequoraBuilder",
                "Sequora.QueueFullBehavior",
                "Sequora.RetryBackoffStrategy",
                "Sequora.SequoraDuplicateJobException",
                "Sequora.SequoraHandlerAlreadyRegisteredException",
                "Sequora.SequoraHandlerNotFoundException",
                "Sequora.SequoraOptions",
                "Sequora.SequoraQueueFullException",
                "Sequora.SequoraServiceCollectionExtensions",
                "Sequora.SequoraStoppedException",
                "Sequora.ShutdownBehavior"
            ],
            exported);
    }

    [Fact]
    public void ExportedTypes_LiveInTheSequoraNamespace()
    {
        Assert.All(
            Library.GetExportedTypes(),
            type => Assert.Equal("Sequora", type.Namespace));
    }

    [Fact]
    public void PublicSurface_MatchesApprovedSnapshot()
    {
        string[] actual = [.. FormatPublicSurface(Library)];
        string approvedPath = Path.Combine(AppContext.BaseDirectory, "PublicApiSurface.txt");
        File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "PublicApiSurface.actual.txt"), actual);

        Assert.True(File.Exists(approvedPath), $"Missing approved public API snapshot at '{approvedPath}'.");
        string[] expected = [.. File.ReadAllLines(approvedPath)
            .Select(line => line.TrimEnd())
            .Where(line => line.Length > 0)];

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ImplementationTypes_AreNotPublic()
    {
        string[] exportedNames = [.. Library.GetExportedTypes()
            .Select(type => type.FullName)
            .Where(name => name is not null)
            .Select(name => name!)];

        Assert.DoesNotContain(exportedNames, name => name is not null && name.Contains("JobQueue", StringComparison.Ordinal) && name != typeof(IJobQueue).FullName);
        Assert.DoesNotContain("Sequora.Internal.JobQueue", exportedNames);
        Assert.DoesNotContain("Sequora.Internal.JobEnvelope", exportedNames);
        Assert.DoesNotContain("Sequora.Internal.SequoraBuilder", exportedNames);
        Assert.DoesNotContain("Sequora.Internal.JobWorker", exportedNames);
        Assert.DoesNotContain("Sequora.Internal.IRetryDelay", exportedNames);
        Assert.DoesNotContain("Sequora.Internal.TaskRetryDelay", exportedNames);
        Assert.DoesNotContain("Sequora.Internal.RetryDelayCalculator", exportedNames);
        Assert.DoesNotContain("Sequora.Internal.JobLifecycle", exportedNames);
        Assert.DoesNotContain("Sequora.Internal.JobLifecycleState", exportedNames);
        Assert.DoesNotContain("Sequora.Internal.JobIdTracker", exportedNames);
        Assert.DoesNotContain("Sequora.Internal.ReadyQueue", exportedNames);
        Assert.DoesNotContain("Sequora.Internal.JobSettingsResolver", exportedNames);
        Assert.DoesNotContain("Sequora.Internal.EffectiveJobSettings", exportedNames);
        Assert.DoesNotContain("Sequora.Internal.SequoraLog", exportedNames);
        Assert.DoesNotContain("Sequora.SequoraInfo", exportedNames);
    }

    [Fact]
    public void InternalsVisibleTo_IsLimitedToTheTestAssembly()
    {
        InternalsVisibleToAttribute[] attributes = [.. Library.GetCustomAttributes<InternalsVisibleToAttribute>()];
        Assert.Equal("Sequora.Tests", Assert.Single(attributes).AssemblyName);
    }

    [Fact]
    public void IJobQueue_DoesNotExposeDisposalOrChannels()
    {
        Assert.False(typeof(IDisposable).IsAssignableFrom(typeof(IJobQueue)));
        Assert.False(typeof(IAsyncDisposable).IsAssignableFrom(typeof(IJobQueue)));
    }

    [Fact]
    public void IJobHandler_IsContravariant()
    {
        Assert.True(typeof(IJobHandler<>).GetGenericArguments()[0].GenericParameterAttributes
            .HasFlag(GenericParameterAttributes.Contravariant));
    }

    [Fact]
    public void IJobQueue_DoesNotExposeChannels()
    {
        foreach (MethodInfo method in typeof(IJobQueue).GetMethods())
        {
            AssertDoesNotExposeChannel(method.ReturnType);

            foreach (ParameterInfo parameter in method.GetParameters())
            {
                AssertDoesNotExposeChannel(parameter.ParameterType);
            }
        }
    }

    [Fact]
    public void ExportedTypes_DoNotExposeChannels()
    {
        foreach (Type type in Library.GetExportedTypes())
        {
            foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                AssertDoesNotExposeChannel(method.ReturnType);

                foreach (ParameterInfo parameter in method.GetParameters())
                {
                    AssertDoesNotExposeChannel(parameter.ParameterType);
                }
            }
        }
    }

    [Fact]
    public void EnqueueOptions_DoNotExposeGlobalOnlySettings()
    {
        Type type = typeof(EnqueueOptions);

        Assert.Null(type.GetProperty(nameof(SequoraOptions.WorkerCount)));
        Assert.Null(type.GetProperty(nameof(SequoraOptions.Capacity)));
        Assert.Null(type.GetProperty(nameof(SequoraOptions.QueueFullBehavior)));
        Assert.Null(type.GetProperty(nameof(SequoraOptions.ShutdownBehavior)));
        Assert.NotNull(type.GetProperty(nameof(SequoraOptions.RetryCount)));
        Assert.NotNull(type.GetProperty(nameof(SequoraOptions.RetryDelay)));
        Assert.NotNull(type.GetProperty(nameof(SequoraOptions.MaxRetryDelay)));
        Assert.NotNull(type.GetProperty(nameof(SequoraOptions.RetryBackoff)));
        Assert.NotNull(type.GetProperty(nameof(EnqueueOptions.JobId)));
        Assert.NotNull(type.GetProperty(nameof(EnqueueOptions.Delay)));
        Assert.NotNull(type.GetProperty(nameof(EnqueueOptions.Priority)));
        Assert.NotNull(typeof(SequoraOptions).GetProperty(nameof(SequoraOptions.Priority)));
        Assert.Null(typeof(SequoraOptions).GetProperty(nameof(EnqueueOptions.JobId)));
        Assert.Null(typeof(SequoraOptions).GetProperty(nameof(EnqueueOptions.Delay)));
        Assert.Null(type.GetProperty(nameof(SequoraOptions.PriorityFairnessLimit)));
    }

    [Fact]
    public void ISequoraBuilder_ExposesConfigureAndHandlerRegistration()
    {
        Assert.NotNull(typeof(ISequoraBuilder).GetMethod(nameof(ISequoraBuilder.Configure)));
        Assert.Equal(2, typeof(ISequoraBuilder).GetMethods().Count(method => method.Name == nameof(ISequoraBuilder.AddHandler)));
    }

    [Fact]
    public void PublicApi_DoesNotExposeBackgroundService()
    {
        foreach (Type type in Library.GetExportedTypes())
        {
            Assert.False(
                typeof(BackgroundService).IsAssignableFrom(type),
                $"{type} exposes {nameof(BackgroundService)}.");
        }
    }

    [Fact]
    public void IJobQueue_EnqueueMethods_AreGenericAndAsync()
    {
        MethodInfo[] enqueueMethods = [.. typeof(IJobQueue)
            .GetMethods()
            .Where(method => method.Name == nameof(IJobQueue.EnqueueAsync))];

        Assert.Equal(2, enqueueMethods.Length);
        Assert.All(enqueueMethods, method =>
        {
            Assert.True(method.IsGenericMethodDefinition);
            Assert.Equal(typeof(Task), method.ReturnType);

            ParameterInfo token = method.GetParameters().Last();
            Assert.Equal(typeof(CancellationToken), token.ParameterType);
            Assert.True(token.HasDefaultValue);
        });
    }

    private static IEnumerable<string> FormatPublicSurface(Assembly assembly)
    {
        foreach (Type type in assembly.GetExportedTypes().OrderBy(item => item.FullName, StringComparer.Ordinal))
        {
            yield return "T:" + FormatTypeName(type);

            foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(item => item.Name != "value__")
                .OrderBy(item => item.Name, StringComparer.Ordinal))
            {
                yield return "F:" + FormatTypeName(type) + "." + field.Name;
            }

            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .OrderBy(item => item.Name, StringComparer.Ordinal))
            {
                yield return "P:" + FormatTypeName(type) + "." + property.Name;
            }

            foreach (ConstructorInfo constructor in type.GetConstructors(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .OrderBy(item => item.GetParameters().Length)
                .ThenBy(item => FormatParameters(item.GetParameters()), StringComparer.Ordinal))
            {
                yield return "M:" + FormatTypeName(type) + ".#ctor(" + FormatParameters(constructor.GetParameters()) + ")";
            }

            foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(item => !item.IsSpecialName)
                .OrderBy(item => item.Name, StringComparer.Ordinal)
                .ThenBy(item => item.GetParameters().Length)
                .ThenBy(item => FormatParameters(item.GetParameters()), StringComparer.Ordinal))
            {
                string arity = method.IsGenericMethodDefinition
                    ? "`" + method.GetGenericArguments().Length.ToString(CultureInfo.InvariantCulture)
                    : string.Empty;
                yield return "M:" + FormatTypeName(type) + "." + method.Name + arity + "(" + FormatParameters(method.GetParameters()) + ")";
            }
        }
    }

    private static string FormatTypeName(Type type)
    {
        if (type.IsGenericTypeParameter)
        {
            return type.Name;
        }

        if (type.IsByRef)
        {
            return FormatTypeName(type.GetElementType()!) + "&";
        }

        if (type.IsGenericType)
        {
            Type definition = type.GetGenericTypeDefinition();
            string name = definition.FullName ?? definition.Name;
            int tick = name.IndexOf('`');
            string prefix = tick >= 0 ? name[..tick] : name;
            return prefix + "<" + string.Join(",", type.GetGenericArguments().Select(FormatTypeName)) + ">";
        }

        return type.FullName ?? type.Name;
    }

    private static string FormatParameters(ParameterInfo[] parameters)
    {
        StringBuilder builder = new();
        for (int i = 0; i < parameters.Length; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            builder.Append(FormatTypeName(parameters[i].ParameterType));
        }

        return builder.ToString();
    }

    private static void AssertDoesNotExposeChannel(Type type)
    {
        Assert.False(
            IsChannelType(type),
            $"Public API unexpectedly exposes {type}.");
    }

    private static bool IsChannelType(Type type)
    {
        if (type.Namespace == typeof(Channel).Namespace)
        {
            return true;
        }

        if (type.IsGenericType)
        {
            return type.GetGenericArguments().Any(IsChannelType);
        }

        return false;
    }
}
