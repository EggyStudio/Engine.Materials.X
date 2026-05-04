using FluentAssertions;
using MaterialX;
using Xunit;

namespace Engine.Tests.Materials.X;

/// <summary>
/// Plugin wiring tests for <see cref="MaterialXPlugin"/>: verify that the plugin inserts
/// its <see cref="MaterialXRuntimeHandle"/> marker resource, registers
/// <see cref="MaterialXLoader"/> with the <see cref="AssetServer"/>, and is idempotent
/// when added more than once. Mirrors <c>UsdScenesPluginTests</c>.
/// </summary>
[Trait("Category", "Unit")]
[Trait("Backend", "MaterialX")]
[Collection(MaterialXTestCollection.Name)]
public sealed class MaterialXPluginTests
{
    [Fact]
    public void MaterialXPlugin_Inserts_MaterialXRuntimeHandle_Resource()
    {
        using var app = new App();
        app.AddPlugin(new MaterialXPlugin());

        app.World.ContainsResource<MaterialXRuntimeHandle>().Should().BeTrue();
        var handle = app.World.Resource<MaterialXRuntimeHandle>();
        handle.StandardLibrariesPath.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void MaterialXPlugin_Registers_MaterialXLoader_With_AssetServer()
    {
        using var app = new App();
        app.AddPlugin(new AssetPlugin());

        var server = app.World.Resource<AssetServer>();
        int before = server.LoaderCount;

        app.AddPlugin(new MaterialXPlugin());

        // MaterialXLoader registers exactly one extension (.mtlx).
        server.LoaderCount.Should().Be(before + 1);
    }

    [Fact]
    public void MaterialXPlugin_Is_Idempotent_When_Added_Twice()
    {
        using var app = new App();
        app.AddPlugin(new AssetPlugin());

        app.AddPlugin(new MaterialXPlugin());
        var firstHandle = app.World.Resource<MaterialXRuntimeHandle>();

        // Re-adding must not throw or wipe the marker.
        app.AddPlugin(new MaterialXPlugin());
        app.World.Resource<MaterialXRuntimeHandle>().Should().BeSameAs(firstHandle);
    }

    [Fact]
    public void MaterialXPlugin_Reports_Native_Availability_Matches_Layout_Probe()
    {
        using var app = new App();
        app.AddPlugin(new MaterialXPlugin());
        var handle = app.World.Resource<MaterialXRuntimeHandle>();

        handle.NativeAvailable.Should().Be(MaterialXRuntimeLayout.IsAvailable(),
            "the plugin's reported availability must match the runtime layout probe");
    }
}