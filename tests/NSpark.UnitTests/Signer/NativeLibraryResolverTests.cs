using System.Runtime.InteropServices;
using NSpark.Signer;

namespace NSpark.UnitTests.Signer;

/// <summary>
/// Tests for <see cref="NativeLibraryResolver"/>, which lets project-reference consumers load
/// <c>libspark_frost</c> from <c>runtimes/{rid}/native/</c> without a deps.json entry.
/// </summary>
public class NativeLibraryResolverTests
{
    [Test]
    public void Candidates_LookUnderTheRuntimesFolderNextToTheApp()
    {
        var candidates = NativeLibraryResolver.Candidates().ToList();

        candidates.Should().NotBeEmpty();
        candidates.Should().OnlyContain(path =>
            path.StartsWith(Path.Combine(AppContext.BaseDirectory, "runtimes"), StringComparison.Ordinal) &&
            Path.GetFileName(Path.GetDirectoryName(path)) == "native");
        candidates.Should().Contain(path => path.Contains(RuntimeInformation.RuntimeIdentifier, StringComparison.Ordinal));
    }

    [Test]
    public void Candidates_UseThePlatformLibraryFileName()
    {
        var expected = OperatingSystem.IsWindows() ? "spark_frost.dll"
            : OperatingSystem.IsMacOS() ? "libspark_frost.dylib"
            : "libspark_frost.so";

        NativeLibraryResolver.Candidates().Should().OnlyContain(path => Path.GetFileName(path) == expected);
    }
}
