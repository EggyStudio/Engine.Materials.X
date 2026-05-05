using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Engine;

/// <summary>
/// Parses the GLSL emitted by <see cref="MaterialX.ShaderGenerator"/> for the
/// <see cref="MaterialX.ShaderTarget.Vulkan"/> backend and recovers the
/// <c>(set, binding, kind, name)</c> tuples needed to build matching Vulkan
/// descriptor-set layouts on the engine side.
/// </summary>
/// <remarks>
/// The MaterialX.Net 0.1.x binding does not expose the C++
/// <c>VariableBlock</c>/<c>ShaderPort</c> reflection API, so the explicit
/// <c>layout(set = N, binding = M)</c> qualifiers the Vulkan target writes into
/// the GLSL itself are the source of truth. The reflection is intentionally a
/// shallow regex scan: it records the resource declarations needed for descriptor
/// allocation and uniform packing, not the full type tree.
/// </remarks>
public static class MaterialXShaderReflection
{
    // Match `layout(... set = N, ... binding = M ...) uniform <Type> [{ ... }] <name>`.
    // The optional `{ ... }` matches MaterialX-style UBO block bodies, e.g.
    //     layout(set=0, binding=0) uniform PrivateUniforms { mat4 m; } u_prv;
    // while the `?` keeps simple sampler declarations matching, e.g.
    //     layout(set=1, binding=2) uniform sampler2D base_color_tex;
    private static readonly Regex LayoutBinding = new(
        @"layout\s*\([^)]*?\bset\s*=\s*(?<set>\d+)[^)]*?\bbinding\s*=\s*(?<binding>\d+)[^)]*\)\s*uniform\s+(?<type>\w+)\s*(?:\{[^}]*\}\s*)?(?<name>\w+)",
        RegexOptions.Compiled | RegexOptions.Singleline);

    /// <summary>
    /// Returns every <c>layout(set, binding) uniform …</c> declaration found in
    /// <paramref name="glsl"/>, classified as a UBO or sampler. Pure / allocation-
    /// free apart from the result list.
    /// </summary>
    public static IReadOnlyList<MaterialXBinding> Reflect(string glsl)
    {
        var list = new List<MaterialXBinding>();
        if (string.IsNullOrEmpty(glsl)) return list;

        foreach (Match m in LayoutBinding.Matches(glsl))
        {
            var type = m.Groups["type"].Value;
            var kind = type.StartsWith("sampler", System.StringComparison.Ordinal)
                ? MaterialXBindingKind.Sampler
                : MaterialXBindingKind.UniformBuffer;
            list.Add(new MaterialXBinding(
                Set: uint.Parse(m.Groups["set"].Value),
                Binding: uint.Parse(m.Groups["binding"].Value),
                Kind: kind,
                Name: m.Groups["name"].Value,
                GlslType: type));
        }
        return list;
    }
}


