using System.Numerics;
using FluentAssertions;
using Xunit;

namespace Engine.Tests.Materials.X;

/// <summary>
/// Material-extraction tests for <see cref="MaterialXMaterialReader"/>: round-trips the
/// authored standard_surface inputs from the bundled MTLX fixture (and an inline
/// gltf_pbr / open_pbr_surface payload) onto <see cref="SceneMaterialPayload"/> factors,
/// matching the engine-neutral payload shape the spawn system already consumes from the
/// USD reader.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Backend", "MaterialX")]
[Collection(MaterialXTestCollection.Name)]
public sealed class MaterialXMaterialReaderTests
{
    private static string FixturePath(string name) => 
        Path.Combine(AppContext.BaseDirectory, "source", "tests", "fixtures", name);

    [Fact]
    public void Reader_Extracts_StandardSurface_Factors_From_Fixture()
    {
        var fx = FixturePath("standard_surface_red.mtlx");
        if (!File.Exists(fx)) SkipTest.With($"fixture not staged at {fx}");

        var xml = File.ReadAllText(fx);
        var payloads = MaterialXMaterialReader.Extract(xml, sourcePath: "tests/fixtures/standard_surface_red.mtlx");

        payloads.Should().HaveCount(1);
        var mat = payloads[0];
        mat.Name.Should().Be("M_red");
        mat.SourcePath.Should().Be("tests/fixtures/standard_surface_red.mtlx");
        mat.BaseColorFactor.Should().Be(new Vector4(1f, 0f, 0f, 1f));
        mat.MetallicFactor.Should().BeApproximately(0.2f, 1e-5f);
        mat.RoughnessFactor.Should().BeApproximately(0.7f, 1e-5f);
        mat.EmissiveFactor.Should().Be(new Vector3(0f, 1f, 0f));
    }

    [Fact]
    public void Reader_Extracts_GltfPbr_Inline_Authoring()
    {
        const string xml = """
            <?xml version="1.0"?>
            <materialx version="1.39">
              <gltf_pbr name="SR_g" type="surfaceshader">
                <input name="base_color" type="color4" value="0.1, 0.2, 0.3, 0.5" />
                <input name="metallic" type="float" value="0.9" />
                <input name="roughness" type="float" value="0.4" />
                <input name="emissive" type="color3" value="0.7, 0.8, 0.9" />
              </gltf_pbr>
              <surfacematerial name="M_g" type="material">
                <input name="surfaceshader" type="surfaceshader" nodename="SR_g" />
              </surfacematerial>
            </materialx>
            """;

        var payloads = MaterialXMaterialReader.Extract(xml);
        payloads.Should().HaveCount(1);
        var mat = payloads[0];
        mat.BaseColorFactor.Should().Be(new Vector4(0.1f, 0.2f, 0.3f, 0.5f));
        mat.MetallicFactor.Should().BeApproximately(0.9f, 1e-5f);
        mat.RoughnessFactor.Should().BeApproximately(0.4f, 1e-5f);
        mat.EmissiveFactor.Should().Be(new Vector3(0.7f, 0.8f, 0.9f));
    }

    [Fact]
    public void Reader_Synthesises_Material_When_No_SurfaceMaterial_Wraps_Shader()
    {
        const string xml = """
            <?xml version="1.0"?>
            <materialx version="1.39">
              <standard_surface name="SR_solo" type="surfaceshader">
                <input name="base_color" type="color3" value="0.25, 0.5, 0.75" />
              </standard_surface>
            </materialx>
            """;

        var payloads = MaterialXMaterialReader.Extract(xml);
        payloads.Should().HaveCount(1);
        payloads[0].Name.Should().Be("SR_solo");
        payloads[0].BaseColorFactor.Should().Be(new Vector4(0.25f, 0.5f, 0.75f, 1f));
    }

    [Fact]
    public void Reader_Returns_Empty_For_Document_Without_Recognised_Shaders()
    {
        const string xml = """
            <?xml version="1.0"?>
            <materialx version="1.39">
              <nodegraph name="NG_decor"/>
            </materialx>
            """;

        MaterialXMaterialReader.Extract(xml).Should().BeEmpty();
    }

    [Fact]
    public async Task Reader_ExtractFirst_Returns_First_Material()
    {
        if (!MaterialXRuntimeLayout.IsAvailable())
            SkipTest.With("MaterialX native shim not reachable on this RID.");

        var fx = FixturePath("standard_surface_red.mtlx");
        if (!File.Exists(fx)) SkipTest.With($"fixture not staged at {fx}");

        var loader = new MaterialXLoader();
        var bytes = File.ReadAllBytes(fx);
        using var ctx = new AssetLoadContext(new MemoryStream(bytes), new AssetPath("tests/fixtures/standard_surface_red.mtlx"), _ => default);
        var result = await loader.LoadAsync(ctx, CancellationToken.None);
        result.Success.Should().BeTrue();
        using var asset = result.Asset!;

        var first = MaterialXMaterialReader.ExtractFirst(asset);
        first.Should().NotBeNull();
        first!.BaseColorFactor.Should().Be(new Vector4(1f, 0f, 0f, 1f));
    }
}