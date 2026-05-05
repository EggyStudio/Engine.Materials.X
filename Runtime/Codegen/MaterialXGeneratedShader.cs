using System.Collections.Generic;

namespace Engine;

/// <summary>
/// Result of a single MaterialX → GLSL generation pass: the per-stage GLSL source
/// strings emitted by <see cref="MaterialX.ShaderGenerator"/> for the
/// <see cref="MaterialX.ShaderTarget.Vulkan"/> backend, plus the renderable's name.
/// </summary>
/// <param name="Name">
/// The renderable name passed to <see cref="MaterialX.ShaderGenerator.Generate"/>; also
/// used as the <c>main</c>-prefix in the generated entry point and as the cache key
/// for downstream per-material pipeline registries.
/// </param>
/// <param name="VertexGlsl">Generated vertex-stage GLSL (Vulkan profile, with explicit set/binding qualifiers).</param>
/// <param name="PixelGlsl">Generated pixel-stage GLSL (Vulkan profile, with explicit set/binding qualifiers).</param>
/// <param name="Bindings">
/// Reflection of every <c>layout(set, binding)</c>-qualified uniform buffer or sampler
/// parsed out of <paramref name="PixelGlsl"/>. Slice 3 consumes this list to allocate
/// matching descriptor-set layouts and to pack uniform bytes in the right order.
/// </param>
public sealed record MaterialXGeneratedShader(
    string Name,
    string VertexGlsl,
    string PixelGlsl,
    IReadOnlyList<MaterialXBinding> Bindings);


