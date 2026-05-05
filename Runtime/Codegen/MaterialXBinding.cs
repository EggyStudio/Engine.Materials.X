namespace Engine;

/// <summary>
/// The kind of shader resource a <see cref="MaterialXBinding"/> describes.
/// </summary>
public enum MaterialXBindingKind
{
    /// <summary>A <c>uniform { ... }</c> block (UBO).</summary>
    UniformBuffer,

    /// <summary>A combined image-sampler (<c>sampler2D</c>, <c>samplerCube</c>, …).</summary>
    Sampler,
}

/// <summary>
/// One descriptor-set binding discovered by parsing the GLSL produced by
/// <see cref="MaterialX.ShaderGenerator"/>. Records the explicit
/// <c>layout(set, binding)</c> coordinates plus the variable / block name so the
/// renderer's per-material pipeline cache can build a matching descriptor-set
/// layout and route the right CPU-side data into the right GPU slot.
/// </summary>
/// <param name="Set">Vulkan descriptor-set index from <c>layout(set = N, ...)</c>.</param>
/// <param name="Binding">Vulkan binding index from <c>layout(..., binding = M)</c>.</param>
/// <param name="Kind">Whether the binding is a UBO or a sampler.</param>
/// <param name="Name">
/// The GLSL identifier following the <c>uniform</c> keyword (block instance name
/// for UBOs; variable name for samplers). Used as the lookup key when matching
/// MaterialX's canonical block names (e.g. <c>PrivateUniforms</c>,
/// <c>PublicUniforms</c>, <c>LightData</c>) and per-input texture samplers.
/// </param>
/// <param name="GlslType">
/// For samplers, the sampler GLSL type (e.g. <c>sampler2D</c>). For UBOs, the
/// block type name (e.g. <c>PublicUniforms</c>). Empty when not parseable.
/// </param>
public sealed record MaterialXBinding(
    uint Set,
    uint Binding,
    MaterialXBindingKind Kind,
    string Name,
    string GlslType);

