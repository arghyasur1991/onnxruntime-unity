using System;
using System.IO;
using System.IO.Compression;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace Microsoft.ML.OnnxRuntime.Editor
{
    /// <summary>
    /// Auto-downloads ONNX Runtime native libraries from NuGet on first load.
    /// Downloads into Assets/Plugins/OnnxRuntime/ (writable) rather than the
    /// package's own Plugins/ directory (which is immutable for git URL packages).
    /// </summary>
    [InitializeOnLoad]
    public static class OrtNativeBootstrap
    {
        const string ORT_VERSION = "1.24.4";
        const string NUGET_URL = "https://www.nuget.org/api/v2/package/Microsoft.ML.OnnxRuntime/" + ORT_VERSION;
        const string SESSION_KEY = "OrtNativeBootstrap_Checked_" + ORT_VERSION;
        const string PLUGINS_ROOT = "Assets/Plugins/OnnxRuntime";

        static OrtNativeBootstrap()
        {
            if (SessionState.GetBool(SESSION_KEY, false))
                return;

            SessionState.SetBool(SESSION_KEY, true);
            EditorApplication.delayCall += CheckAndDownload;
        }

        static void CheckAndDownload()
        {
            string pluginsPath = Path.GetFullPath(PLUGINS_ROOT);

            string sentinel = Path.Combine(pluginsPath, "macOS", "arm64", "libonnxruntime.dylib");
            if (File.Exists(sentinel))
                return;

            Debug.Log($"[OrtNativeBootstrap] ONNX Runtime {ORT_VERSION} native libraries not found. Downloading from NuGet...");
            DownloadAndExtract(pluginsPath);
        }

        static void DownloadAndExtract(string pluginsPath)
        {
            string tmpDir = Path.Combine(Path.GetTempPath(), $"ort-nuget-{ORT_VERSION}");
            string nupkgPath = Path.Combine(tmpDir, $"Microsoft.ML.OnnxRuntime-{ORT_VERSION}.nupkg");
            string extractDir = Path.Combine(tmpDir, "extracted");

            if (File.Exists(nupkgPath) && Directory.Exists(extractDir))
            {
                CopyNativeLibs(extractDir, pluginsPath);
                ConfigurePluginImporters();
                return;
            }

            Directory.CreateDirectory(tmpDir);

            EditorUtility.DisplayProgressBar(
                "ONNX Runtime Setup",
                $"Downloading ONNX Runtime {ORT_VERSION} native libraries...",
                0.1f);

            var request = UnityWebRequest.Get(NUGET_URL);
            var operation = request.SendWebRequest();

            void PollDownload()
            {
                if (!operation.isDone)
                {
                    EditorUtility.DisplayProgressBar(
                        "ONNX Runtime Setup",
                        $"Downloading ONNX Runtime {ORT_VERSION}... ({request.downloadProgress * 100:F0}%)",
                        0.1f + request.downloadProgress * 0.6f);
                    return;
                }

                EditorApplication.update -= PollDownload;

                if (request.result != UnityWebRequest.Result.Success)
                {
                    EditorUtility.ClearProgressBar();
                    Debug.LogError($"[OrtNativeBootstrap] Download failed: {request.error}");
                    request.Dispose();
                    return;
                }

                try
                {
                    EditorUtility.DisplayProgressBar(
                        "ONNX Runtime Setup",
                        "Extracting native libraries...",
                        0.75f);

                    File.WriteAllBytes(nupkgPath, request.downloadHandler.data);
                    request.Dispose();

                    if (Directory.Exists(extractDir))
                        Directory.Delete(extractDir, true);
                    ZipFile.ExtractToDirectory(nupkgPath, extractDir);

                    EditorUtility.DisplayProgressBar(
                        "ONNX Runtime Setup",
                        "Copying native libraries to project...",
                        0.9f);

                    CopyNativeLibs(extractDir, pluginsPath);

                    AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

                    ConfigurePluginImporters();

                    Debug.Log($"[OrtNativeBootstrap] ONNX Runtime {ORT_VERSION} native libraries installed to {PLUGINS_ROOT}.");
                }
                catch (Exception e)
                {
                    Debug.LogError($"[OrtNativeBootstrap] Extraction failed: {e.Message}\n{e.StackTrace}");
                    SessionState.SetBool(SESSION_KEY, false);
                }
                finally
                {
                    EditorUtility.ClearProgressBar();
                }
            }

            EditorApplication.update += PollDownload;
        }

        static void CopyNativeLibs(string extractDir, string pluginsPath)
        {
            string runtimes = Path.Combine(extractDir, "runtimes");
            if (!Directory.Exists(runtimes))
                throw new DirectoryNotFoundException($"runtimes directory not found in extracted NuGet at {runtimes}");

            CopyFile(
                Path.Combine(runtimes, "android", "native", "onnxruntime.aar"),
                Path.Combine(pluginsPath, "Android", "onnxruntime.aar"));

            CopyFile(
                Path.Combine(runtimes, "osx-arm64", "native", "libonnxruntime.dylib"),
                Path.Combine(pluginsPath, "macOS", "arm64", "libonnxruntime.dylib"));

            CopyFile(
                Path.Combine(runtimes, "osx-x64", "native", "libonnxruntime.dylib"),
                Path.Combine(pluginsPath, "macOS", "x64", "libonnxruntime.dylib"));

            CopyGlob(
                Path.Combine(runtimes, "win-x64", "native"),
                "*.dll",
                Path.Combine(pluginsPath, "Windows", "x64"));

            CopyGlob(
                Path.Combine(runtimes, "win-arm64", "native"),
                "*.dll",
                Path.Combine(pluginsPath, "Windows", "arm64"));

            CopyGlob(
                Path.Combine(runtimes, "linux-x64", "native"),
                "*.so",
                Path.Combine(pluginsPath, "Linux", "x64"));

            string iosXcfZip = Path.Combine(runtimes, "ios", "native", "onnxruntime.xcframework.zip");
            if (File.Exists(iosXcfZip))
            {
                string iosDst = Path.Combine(pluginsPath, "iOS~", "onnxruntime.xcframework");
                if (Directory.Exists(iosDst))
                    Directory.Delete(iosDst, true);
                Directory.CreateDirectory(Path.GetDirectoryName(iosDst));
                ZipFile.ExtractToDirectory(iosXcfZip, Path.Combine(pluginsPath, "iOS~"));
            }
        }

        static void ConfigurePluginImporters()
        {
            ConfigurePlugin("Android/onnxruntime.aar", imp =>
            {
                imp.SetCompatibleWithAnyPlatform(false);
                imp.SetCompatibleWithPlatform(BuildTarget.Android, true);
                imp.SetCompatibleWithEditor(false);
                imp.SetPlatformData(BuildTarget.Android, "CPU", "ARMv7");
                imp.isPreloaded = true;
            });

            ConfigurePlugin("macOS/arm64/libonnxruntime.dylib", imp =>
            {
                imp.SetCompatibleWithAnyPlatform(false);
                imp.SetCompatibleWithEditor(true);
                imp.SetEditorData("CPU", "ARM64");
                imp.SetEditorData("OS", "OSX");
                imp.SetCompatibleWithPlatform(BuildTarget.StandaloneOSX, true);
                imp.SetPlatformData(BuildTarget.StandaloneOSX, "CPU", "ARM64");
                imp.isPreloaded = true;
            });

            ConfigurePlugin("macOS/x64/libonnxruntime.dylib", imp =>
            {
                imp.SetCompatibleWithAnyPlatform(false);
                imp.SetCompatibleWithEditor(true);
                imp.SetEditorData("CPU", "x86_64");
                imp.SetEditorData("OS", "OSX");
                imp.SetCompatibleWithPlatform(BuildTarget.StandaloneOSX, true);
                imp.SetPlatformData(BuildTarget.StandaloneOSX, "CPU", "x86_64");
                imp.isPreloaded = true;
            });

            ConfigurePlugin("Windows/x64/onnxruntime.dll", imp =>
            {
                imp.SetCompatibleWithAnyPlatform(false);
                imp.SetCompatibleWithEditor(true);
                imp.SetEditorData("CPU", "x86_64");
                imp.SetEditorData("OS", "Windows");
                imp.SetCompatibleWithPlatform(BuildTarget.StandaloneWindows64, true);
                imp.SetPlatformData(BuildTarget.StandaloneWindows64, "CPU", "x86_64");
            });

            ConfigurePlugin("Windows/x64/onnxruntime_providers_shared.dll", imp =>
            {
                imp.SetCompatibleWithAnyPlatform(false);
                imp.SetCompatibleWithEditor(true);
                imp.SetEditorData("CPU", "x86_64");
                imp.SetEditorData("OS", "Windows");
                imp.SetCompatibleWithPlatform(BuildTarget.StandaloneWindows64, true);
                imp.SetPlatformData(BuildTarget.StandaloneWindows64, "CPU", "x86_64");
            });

            ConfigurePlugin("Windows/arm64/onnxruntime.dll", imp =>
            {
                imp.SetCompatibleWithAnyPlatform(false);
                imp.SetCompatibleWithEditor(true);
                imp.SetEditorData("CPU", "ARM64");
                imp.SetEditorData("OS", "Windows");
                imp.SetCompatibleWithPlatform(BuildTarget.StandaloneWindows64, true);
                imp.SetPlatformData(BuildTarget.StandaloneWindows64, "CPU", "ARM64");
            });

            ConfigurePlugin("Windows/arm64/onnxruntime_providers_shared.dll", imp =>
            {
                imp.SetCompatibleWithAnyPlatform(false);
                imp.SetCompatibleWithEditor(true);
                imp.SetEditorData("CPU", "ARM64");
                imp.SetEditorData("OS", "Windows");
                imp.SetCompatibleWithPlatform(BuildTarget.StandaloneWindows64, true);
                imp.SetPlatformData(BuildTarget.StandaloneWindows64, "CPU", "ARM64");
            });

            ConfigurePlugin("Linux/x64/libonnxruntime.so", imp =>
            {
                imp.SetCompatibleWithAnyPlatform(false);
                imp.SetCompatibleWithEditor(true);
                imp.SetEditorData("CPU", "x86_64");
                imp.SetEditorData("OS", "Linux");
                imp.SetCompatibleWithPlatform(BuildTarget.StandaloneLinux64, true);
                imp.SetPlatformData(BuildTarget.StandaloneLinux64, "CPU", "AnyCPU");
            });

            ConfigurePlugin("Linux/x64/libonnxruntime_providers_shared.so", imp =>
            {
                imp.SetCompatibleWithAnyPlatform(false);
                imp.SetCompatibleWithEditor(true);
                imp.SetEditorData("CPU", "x86_64");
                imp.SetEditorData("OS", "Linux");
                imp.SetCompatibleWithPlatform(BuildTarget.StandaloneLinux64, true);
                imp.SetPlatformData(BuildTarget.StandaloneLinux64, "CPU", "AnyCPU");
            });
        }

        static void ConfigurePlugin(string relativePath, Action<PluginImporter> configure)
        {
            string assetPath = PLUGINS_ROOT + "/" + relativePath;
            var importer = AssetImporter.GetAtPath(assetPath) as PluginImporter;
            if (importer == null)
                return;

            configure(importer);
            importer.SaveAndReimport();
        }

        static void CopyFile(string src, string dst)
        {
            if (!File.Exists(src))
            {
                Debug.LogWarning($"[OrtNativeBootstrap] Source not found, skipping: {src}");
                return;
            }
            string dir = Path.GetDirectoryName(dst);
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.Copy(src, dst, true);
        }

        static void CopyGlob(string srcDir, string pattern, string dstDir)
        {
            if (!Directory.Exists(srcDir))
            {
                Debug.LogWarning($"[OrtNativeBootstrap] Source dir not found, skipping: {srcDir}");
                return;
            }
            if (!Directory.Exists(dstDir))
                Directory.CreateDirectory(dstDir);
            foreach (string file in Directory.GetFiles(srcDir, pattern))
            {
                string dst = Path.Combine(dstDir, Path.GetFileName(file));
                File.Copy(file, dst, true);
            }
        }
    }
}
