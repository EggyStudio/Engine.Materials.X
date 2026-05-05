using System.Globalization;
using System.Numerics;
using System.Xml.Linq;

namespace Engine;

/// <summary>
/// Translates a <see cref="MaterialXDocumentAsset"/> (or any MaterialX XML payload) into
/// engine-neutral <see cref="SceneMaterialPayload"/> instances by walking the source XML.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why XML, not the C# API?</b> MaterialX.Net 0.1.x exposes <c>Input.SetValue</c> but
/// no symmetric <c>GetValue</c>; the <c>Node</c> wrapper has no input-enumeration helper
/// either. Reading authored values therefore requires re-parsing the XML, which we keep
/// on the <see cref="MaterialXDocumentAsset.Xml"/> payload precisely for this purpose.
/// As soon as the binding adds value-getters this extractor can switch over without
/// changing its surface.
/// </para>
/// <para>
/// <b>Surface coverage:</b> recognises the two PBR shaders the engine ships fixtures for -
/// <c>standard_surface</c> and <c>gltf_pbr</c> - and maps their authored numeric inputs
/// (<c>base_color</c>, <c>metalness</c>/<c>metallic</c>, <c>specular_roughness</c>/<c>roughness</c>,
/// <c>emission_color</c>/<c>emissive</c>, <c>opacity</c>) onto the corresponding
/// <see cref="SceneMaterialPayload"/> factors. Inputs whose <c>nodename</c> resolves to an
/// <c>image</c> or <c>tiledimage</c> node are followed and their <c>file</c> input is
/// surfaced as a <see cref="SceneTextureRef"/> on the matching texture slot.
/// </para>
/// </remarks>
public static class MaterialXMaterialReader
{
    private static readonly ILogger Logger = Log.Category("Engine.Materials.X");

    /// <summary>
    /// Walks <paramref name="asset"/> and emits one <see cref="SceneMaterialPayload"/> per
    /// <c>surfacematerial</c> element (or per recognised standalone surface shader when no
    /// <c>surfacematerial</c> wraps it). Returns an empty list when no recognised surface
    /// shader exists.
    /// </summary>
    public static List<SceneMaterialPayload> Extract(MaterialXDocumentAsset asset)
        => Extract(asset.Xml, asset.SourcePath);

    /// <summary>
    /// Same as <see cref="Extract(MaterialXDocumentAsset)"/> but takes raw XML directly
    /// (used by the USD-MaterialX bridge in <see cref="UsdMaterialReader"/>).
    /// </summary>
    public static List<SceneMaterialPayload> Extract(string xml, string? sourcePath = null)
    {
        var results = new List<SceneMaterialPayload>();
        XDocument doc;
        try { doc = XDocument.Parse(xml); }
        catch (Exception ex)
        {
            Logger.Debug($"MaterialXMaterialReader: XML parse failed: {ex.Message}");
            return results;
        }

        var root = doc.Root;
        if (root is null) return results;

        // Index every node by name so <surfacematerial> -> nodename references resolve.
        var byName = root.Elements()
            .Where(e => e.Attribute("name") is not null)
            .ToDictionary(e => e.Attribute("name")!.Value, StringComparer.Ordinal);

        // surfacematerial wraps a surfaceshader via inputs:surfaceshader.nodename.
        var materials = root.Elements().Where(e => e.Name.LocalName == "surfacematerial").ToList();
        if (materials.Count == 0)
        {
            // No explicit surfacematerial - synthesise one per recognised standalone shader.
            foreach (var shaderEl in root.Elements().Where(IsRecognisedShader))
            {
                var payload = ExtractFromShader(shaderEl, sourcePath, byName, mtlxXml: xml);
                if (payload is not null) results.Add(payload);
            }
            return results;
        }

        foreach (var matEl in materials)
        {
            var shaderRef = matEl.Elements()
                .FirstOrDefault(e => e.Name.LocalName == "input"
                                     && string.Equals((string?)e.Attribute("name"), "surfaceshader", StringComparison.Ordinal));
            var nodename = (string?)shaderRef?.Attribute("nodename");
            if (nodename is null || !byName.TryGetValue(nodename, out var shaderEl))
            {
                Logger.Debug($"MaterialXMaterialReader: surfacematerial '{matEl.Attribute("name")?.Value}' has no resolvable surfaceshader nodename; skipping.");
                continue;
            }

            var payload = ExtractFromShader(shaderEl, sourcePath, byName, mtlxXml: xml, materialName: matEl.Attribute("name")?.Value);
            if (payload is not null) results.Add(payload);
        }
        return results;
    }

    /// <summary>
    /// Same shape as <see cref="Extract(MaterialXDocumentAsset)"/> but returns at most one
    /// payload - convenience for fixtures that ship a single material.
    /// </summary>
    public static SceneMaterialPayload? ExtractFirst(MaterialXDocumentAsset asset)
        => Extract(asset).FirstOrDefault();

    // -- internals --

    private static bool IsRecognisedShader(XElement el)
        => el.Name.LocalName is "standard_surface" or "gltf_pbr" or "open_pbr_surface";

    private static SceneMaterialPayload? ExtractFromShader(
        XElement shaderEl,
        string? sourcePath,
        IReadOnlyDictionary<string, XElement> byName,
        string? mtlxXml = null,
        string? materialName = null)
    {
        if (!IsRecognisedShader(shaderEl))
        {
            Logger.Debug($"MaterialXMaterialReader: shader '{shaderEl.Attribute("name")?.Value}' has unrecognised category '{shaderEl.Name.LocalName}'.");
            return null;
        }

        Vector4 baseColor = Vector4.One;
        float metallic = 0f;
        float roughness = 1f;
        Vector3 emissive = Vector3.Zero;

        SceneTextureRef? baseColorTex = null;
        SceneTextureRef? metallicRoughnessTex = null;
        SceneTextureRef? normalTex = null;
        SceneTextureRef? emissiveTex = null;
        SceneTextureRef? occlusionTex = null;

        foreach (var input in shaderEl.Elements().Where(e => e.Name.LocalName == "input"))
        {
            var name = (string?)input.Attribute("name");
            if (name is null) continue;
            var value = (string?)input.Attribute("value");

            // Texture connection: nodename points at an image / tiledimage node whose
            // "file" input holds the asset path. The slot we route the texture into is
            // chosen by the surface-shader input name (engine-canonical PBR mapping).
            var nodename = (string?)input.Attribute("nodename");
            if (nodename is not null && byName.TryGetValue(nodename, out var connected))
            {
                var tex = TryExtractTextureRef(connected, byName);
                if (tex is not null)
                {
                    AssignTexture(shaderEl.Name.LocalName, name, tex,
                        ref baseColorTex, ref metallicRoughnessTex,
                        ref normalTex, ref emissiveTex, ref occlusionTex);
                    continue;
                }
            }

            if (value is null) continue;

            switch (shaderEl.Name.LocalName, name)
            {
                // standard_surface
                case ("standard_surface", "base_color"):
                    if (TryParseColor3(value, out var sc)) baseColor = new Vector4(sc, baseColor.W);
                    break;
                case ("standard_surface", "metalness"):
                    if (TryParseFloat(value, out var sm)) metallic = sm;
                    break;
                case ("standard_surface", "specular_roughness"):
                    if (TryParseFloat(value, out var sr)) roughness = sr;
                    break;
                case ("standard_surface", "emission_color"):
                    if (TryParseColor3(value, out var se)) emissive = se;
                    break;

                // gltf_pbr (closer to the SceneMaterialPayload field layout)
                case ("gltf_pbr", "base_color"):
                    if (TryParseColor4(value, out var gc)) baseColor = gc;
                    else if (TryParseColor3(value, out var gc3)) baseColor = new Vector4(gc3, 1f);
                    break;
                case ("gltf_pbr", "metallic"):
                    if (TryParseFloat(value, out var gm)) metallic = gm;
                    break;
                case ("gltf_pbr", "roughness"):
                    if (TryParseFloat(value, out var gr)) roughness = gr;
                    break;
                case ("gltf_pbr", "emissive"):
                    if (TryParseColor3(value, out var ge)) emissive = ge;
                    break;

                // open_pbr_surface (initial coverage; align with standard_surface where it overlaps)
                case ("open_pbr_surface", "base_color"):
                    if (TryParseColor3(value, out var oc)) baseColor = new Vector4(oc, baseColor.W);
                    break;
                case ("open_pbr_surface", "base_metalness"):
                    if (TryParseFloat(value, out var om)) metallic = om;
                    break;
                case ("open_pbr_surface", "specular_roughness"):
                    if (TryParseFloat(value, out var orgh)) roughness = orgh;
                    break;
                case ("open_pbr_surface", "emission_color"):
                    if (TryParseColor3(value, out var oe)) emissive = oe;
                    break;
            }
        }

        var name1 = materialName ?? (string?)shaderEl.Attribute("name") ?? "Material";
        return new SceneMaterialPayload
        {
            Name = name1,
            SourcePath = sourcePath ?? name1,
            BaseColorFactor = baseColor,
            MetallicFactor = metallic,
            RoughnessFactor = roughness,
            EmissiveFactor = emissive,
            BaseColorTexture = baseColorTex,
            MetallicRoughnessTexture = metallicRoughnessTex,
            NormalTexture = normalTex,
            EmissiveTexture = emissiveTex,
            OcclusionTexture = occlusionTex,
            MaterialXSource = mtlxXml,
        };
    }

    /// <summary>
    /// Routes a discovered <see cref="SceneTextureRef"/> into the matching
    /// <see cref="SceneMaterialPayload"/> texture slot, given the surface-shader
    /// category and the input name authored on the surface node.
    /// </summary>
    private static void AssignTexture(
        string shaderCategory, string inputName, SceneTextureRef tex,
        ref SceneTextureRef? baseColor, ref SceneTextureRef? mr,
        ref SceneTextureRef? normal, ref SceneTextureRef? emissive, ref SceneTextureRef? occlusion)
    {
        switch (shaderCategory, inputName)
        {
            case ("standard_surface", "base_color"):
            case ("gltf_pbr", "base_color"):
            case ("open_pbr_surface", "base_color"):
                baseColor = tex; break;

            case ("standard_surface", "metalness"):
            case ("gltf_pbr", "metallic"):
            case ("gltf_pbr", "metallic_roughness"):
            case ("open_pbr_surface", "base_metalness"):
                mr = tex; break;

            case ("standard_surface", "specular_roughness"):
            case ("gltf_pbr", "roughness"):
            case ("open_pbr_surface", "specular_roughness"):
                // If the same packed texture is bound to both metallic and roughness
                // we keep the single ref; SceneMaterialPayload already documents the
                // gltf-style packing (G = roughness, B = metallic).
                mr ??= tex; break;

            case ("standard_surface", "normal"):
            case ("gltf_pbr", "normal"):
            case ("open_pbr_surface", "geometry_normal"):
                normal = tex; break;

            case ("standard_surface", "emission_color"):
            case ("gltf_pbr", "emissive"):
            case ("open_pbr_surface", "emission_color"):
                emissive = tex; break;

            case ("standard_surface", "occlusion"):
            case ("gltf_pbr", "occlusion"):
                occlusion = tex; break;
        }
    }

    /// <summary>
    /// Reads the <c>file</c> input (and optional <c>uvtiling</c> / wrap inputs) of an
    /// <c>image</c> or <c>tiledimage</c> node and returns a <see cref="SceneTextureRef"/>.
    /// Returns <c>null</c> when <paramref name="node"/> is not an image node or its
    /// <c>file</c> input is missing / empty.
    /// </summary>
    private static SceneTextureRef? TryExtractTextureRef(XElement node, IReadOnlyDictionary<string, XElement> byName)
    {
        if (node.Name.LocalName is not ("image" or "tiledimage")) return null;

        string? file = null;
        var wrapS = SceneWrapMode.Repeat;
        var wrapT = SceneWrapMode.Repeat;

        foreach (var input in node.Elements().Where(e => e.Name.LocalName == "input"))
        {
            var n = (string?)input.Attribute("name");
            if (n is null) continue;
            switch (n)
            {
                case "file":
                    file = (string?)input.Attribute("value");
                    break;
                case "uaddressmode":
                    wrapS = ParseWrap((string?)input.Attribute("value"));
                    break;
                case "vaddressmode":
                    wrapT = ParseWrap((string?)input.Attribute("value"));
                    break;
            }
        }

        if (string.IsNullOrEmpty(file)) return null;
        return new SceneTextureRef(file, UvSet: 0, wrapS, wrapT);
    }

    private static SceneWrapMode ParseWrap(string? v) => v switch
    {
        "periodic" or "repeat"  => SceneWrapMode.Repeat,
        "mirror"                => SceneWrapMode.Mirror,
        "clamp"                 => SceneWrapMode.Clamp,
        "constant" or "black"   => SceneWrapMode.Black,
        _                       => SceneWrapMode.Repeat,
    };

    private static bool TryParseFloat(string s, out float v) => 
        float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);

    private static bool TryParseColor3(string s, out Vector3 v)
    {
        v = default;
        var parts = s.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3) return false;
        if (!TryParseFloat(parts[0], out var r) ||
            !TryParseFloat(parts[1], out var g) ||
            !TryParseFloat(parts[2], out var b)) return false;
        v = new Vector3(r, g, b);
        return true;
    }

    private static bool TryParseColor4(string s, out Vector4 v)
    {
        v = default;
        var parts = s.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 4) return false;
        if (!TryParseFloat(parts[0], out var r) ||
            !TryParseFloat(parts[1], out var g) ||
            !TryParseFloat(parts[2], out var b) ||
            !TryParseFloat(parts[3], out var a)) return false;
        v = new Vector4(r, g, b, a);
        return true;
    }
}