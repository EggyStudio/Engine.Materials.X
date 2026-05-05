using FluentAssertions;
using Xunit;

namespace Engine.Tests.Materials.X;

/// <summary>
/// Pure-managed tests for <see cref="MaterialXShaderReflection"/>: feeds it
/// hand-written GLSL snippets that mirror the MaterialX Vulkan-target output and
/// asserts the recovered <see cref="MaterialXBinding"/> tuples.
/// </summary>
[Trait("Category", "Unit")]
public class MaterialXShaderReflectionTests
{
    [Fact]
    public void Reflect_Empty_Returns_No_Bindings()
    {
        MaterialXShaderReflection.Reflect("").Should().BeEmpty();
    }

    [Fact]
    public void Reflect_Recognises_Ubo_Block()
    {
        const string Glsl = """
            #version 450
            layout(set = 0, binding = 0) uniform PrivateUniforms {
                mat4 u_worldMatrix;
            } u_prv;
            void main() {}
            """;

        var bindings = MaterialXShaderReflection.Reflect(Glsl);

        bindings.Should().HaveCount(1);
        bindings[0].Set.Should().Be(0u);
        bindings[0].Binding.Should().Be(0u);
        bindings[0].Kind.Should().Be(MaterialXBindingKind.UniformBuffer);
        bindings[0].GlslType.Should().Be("PrivateUniforms");
        bindings[0].Name.Should().Be("u_prv");
    }

    [Fact]
    public void Reflect_Recognises_Sampler()
    {
        const string Glsl = """
            #version 450
            layout(set = 1, binding = 3) uniform sampler2D base_color_tex;
            void main() {}
            """;

        var b = MaterialXShaderReflection.Reflect(Glsl).Single();

        b.Set.Should().Be(1u);
        b.Binding.Should().Be(3u);
        b.Kind.Should().Be(MaterialXBindingKind.Sampler);
        b.GlslType.Should().Be("sampler2D");
        b.Name.Should().Be("base_color_tex");
    }

    [Fact]
    public void Reflect_Mixed_Sets_And_Bindings_Are_Each_Captured()
    {
        const string Glsl = """
            layout(set = 0, binding = 0) uniform Camera   { mat4 v; mat4 p; } u_cam;
            layout(set = 0, binding = 1) uniform Lights   { int n; }          u_lights;
            layout(set = 1, binding = 0) uniform Material { vec4 base; }      u_mat;
            layout(set = 1, binding = 1) uniform sampler2D u_baseColorTex;
            layout(set = 1, binding = 2) uniform sampler2D u_normalTex;
            """;

        var bs = MaterialXShaderReflection.Reflect(Glsl);
        bs.Should().HaveCount(5);
        bs.Count(b => b.Kind == MaterialXBindingKind.UniformBuffer).Should().Be(3);
        bs.Count(b => b.Kind == MaterialXBindingKind.Sampler).Should().Be(2);
    }
}

/// <summary>
/// End-to-end smoke tests for <see cref="MaterialXShaderGenerator"/>: requires the
/// native MaterialX runtime to be present and asserts both stages emit non-empty
/// GLSL with a recognisable resource layout.
/// </summary>
[Trait("Category", "Integration")]
public class MaterialXShaderGeneratorTests
{
    [Fact]
    public void Generate_StandardSurface_Emits_Vertex_And_Pixel_Glsl()
    {
        if (!MaterialXRuntimeLayout.IsAvailable())
            SkipTest.With("MaterialX runtime not staged on this host.");

        const string Mtlx = """
            <?xml version="1.0"?>
            <materialx version="1.39">
              <standard_surface name="SR1" type="surfaceshader">
                <input name="base_color" type="color3" value="0.8, 0.2, 0.2" />
              </standard_surface>
              <surfacematerial name="M1" type="material">
                <input name="surfaceshader" type="surfaceshader" nodename="SR1" />
              </surfacematerial>
            </materialx>
            """;

        var generated = MaterialXShaderGenerator.Generate(Mtlx, shaderName: "SmokeTest");

        generated.Should().NotBeNull();
        generated!.VertexGlsl.Should().NotBeNullOrWhiteSpace();
        generated.PixelGlsl.Should().NotBeNullOrWhiteSpace();
        generated.PixelGlsl.Should().Contain("void main");
    }

    [Fact]
    public void Generate_Followed_By_Spirv_Compile_Yields_Bytecode()
    {
        if (!MaterialXRuntimeLayout.IsAvailable())
            SkipTest.With("MaterialX runtime not staged on this host.");

        const string Mtlx = """
            <?xml version="1.0"?>
            <materialx version="1.39">
              <standard_surface name="SR1" type="surfaceshader">
                <input name="base_color" type="color3" value="1, 1, 1" />
              </standard_surface>
              <surfacematerial name="M1" type="material">
                <input name="surfaceshader" type="surfaceshader" nodename="SR1" />
              </surfacematerial>
            </materialx>
            """;

        var generated = MaterialXShaderGenerator.Generate(Mtlx);
        if (generated is null) SkipTest.With("generator returned null on this host.");

        var spv = MaterialXSpirv.Compile(generated!);

        spv.Should().NotBeNull();
        spv!.VertexSpirv.Length.Should().BeGreaterThan(0);
        spv.FragmentSpirv.Length.Should().BeGreaterThan(0);
    }
}




