using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.Profiling;

namespace AlternativeGameInterface
{
    /// <summary>
    /// Snapshot (2.2.0, a diagnostic tool): switching [Debug] Snapshot on (a button in the settings window) appends the state of the game process to
    /// BepInEx/AlternativeGameInterface_snapshots.txt and switches itself off. Take one before saving and one after loading (or early and late in a long
    /// session) - every figure is followed by its change since the previous snapshot, so what grows and what a load resets can be read at once:
    /// frame time, GC / Unity memory, Unity objects and MonoBehaviours by type, particles, log errors, the mod's own collections.
    /// </summary>
    public partial class AlternativeGameInterfacePlugin
    {
        ConfigEntry<bool> _snapshot, _collectGarbage;
        static float _snFrameSum, _snFrameMax; static int _snFrames; static readonly float[] _snRing = new float[120]; static int _snRingAt, _snRingN;
        static int _snErr, _snExc, _snWarn; static bool _snLogHooked;
        static Dictionary<string, long> _snPrev = new Dictionary<string, long>();
        static int _snIndex;

        void BindSnapshotConfig()
        {
            _snapshot = Config.Bind("Debug", "Snapshot", false,
                "Diagnostics: switching it on appends the state of the game (frame time, memory, number of objects by type, errors in the log, the mod's collections) to BepInEx/AlternativeGameInterface_snapshots.txt, then it switches itself off. Take one before saving and one after loading to see what grows and what a load resets; every figure is followed by its change since the previous snapshot.");
            _collectGarbage = Config.Bind("Debug", "CollectGarbage", false,
                "Diagnostics: switching it on runs a full garbage collection and unloads unused assets once (like GC.Collect and Resources.UnloadUnusedAssets), writes the memory before and after to the BepInEx log, then switches itself off.");
            try { var old = Path.Combine(Paths.BepInExRootPath, "AlternativeGameInterface_snapshots.txt"); if (File.Exists(old)) File.Delete(old); } catch { }   // one file = one session
            if (!_snLogHooked)
            {
                _snLogHooked = true;
                try { Application.logMessageReceived += (c, s, t) => { if (t == LogType.Exception) _snExc++; else if (t == LogType.Error || t == LogType.Assert) _snErr++; else if (t == LogType.Warning) _snWarn++; }; } catch { }
            }
        }

        void SnapshotStep(float now)
        {
            float dt = Time.unscaledDeltaTime;
            if (dt > 0f)
            {
                _snFrameSum += dt; _snFrames++; if (dt > _snFrameMax) _snFrameMax = dt;
                _snRing[_snRingAt] = dt; _snRingAt = (_snRingAt + 1) % _snRing.Length; if (_snRingN < _snRing.Length) _snRingN++;
            }
            if (_collectGarbage != null && _collectGarbage.Value)
            {
                _collectGarbage.Value = false;
                try
                {
                    long b = GC.GetTotalMemory(false); var sw = System.Diagnostics.Stopwatch.StartNew();
                    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); Resources.UnloadUnusedAssets();
                    sw.Stop();
                    Logger.LogInfo("GC.Collect: managed heap " + (b / 1048576) + " MB -> " + (GC.GetTotalMemory(false) / 1048576) + " MB in " + sw.ElapsedMilliseconds + " ms");
                    Trace("GC.Collect: managed heap " + (b / 1048576) + " MB -> " + (GC.GetTotalMemory(false) / 1048576) + " MB in " + sw.ElapsedMilliseconds + " ms");
                }
                catch (Exception e) { Logger.LogWarning("GC.Collect: " + e.Message); }
            }
            if (_snapshot == null || !_snapshot.Value) return;
            _snapshot.Value = false;
            try { TakeSnapshot(); } catch (Exception e) { Logger.LogWarning("Snapshot: " + e); }
        }

        static string Delta(string key, long v)
        {
            long p; string s = v.ToString("N0", CultureInfo.InvariantCulture);
            if (_snPrev.TryGetValue(key, out p) && p != v) s += "  (" + (v > p ? "+" : "") + (v - p).ToString("N0", CultureInfo.InvariantCulture) + ")";
            _snNow[key] = v;
            return s;
        }
        static Dictionary<string, long> _snNow = new Dictionary<string, long>();

        static string SnName(string n)
        {
            if (string.IsNullOrEmpty(n)) return "(no name)";
            n = n.Replace("(Clone)", "").Trim();
            int e = n.Length; while (e > 0 && (char.IsDigit(n[e - 1]) || n[e - 1] == ' ' || n[e - 1] == '_' || n[e - 1] == '-')) e--;
            return e > 0 ? n.Substring(0, e) : n;
        }

        static void Line(StringBuilder sb, string name, long v, string unit = "")
        {
            sb.Append("  ").Append(name).Append(" = ").Append(Delta(name, v)).Append(unit).AppendLine();
        }

        void TakeSnapshot()
        {
            _snNow = new Dictionary<string, long>();
            var sb = new StringBuilder();
            _snIndex++;
            sb.AppendLine();
            sb.AppendLine("=== snapshot " + _snIndex + "  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  mod " + PluginVersion + " ===");
            sb.AppendLine("  real time since start = " + TimeSpan.FromSeconds(Time.realtimeSinceStartup).ToString(@"h\:mm\:ss") + ", game Time.time = " + Time.time.ToString("F0", CultureInfo.InvariantCulture) + " s, timeScale " + Time.timeScale.ToString("F2", CultureInfo.InvariantCulture) + ", frame " + Time.frameCount);
            try { sb.AppendLine("  scene = " + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name + ", scenes loaded " + UnityEngine.SceneManagement.SceneManager.sceneCount); } catch { }

            // frame time: since the previous snapshot, and the last 120 frames
            float avg = _snFrames > 0 ? _snFrameSum / _snFrames * 1000f : 0f, rsum = 0f;
            for (int i = 0; i < _snRingN; i++) rsum += _snRing[i];
            float ravg = _snRingN > 0 ? rsum / _snRingN * 1000f : 0f;
            sb.AppendLine("-- frame time");
            Line(sb, "frame ms, average since the previous snapshot (x100)", (long)Math.Round(avg * 100f));
            Line(sb, "frame ms, last 120 frames (x100)", (long)Math.Round(ravg * 100f));
            Line(sb, "frame ms, worst since the previous snapshot (x100)", (long)Math.Round(_snFrameMax * 100000f));
            sb.AppendLine("  frames since the previous snapshot = " + _snFrames);
            _snFrameSum = 0f; _snFrames = 0; _snFrameMax = 0f;

            sb.AppendLine("-- memory (bytes)");
            Line(sb, "GC.GetTotalMemory", GC.GetTotalMemory(false));
            Line(sb, "GC collections gen0", GC.CollectionCount(0)); Line(sb, "GC collections gen1", GC.CollectionCount(1)); Line(sb, "GC collections gen2", GC.CollectionCount(2));
            try { Line(sb, "Unity allocated", Profiler.GetTotalAllocatedMemoryLong()); Line(sb, "Unity reserved", Profiler.GetTotalReservedMemoryLong()); } catch { }
            try { Line(sb, "Mono used", Profiler.GetMonoUsedSizeLong()); Line(sb, "Mono heap", Profiler.GetMonoHeapSizeLong()); } catch { }
            try { Line(sb, "Graphics driver", Profiler.GetAllocatedMemoryForGraphicsDriver()); } catch { }
            try { Line(sb, "process working set", System.Diagnostics.Process.GetCurrentProcess().WorkingSet64); } catch { }

            sb.AppendLine("-- log since start (Unity)");
            Line(sb, "exceptions", _snExc); Line(sb, "errors", _snErr); Line(sb, "warnings", _snWarn);

            sb.AppendLine("-- Unity objects (all, including inactive)");
            var all = Resources.FindObjectsOfTypeAll(typeof(UnityEngine.Object));
            Line(sb, "objects total", all.Length);
            var byType = new Dictionary<string, int>(); var mono = new Dictionary<string, int>(); long particles = 0; int ps = 0;
            PropertyInfo pCount = null; Type tPs = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies()) { tPs = asm.GetType("UnityEngine.ParticleSystem"); if (tPs != null) { pCount = tPs.GetProperty("particleCount"); break; } }
            foreach (var o in all)
            {
                if (o == null) continue;
                var t = o.GetType(); string n = t.Name; int c;
                byType.TryGetValue(n, out c); byType[n] = c + 1;
                if (o is MonoBehaviour) { string full = t.FullName ?? n; mono.TryGetValue(full, out c); mono[full] = c + 1; }
                if (tPs != null && pCount != null && t == tPs) { ps++; try { particles += (int)pCount.GetValue(o, null); } catch { } }
            }
            foreach (var kv in byType.OrderByDescending(k => k.Value).Take(30)) Line(sb, kv.Key, kv.Value);
            Line(sb, "ParticleSystem alive particles", particles);
            sb.AppendLine("-- MonoBehaviours by type (top 40)");
            foreach (var kv in mono.OrderByDescending(k => k.Value).Take(40)) Line(sb, kv.Key, kv.Value);

            // which prefabs pile up: the objects of the effect types by name, active or not
            foreach (var tn in new[] { "ParticleSystem", "Light", "AudioSource", "SphereCollider", "TrailRenderer" })
            {
                try
                {
                    Type tt = null;
                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies()) { tt = asm.GetType("UnityEngine." + tn); if (tt != null) break; }
                    if (tt == null) continue;
                    var objs = Resources.FindObjectsOfTypeAll(tt);
                    var names = new Dictionary<string, int>(); var roots = new Dictionary<string, int>(); int act = 0, c;
                    PropertyInfo pPlay = tt.GetProperty("isPlaying"), pEn = tt.GetProperty("enabled");
                    int playing = 0, enabledOn = 0;
                    foreach (var o in objs)
                    {
                        var comp = o as Component; if (comp == null) continue;
                        var go = comp.gameObject; if (go == null) continue;
                        if (go.activeInHierarchy) act++;
                        string n = SnName(go.name); names.TryGetValue(n, out c); names[n] = c + 1;
                        string r = SnName(comp.transform.root.name); roots.TryGetValue(r, out c); roots[r] = c + 1;
                        try { if (pPlay != null && (bool)pPlay.GetValue(o, null)) playing++; } catch { }
                        try { if (pEn != null && go.activeInHierarchy && (bool)pEn.GetValue(o, null)) enabledOn++; } catch { }
                    }
                    sb.AppendLine("-- " + tn + ": " + objs.Length + " total, " + act + " active in hierarchy, " + enabledOn + " enabled and active" + (pPlay != null ? ", " + playing + " playing" : ""));
                    _snNow[tn + " active"] = act; _snNow[tn + " playing"] = playing;
                    foreach (var kv in names.OrderByDescending(k => k.Value).Take(15)) Line(sb, "name " + tn + " | " + kv.Key, kv.Value);
                    foreach (var kv in roots.OrderByDescending(k => k.Value).Take(10)) Line(sb, "root " + tn + " | " + kv.Key, kv.Value);
                }
                catch (Exception e) { sb.AppendLine("  (" + tn + ": " + e.Message + ")"); }
            }

            sb.AppendLine("-- mod collections (static and plugin fields that hold items)");
            try
            {
                foreach (var t in typeof(AlternativeGameInterfacePlugin).Assembly.GetTypes())
                {
                    foreach (var f in t.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    {
                        if (f.IsLiteral) continue;
                        object v = null; try { v = f.GetValue(null); } catch { }
                        var col = v as ICollection;
                        if (col != null && col.Count > 0) Line(sb, t.Name + "." + f.Name, col.Count);
                    }
                }
                foreach (var f in typeof(AlternativeGameInterfacePlugin).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    var col = f.GetValue(this) as ICollection;
                    if (col != null && col.Count > 0) Line(sb, "plugin." + f.Name, col.Count);
                }
            }
            catch (Exception e) { sb.AppendLine("  (collections: " + e.Message + ")"); }

            // the live part of the managed heap: after a full collection what is left is held by something
            try
            {
                sb.AppendLine("-- after GC.Collect()");
                long before = GC.GetTotalMemory(false); GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                Line(sb, "managed heap before the collection", before);
                Line(sb, "managed heap live (after the collection)", GC.GetTotalMemory(true));
                try { Line(sb, "Mono used after", Profiler.GetMonoUsedSizeLong()); Line(sb, "Mono heap after", Profiler.GetMonoHeapSizeLong()); } catch { }
            }
            catch (Exception e) { sb.AppendLine("  (GC: " + e.Message + ")"); }

            _snPrev = _snNow;
            string file = Path.Combine(Paths.BepInExRootPath, "AlternativeGameInterface_snapshots.txt");
            File.AppendAllText(file, sb.ToString());
            Logger.LogInfo("Snapshot " + _snIndex + " written to " + file);
        }
    }
}
