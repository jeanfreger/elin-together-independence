#if DEBUG
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ElinTogether.Net;
using EModding;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using STask = System.Threading.Tasks.Task;

namespace ElinTogether;

internal sealed class EmpDebugListener : MonoBehaviour
{
    // a second bench on the same PC takes its own range (dev/BENCH2.md)
    internal static readonly int PortStart =
        int.TryParse(System.Environment.GetEnvironmentVariable("EMP_PORT_BASE"), out var start) && start > 0 ? start : 27551;
    internal const int PortCount = 10;
    private const string ScriptState = "dev";

    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentQueue<Request> _queue = [];
    private TcpListener? _listener;

    internal int Port { get; private set; }

    /// <summary>
    ///     Test instances are started with -empmute: several games side by side are not to be heard
    /// </summary>
    private static readonly bool _muted = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-empmute") >= 0;

    private void Awake()
    {
        if (_muted) {
            AudioListener.volume = 0f;
        }

        for (var port = PortStart; port < PortStart + PortCount; port++) {
            var listener = new TcpListener(IPAddress.Loopback, port);
            try {
                listener.Start();
            } catch (SocketException) {
                continue;
            }

            _listener = listener;
            Port = port;
            break;
        }

        if (_listener is null) {
            EmpLog.Warning("Debug listener found no free port from {Port}", PortStart);
            return;
        }

        EmpLog.Information("Debug listener bound to {Port}", Port);
        STask.Run(AcceptLoop);
    }

    private void Update()
    {
        // the game sets the volume again when its settings load
        if (_muted && AudioListener.volume != 0f) {
            AudioListener.volume = 0f;
        }

        // and its vsync: a minimized window waiting for vsync ran at about 4 fps, so every frame time the bench
        // measured was the window's, not the game's (2026-10-28)
        if (_muted && QualitySettings.vSyncCount != 0) {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 30;
        }

        while (_queue.TryDequeue(out var request)) {
            Execute(request);
        }
    }

    private void OnDestroy()
    {
        _cts.Cancel();
        _listener?.Stop();
    }

    private sealed class Request(JObject msg)
    {
        public readonly JObject Msg = msg;
        public readonly TaskCompletionSource<JToken?> Tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

#region Socket threads

    private async STask AcceptLoop()
    {
        while (!_cts.IsCancellationRequested) {
            TcpClient client;
            try {
                client = await _listener!.AcceptTcpClientAsync();
            } catch when (_cts.IsCancellationRequested) {
                return;
            } catch (Exception ex) {
                EmpLog.Warning(ex, "Debug listener accept failed");
                await STask.Delay(500);
                continue;
                // noexcept
            }

            _ = HandleClient(client);
        }
    }

    private async STask HandleClient(TcpClient client)
    {
        using (client) {
            client.NoDelay = true;
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, new UTF8Encoding(false));
            await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
            writer.AutoFlush = true;
            writer.NewLine = "\n";

            while (!_cts.IsCancellationRequested) {
                string? line;
                try {
                    line = await reader.ReadLineAsync();
                } catch (Exception) {
                    break;
                }

                if (line is null) {
                    break;
                }

                if (line.Length == 0) {
                    continue;
                }

                if (line[0] != '{') {
                    break;
                }

                var sw = Stopwatch.StartNew();
                JObject response;
                JToken? id = null;
                try {
                    var msg = JObject.Parse(line);
                    id = msg["id"];
                    var request = new Request(msg);
                    _queue.Enqueue(request);
                    var result = await request.Tcs.Task;
                    response = new() {
                        ["id"] = id,
                        ["ok"] = true,
                        ["result"] = result,
                        ["ms"] = sw.ElapsedMilliseconds,
                    };
                } catch (Exception ex) {
                    response = new() {
                        ["id"] = id,
                        ["ok"] = false,
                        ["error"] = $"{ex.GetType().Name}: {ex.Message}",
                        ["trace"] = ex.StackTrace,
                        ["ms"] = sw.ElapsedMilliseconds,
                    };
                }

                try {
                    await writer.WriteLineAsync(response.ToString(Formatting.None));
                } catch {
                    break;
                    // noexcept
                }
            }
        }
    }

#endregion

#region Main thread

    private void Execute(Request request)
    {
        try {
            var op = request.Msg.Value<string>("op") ?? throw new ArgumentException("missing string 'op'");
            var args = request.Msg["args"] as JObject ?? new JObject();

            switch (op) {
                case "hello":
                    request.Tcs.SetResult(Hello());
                    break;
                case "state":
                    request.Tcs.SetResult(State());
                    break;
                case "command":
                    request.Tcs.SetResult(Require(args, "cmd").EvaluateAsCommand());
                    break;
                case "eval":
                    request.Tcs.SetResult(Eval(Require(args, "code")));
                    break;
                case "screenshot":
                    StartCoroutine(Screenshot(request, args));
                    break;
                case "load":
                    request.Tcs.SetResult(Load(Require(args, "path"), args.Value<string>("name")));
                    break;
                case "unload":
                    request.Tcs.SetResult(EScriptLoader.TryUnloadScript(Require(args, "name")));
                    break;
                default:
                    throw new ArgumentException($"unknown op '{op}'");
            }
        } catch (Exception ex) {
            request.Tcs.TrySetException(ex);
            // noexcept?
        }
    }

    private static string Require(JObject args, string key)
    {
        return args.Value<string>(key) ?? throw new ArgumentException($"missing arg '{key}'");
    }

    private static string RoleName()
    {
        var connection = NetSession.Instance.Transport;
        return connection is null ? "None" : connection.IsHost ? "Host" : "Client";
    }

    private JObject Hello()
    {
        var core = EClass.core;
        return new() {
            ["pid"] = Process.GetCurrentProcess().Id,
            ["port"] = Port,
            ["role"] = RoleName(),
            ["modVersion"] = ModInfo.Version,
            ["gameVersion"] = core?.version.GetText(),
            ["gameStarted"] = core?.IsGameStarted ?? false,
            ["sceneMode"] = core?.scene?.mode.ToString(),
            ["scripting"] = EScript.IsScriptingAvailable,
        };
    }

    private static JObject State()
    {
        var session = NetSession.Instance;
        var core = EClass.core;
        var state = new JObject {
            ["role"] = RoleName(),
            ["connected"] = session.HasActiveConnection,
            ["syncMode"] = session.SyncMode.ToString(),
            ["awayZone"] = session.AwayZone?.ZoneFullName,
            ["guest"] = session.IsGuest,
            ["inTransfer"] = (session.Transport as ElinNetClient)?.IsInTransfer ?? false,
            ["zoneSession"] = session.ZoneSession is null ? null : session.ZoneSession.IsHost ? "Host" : "Client",
            ["hostZone"] = session.CurrentZone?.ZoneFullName,
            ["sessionId"] = session.SessionId.ToString(),
            ["tick"] = session.Tick,
            ["players"] = new JArray(session.CurrentPlayers.Select(p => new JObject {
                ["index"] = p.Index,
                ["name"] = p.User.Name,
                ["charaUid"] = p.CharaUid,
                ["pingMs"] = p.LastPingMs,
            })),
            ["gameStarted"] = core.IsGameStarted,
            ["sceneMode"] = core.scene?.mode.ToString(),
        };

        if (!core.IsGameStarted) {
            return state;
        }

        var zone = EClass._zone;
        state["zone"] = new JObject {
            ["name"] = zone.ZoneFullName,
            ["uid"] = zone.uid,
            ["isRegion"] = zone.IsRegion,
        };

        var pc = EClass.pc;
        state["pc"] = new JObject {
            ["uid"] = pc.uid,
            ["name"] = pc.Name,
            ["x"] = pc.pos.x,
            ["z"] = pc.pos.z,
            ["hp"] = pc.hp,
            ["maxHp"] = pc.MaxHP,
            ["isDead"] = pc.isDead,
            ["held"] = pc.held?.Name,
            ["heldUid"] = pc.held?.uid,
            ["ai"] = pc.ai?.GetType().Name,
        };

        return state;
    }

    private static JToken? Eval(string code)
    {
        if (!EScript.IsScriptingAvailable) {
            throw new InvalidOperationException("scripting provider unavailable, is _ModdingKit loaded?");
        }

        var states = (Dictionary<string, EScriptState>)AccessTools.Field(typeof(EScriptState), "ScriptStates").GetValue(null);
        if (!states.ContainsKey(ScriptState)) {
            states[ScriptState] = new();
        }

        var context = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        object? result;
        try {
            result = code.EvaluateAsCsharp(useState: ScriptState, useCache: true, throwOnError: true);
        } finally {
            SynchronizationContext.SetSynchronizationContext(context);
        }

        return result switch {
            null => null,
            string s => s,
            IntPtr or UIntPtr => result.ToString(),
            _ when result.GetType().IsPrimitive || result is decimal => new JValue(result),
            _ => result.ToString(),
        };
    }

    private static string Load(string path, string? name)
    {
        if (!File.Exists(path)) {
            throw new FileNotFoundException("script file not found", path);
        }

        name ??= $"hot_{Path.GetFileNameWithoutExtension(path)}_{DateTime.Now:HHmmssfff}";
        if (AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == name)) {
            throw new InvalidOperationException(
                $"assembly '{name}' is already loaded and Mono cannot unload it, pick another name");
        }

        var bytes = EScript.ScriptProvider.CompileAssembly([(File.ReadAllText(path), path)], name, true);

        var dir = Path.Combine(Application.persistentDataPath, "ElinMP/Logs/Hot");
        Directory.CreateDirectory(dir);
        var dll = Path.Combine(dir, name + ".dll");
        File.WriteAllBytes(dll, bytes);

        return $"{name}: {EScriptLoader.TryLoadScript(dll)}";
    }

    private static IEnumerator Screenshot(Request request, JObject args)
    {
        yield return new WaitForEndOfFrame();

        Texture2D? tex = null;
        Texture2D? scaled = null;
        try {
            tex = ScreenCapture.CaptureScreenshotAsTexture();
            var src = tex;

            var scale = Mathf.Clamp(args.Value<float?>("scale") ?? 0.5f, 0.1f, 1f);
            if (scale < 1f) {
                var w = Mathf.Max(1, Mathf.RoundToInt(tex.width * scale));
                var h = Mathf.Max(1, Mathf.RoundToInt(tex.height * scale));
                var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
                var prev = RenderTexture.active;
                Graphics.Blit(tex, rt);
                RenderTexture.active = rt;
                scaled = new(w, h, TextureFormat.RGB24, false);
                scaled.ReadPixels(new(0, 0, w, h), 0, 0);
                scaled.Apply();
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
                src = scaled;
            }

            var dir = Path.Combine(Application.persistentDataPath, "ElinMP/Logs/Screens");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"dev_{Process.GetCurrentProcess().Id}_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png");
            File.WriteAllBytes(path, src.EncodeToPNG());

            request.Tcs.SetResult(new JObject {
                ["path"] = path,
                ["width"] = src.width,
                ["height"] = src.height,
            });
        } catch (Exception ex) {
            request.Tcs.TrySetException(ex);
            // noexcept?
        } finally {
            if (tex) {
                Destroy(tex);
            }

            if (scaled) {
                Destroy(scaled);
            }
        }
    }

#endregion
}
#endif