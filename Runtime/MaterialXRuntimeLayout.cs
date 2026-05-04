using System.Runtime.InteropServices;
using MaterialX;

namespace Engine;

/// <summary>
/// Locates the MaterialX standard-libraries directory and confirms that the
/// <c>MaterialXC</c> native shim is loadable on the current platform.
/// </summary>
/// <remarks>
/// <para>
/// The <c>MaterialX.Net</c> NuGet ships its data libraries under
/// <c>runtimes/any/native/libraries/</c> and its <see cref="LibrarySearch.GetDefaultLibrariesPath"/>
/// helper resolves them next to the host assembly (its build targets stage them as
/// <c>$(OutDir)libraries/</c>). Native shims (<c>libMaterialXC.so / .dylib / MaterialXC.dll</c>)
/// are deployed under <c>runtimes/&lt;rid&gt;/native/</c>.
/// </para>
/// <para>
/// In test runs (and on niche RIDs without a published native), the native lib may be
/// absent. <see cref="IsAvailable"/> probes for both - tests gate on it the same way
/// <c>UsdRuntimeLayout.IsAvailable</c> guards OpenUSD-dependent suites.
/// </para>
/// </remarks>
public static class MaterialXRuntimeLayout
{
    /// <summary>
    /// Returns the directory the MaterialX library should use for
    /// <see cref="MaterialX.Document.LoadStandardLibraries(string)"/>. Falls back to
    /// <see cref="LibrarySearch.GetDefaultLibrariesPath"/> when the runtime-asset
    /// resolver hasn't staged a side-by-side <c>libraries/</c> folder yet.
    /// </summary>
    public static string ResolveStandardLibrariesPath()
    {
        var sideBySide = Path.Combine(AppContext.BaseDirectory, "libraries");
        if (Directory.Exists(sideBySide)) return sideBySide;

        // The package's LibrarySearch helper computes the same thing internally; use it
        // as a graceful fallback so we still return *something* for callers that wish to
        // log the resolved path.
        try { return LibrarySearch.GetDefaultLibrariesPath() ?? sideBySide; }
        catch { return sideBySide; }
    }

    /// <summary>
    /// Returns <c>true</c> if a MaterialX standard-libraries tree appears reachable AND the
    /// native shim resolves on the current platform. Tests that initialize the native
    /// MaterialX runtime should skip when this returns <c>false</c>.
    /// </summary>
    public static bool IsAvailable()
    {
        if (!Directory.Exists(ResolveStandardLibrariesPath())) return false;

        try
        {
            // Cheap touch of a static helper - the SafeHandle world catches a missing
            // native lib by throwing DllNotFoundException on the first P/Invoke.
            _ = LibrarySearch.GetDefaultLibrariesPath();
            return true;
        }
        catch (DllNotFoundException) { return false; }
        catch (TypeInitializationException tie) when (tie.InnerException is DllNotFoundException)
        {
            return false;
        }
    }

    /// <summary>
    /// Returns the <c>runtimes/&lt;rid&gt;/native</c> sub-folder layout we expect (informational
    /// only; the .NET runtime asset resolver does the actual probing for the P/Invoke).
    /// </summary>
    public static string ExpectedNativeRid() => RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.X64 when RuntimeInformation.IsOSPlatform(OSPlatform.Windows) => "win-x64",
        Architecture.X64 when RuntimeInformation.IsOSPlatform(OSPlatform.Linux)   => "linux-x64",
        Architecture.X64 when RuntimeInformation.IsOSPlatform(OSPlatform.OSX)     => "osx-x64",
        Architecture.Arm64 when RuntimeInformation.IsOSPlatform(OSPlatform.OSX)   => "osx-arm64",
        Architecture.Arm64 when RuntimeInformation.IsOSPlatform(OSPlatform.Linux) => "linux-arm64",
        _ => "unknown",
    };
}