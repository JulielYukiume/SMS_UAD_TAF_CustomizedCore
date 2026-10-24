using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Il2Cpp;

namespace TAFHotfixBatch1.Modules
{
    /// <summary>
    /// **G1 探针**（只读，零副作用）：观察 AI 现场生成设计的协程在做什么。
    ///
    /// 目的：为 Module G（真混合）收集数据 —— AI 到底在什么时候、为哪个**舰种**去现场生成设计；
    ///       以及同一舰种重试了几次。**不做任何修改**（不改状态、不调仓库、不登记设计）。
    ///
    /// 抓手（`dump.cs`）：协程 `CampaignController.&lt;GenerateRandomDesigns&gt;d__202`
    ///   字段：`player`(0x20)、`prewarming`(0x30)、`&lt;i&gt;5__2`(0x40)、
    ///         **`&lt;shipType&gt;5__3`(0x48)**、`&lt;tries&gt;5__4`(0x50)、`&lt;ship&gt;5__5`(0x58)
    ///
    /// 实现要点：
    ///   · `MoveNext` 的 `__instance` 用 `object` 接收（Il2CppInterop 只会给出基类包装），
    ///     因此字段读取全部走 **反射**，并对编译器生成的奇怪成员名做多候选尝试
    ///     （`<shipType>5__3` / `shipType_5__3` / `shipType` …）。
    ///   · 首次调用时打印一次「成员清单」，用于确认实际属性名（诊断用）。
    ///   · 只在 (state, shipType, tries) 组合变化时记一行，避免每帧刷屏。
    ///
    /// 开关：`debug_ai_design_probe`（默认 0）。开启后建议配合 `verbose=0` 观察。
    /// </summary>
    internal static class DebugAiDesignProbe
    {
        private static readonly Dictionary<string, int> _seen = new Dictionary<string, int>();
        private static bool _dumpedMembers;
        private static int _lines;

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            if (!HotfixParams.DebugAiDesignProbe)
            {
                HotfixLog.Info("[SMS.1.G1] DebugAiDesignProbe: disabled by config");
                return;
            }

            try
            {
                // ⚠ 绝不能用 AccessTools.TypeByName / Assembly.GetTypes()：那会对**所有程序集**
                //   枚举类型，产生大量 ReflectionTypeLoadException / "Could not load type" 黄字
                //   （2026-10-18 实测：444 条警告）。只在 CampaignController 内部找嵌套类型即可。
                Type t = null;
                Type moveNextOwner = null;

                try
                {
                    // ① 精确名字（编译器生成的嵌套类）
                    // 实测的代理类型名（尖括号被 Il2CppInterop 换成下划线）
                    t = AccessTools.Inner(typeof(CampaignController), "_GenerateRandomDesigns_d__202");
                    if (t == null)
                        t = AccessTools.Inner(typeof(CampaignController), "<GenerateRandomDesigns>d__202");
                }
                catch { }

                if (t == null)
                {
                    // ② 只遍历 CampaignController 自己的嵌套类型（单类型，安全）
                    try
                    {
                        foreach (var nt in typeof(CampaignController).GetNestedTypes(
                                     BindingFlags.Public | BindingFlags.NonPublic))
                        {
                            if (nt.Name != null && nt.Name.Contains("GenerateRandomDesigns"))
                            { t = nt; break; }
                        }
                    }
                    catch { }
                }

                MethodInfo moveNext = t != null ? AccessTools.Method(t, "MoveNext") : null;
                if (moveNext == null)
                {
                    HotfixLog.Warn("[SMS.1.G1] could not locate <GenerateRandomDesigns>d__202::MoveNext -- probe inactive");
                    return;
                }

                harmony.Patch(moveNext, postfix: new HarmonyMethod(
                    AccessTools.Method(typeof(DebugAiDesignProbe), nameof(Postfix_MoveNext))));

                HotfixLog.Info("[SMS.1.G1] DebugAiDesignProbe: armed on " + moveNext.DeclaringType.FullName
                               + "::MoveNext  (read-only)");
            }
            catch (Exception e)
            {
                HotfixLog.Error("[SMS.1.G1] install failed: " + e);
            }
        }

        internal static void Postfix_MoveNext(object __instance)
        {
            if (__instance == null) return;

            try
            {
                if (!_dumpedMembers) { _dumpedMembers = true; DumpMembers(__instance); }

                int state = AsInt(Read(__instance, "<>1__state", "_1__state", "state"));
                object player = Read(__instance, "player");
                object shipType = Read(__instance, "<shipType>5__3", "shipType_5__3", "<shipType>_5__3", "shipType");
                int tries = AsInt(Read(__instance, "<tries>5__4", "tries_5__4", "<tries>_5__4", "tries"));

                string pName = NameOf(player);
                string tName = NameOf(shipType);

                if (string.IsNullOrEmpty(tName) || tName == "<null>") return;

                string key = pName + "|" + tName;
                int prev;
                bool first = !_seen.TryGetValue(key, out prev);
                _seen[key] = tries;

                // 只在"首次见到该 (玩家,舰种)"或"tries 增加（重试）"时记录
                if (!first && tries <= prev) return;
                if (_lines++ >= 400) return;

                HotfixLog.Info("[SMS.1.G1] AI design gen: player=" + pName + " type=" + tName
                               + "  tries=" + tries + "  state=" + state
                               + (first ? "  (first for this type)" : "  (retry)"));
            }
            catch (Exception e)
            {
                if (_lines++ < 5) HotfixLog.Warn("[SMS.1.G1] Postfix_MoveNext: " + e.Message);
            }
        }

        // ---------------- 反射辅助 ----------------

        private static object Read(object o, params string[] names)
        {
            var t = o.GetType();
            const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

            for (int i = 0; i < names.Length; i++)
            {
                try
                {
                    var p = t.GetProperty(names[i], F);
                    if (p != null && p.CanRead) return p.GetValue(o);
                }
                catch { }
                try
                {
                    var f = t.GetField(names[i], F);
                    if (f != null) return f.GetValue(o);
                }
                catch { }

                // 语义兜底：按包含关系找一次
                try
                {
                    foreach (var p in t.GetProperties(F))
                        if (p.Name != null && p.Name.Contains(names[i].Replace("<", "").Replace(">", "")))
                            return p.GetValue(o);
                }
                catch { }
            }
            return null;
        }

        private static int AsInt(object v)
        {
            try { return v == null ? -1 : Convert.ToInt32(v); } catch { return -1; }
        }

        private static string NameOf(object o)
        {
            if (o == null) return "<null>";
            try
            {
                var t = o.GetType();
                const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
                foreach (var n in new string[] { "name", "vesselName", "aiTextName" })
                {
                    var p = t.GetProperty(n, F);
                    if (p != null && p.CanRead) { var v = p.GetValue(o); if (v != null) return v.ToString(); }
                }
                return t.Name;
            }
            catch { return "<err>"; }
        }

        private static void DumpMembers(object o)
        {
            try
            {
                var t = o.GetType();
                const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
                var names = new List<string>();
                foreach (var p in t.GetProperties(F))
                    if (p.Name != null && (p.Name.Contains("5__") || p.Name.Contains("state") || p.Name == "player" || p.Name == "prewarming"))
                        names.Add("P:" + p.Name);
                foreach (var f in t.GetFields(F))
                    if (f.Name != null && (f.Name.Contains("5__") || f.Name.Contains("state") || f.Name == "player" || f.Name == "prewarming"))
                        names.Add("F:" + f.Name);
                HotfixLog.Info("[SMS.1.G1] members of " + t.FullName + " (" + names.Count + "): "
                               + string.Join(", ", names.ToArray()));
            }
            catch (Exception e) { HotfixLog.Warn("[SMS.1.G1] DumpMembers: " + e.Message); }
        }
    }
}
