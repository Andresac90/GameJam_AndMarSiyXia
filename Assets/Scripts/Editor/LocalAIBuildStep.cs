using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Networking;
using Debug = UnityEngine.Debug;

/// <summary>
/// Desktop builds ship the AI (llama.cpp + the 3B model) so players install nothing.
/// - After a Windows/macOS build, copies LocalAI/ into the build's StreamingAssets/LocalAI.
///   WebGL builds are skipped (they run the small model in the browser instead).
/// - Menu "Trick or Threat > Download Local AI" fetches the pinned files into LocalAI/
///   (git-ignored, ~2.2 GB) on a new machine.
/// </summary>
public class LocalAIBuildStep : IPostprocessBuildWithReport
{
    private const string LlamaRelease = "b11606";
    private const string ModelFile = "Llama-3.2-3B-Instruct-Q4_K_M.gguf";
    private const string ModelUrl = "https://huggingface.co/bartowski/Llama-3.2-3B-Instruct-GGUF/resolve/main/" + ModelFile;

    private static readonly (string folder, string asset, bool stripTopFolder)[] Binaries =
    {
        ("mac-arm64", "macos-arm64.tar.gz", true),
        ("mac-x64", "macos-x64.tar.gz", true),
        ("win-vulkan", "win-vulkan-x64.zip", false),
        ("win-cpu", "win-cpu-x64.zip", false),
    };

    private static string Source => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "LocalAI"));

    public int callbackOrder => 100;

    public void OnPostprocessBuild(BuildReport report)
    {
        string output = report.summary.outputPath;
        string destination;
        string[] folders;

        switch (report.summary.platform)
        {
            case BuildTarget.StandaloneWindows:
            case BuildTarget.StandaloneWindows64:
                destination = Path.Combine(Path.GetDirectoryName(output), Path.GetFileNameWithoutExtension(output) + "_Data",
                    "StreamingAssets", "LocalAI");
                folders = new[] { "win-vulkan", "win-cpu" };
                break;
            case BuildTarget.StandaloneOSX:
                destination = Path.Combine(output, "Contents", "Resources", "Data", "StreamingAssets", "LocalAI");
                folders = new[] { "mac-arm64", "mac-x64" };
                break;
            default:
                return; // WebGL & others: no bundled AI
        }

        string modelFolder = Path.Combine(Source, "model");
        if (!Directory.Exists(modelFolder) || Directory.GetFiles(modelFolder, "*.gguf").Length == 0)
        {
            Debug.LogWarning("LocalAI: no model in " + modelFolder + ". This desktop build has NO AI. " +
                             "Run 'Trick or Threat > Download Local AI' and build again.");
            return;
        }

        CopyFolder(modelFolder, Path.Combine(destination, "model"));
        foreach (string folder in folders)
        {
            string from = Path.Combine(Source, folder);
            if (Directory.Exists(from)) CopyFolder(from, Path.Combine(destination, folder));
            else Debug.LogWarning("LocalAI: missing " + from + " (run 'Trick or Threat > Download Local AI').");
        }

        if (Application.platform != RuntimePlatform.WindowsEditor)
        {
            foreach (string folder in folders)
            {
                string server = Path.Combine(destination, folder, "llama-server");
                if (File.Exists(server)) Run("/bin/chmod", $"+x \"{server}\"");
            }
        }

        // Adding files after Unity signed the .app breaks its seal ("app is damaged"): re-sign ad hoc.
        if (report.summary.platform == BuildTarget.StandaloneOSX && Application.platform == RuntimePlatform.OSXEditor)
            Run("/usr/bin/codesign", $"--force --deep --sign - \"{output}\"");

        Debug.Log("LocalAI: bundled the AI into " + destination);
    }

    private static void CopyFolder(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (string file in Directory.GetFiles(from))
            File.Copy(file, Path.Combine(to, Path.GetFileName(file)), true);
        foreach (string dir in Directory.GetDirectories(from))
            CopyFolder(dir, Path.Combine(to, Path.GetFileName(dir)));
    }

    // ------------------------------------------------------------ download

    [MenuItem("Trick or Threat/Download Local AI (desktop builds)")]
    public static void Download()
    {
        string downloads = Path.Combine(Source, "_downloads");
        Directory.CreateDirectory(downloads);
        Directory.CreateDirectory(Path.Combine(Source, "model"));

        try
        {
            foreach (var (folder, asset, strip) in Binaries)
            {
                string archive = Path.Combine(downloads, "llama-" + asset);
                string url = $"https://github.com/ggml-org/llama.cpp/releases/download/{LlamaRelease}/llama-{LlamaRelease}-bin-{asset}";
                if (!File.Exists(archive) && !Fetch(url, archive, "llama.cpp " + folder)) return;

                string target = Path.Combine(Source, folder);
                Directory.CreateDirectory(target);
                Run("tar", $"-xf \"{archive}\" -C \"{target}\"" + (strip ? " --strip-components 1" : ""));
            }

            string model = Path.Combine(Source, "model", ModelFile);
            if (!File.Exists(model) && !Fetch(ModelUrl, model, "Llama 3.2 3B model (~2 GB)")) return;
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        Debug.Log("LocalAI: ready in " + Source + ". Windows/macOS builds will include it.");
    }

    private static bool Fetch(string url, string path, string label)
    {
        string partial = path + ".part";
        using (var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbGET))
        {
            request.downloadHandler = new DownloadHandlerFile(partial) { removeFileOnAbort = true };
            UnityWebRequestAsyncOperation operation = request.SendWebRequest();

            while (!operation.isDone)
            {
                if (EditorUtility.DisplayCancelableProgressBar("Download Local AI", label, request.downloadProgress))
                {
                    request.Abort();
                    Debug.LogWarning("LocalAI: download cancelled.");
                    return false;
                }
                System.Threading.Thread.Sleep(100);
            }

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("LocalAI: download failed (" + label + "): " + request.error);
                return false;
            }
        }

        if (File.Exists(path)) File.Delete(path);
        File.Move(partial, path);
        return true;
    }

    private static void Run(string file, string arguments)
    {
        try
        {
            using (Process p = Process.Start(new ProcessStartInfo(file, arguments) { UseShellExecute = false, CreateNoWindow = true }))
                p?.WaitForExit(120000);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("LocalAI: '" + file + " " + arguments + "' failed: " + e.Message);
        }
    }
}
