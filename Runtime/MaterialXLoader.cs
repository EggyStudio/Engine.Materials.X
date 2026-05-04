using System.Text;
using MaterialX;

namespace Engine;

/// <summary>
/// <see cref="IAssetLoader{T}"/> that turns <c>.mtlx</c> bytes into a parsed
/// <see cref="MaterialXDocumentAsset"/>. Registered with the <see cref="AssetServer"/> by
/// <see cref="MaterialXPlugin"/>.
/// </summary>
/// <remarks>
/// <para>
/// The loader reads the entire stream into memory (MaterialX documents are XML and
/// typically &lt; a few hundred KB), then hands the text to
/// <see cref="Document.ReadFromXmlString(string,string)"/>. The standard-libraries search
/// path comes from <see cref="MaterialXRuntimeLayout.ResolveStandardLibrariesPath"/> so
/// node-definition references (e.g. <c>standard_surface</c>) resolve.
/// </para>
/// <para>
/// On parse failure (or if the native shim is missing) the loader returns a failed
/// <see cref="AssetLoadResult{T}"/> with a diagnostic message instead of throwing - the
/// AssetServer surfaces that as an <see cref="AssetEvent{T}"/>.<c>Failed</c>.
/// </para>
/// </remarks>
public sealed class MaterialXLoader : IAssetLoader<MaterialXDocumentAsset>
{
    private static readonly ILogger Logger = Log.Category("Engine.Materials.X");

    /// <inheritdoc />
    public string[] Extensions => [".mtlx"];

    /// <inheritdoc />
    public async Task<AssetLoadResult<MaterialXDocumentAsset>> LoadAsync(AssetLoadContext context, CancellationToken ct)
    {
        try
        {
            byte[] bytes;
            await using (var ms = new MemoryStream())
            {
                await context.GetStream().CopyToAsync(ms, ct).ConfigureAwait(false);
                bytes = ms.ToArray();
            }

            string xml = Encoding.UTF8.GetString(bytes);
            string searchPath = MaterialXRuntimeLayout.ResolveStandardLibrariesPath();

            var doc = Document.ReadFromXmlString(xml, searchPath);
            var asset = new MaterialXDocumentAsset
            {
                Document = doc,
                Xml = xml,
                SourcePath = context.Path.ToString(),
                SearchPath = searchPath,
            };
            Logger.Debug($"MaterialXLoader: parsed '{context.Path}' (xml={bytes.Length} bytes, search='{searchPath}').");
            return AssetLoadResult<MaterialXDocumentAsset>.Ok(asset);
        }
        catch (Exception ex)
        {
            Logger.Warn($"MaterialXLoader: failed to parse '{context.Path}': {ex.Message}");
            return AssetLoadResult<MaterialXDocumentAsset>.Fail($"MaterialX load failed for '{context.Path}': {ex.Message}");
        }
    }
}

