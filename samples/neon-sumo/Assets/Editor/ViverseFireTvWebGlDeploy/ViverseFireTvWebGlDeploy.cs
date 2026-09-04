#if UNITY_EDITOR
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Viverse.FireTv
{
    /// <summary>
    /// Deploy a WebGL build folder to the Viverse Fire TV debug app and launch the localhost profiler host activity.
    /// </summary>
    public sealed class ViverseFireTvDeployWindow : EditorWindow
    {
        private const string PackageName = "com.viverse.firetv";

        /// <summary>
        /// Canonical app-private spike root (single-user <c>u0</c>). On some Fire OS builds <c>/data/data/pkg/…</c> is
        /// SELinux-blocked for <c>run-as</c> while <c>/data/user/0/pkg/…</c> is the path the process can read/write — must match WebView.
        /// </summary>
        private const string PrivateSpikeRootData = "/data/user/0/com.viverse.firetv/files/unity_webgl_spike";

        /// <summary>Legacy symlink target — diagnostics only; direct access often returns Permission denied under run-as.</summary>
        private const string PrivateSpikeRootDataLegacy = "/data/data/com.viverse.firetv/files/unity_webgl_spike";

        /// <summary>
        /// Push WebGL here first, then <c>run-as cp</c> into app-private. Copying from <c>/sdcard/Android/data/…/files/</c>
        /// is unreliable on some OS versions (scoped storage); staging in <c>/data/local/tmp/</c> matches adb + run-as visibility.
        /// </summary>
        private const string DeviceWebGlStagingDir = "/data/local/tmp/viverse_unity_webgl_spike";

        /// <summary>
        /// Script pushed to the device so we run <c>rm/cp/sync</c> without embedding <c>sh -c "…&amp;&amp;…"</c> in adb args
        /// (Windows → adb quoting around <c>&amp;&amp;</c> can truncate or mangle the command; private index stayed stale while staging matched push).
        /// </summary>
        private const string DeviceRunAsSpikeScriptPath = "/data/local/tmp/viverse_runas_spike.sh";

        /// <summary>Printed by the device copy script only after all embedded cmp gates pass — adb Process.ExitCode is not reliable on some hosts.</summary>
        private const string SpikeScriptGateOkLine = "VIVERSE_SPIKE_GATE_OK";
        private const string PrefAdb = "ViverseFireTv_AdbPath";
        private const string PrefSerial = "ViverseFireTv_DeviceSerial";
        private const string PrefHost = "ViverseFireTv_PcLanHost";
        private const string PrefPort = "ViverseFireTv_WebsockifyPort";
        private const string PrefLastBuild = "ViverseFireTv_LastWebGlPath";
        private const string PrefVerboseDeploy = "ViverseFireTv_VerboseDeploy";

        private const string MenuDeploy = "Viverse/Fire TV/Deploy WebGL build to Fire TV…";

        private string _buildFolder = "";
        private string _adb = "adb";
        private string _serial = "";
        private string _pcHost = "";
        private string _wsPort = "35020";
        private bool _verboseDeploy;
        private Vector2 _scroll;

        [MenuItem(MenuDeploy)]
        public static void Open()
        {
            var w = GetWindow<ViverseFireTvDeployWindow>(true, "Fire TV WebGL deploy", true);
            w.minSize = new Vector2(460, 300);
            w._adb = EditorPrefs.GetString(PrefAdb, "adb");
            w._serial = EditorPrefs.GetString(PrefSerial, "192.168.1.224:5555");
            w._pcHost = EditorPrefs.GetString(PrefHost, "192.168.1.212");
            w._wsPort = EditorPrefs.GetString(PrefPort, "35020");
            w._buildFolder = EditorPrefs.GetString(PrefLastBuild, "");
            w._verboseDeploy = EditorPrefs.GetBool(PrefVerboseDeploy, false);
        }

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.HelpBox(
                "Deploys a WebGL build into the Viverse debug app (com.viverse.firetv), writes upstream host/port for the relay, and starts the profiler host activity.\n\n" +
                "Use the **same folder** as your last **File → Build Settings → Build** (not only Build and Run’s temp output). If the HUD works in the browser but not on device, the pushed **Build/*.wasm** is often stale — check the timestamp below.\n\n" +
                "Patches **index.html** (player connection, deploy id, cache headers) then **adb push**’s to **/data/local/tmp/** and **run-as** copy into **`/data/user/0/…/files/unity_webgl_spike`**. Normal deploy: **index.html canary** gate + short summary. **Debug deploy** adds full path find/ls checks.",
                MessageType.Info);
            EditorGUILayout.Space(4);
            _verboseDeploy = EditorGUILayout.ToggleLeft(
                "Debug deploy (verbose adb: full path diagnostics, grep/head verification — slower)",
                _verboseDeploy);
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("WebGL build folder (contains index.html)", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.SelectableLabel(string.IsNullOrEmpty(_buildFolder) ? "(not set)" : _buildFolder, GUILayout.Height(18));
            if (GUILayout.Button("Browse…", GUILayout.Width(90)))
            {
                var p = EditorUtility.OpenFolderPanel("WebGL build folder", _buildFolder, "");
                if (!string.IsNullOrEmpty(p)) _buildFolder = p;
            }
            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(_buildFolder.Trim()) && Directory.Exists(_buildFolder.Trim()))
            {
                EditorGUILayout.HelpBox(TryDescribeBuildArtifacts(_buildFolder.Trim()), MessageType.None);
            }

            EditorGUILayout.Space(8);
            _adb = EditorGUILayout.TextField("adb executable", _adb);
            _serial = EditorGUILayout.TextField("Device serial", _serial);
            _pcHost = EditorGUILayout.TextField("PC LAN (websockify host)", _pcHost);
            _wsPort = EditorGUILayout.TextField("Websockify port", _wsPort);
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(8);
            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(_buildFolder.Trim())))
            {
                if (GUILayout.Button("Patch index.html, push to device, launch profiler host", GUILayout.Height(32)))
                {
                    RunDeploy();
                }
            }
        }

        private void RunDeploy()
        {
            var path = _buildFolder.Trim();
            var index = Path.Combine(path, "index.html");
            if (!File.Exists(index))
            {
                EditorUtility.DisplayDialog("Viverse Fire TV", "index.html not found in:\n" + path, "OK");
                return;
            }

            if (!int.TryParse(_wsPort.Trim(), out var upstreamPort) || upstreamPort < 1 || upstreamPort > 65535)
            {
                EditorUtility.DisplayDialog("Viverse Fire TV", "Invalid websockify port.", "OK");
                return;
            }

            EditorPrefs.SetString(PrefAdb, _adb.Trim());
            EditorPrefs.SetString(PrefSerial, _serial.Trim());
            EditorPrefs.SetString(PrefHost, _pcHost.Trim());
            EditorPrefs.SetString(PrefPort, upstreamPort.ToString());
            EditorPrefs.SetString(PrefLastBuild, path);
            EditorPrefs.SetBool(PrefVerboseDeploy, _verboseDeploy);

            try
            {
                PatchIndexHtmlPlayerConnection(index, "127.0.0.1:19000");
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("Viverse Fire TV", "index.html patch failed:\n" + ex.Message, "OK");
                return;
            }

            try
            {
                PatchIndexHtmlNoCacheHeaders(index);
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("Viverse Fire TV", "index.html cache-headers patch failed:\n" + ex.Message, "OK");
                return;
            }

            var deployId = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            try
            {
                PatchIndexHtmlDeployDeployId(index, deployId);
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("Viverse Fire TV", "Deploy meta (unity-deploy-id) failed:\n" + ex.Message, "OK");
                return;
            }

            var adbExe = _adb.Trim();
            var log = new StringBuilder();

            void RunAdb(string args, out string stderr, out int exitCode)
            {
                stderr = "";
                exitCode = -1;
                var psi = new ProcessStartInfo
                {
                    FileName = adbExe,
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };
                using var p = Process.Start(psi);
                if (p == null) throw new IOException("Could not start adb.");
                var stdout = p.StandardOutput.ReadToEnd();
                stderr = p.StandardError.ReadToEnd();
                p.WaitForExit(120000);
                try
                {
                    exitCode = p.ExitCode;
                }
                catch
                {
                    exitCode = -1;
                }

                log.AppendLine("$ adb " + args);
                log.AppendLine(stdout);
                if (!string.IsNullOrEmpty(stderr)) log.AppendLine(stderr);
            }

            void RunAdbCapture(string args, out string stdout, out string stderr, out int exitCode)
            {
                stderr = "";
                stdout = "";
                exitCode = -1;
                var psi = new ProcessStartInfo
                {
                    FileName = adbExe,
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };
                using var p = Process.Start(psi);
                if (p == null) throw new IOException("Could not start adb.");
                stdout = p.StandardOutput.ReadToEnd();
                stderr = p.StandardError.ReadToEnd();
                p.WaitForExit(120000);
                try
                {
                    exitCode = p.ExitCode;
                }
                catch
                {
                    exitCode = -1;
                }

                log.AppendLine("$ adb " + args);
                log.AppendLine(stdout);
                if (!string.IsNullOrEmpty(stderr)) log.AppendLine(stderr);
            }

            var s = _serial.Trim();
            var stagingRoot = DeviceWebGlStagingDir;

            try
            {
                LogDeployPhase(log, 1, "device staging clean + force-stop");
                RunAdb($"-s {QuoteArg(s)} connect {QuoteArg(s)}", out _, out _);
                RunAdb($"-s {QuoteArg(s)} shell rm -rf {stagingRoot} && mkdir -p {stagingRoot}", out _, out _);

                /* Cold start so WebView is less likely to keep an in-memory cache of old TemplateData URLs. */
                RunAdb($"-s {QuoteArg(s)} shell am force-stop {PackageName}", out _, out _);

                LogDeployPhase(log, 2, "adb push — WebGL tree + upstream json");
                var trail = path.EndsWith(Path.DirectorySeparatorChar) || path.EndsWith("/")
                    ? path
                    : path + Path.DirectorySeparatorChar;
                var pushFrom = trail + ".";
                var pushArgs = $"-s {QuoteArg(s)} push {QuoteArg(pushFrom)} {QuoteArg(stagingRoot + "/")}";
                RunAdb(pushArgs, out var ePush, out _);

                var upstreamJson =
                    "{\n  \"upstreamHost\": " + JsonQuote(_pcHost.Trim()) + ",\n  \"upstreamPort\": " + upstreamPort + "\n}\n";
                var tmpJson = Path.Combine(Path.GetTempPath(), "viverse_upstream_" + Process.GetCurrentProcess().Id + ".json");
                File.WriteAllText(tmpJson, upstreamJson, new UTF8Encoding(false));
                var remoteJson = $"{stagingRoot}/.viverse_upstream.json";
                RunAdb($"-s {QuoteArg(s)} push {QuoteArg(tmpJson)} {QuoteArg(remoteJson)}", out _, out _);
                try { File.Delete(tmpJson); } catch { /* ignore */ }

                LogDeployPhase(log, 3, "push run-as copy script + chmod 644 + sync");
                /* Run rm/cp/sync from a pushed script — avoids adb+Windows mangling sh -c "…&&…" so copy actually reaches app-private. */
                var localSh = Path.Combine(Path.GetTempPath(), "viverse_runas_spike_" + Process.GetCurrentProcess().Id + ".sh");
                File.WriteAllText(
                    localSh,
                    BuildRunAsSpikeShellScriptBody(stagingRoot, PrivateSpikeRootData),
                    new UTF8Encoding(false));
                RunAdb($"-s {QuoteArg(s)} push {QuoteArg(localSh)} {QuoteArg(DeviceRunAsSpikeScriptPath)}", out _, out _);
                try
                {
                    File.Delete(localSh);
                }
                catch
                {
                    /* ignore */
                }

                /* adb push leaves owner shell; 700 blocks other UIDs — run-as must be able to read the script (sh file). */
                RunAdb($"-s {QuoteArg(s)} shell chmod 644 {QuoteArg(DeviceRunAsSpikeScriptPath)}", out _, out _);
                RunAdb($"-s {QuoteArg(s)} shell sync", out _, out _);

                /* WebView reads app-private tree; verify with run-as cmp (absolute paths only). */
                const int maxRunAsSyncAttempts = 4;
                for (var attempt = 1; attempt <= maxRunAsSyncAttempts; attempt++)
                {
                    LogDeployPhase(log, 4, "run-as private tree copy (script) — attempt " + attempt + "/" + maxRunAsSyncAttempts);
                    var runAsArgs =
                        $"-s {QuoteArg(s)} shell run-as {PackageName} sh {QuoteArg(DeviceRunAsSpikeScriptPath)}";
                    RunAdbCapture(runAsArgs, out var scriptStdout, out var eSh, out var runAsEc);
                    /* Host adb often maps remote exit codes poorly; require script to print marker after in-script cmp. */
                    if (string.IsNullOrEmpty(scriptStdout) ||
                        scriptStdout.IndexOf(SpikeScriptGateOkLine, StringComparison.Ordinal) < 0)
                    {
                        throw new IOException(
                            "run-as copy script did not print " + SpikeScriptGateOkLine +
                            " (embedded cmp/copy failed, or adb hid the real exit). See log output above. stderr: " + (eSh ?? "").Trim());
                    }

                    if (runAsEc != 0)
                        log.AppendLine(
                            "(note: adb Process.ExitCode was " + runAsEc + " but " + SpikeScriptGateOkLine +
                            " was printed — host adb exit mapping can be wrong; trusting device gate.)");
                    if (!string.IsNullOrEmpty(eSh) &&
                        (eSh.IndexOf("Permission denied", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         eSh.IndexOf(": denied", StringComparison.OrdinalIgnoreCase) >= 0))
                        throw new IOException("run-as spike script could not run (permission). chmod 644 on script under /data/local/tmp is required so app uid can read it. stderr: " + eSh.Trim());
                    if (!string.IsNullOrEmpty(eSh) && eSh.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0)
                        throw new IOException("run-as: " + eSh);

                    /* Hard gate: cmp FIRST — no wc/sha256 before it (avoid any extra read pass before identity check). */
                    LogDeployPhase(log, 5, "immediate post-copy gate — index.html canary (cmp, then wc/sha proof on success)");
                    var stIndex = stagingRoot + "/index.html";
                    var privIndex = PrivateSpikeRootData + "/index.html";
                    if (!RunAsStagingMatchesPrivateFile(adbExe, s, stIndex, privIndex, out var canaryEc, out var canaryDiag))
                    {
                        AppendQuickCanaryIndexProof(adbExe, s, stagingRoot, log);
                        if (_verboseDeploy)
                        {
                            log.AppendLine("--- full path diagnostics (debug deploy) — canary failed ---");
                            AppendRunAsSpikePathDiagnostics(adbExe, s, stagingRoot, log);
                        }
                        else
                            log.AppendLine(
                                "Canary failed — enable **Debug deploy** for full path diagnostics (find/ls/legacy paths).");

                        if (attempt >= maxRunAsSyncAttempts)
                        {
                            throw new IOException(
                                "Canary: staging vs app-private index.html still differed after " + maxRunAsSyncAttempts +
                                " attempts (run-as cmp). Staging: " + stagingRoot + ".");
                        }

                        log.AppendLine("(retrying full copy.)");
                        continue;
                    }

                    AppendQuickCanaryIndexProof(adbExe, s, stagingRoot, log);

                    if (attempt > 1)
                        log.AppendLine("(run-as copy succeeded on attempt " + attempt + " — all gates passed.)");
                    if (_verboseDeploy)
                    {
                        log.AppendLine();
                        log.AppendLine("--- full path diagnostics (debug deploy) — after successful gates ---");
                        AppendRunAsSpikePathDiagnostics(adbExe, s, stagingRoot, log);
                    }

                    break;
                }

                LogDeployPhase(log, 7, "launch profiler host activity (LocalhostRelaySpikeActivity)");
                RunAdb(
                    $"-s {QuoteArg(s)} shell am start -a com.viverse.firetv.action.LOCALHOST_SPIKE " +
                    $"-n {PackageName}/.LocalhostRelaySpikeActivity " +
                    $"--es upstream_host {QuoteArg(_pcHost.Trim())} --ei upstream_port {upstreamPort}",
                    out var eAm,
                    out _);
                if (!string.IsNullOrEmpty(eAm) && eAm.IndexOf("Error", StringComparison.OrdinalIgnoreCase) >= 0)
                    throw new IOException("am start: " + eAm);

                AppendDeployEndSummary(adbExe, s, stagingRoot, deployId, log);

                LogDeployPhase(log, 9, "remove device staging + helper script");
                RunAdb($"-s {QuoteArg(s)} shell rm -rf {QuoteArg(stagingRoot)} {QuoteArg(DeviceRunAsSpikeScriptPath)}", out _, out _);
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("Viverse Fire TV", "Deploy failed:\n" + ex.Message + "\n\n" + log, "OK");
                return;
            }

            EditorUtility.DisplayDialog(
                "Viverse Fire TV",
                "Deploy finished. Profiler host activity started.\n" +
                "Open Unity Profiler (Web) and connect using the websockify port.\n\n" + log,
                "OK");
        }

        private static string QuoteArg(string a)
        {
            if (a.IndexOfAny(new[] { ' ', '&', '(', ')', '[', ']', '{', '}', ';' }) < 0)
                return a;
            return "\"" + a.Replace("\"", "\\\"") + "\"";
        }

        private static string JsonQuote(string s) =>
            "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

        private static void LogDeployPhase(StringBuilder log, int phaseNumber, string title)
        {
            log.AppendLine();
            log.AppendLine("=== PHASE " + phaseNumber + ": " + title + " ===");
        }

        /// <summary>Minimal run-as proof before any heavy diagnostics — wc + sha256 for canary index only.</summary>
        private static void AppendQuickCanaryIndexProof(string adbExe, string serial, string stagingRoot, StringBuilder log)
        {
            var st = stagingRoot + "/index.html";
            var pr = PrivateSpikeRootData + "/index.html";
            var inner = "echo QUICK_WC; wc -c " + st + " " + pr + "; echo QUICK_SHA256; sha256sum " + st + " " + pr;
            var o = TryAdbRunAsShStdout(adbExe, serial, inner);
            if (!string.IsNullOrEmpty(o))
                log.AppendLine(o.TrimEnd());
        }

        /// <summary>One-screen facts: canonical roots, deploy id line, cmp/wc/sha256 for index (staging still on device).</summary>
        private static void AppendDeployEndSummary(string adbExe, string serial, string stagingRoot, string deployId, StringBuilder log)
        {
            log.AppendLine();
            LogDeployPhase(log, 8, "deploy summary");
            log.AppendLine("staging_root (device):  " + stagingRoot);
            log.AppendLine("private_root (canonical): " + PrivateSpikeRootData);
            log.AppendLine("unity-deploy-id (meta):  " + deployId);
            log.AppendLine("legacy path (logs only):  " + PrivateSpikeRootDataLegacy);
            var st = stagingRoot + "/index.html";
            var pr = PrivateSpikeRootData + "/index.html";
            var inner =
                "cmp -s " + st + " " + pr + " && echo cmp_index:IDENTICAL || echo cmp_index:DIFFER; wc -c " + st + " " + pr + "; sha256sum " + st + " " + pr;
            var o = TryAdbRunAsShStdout(adbExe, serial, inner);
            if (!string.IsNullOrEmpty(o))
                log.AppendLine(o.TrimEnd());
            else
                log.AppendLine("(could not summarize index.html on device.)");
            log.AppendLine(
                "WebView/serving uses **private_root** only; do not mix with legacy path for deploy verification.");
        }

        private static string BuildRunAsSpikeShellScriptBody(string stagingRoot, string privateSpikeRoot)
        {
            return "#!/system/bin/sh\n" +
                "set -e\n" +
                "STG=" + stagingRoot + "\n" +
                "PRIV=" + privateSpikeRoot + "\n" +
                "rm -rf \"$PRIV\"\n" +
                "mkdir -p \"$PRIV/TemplateData\"\n" +
                "cp -R \"$STG/.\" \"$PRIV/\"\n" +
                "cp -f \"$STG/index.html\" \"$PRIV/index.html\"\n" +
                "sync\n" +
                "cmp -s \"$STG/index.html\" \"$PRIV/index.html\" || exit 9\n" +
                "echo " + SpikeScriptGateOkLine + "\n";
        }

        /// <summary>Adb-only: capture stdout from <c>adb -s SERIAL shell …</c> (best-effort).</summary>
        private static string TryAdbShellStdout(string adbExe, string serial, string shellArgsNoPrefix)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = adbExe,
                    Arguments = $"-s {QuoteArg(serial)} shell {shellArgsNoPrefix}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };
                using var p = Process.Start(psi);
                if (p == null) return "";
                var o = p.StandardOutput.ReadToEnd();
                p.WaitForExit(20000);
                return o ?? "";
            }
            catch
            {
                return "";
            }
        }

        /// <summary>run-as package sh -c '…' stdout (best-effort).</summary>
        private static string TryAdbRunAsShStdout(string adbExe, string serial, string innerSh)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = adbExe,
                    Arguments = $"-s {QuoteArg(serial)} shell run-as {PackageName} sh -c {QuoteArg(innerSh)}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };
                using var p = Process.Start(psi);
                if (p == null) return "";
                var o = p.StandardOutput.ReadToEnd();
                p.WaitForExit(20000);
                return o ?? "";
            }
            catch
            {
                return "";
            }
        }

        /// <summary>Best-effort cache headers for embedded WebViews that respect meta http-equiv.</summary>
        private static void PatchIndexHtmlNoCacheHeaders(string indexPath)
        {
            var text = File.ReadAllText(indexPath);
            PatchIndexHtmlNoCacheMetaIfMissing(ref text);
            File.WriteAllText(indexPath, text, new UTF8Encoding(false));
        }

        /// <summary>Best-effort hint for embedded WebViews that respect meta http-equiv.</summary>
        private static void PatchIndexHtmlNoCacheMetaIfMissing(ref string html)
        {
            if (html.IndexOf("http-equiv", StringComparison.OrdinalIgnoreCase) >= 0 &&
                html.IndexOf("Cache-Control", StringComparison.OrdinalIgnoreCase) >= 0)
                return;
            const string meta =
                "<meta http-equiv=\"Cache-Control\" content=\"no-cache, no-store, must-revalidate\" />\n    <meta http-equiv=\"Pragma\" content=\"no-cache\" />\n    ";
            var headOpen = Regex.Match(html, @"<head[^>]*>", RegexOptions.IgnoreCase);
            if (!headOpen.Success)
                return;
            var insertAt = headOpen.Index + headOpen.Length;
            html = html.Substring(0, insertAt) + meta + html.Substring(insertAt);
        }

        /// <summary>Embeds <c>meta name="unity-deploy-id"</c> for WebView / logcat verification.</summary>
        private static void PatchIndexHtmlDeployDeployId(string indexPath, string deployId)
        {
            var html = File.ReadAllText(indexPath);
            var safe = deployId.Replace("&", "&amp;").Replace("\"", "&quot;").Replace("<", "&lt;");
            if (Regex.IsMatch(html, @"<meta\s+name=[""']unity-deploy-id[""']", RegexOptions.IgnoreCase))
            {
                html = Regex.Replace(
                    html,
                    @"<meta\s+name=[""']unity-deploy-id[""'][^>]*>",
                    "<meta name=\"unity-deploy-id\" content=\"" + safe + "\" />",
                    RegexOptions.IgnoreCase);
            }
            else
            {
                var headOpen = Regex.Match(html, @"<head[^>]*>", RegexOptions.IgnoreCase);
                if (!headOpen.Success)
                    throw new InvalidOperationException("index.html has no <head> for unity-deploy-id.");
                var insertAt = headOpen.Index + headOpen.Length;
                html = html.Substring(0, insertAt) + "\n    <meta name=\"unity-deploy-id\" content=\"" + safe + "\" />\n    " + html.Substring(insertAt);
            }

            File.WriteAllText(indexPath, html, new UTF8Encoding(false));
        }

        private static void AppendAdbStdoutToLog(string adbExe, string arguments, StringBuilder log)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = adbExe,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };
                using var p = Process.Start(psi);
                if (p == null)
                {
                    log.AppendLine("(adb: could not start process)");
                    return;
                }

                var stdout = p.StandardOutput.ReadToEnd();
                var stderr = p.StandardError.ReadToEnd();
                p.WaitForExit(120000);
                log.AppendLine("$ adb " + arguments);
                log.AppendLine(stdout);
                if (!string.IsNullOrEmpty(stderr))
                    log.AppendLine(stderr);
            }
            catch (Exception ex)
            {
                log.AppendLine("(adb diagnostics exception: " + ex.Message + ")");
            }
        }

        /// <summary>pwd, ls, stat, sha256sum, find — canonical <c>user/0</c> spike vs legacy <c>/data/data</c> (often denied).</summary>
        private static void AppendRunAsSpikePathDiagnostics(string adbExe, string serial, string stagingRoot, StringBuilder log)
        {
            var st = stagingRoot + "/index.html";
            var d = PrivateSpikeRootData + "/index.html";
            var legacy = PrivateSpikeRootDataLegacy + "/index.html";
            var inn = new StringBuilder();
            inn.Append("echo '===pwd==='; pwd; ");
            inn.Append("echo '===ls_data_files==='; ls -la /data/data/com.viverse.firetv/files 2>&1; ");
            inn.Append("echo '===ls_user0_files==='; ls -la /data/user/0/com.viverse.firetv/files 2>&1; ");
            inn.Append("echo '===ls_canonical_spike==='; ls -la ").Append(PrivateSpikeRootData).Append(" 2>&1; ");
            inn.Append("echo '===ls_legacy_spike==='; ls -la ").Append(PrivateSpikeRootDataLegacy).Append(" 2>&1; ");
            inn.Append("echo '===stat_staging=== '; stat ").Append(st).Append(" 2>&1; ");
            inn.Append("echo '===stat_canonical=== '; stat ").Append(d).Append(" 2>&1; ");
            inn.Append("echo '===stat_legacy=== '; stat ").Append(legacy).Append(" 2>&1; ");
            inn.Append("echo '===sha256_staging=== '; sha256sum ").Append(st).Append(" 2>&1; ");
            inn.Append("echo '===sha256_canonical=== '; sha256sum ").Append(d).Append(" 2>&1; ");
            inn.Append("echo '===sha256_legacy=== '; sha256sum ").Append(legacy).Append(" 2>&1; ");
            inn.Append("echo '===find_data_spike=== '; find ").Append(PrivateSpikeRootData).Append(" -maxdepth 3 -type f 2>&1 | sort; ");
            var args = $"-s {QuoteArg(serial)} shell run-as {PackageName} sh -c {QuoteArg(inn.ToString())}";
            AppendAdbStdoutToLog(adbExe, args, log);
        }

        /// <summary>Exit 0 = identical. Exit 1 = differ. Other = tool error or missing file.</summary>
        private static bool RunAsStagingMatchesPrivateFile(
            string adbExe, string serial, string absStagingPath, string privateAbsolutePath, out int exitCode, out string diag)
        {
            exitCode = -1;
            diag = "";
            /* Direct: adb shell run-as pkg cmp -s a b — avoids sh -c quoting that broke canary cmp on device. */
            string err;
            if (TryRunAsCmpDirect(adbExe, serial, absStagingPath, privateAbsolutePath, out exitCode, out err))
            {
                if (exitCode == 0)
                    return true;
                if (exitCode == 1)
                {
                    diag = string.IsNullOrEmpty(err) ? "cmp: files differ" : err.Trim();
                    return false;
                }

                diag = "cmp ec=" + exitCode + " " + err.Trim();
            }
            else
            {
                diag = "cmp: could not execute";
            }

            var innerDiff = "diff -q " + absStagingPath + " " + privateAbsolutePath;
            if (!TryRunAsShExit(adbExe, serial, innerDiff, out exitCode, out err))
            {
                return false;
            }

            if (exitCode == 0)
                return true;
            if (exitCode == 1)
            {
                diag = "diff -q: files differ";
                return false;
            }

            diag = "diff ec=" + exitCode + " " + err.Trim();
            return false;
        }

        /// <summary><c>adb shell run-as pkg cmp -s</c> — no nested shell; paths passed as discrete argv via QuoteArg.</summary>
        private static bool TryRunAsCmpDirect(
            string adbExe, string serial, string pathA, string pathB, out int exitCode, out string stderrAndStdout)
        {
            stderrAndStdout = "";
            exitCode = -1;
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = adbExe,
                    Arguments = $"-s {QuoteArg(serial)} shell run-as {PackageName} cmp -s {QuoteArg(pathA)} {QuoteArg(pathB)}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };
                using var p = Process.Start(psi);
                if (p == null) return false;
                var stdout = p.StandardOutput.ReadToEnd();
                var stderr = p.StandardError.ReadToEnd();
                p.WaitForExit(25000);
                exitCode = p.ExitCode;
                stderrAndStdout = (stderr + stdout).Trim();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryRunAsShExit(string adbExe, string serial, string innerSh, out int exitCode, out string stderr)
        {
            stderr = "";
            exitCode = -1;
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = adbExe,
                    Arguments = $"-s {QuoteArg(serial)} shell run-as {PackageName} sh -c {QuoteArg(innerSh)}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };
                using var p = Process.Start(psi);
                if (p == null) return false;
                var stdout = p.StandardOutput.ReadToEnd();
                stderr = p.StandardError.ReadToEnd();
                p.WaitForExit(25000);
                exitCode = p.ExitCode;
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Show newest .wasm in Build/ so deploy folder matches last WebGL build.</summary>
        private static string TryDescribeBuildArtifacts(string root)
        {
            try
            {
                var buildDir = Path.Combine(root, "Build");
                if (!Directory.Exists(buildDir))
                    return "No Build/ subfolder — pick the Unity WebGL output root (contains index.html and Build/).";

                var files = Directory.GetFiles(buildDir, "*.wasm", SearchOption.TopDirectoryOnly);
                if (files.Length == 0)
                    return "Build/ has no .wasm — rebuild WebGL or select the correct output folder.";

                var newest = files[0];
                var newestTime = File.GetLastWriteTimeUtc(newest);
                foreach (var f in files)
                {
                    var t = File.GetLastWriteTimeUtc(f);
                    if (t > newestTime)
                    {
                        newest = f;
                        newestTime = t;
                    }
                }

                return "Newest wasm (UTC): " + Path.GetFileName(newest) + " @ " + newestTime.ToString("yyyy-MM-dd HH:mm:ss") +
                       "\nRebuild WebGL before deploy so this time matches your latest build.";
            }
            catch (Exception ex)
            {
                return "Could not read Build/: " + ex.Message;
            }
        }

        /// <summary>Replace only the first regex match (Unity resolves Regex.Replace(..., 1) to the wrong overload).</summary>
        private static string ReplaceRegexFirst(string input, string pattern, string replacement)
        {
            var m = Regex.Match(input, pattern);
            if (!m.Success) return input;
            return input.Substring(0, m.Index) + replacement + input.Substring(m.Index + m.Length);
        }

        /// <summary>Same behavior as Viverse inject-webgl-profiler-connection.ps1</summary>
        public static void PatchIndexHtmlPlayerConnection(string indexPath, string hostPort)
        {
            var text = File.ReadAllText(indexPath);
            var arg = "--player-connection-ip=" + hostPort;
            var quotedArg = "\"" + arg + "\"";
            string newText = null;

            if (Regex.IsMatch(text, "\"--player-connection-ip=[^\"]*\""))
            {
                newText = ReplaceRegexFirst(text, "\"--player-connection-ip=[^\"]*\"", quotedArg);
            }
            else if (Regex.IsMatch(text, @"arguments\s*:\s*\[\s*\]"))
            {
                var replacement = "arguments: [" + quotedArg + "]";
                newText = ReplaceRegexFirst(text, @"arguments\s*:\s*\[\s*\]", replacement);
            }

            if (newText == null || newText == text)
            {
                if (text.Contains(arg, StringComparison.Ordinal))
                    return;
                throw new InvalidOperationException(
                    "Could not patch index.html (no empty arguments: [] and no existing --player-connection-ip).");
            }

            var bak = indexPath + ".bak";
            File.Copy(indexPath, bak, true);
            File.WriteAllText(indexPath, newText, new UTF8Encoding(false));
        }

        /// <summary>Optional scrub — removes the same staging + app-private trees as deploy. Does not fix adb/run-as bugs.</summary>
        [MenuItem("Viverse/Fire TV/Clear device WebGL spike (staging + app-private)…")]
        private static void MenuClearDeviceSpike()
        {
            if (!EditorUtility.DisplayDialog(
                    "Viverse Fire TV — deep clean",
                    "Removes:\n• " + DeviceWebGlStagingDir + "\n• " + PrivateSpikeRootData +
                    "\n\nUse when you want a cold tree on device; standard deploy already rm -rf’s these before copy.\n\nContinue?",
                    "Clear",
                    "Cancel"))
                return;

            var adb = EditorPrefs.GetString(PrefAdb, "adb").Trim();
            var serial = EditorPrefs.GetString(PrefSerial, "").Trim();
            if (string.IsNullOrEmpty(serial))
            {
                EditorUtility.DisplayDialog("Viverse Fire TV", "Set **Device serial** in Viverse/Fire TV/Deploy once so EditorPrefs has it.", "OK");
                return;
            }

            var sb = new StringBuilder();
            void Run(string args, out string stderr, out int exitCode)
            {
                stderr = "";
                exitCode = -1;
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = adb,
                        Arguments = args,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                    };
                    using var p = Process.Start(psi);
                    if (p == null)
                    {
                        sb.AppendLine("(adb: could not start)");
                        return;
                    }

                    var stdout = p.StandardOutput.ReadToEnd();
                    stderr = p.StandardError.ReadToEnd();
                    p.WaitForExit(60000);
                    try
                    {
                        exitCode = p.ExitCode;
                    }
                    catch
                    {
                        exitCode = -1;
                    }

                    sb.AppendLine("$ adb " + args);
                    sb.AppendLine(stdout);
                    if (!string.IsNullOrEmpty(stderr)) sb.AppendLine(stderr);
                }
                catch (Exception ex)
                {
                    sb.AppendLine(ex.Message);
                }
            }

            Run($"-s {QuoteArg(serial)} connect {QuoteArg(serial)}", out _, out _);
            Run($"-s {QuoteArg(serial)} shell rm -rf {DeviceWebGlStagingDir}", out _, out _);
            var rmPriv = "rm -rf " + PrivateSpikeRootData;
            Run($"-s {QuoteArg(serial)} shell run-as {PackageName} sh -c {QuoteArg(rmPriv)}", out _, out _);
            EditorUtility.DisplayDialog("Viverse Fire TV — deep clean", sb.ToString(), "OK");
        }
    }
}
#endif
