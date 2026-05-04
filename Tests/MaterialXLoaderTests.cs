using FluentAssertions;
using Xunit;

namespace Engine.Tests.Materials.X;

/// <summary>
/// Integration tests for <see cref="MaterialXLoader"/>: loads the staged MTLX fixture
/// through the same <see cref="AssetLoadContext"/> shape the <see cref="AssetServer"/>
/// uses, asserts a parsed <see cref="MaterialXDocumentAsset"/> comes back, and verifies
/// that the source XML and resolved standard-libraries path round-trip onto the asset.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Backend", "MaterialX")]
[Collection(MaterialXTestCollection.Name)]
public sealed class MaterialXLoaderTests
{
    private static string FixturePath(string name) => 
        Path.Combine(AppContext.BaseDirectory, "source", "tests", "fixtures", name);

    private static AssetLoadContext OpenFixture(string name)
    {
        var bytes = File.ReadAllBytes(FixturePath(name));
        return new AssetLoadContext(new MemoryStream(bytes), new AssetPath($"tests/fixtures/{name}"), _ => default);
    }

    [Fact]
    public async Task Loader_Parses_StandardSurface_Fixture_Into_DocumentAsset()
    {
        if (!MaterialXRuntimeLayout.IsAvailable())
            SkipTest.With("MaterialX native shim not reachable on this RID.");
        var fx = FixturePath("standard_surface_red.mtlx");
        if (!File.Exists(fx)) SkipTest.With($"fixture not staged at {fx}");

        var loader = new MaterialXLoader();
        using var ctx = OpenFixture("standard_surface_red.mtlx");
        var result = await loader.LoadAsync(ctx, CancellationToken.None);

        result.Success.Should().BeTrue($"loader should accept the bundled fixture (error: {result.Error})");
        var asset = result.Asset!;
        asset.Document.Should().NotBeNull();
        asset.Xml.Should().Contain("standard_surface").And.Contain("base_color");
        asset.SearchPath.Should().NotBeNullOrEmpty();
        asset.SourcePath.Should().Contain("standard_surface_red.mtlx");

        // The document must expose the two top-level elements the fixture authored.
        asset.Document.Children.Should().Contain(c => c.Name == "SR_red" && c.Category == "standard_surface");
        asset.Document.Children.Should().Contain(c => c.Name == "M_red"  && c.Category == "surfacematerial");
        asset.Document.Renderables.Should().Contain(r => r.Name == "M_red");

        asset.Dispose();
    }

    [Fact]
    public async Task Loader_Returns_Failure_For_Malformed_Xml()
    {
        if (!MaterialXRuntimeLayout.IsAvailable())
            SkipTest.With("MaterialX native shim not reachable on this RID.");

        var loader = new MaterialXLoader();
        var bytes = System.Text.Encoding.UTF8.GetBytes("<not_materialx><");
        using var ctx = new AssetLoadContext(new MemoryStream(bytes), new AssetPath("tests/garbage.mtlx"), _ => default);

        var result = await loader.LoadAsync(ctx, CancellationToken.None);
        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Loader_Advertises_Mtlx_Extension()
    {
        new MaterialXLoader().Extensions.Should().BeEquivalentTo(new[] { ".mtlx" });
    }
}