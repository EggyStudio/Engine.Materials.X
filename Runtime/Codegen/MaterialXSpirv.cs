namespace Engine;

/// <summary>
/// Per-stage SPIR-V bytecode produced by compiling a <see cref="MaterialXGeneratedShader"/>
/// through <see cref="GlslCompiler"/>. Carries the originating bindings so descriptor
/// allocation and uniform packing can stay co-located with the bytecode.
/// </summary>
/// <param name="Source">The GLSL pair this SPIR-V was compiled from.</param>
/// <param name="VertexSpirv">Vertex-stage SPIR-V bytecode.</param>
/// <param name="FragmentSpirv">Fragment-stage SPIR-V bytecode.</param>
public sealed record MaterialXSpirvShader(
    MaterialXGeneratedShader Source,
    byte[] VertexSpirv,
    byte[] FragmentSpirv);

/// <summary>
/// Compiles the GLSL emitted by <see cref="MaterialXShaderGenerator"/> down to
/// Vulkan SPIR-V via the in-process shaderc <see cref="GlslCompiler"/>. Returns
/// <c>null</c> when either stage fails to compile, so callers can fall back to
/// the shared static-white pipeline without aborting the frame.
/// </summary>
public static class MaterialXSpirv
{
    private static readonly ILogger Logger = Log.Category("Engine.Materials.X");

    /// <summary>
    /// Compiles both stages of <paramref name="generated"/> to SPIR-V. The
    /// <paramref name="fileNameHint"/> is forwarded to shaderc and surfaces in
    /// compile-error messages; defaults to the generated shader's name.
    /// </summary>
    public static MaterialXSpirvShader? Compile(MaterialXGeneratedShader generated, string? fileNameHint = null)
    {
        var hint = fileNameHint ?? generated.Name;
        try
        {
            var vert = GlslCompiler.Compile(generated.VertexGlsl, $"{hint}.vert.glsl", ShaderStage.Vertex);
            var frag = GlslCompiler.Compile(generated.PixelGlsl,  $"{hint}.frag.glsl", ShaderStage.Fragment);
            return new MaterialXSpirvShader(generated, vert, frag);
        }
        catch (System.Exception ex)
        {
            Logger.Warn($"MaterialXSpirv: SPIR-V compile failed for '{hint}': {ex.Message}");
            return null;
        }
    }
}

