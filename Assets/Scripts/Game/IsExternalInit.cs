// Unity 6000.0 compiles at C# 9, which supports `record` and `init` accessors — but its .NET
// profile does not ship the marker type the compiler emits them against. This one-line shim is
// the standard fix and is compile-time only; it produces no runtime cost and no IL of its own.
//
// Without it, every record in the port fails with CS0518. WITH it, the immutable-state
// architecture ports across as-is: `record` gives value equality and `with` gives copy(),
// which is exactly the Kotlin data class contract GameState depends on.
//
// Note `record class` (with the explicit keyword) is C# 10 and does NOT compile here. Plain
// `record` does.
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}
