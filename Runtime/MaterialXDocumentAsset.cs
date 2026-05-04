using MaterialX;

namespace Engine;

/// <summary>
/// Engine-side asset that wraps a parsed <see cref="MaterialX.Document"/> together with the
/// raw XML it was parsed from. Produced by <see cref="MaterialXLoader"/> when the
/// <see cref="AssetServer"/> resolves a <c>.mtlx</c> file.
/// </summary>
/// <remarks>
/// <para>
/// The native <see cref="Document"/> is owned by this asset and disposed when the asset is
/// disposed (e.g. when <c>Assets&lt;MaterialXDocumentAsset&gt;</c> evicts it). The raw XML
/// is retained so downstream consumers - <see cref="MaterialXMaterialReader"/>,
/// shader-graph importers, USD-MaterialX bridges - can re-parse, validate or transform
/// without going through another P/Invoke. The MaterialX.Net 0.1.x public API exposes
/// <c>Input.SetValue</c> overloads but no <c>GetValue</c>, so value extraction has to
/// round-trip the XML for now.
/// </para>
/// </remarks>
public sealed class MaterialXDocumentAsset : IDisposable
{
    /// <summary>The parsed MaterialX document. Owned by this asset.</summary>
    public required Document Document { get; init; }

    /// <summary>The raw XML the document was parsed from.</summary>
    public required string Xml { get; init; }

    /// <summary>Origin path (for diagnostics / round-tripping).</summary>
    public string? SourcePath { get; init; }

    /// <summary>Standard-libraries search path used at parse time.</summary>
    public string? SearchPath { get; init; }

    /// <inheritdoc />
    public void Dispose() => Document.Dispose();
}


