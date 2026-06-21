// Polyfill required so that C# 9 records and `init` accessors compile against
// netstandard2.0, which does not ship this type.
namespace System.Runtime.CompilerServices
{
    using System.ComponentModel;

    [EditorBrowsable(EditorBrowsableState.Never)]
    internal static class IsExternalInit
    {
    }
}
