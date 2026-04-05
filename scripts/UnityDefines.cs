// Modified for Unity — force NETSTANDARD2_0 so .NET 5+ APIs
// (NativeLibrary, RuntimeIdentifier) are excluded.
#define NETSTANDARD2_0
#if UNITY_ANDROID && !UNITY_EDITOR
#define __ANDROID__
#define __MOBILE__
#elif UNITY_IOS && !UNITY_EDITOR
#define __IOS__
#define __ENABLE_COREML__
#define __MOBILE__
#elif UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
#define __ENABLE_COREML__
#endif

