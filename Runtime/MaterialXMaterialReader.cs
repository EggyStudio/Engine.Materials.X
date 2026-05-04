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
/// <see cref="SceneMaterialPayload"/> factors. Texture/connection following is intentionally
/// out of scope for the first slice (keeps the test surface tractable; the engine already
/// has a UsdUVTexture-style helper on the USD side that can be ported here later).
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
                var payload = ExtractFromShader(shaderEl, sourcePath);
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

            var payload = ExtractFromShader(shaderEl, sourcePath, materialName: matEl.Attribute("name")?.Value);
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

    private static SceneMaterialPayload? ExtractFromShader(XElement shaderEl, string? sourcePath, string? materialName = null)
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

        foreach (var input in shaderEl.Elements().Where(e => e.Name.LocalName == "input"))
        {
            var name = (string?)input.Attribute("name");
            var value = (string?)input.Attribute("value");
            if (name is null || value is null) continue;

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
        };
    }

    private static bool TryParseFloat(string s, out float v)
        => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);

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