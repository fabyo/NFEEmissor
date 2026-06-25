using System.Reflection;

namespace Nfe.Core;

public static class NfeVersion
{
    public static string Current { get; } =
        typeof(NfeVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion
            .Split('+')[0]
        ?? typeof(NfeVersion).Assembly.GetName().Version?.ToString(3)
        ?? "0.0.0";
}
