using FluentAssertions;
using MaterialX;
using Xunit;
using Xunit.Abstractions;

namespace Engine.Tests.Materials.X;

/// <summary>
/// Spike tests that exercise the <c>MaterialX.Net</c> 0.1.x binding surface end-to-end on
/// the actual native runtime, mirroring <c>UsdReaderSpikeTests</c>. Their purpose is NOT
/// to assert engine behaviour - it is to lock in, with one failing-fast assertion per
/// API the engine relies on, every entry point the production
/// <see cref="MaterialXLoader"/> / <see cref="MaterialXMaterialReader"/> depend on:
/// document creation, standard-libraries load, XML parse from string, top-level child
/// enumeration via <see cref="Document.Children"/> and renderable enumeration via
/// <see cref="Document.Renderables"/>, the <see cref="Element.AsNode"/> downcast,
/// and the standard-libraries search-path resolver.
/// </summary>
[Trait("Category", "Spike")]
[Trait("Backend", "MaterialX")]
[Collection(MaterialXTestCollection.Name)]
public sealed class MaterialXSpikeTests
{
    private readonly ITestOutputHelper _output;
    private readonly bool _ready;

    public MaterialXSpikeTests(ITestOutputHelper output)
    {
        _output = output;
        _ready = MaterialXRuntimeLayout.IsAvailable();
    }

    [Fact]
    public void Spike01_StandardLibraries_Path_Is_Reachable()
    {
        if (!_ready) SkipTest.With("MaterialX native shim or libraries not reachable.");

        var libs = MaterialXRuntimeLayout.ResolveStandardLibrariesPath();
        _output.WriteLine($"[mtlx] libraries='{libs}'");
        Directory.Exists(libs).Should().BeTrue("MaterialX.Net.targets stages runtimes/any/native/libraries -> $(OutDir)libraries");

        // The library packs at minimum the BXDF standard-surface definition.
        File.Exists(Path.Combine(libs, "bxdf", "standard_surface.mtlx"))
            .Should().BeTrue("standard_surface.mtlx is the canonical surface shader fixture relies on");
    }

    [Fact]
    public void Spike02_Document_Create_And_LoadStandardLibraries_Roundtrips()
    {
        if (!_ready) SkipTest.With("MaterialX native shim or libraries not reachable.");

        using var doc = Document.Create();
        var libs = MaterialXRuntimeLayout.ResolveStandardLibrariesPath();
        var act = () => doc.LoadStandardLibraries(libs);
        act.Should().NotThrow("the standard library path returned by the layout helper must be loadable by the native runtime");
    }

    [Fact]
    public void Spike03_ReadFromXmlString_Parses_StandardSurface_And_Enumerates_Children()
    {
        if (!_ready) SkipTest.With("MaterialX native shim or libraries not reachable.");

        const string xml = """
            <?xml version="1.0"?>
            <materialx version="1.39">
              <standard_surface name="SR_x" type="surfaceshader">
                <input name="base_color" type="color3" value="0.5, 0.25, 0.125" />
              </standard_surface>
              <surfacematerial name="M_x" type="material">
                <input name="surfaceshader" type="surfaceshader" nodename="SR_x" />
              </surfacematerial>
            </materialx>
            """;

        using var doc = Document.ReadFromXmlString(xml, MaterialXRuntimeLayout.ResolveStandardLibrariesPath());

        var children = doc.Children.ToList();
        foreach (var c in children)
            _output.WriteLine($"  child name='{c.Name}' category='{c.Category}'");

        children.Should().Contain(c => c.Name == "SR_x" && c.Category == "standard_surface");
        children.Should().Contain(c => c.Name == "M_x" && c.Category == "surfacematerial");

        // Renderables must surface the surfacematerial.
        doc.Renderables.Should().Contain(r => r.Name == "M_x");
    }

    [Fact]
    public void Spike04_Element_AsNode_Returns_Typed_Node_For_Shader_Categories()
    {
        if (!_ready) SkipTest.With("MaterialX native shim or libraries not reachable.");

        const string xml = """
            <?xml version="1.0"?>
            <materialx version="1.39">
              <standard_surface name="SR_x" type="surfaceshader" />
            </materialx>
            """;
        using var doc = Document.ReadFromXmlString(xml, MaterialXRuntimeLayout.ResolveStandardLibrariesPath());
        var element = doc.Children.First(c => c.Name == "SR_x");
        var node = element.AsNode();
        node.Should().NotBeNull("standard_surface elements expose themselves as Node via AsNode()");
        node!.Type.Should().Be("surfaceshader");
        node.Category.Should().Be("standard_surface");
    }
}

