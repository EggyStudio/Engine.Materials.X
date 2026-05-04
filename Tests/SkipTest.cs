namespace Engine.Tests.Materials.X;

/// <summary>
/// xunit 2.9 doesn't expose <c>Assert.Skip(string)</c> directly; instead the convention
/// is to throw an exception whose message starts with <c>"$XunitDynamicSkip$"</c>
/// (see <c>Xunit.Sdk.DynamicSkipToken.Value</c>, which is internal to xunit). The runner
/// recognizes the prefix and reports the test as skipped with the trailing reason.
/// Mirrors the helper used by the USD test suite.
/// </summary>
internal static class SkipTest
{
    private const string DynamicSkipToken = "$XunitDynamicSkip$";

    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    public static void With(string reason) =>
        throw new InvalidOperationException(DynamicSkipToken + reason);

    public static void If(bool condition, string reason)
    {
        if (condition) With(reason);
    }
}