// Polyfill for AOT.MonoPInvokeCallbackAttribute when noEngineReferences is true.
// IL2CPP recognizes this attribute by namespace+name regardless of which assembly defines it.

namespace AOT
{
    [System.AttributeUsage(System.AttributeTargets.Method)]
    sealed class MonoPInvokeCallbackAttribute : System.Attribute
    {
        public MonoPInvokeCallbackAttribute(System.Type type) { }
    }
}
