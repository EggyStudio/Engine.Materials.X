using System.Linq;
using MX = MaterialX;

namespace Engine;

/// <summary>
/// Engine-side wrapper over <see cref="MX.ShaderGenerator"/>: parses a MaterialX XML
/// document, runs the Vulkan-target generator, and returns the per-stage GLSL plus
/// a flat list of every <c>layout(set, binding)</c> resource declared in the pixel
/// stage. Pure host-side - the generated text is compiled to SPIR-V by
/// <see cref="MaterialXSpirv"/> when a renderer is wired in.
/// </summary>
/// <remarks>
/// <para>
/// The generator instance and its <see cref="MX.GenContext"/> are created and
/// disposed per call. They wrap unmanaged C++ objects that hold an internal
/// search-path state; sharing across threads is unsafe and pooling them is not
/// worth the complexity for material counts measured in the hundreds.
/// </para>
/// <para>
/// Standard-library resolution: <see cref="MX.Document.LoadStandardLibraries"/> and
/// <see cref="MX.GenContext.AddStandardLibrarySearchPath"/> both consult the
/// directory returned by <see cref="MaterialXRuntimeLayout.ResolveStandardLibrariesPath"/>.
/// </para>
/// </remarks>
public static class MaterialXShaderGenerator
{
    private static readonly ILogger Logger = Log.Category("Engine.Materials.X");

    /// <summary>
    /// Generates Vulkan-target GLSL for the first renderable in <paramref name="mtlxXml"/>.
    /// Returns <c>null</c> when the document parses but contains no
    /// <see cref="MX.Document.Renderables"/> entry, or when the native MaterialX
    /// runtime is unavailable on the current platform.
    /// </summary>
    /// <param name="mtlxXml">Raw MaterialX XML payload.</param>
    /// <param name="shaderName">
    /// Identifier baked into the generated entry-point and used as the lookup key
    /// for downstream pipeline caches. Defaults to the renderable's element name.
    /// </param>
    public static MaterialXGeneratedShader? Generate(string mtlxXml, string? shaderName = null)
    {
        if (!MaterialXRuntimeLayout.IsAvailable())
        {
            Logger.Debug("MaterialXShaderGenerator: native MaterialX runtime not available; skipping generation.");
            return null;
        }

        var librariesPath = MaterialXRuntimeLayout.ResolveStandardLibrariesPath();

        try
        {
            using var doc = MX.Document.ReadFromXmlString(mtlxXml, librariesPath);
            doc.LoadStandardLibraries(librariesPath);

            var renderable = doc.Renderables.FirstOrDefault();
            if (renderable is null)
            {
                Logger.Debug("MaterialXShaderGenerator: document has no renderables; nothing to generate.");
                return null;
            }

            var name = shaderName ?? renderable.Name ?? "MaterialXShader";

            using var gen = MX.ShaderGenerator.Create(MX.ShaderTarget.Vulkan);
            using var ctx = MX.GenContext.Create(gen);
            ctx.AddStandardLibrarySearchPath();

            using var shader = gen.Generate(renderable, ctx, name);
            var vertexSrc = shader.GetSourceCode(MX.ShaderStage.Vertex) ?? string.Empty;
            var pixelSrc  = shader.GetSourceCode(MX.ShaderStage.Pixel)  ?? string.Empty;
            var bindings  = MaterialXShaderReflection.Reflect(pixelSrc);

            Logger.Debug(
                $"MaterialXShaderGenerator: '{name}' generated " +
                $"vertex={vertexSrc.Length}B, pixel={pixelSrc.Length}B, bindings={bindings.Count}.");
            return new MaterialXGeneratedShader(name, vertexSrc, pixelSrc, bindings);
        }
        catch (System.Exception ex)
        {
            Logger.Warn($"MaterialXShaderGenerator: generation failed: {ex.Message}");
            return null;
        }
    }
}

