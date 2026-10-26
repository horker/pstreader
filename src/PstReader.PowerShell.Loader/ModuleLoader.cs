using System.Reflection;
using System.Runtime.Loader;

namespace PstReader.PowerShell;

/// <summary>Keeps parser dependencies separate from the assemblies shipped with PowerShell 7.</summary>
public static class ModuleLoader
{
    private static readonly object Gate = new();
    private static Assembly? parserAssembly;

    public static void Load(string directory)
    {
        lock (Gate)
        {
            // PowerShell assemblies stay loaded after Remove-Module. Reuse their type identity.
            if (parserAssembly is not null) return;
            var context = new ReaderLoadContext(Path.GetFullPath(directory));
            parserAssembly = context.LoadFromAssemblyPath(Path.Combine(directory, "XstReader.Base.dll"));
            // The public Core API loads normally so its model types are visible to PowerShell.
            // Only its parser dependency crosses into the private context.
            AssemblyLoadContext.Default.Resolving += (_, name) =>
                name.Name == "XstReader.Base" ? parserAssembly : null;
        }
    }

    private sealed class ReaderLoadContext(string directory) : AssemblyLoadContext
    {
        protected override Assembly? Load(AssemblyName name)
        {
            var path = Path.Combine(directory, name.Name + ".dll");
            return File.Exists(path) ? LoadFromAssemblyPath(path) : null;
        }
    }
}
