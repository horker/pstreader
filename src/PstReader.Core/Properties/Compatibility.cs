#if NETSTANDARD2_0 || NET472
namespace System.Runtime.CompilerServices;

// Records are compiled to ordinary CLR types; older runtimes need this marker at build time.
internal static class IsExternalInit;
#endif
