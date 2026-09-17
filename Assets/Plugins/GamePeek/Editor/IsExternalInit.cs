// Polyfill required for C# 9 'init' accessors when targeting .NET Standard 2.0 / Unity.
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}
