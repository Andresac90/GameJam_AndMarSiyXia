using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using UnityEngine;
using UnityEngine.Networking;
using Debug = UnityEngine.Debug;

/// <summary>
/// Runs the AI that ships inside desktop builds: llama.cpp's llama-server plus a GGUF model,
/// started in the background on a free localhost port. Players install nothing.
///
/// Files live in LocalAI/ (project root, git-ignored; fetch with Trick or Threat > Download Local AI)
/// and are copied into Windows/macOS builds by LocalAIBuildStep (never into WebGL):
///   LocalAI/model/*.gguf
///   LocalAI/win-vulkan, LocalAI/win-cpu   (llama-server.exe + DLLs)
///   LocalAI/mac-arm64, LocalAI/mac-x64    (llama-server + dylibs)
/// </summary>
public static class LocalLLMServer
{
    public enum Status { NotStarted, Starting, Ready, Failed }

    public static Status State { get; private set; } = Status.NotStarted;
    public static string Error { get; private set; } = "";
    public static string BaseUrl => "http://127.0.0.1:" + port;

    private const float LoadTimeoutSeconds = 120f;

    private static Process process;
    private static int port;
    private static readonly Queue<string> recentOutput = new Queue<string>();

    /// <summary>Editor: LocalAI/ in the project root. Builds: StreamingAssets/LocalAI.</summary>
    public static string RootFolder => Application.isEditor
        ? Path.GetFullPath(Path.Combine(Application.dataPath, "..", "LocalAI"))
        : Path.Combine(Application.streamingAssetsPath, "LocalAI");

    public static bool IsAvailable => FindModel() != null && ServerCandidates().Count > 0;

    public static IEnumerator EnsureStarted()
    {
        if (State == Status.Ready) yield break;

        if (State == Status.Starting)
        {
            while (State == Status.Starting) yield return null;
            yield break;
        }

        State = Status.Starting;
        string model = FindModel();
        List<string> servers = ServerCandidates();

        if (model == null || servers.Count == 0)
        {
            Fail("The bundled AI files are missing (" + RootFolder + ").");
            yield break;
        }

        Application.quitting -= Stop;
        Application.quitting += Stop;

        // Try each build in order (e.g. Vulkan GPU first, then CPU) until one answers.
        foreach (string server in servers)
        {
            if (!TryLaunch(server, model))
                continue;

            float deadline = Time.realtimeSinceStartup + LoadTimeoutSeconds;
            while (Time.realtimeSinceStartup < deadline && !process.HasExited)
            {
                using (UnityWebRequest health = UnityWebRequest.Get(BaseUrl + "/health"))
                {
                    health.timeout = 2;
                    yield return health.SendWebRequest();
                    if (health.result == UnityWebRequest.Result.Success)
                    {
                        State = Status.Ready;
                        Debug.Log("LocalLLMServer: ready on port " + port + " using " + server);
                        yield break;
                    }
                }
                yield return new WaitForSecondsRealtime(0.5f);
            }

            string output;
            lock (recentOutput) output = string.Join("\n", recentOutput);
            Debug.LogWarning("LocalLLMServer: " + Path.GetFileName(Path.GetDirectoryName(server)) +
                             " build did not start. Last output:\n" + output);
            KillProcess();
        }

        Fail("The bundled AI could not start on this computer.");
    }

    public static void Stop()
    {
        KillProcess();
        if (State != Status.Failed) State = Status.NotStarted;
    }

    private static void KillProcess()
    {
        try
        {
            if (process != null && !process.HasExited)
                process.Kill();
        }
        catch (Exception e)
        {
            Debug.LogWarning("LocalLLMServer: could not stop the AI process: " + e.Message);
        }

        process = null;
    }

    private static bool TryLaunch(string server, string model)
    {
        try
        {
            string folder = Path.GetDirectoryName(server);
            PrepareMacBinaries(folder);

            port = FreePort();
            lock (recentOutput) recentOutput.Clear();

            var info = new ProcessStartInfo
            {
                FileName = server,
                Arguments = $"-m \"{model}\" --host 127.0.0.1 --port {port} -c 4096 -ngl 99",
                WorkingDirectory = folder, // Windows finds the DLLs next to the exe
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            process = new Process { StartInfo = info, EnableRaisingEvents = true };
            process.OutputDataReceived += (_, e) => Remember(e.Data);
            process.ErrorDataReceived += (_, e) => Remember(e.Data);
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            return true;
        }
        catch (Exception e)
        {
            Debug.LogWarning("LocalLLMServer: could not launch " + server + ": " + e.Message);
            process = null;
            return false;
        }
    }

    // macOS: make the helper executable and clear the download quarantine flag,
    // otherwise Gatekeeper silently blocks the unsigned helper inside the game.
    private static void PrepareMacBinaries(string folder)
    {
        if (Application.platform != RuntimePlatform.OSXPlayer &&
            Application.platform != RuntimePlatform.OSXEditor)
            return;

        RunQuietly("/bin/chmod", $"+x \"{Path.Combine(folder, "llama-server")}\"");
        RunQuietly("/usr/bin/xattr", $"-dr com.apple.quarantine \"{folder}\"");
    }

    private static void RunQuietly(string file, string arguments)
    {
        try
        {
            using (Process p = Process.Start(new ProcessStartInfo(file, arguments) { UseShellExecute = false, CreateNoWindow = true }))
                p?.WaitForExit(5000);
        }
        catch (Exception e)
        {
            Debug.LogWarning("LocalLLMServer: " + file + " failed: " + e.Message);
        }
    }

    private static string FindModel()
    {
        string folder = Path.Combine(RootFolder, "model");
        if (!Directory.Exists(folder)) return null;
        string[] models = Directory.GetFiles(folder, "*.gguf");
        return models.Length > 0 ? models[0] : null;
    }

    private static List<string> ServerCandidates()
    {
        var candidates = new List<string>();
        void Add(string sub, string exe)
        {
            string path = Path.Combine(RootFolder, sub, exe);
            if (File.Exists(path)) candidates.Add(path);
        }

        switch (Application.platform)
        {
            case RuntimePlatform.WindowsPlayer:
            case RuntimePlatform.WindowsEditor:
                Add("win-vulkan", "llama-server.exe");
                Add("win-cpu", "llama-server.exe");
                break;
            case RuntimePlatform.OSXPlayer:
            case RuntimePlatform.OSXEditor:
                bool arm = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture ==
                           System.Runtime.InteropServices.Architecture.Arm64;
                Add(arm ? "mac-arm64" : "mac-x64", "llama-server");
                break;
        }

        return candidates;
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int free = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return free;
    }

    private static void Remember(string line)
    {
        if (string.IsNullOrEmpty(line)) return;
        lock (recentOutput)
        {
            recentOutput.Enqueue(line);
            while (recentOutput.Count > 12) recentOutput.Dequeue();
        }
    }

    private static void Fail(string message)
    {
        State = Status.Failed;
        Error = message;
        Debug.LogError("LocalLLMServer: " + message);
    }
}
