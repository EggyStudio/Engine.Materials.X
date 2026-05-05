using FluentAssertions;
using Xunit;

namespace Engine.Tests.Materials.X;

/// <summary>
/// XML-walk tests for the texture-extraction path in <see cref="MaterialXMaterialReader"/>.
/// Each fixture connects an <c>image</c> or <c>tiledimage</c> node to a recognised PBR
/// surface input and asserts the resulting <see cref="SceneTextureRef"/> lands on the
/// matching <see cref="SceneMaterialPayload"/> slot.
/// </summary>
[Trait("Category", "Unit")]
public class MaterialXTextureExtractionTests
{
    [Fact]
    public void StandardSurface_BaseColor_Image_Becomes_BaseColorTexture()
    {
        const string Mtlx = """
            <?xml version="1.0"?>
            <materialx version="1.39">
              <image name="tex_baseColor" type="color3">
                <input name="file" type="filename" value="textures/brass_basecolor.png" />
                <input name="uaddressmode" type="string" value="clamp" />
                <input name="vaddressmode" type="string" value="mirror" />
              </image>
              <standard_surface name="SR1" type="surfaceshader">
                <input name="base_color" type="color3" nodename="tex_baseColor" />
              </standard_surface>
              <surfacematerial name="M1" type="material">
                <input name="surfaceshader" type="surfaceshader" nodename="SR1" />
              </surfacematerial>
            </materialx>
            """;

        var payload = MaterialXMaterialReader.Extract(Mtlx).Single();

        payload.BaseColorTexture.Should().NotBeNull();
        payload.BaseColorTexture!.AssetPath.Should().Be("textures/brass_basecolor.png");
        payload.BaseColorTexture.WrapS.Should().Be(SceneWrapMode.Clamp);
        payload.BaseColorTexture.WrapT.Should().Be(SceneWrapMode.Mirror);
    }

    [Fact]
    public void StandardSurface_Tiledimage_Routed_Same_As_Image()
    {
        const string Mtlx = """
            <?xml version="1.0"?>
            <materialx version="1.39">
              <tiledimage name="t" type="color3">
                <input name="file" type="filename" value="t.png" />
              </tiledimage>
              <standard_surface name="SR1" type="surfaceshader">
                <input name="base_color" type="color3" nodename="t" />
              </standard_surface>
            </materialx>
            """;

        var payload = MaterialXMaterialReader.Extract(Mtlx).Single();

        payload.BaseColorTexture!.AssetPath.Should().Be("t.png");
    }

    [Fact]
    public void StandardSurface_Routes_Each_Pbr_Slot_To_Distinct_Texture()
    {
        const string Mtlx = """
            <?xml version="1.0"?>
            <materialx version="1.39">
              <image name="bc" type="color3"><input name="file" type="filename" value="bc.png"/></image>
              <image name="m"  type="float"><input name="file" type="filename" value="m.png"/></image>
              <image name="n"  type="vector3"><input name="file" type="filename" value="n.png"/></image>
              <image name="e"  type="color3"><input name="file" type="filename" value="e.png"/></image>
              <image name="o"  type="float"><input name="file" type="filename" value="o.png"/></image>
              <standard_surface name="SR1" type="surfaceshader">
                <input name="base_color"         type="color3" nodename="bc"/>
                <input name="metalness"          type="float"  nodename="m"/>
                <input name="normal"             type="vector3" nodename="n"/>
                <input name="emission_color"     type="color3" nodename="e"/>
                <input name="occlusion"          type="float"  nodename="o"/>
              </standard_surface>
            </materialx>
            """;

        var p = MaterialXMaterialReader.Extract(Mtlx).Single();

        p.BaseColorTexture!.AssetPath.Should().Be("bc.png");
        p.MetallicRoughnessTexture!.AssetPath.Should().Be("m.png");
        p.NormalTexture!.AssetPath.Should().Be("n.png");
        p.EmissiveTexture!.AssetPath.Should().Be("e.png");
        p.OcclusionTexture!.AssetPath.Should().Be("o.png");
    }

    [Fact]
    public void GltfPbr_Metallic_And_Roughness_Sharing_Texture_Collapse_To_Single_MR_Ref()
    {
        const string Mtlx = """
            <?xml version="1.0"?>
            <materialx version="1.39">
              <image name="mr" type="float"><input name="file" type="filename" value="mr.png"/></image>
              <gltf_pbr name="SR1" type="surfaceshader">
                <input name="metallic"  type="float" nodename="mr"/>
                <input name="roughness" type="float" nodename="mr"/>
              </gltf_pbr>
            </materialx>
            """;

        var p = MaterialXMaterialReader.Extract(Mtlx).Single();

        p.MetallicRoughnessTexture.Should().NotBeNull();
        p.MetallicRoughnessTexture!.AssetPath.Should().Be("mr.png");
    }

    [Fact]
    public void Connected_Input_With_Empty_File_Leaves_Slot_Null()
    {
        const string Mtlx = """
            <?xml version="1.0"?>
            <materialx version="1.39">
              <image name="bc" type="color3"><input name="file" type="filename" value=""/></image>
              <standard_surface name="SR1" type="surfaceshader">
                <input name="base_color" type="color3" nodename="bc"/>
              </standard_surface>
            </materialx>
            """;

        var p = MaterialXMaterialReader.Extract(Mtlx).Single();

        p.BaseColorTexture.Should().BeNull();
    }

    [Fact]
    public void Constant_Value_Inputs_Still_Populate_Factors_When_No_Connection()
    {
        const string Mtlx = """
            <?xml version="1.0"?>
            <materialx version="1.39">
              <standard_surface name="SR1" type="surfaceshader">
                <input name="base_color" type="color3" value="0.8,0.2,0.1"/>
                <input name="metalness"  type="float"  value="0.5"/>
              </standard_surface>
            </materialx>
            """;

        var p = MaterialXMaterialReader.Extract(Mtlx).Single();

        p.BaseColorFactor.X.Should().BeApproximately(0.8f, 1e-5f);
        p.MetallicFactor.Should().BeApproximately(0.5f, 1e-5f);
        p.BaseColorTexture.Should().BeNull();
    }
}

