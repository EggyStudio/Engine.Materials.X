using Xunit;

namespace Engine.Tests.Materials.X;

/// <summary>
/// Forces every test class in this namespace that opts in via
/// <c>[Collection(MaterialXTestCollection.Name)]</c> to run serially. The MaterialX.Net
/// 0.1.x C# binding wraps a SWIG-generated native shim around the Pixar-style
/// MaterialX libraries; the standard-library load (<see cref="MaterialX.Document.LoadStandardLibraries"/>)
/// mutates a process-wide registry, so multi-document tests racing in parallel xUnit
/// collections are a known source of flakes. Mirrors <c>UsdTestCollection</c>.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class MaterialXTestCollection
{
    public const string Name = "MaterialX";
}

