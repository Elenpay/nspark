using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace NSpark.Signer;

/// <summary>
/// Locates <c>libspark_frost</c> under <c>runtimes/{rid}/native/</c> next to the application.
/// </summary>
/// <remarks>
/// A NuGet consumer resolves the library through <c>deps.json</c>. A consumer that references
/// NSpark as a project (for example from a git submodule) gets the same <c>runtimes/</c> tree
/// copied to its output but no <c>deps.json</c> entry for it, so default probing misses it.
/// This resolver covers that case and otherwise defers to default probing.
/// </remarks>
internal static class NativeLibraryResolver
{
    internal const string LibraryName = "spark_frost";

    [ModuleInitializer]
    [SuppressMessage("Usage", "CA2255:The 'ModuleInitializer' attribute should not be used in libraries",
        Justification = "The resolver must be in place before the first P/Invoke into spark_frost, from any entry point.")]
    internal static void Register() =>
        NativeLibrary.SetDllImportResolver(typeof(NativeLibraryResolver).Assembly, Resolve);

    private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!string.Equals(libraryName, LibraryName, StringComparison.Ordinal))
        {
            return IntPtr.Zero;
        }

        foreach (var candidate in Candidates())
        {
            if (File.Exists(candidate) && NativeLibrary.TryLoad(candidate, out var handle))
            {
                return handle;
            }
        }

        return IntPtr.Zero;
    }

    internal static IEnumerable<string> Candidates()
    {
        var fileName = OperatingSystem.IsWindows() ? "spark_frost.dll"
            : OperatingSystem.IsMacOS() ? "libspark_frost.dylib"
            : "libspark_frost.so";

        // The runtime's RID first; a distro-built (non-portable) runtime reports e.g.
        // "ubuntu.24.04-x64", so fall back to the portable RID for this OS and architecture.
        foreach (var rid in new[] { RuntimeInformation.RuntimeIdentifier, PortableRid() }.Distinct(StringComparer.Ordinal))
        {
            yield return Path.Combine(AppContext.BaseDirectory, "runtimes", rid, "native", fileName);
        }
    }

    private static string PortableRid()
    {
        var os = OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux";
        var arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.Arm64 => "arm64",
            Architecture.X86 => "x86",
            _ => "x64",
        };
        return $"{os}-{arch}";
    }
}
