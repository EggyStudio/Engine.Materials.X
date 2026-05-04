using MaterialX;

namespace Engine;

/// <summary>
/// Plugin that brings up the MaterialX backend. Probes the native shim, registers
/// <see cref="MaterialXLoader"/> with the <see cref="AssetServer"/>, and inserts
/// <see cref="MaterialXRuntimeHandle"/> as a marker resource so other systems can
/// declare an ordering dependency on the runtime.
/// </summary>
/// <remarks>
/// <para>
/// <b>Order:</b> add <i>after</i> <see cref="AssetPlugin"/>; <see cref="DefaultPlugins"/>
/// already wires this up. Standalone consumers can opt in:
/// <code>
/// app.AddPlugin(new AssetPlugin())
///    .AddPlugin(new MaterialXPlugin());
/// </code>
/// </para>
/// <para>
/// <b>Native availability:</b> if <see cref="MaterialXRuntimeLayout.IsAvailable"/> reports
/// the native shim isn't reachable on this RID (e.g. <c>linux-arm64</c> in 0.1.x), the
/// plugin still inserts the marker resource and registers the loader, but logs a warning
/// so consumers can react. The first actual <c>.mtlx</c> load is what raises
/// <see cref="DllNotFoundException"/> in that case - mirroring how the rest of the engine
/// handles optional native backends.
/// </para>
/// </remarks>
/// <seealso cref="MaterialXLoader"/>
/// <seealso cref="MaterialXMaterialReader"/>
public sealed class MaterialXPlugin : IPlugin
{
    private static readonly ILogger Logger = Log.Category("Engine.Materials.X");

    /// <inheritdoc />
    public void Build(App app)
    {
        Logger.Info("MaterialXPlugin: Initializing MaterialX backend...");

        if (app.World.ContainsResource<MaterialXRuntimeHandle>())
        {
            Logger.Debug("MaterialXPlugin: MaterialXRuntimeHandle already present; skipping re-initialization.");
            return;
        }

        var librariesPath = MaterialXRuntimeLayout.ResolveStandardLibrariesPath();
        bool nativeReachable = MaterialXRuntimeLayout.IsAvailable();

        if (!nativeReachable)
        {
            Logger.Warn($"MaterialXPlugin: native MaterialXC shim not reachable for RID '{MaterialXRuntimeLayout.ExpectedNativeRid()}' (libraries='{librariesPath}'). " +
                        ".mtlx loads will fail until the runtime is published.");
        }
        else
        {
            Logger.Info($"MaterialXPlugin: standard libraries at '{librariesPath}'.");
        }

        app.World.InsertResource(new MaterialXRuntimeHandle
        {
            StandardLibrariesPath = librariesPath,
            NativeAvailable = nativeReachable,
        });

        if (app.World.TryGetResource<AssetServer>(out var server))
        {
            server.RegisterLoader(new MaterialXLoader());
            Logger.Debug("MaterialXPlugin: MaterialXLoader registered with AssetServer.");
        }
        else
        {
            Logger.Warn("MaterialXPlugin: AssetServer not found - MaterialXLoader was NOT registered. Add AssetPlugin first.");
        }

        Logger.Info("MaterialXPlugin: MaterialX backend ready.");
    }
}

/// <summary>
/// Marker resource indicating that the MaterialX backend has been initialized by
/// <see cref="MaterialXPlugin"/>. Systems that touch <see cref="Document"/> or related
/// types should declare a <c>Read&lt;MaterialXRuntimeHandle&gt;()</c> dependency on their
/// <see cref="SystemDescriptor"/> so the parallel scheduler sees the order constraint.
/// </summary>
public sealed class MaterialXRuntimeHandle
{
    /// <summary>Resolved path to the MaterialX standard-libraries tree.</summary>
    public required string StandardLibrariesPath { get; init; }

    /// <summary>Whether the native MaterialXC shim was reachable at plugin-build time.</summary>
    public required bool NativeAvailable { get; init; }
}