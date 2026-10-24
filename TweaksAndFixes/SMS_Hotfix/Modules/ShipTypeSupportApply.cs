using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2Cpp;

namespace TAFHotfixBatch1.Modules
{
    /// <summary>
    /// 非原版舰种：**UI 序号补全**（对应 ShipTypesSupport 的前两个 UI 补丁）。
    ///
    /// 规则（2026-10-24 明确）：
    ///   · 原版 6 战斗舰种（bb/bc/ca/cl/dd/tb）**保持原版序号不变**（0..5，绝不改写）；
    ///   · 其余舰种（来自当前 `shipTypes.csv`，即 `G.GameData.shipTypes` 中不属于上面 6 种的条目）
    ///     分配 **20 + 序号** 的 int（20、21、22…），与"字典枚举顺序"绑定；
    ///   · 两个方向共用**同一张表**，保证 `int → type` 与 `type → int` 自洽。
    ///
    /// 关键修正（上一版 bug）：
    ///   上一版把"扩展表"当成全量顺序表，导致 `cb → 0`，而 `0 → bb`，映射互相打脸。
    ///   现在**只追加、绝不占用 0..5**，并且表未就绪时不缓存、不返回。
    ///
    /// 只在原方法返回 null / -1 时介入；原版能解析的一律不动。
    /// </summary>
    internal static class ShipTypeSupportApply
    {
        private const int ExtBase = 20;

        private static readonly Dictionary<int, ShipType> _intToType = new Dictionary<int, ShipType>();
        private static readonly Dictionary<string, int> _typeToInt = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private static bool _built;
        private static int _callInt, _callNum;

        private static readonly string[] Vanilla = { "tb", "dd", "cl", "ca", "bc", "bb" };

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            if (!HotfixParams.ShipTypeApply) { HotfixLog.Info("[SMS.1.Y2] ShipTypeSupportApply: disabled"); return; }
            int n = 0;
            n += Patch(harmony, typeof(Ui), "GetShipTypeFromIntNumber", nameof(Postfix_GetTypeFromInt));
            n += Patch(harmony, typeof(Ui), "GetIntNumberFromShipType", nameof(Postfix_GetNumFromType));
            n += Patch(harmony, typeof(Ui), "IsShipTypeActiveForCurrentPlayer", nameof(Postfix_IsActive));
            // ★ 主菜单阶段（GameData 已加载、hull 自检已做过）就把扩展表建好
            try
            {
                var cs = AccessTools.Method(typeof(GameManager), "ChangeState");
                if (cs != null)
                {
                    harmony.Patch(cs, postfix: new HarmonyMethod(AccessTools.Method(typeof(ShipTypeSupportApply), nameof(Postfix_ChangeState))));
                    HotfixLog.Info("[SMS.1.Y2] ChangeState hook armed (build table at menu)");
                }
            }
            catch (Exception e) { HotfixLog.Warn("[SMS.1.Y2] ChangeState hook: " + e.Message); }

            HotfixLog.Info("[SMS.1.Y2] ShipTypeSupportApply v3: armed on " + n + "/3 (原版 0..5 不动; 其它从 " + ExtBase + " 起)");
        }

        private static int Patch(HarmonyLib.Harmony harmony, Type t, string name, string patchName)
        {
            try
            {
                var m = AccessTools.Method(t, name);
                if (m == null) { HotfixLog.Warn("[SMS.1.Y2] " + t.Name + "." + name + " not found"); return 0; }
                harmony.Patch(m, postfix: new HarmonyMethod(AccessTools.Method(typeof(ShipTypeSupportApply), patchName)));
                return 1;
            }
            catch (Exception e) { HotfixLog.Warn("[SMS.1.Y2] patch " + name + ": " + e.Message); return 0; }
        }

        /// <summary>构建扩展表：只收录"非原版 6 种"的舰种，序号从 ExtBase 起。</summary>
        private static void EnsureTable()
        {
            if (_built) return;
            Il2CppSystem.Collections.Generic.Dictionary<string, ShipType> dict = null;
            try { dict = G.GameData != null ? G.GameData.shipTypes : null; } catch { }
            if (dict == null || dict.Count == 0) return;      // 数据未就绪：不缓存，等下次

            int next = ExtBase;
            int added = 0;
            try
            {
                foreach (var kv in dict)
                {
                    if (kv.Value == null) continue;
                    string id = kv.Key != null ? kv.Key.ToLowerInvariant() : null;
                    if (string.IsNullOrEmpty(id)) continue;
                    if (IsVanilla(id)) continue;             // 原版 6 种：绝不改序号
                    if (_typeToInt.ContainsKey(id)) continue;
                    _intToType[next] = kv.Value;
                    _typeToInt[id] = next;
                    next++; added++;
                }
            }
            catch (Exception e) { HotfixLog.Warn("[SMS.1.Y2] build table: " + e.Message); return; }

            _built = true;
            HotfixLog.Info("[SMS.1.Y2] extension table built: " + added + " type(s), range " + ExtBase + ".." + (next - 1));
            int shown = 0;
            foreach (var kv in _typeToInt)
            {
                if (shown++ >= 30) { HotfixLog.Info("[SMS.1.Y2]   ..."); break; }
                HotfixLog.Info("[SMS.1.Y2]   " + kv.Value + " <= '" + kv.Key + "'");
            }
        }

        // ① int -> ShipType（原版 null 时才补；绝不改写 0..5 的原版解析）
        internal static void Postfix_GetTypeFromInt(int __0, ref ShipType __result)
        {
            try
            {
                _callInt++;
                if (__result != null)
                {
                    if (_callInt <= 40 || HotfixParams.Verbose)
                        HotfixLog.Info("[SMS.1.Y2] int->ShipType: arg=" + __0 + " -> '" + __result.name + "' (vanilla)  n=" + _callInt);
                    return;
                }

                EnsureTable();
                ShipType ext;
                if (_intToType.TryGetValue(__0, out ext) && ext != null)
                {
                    __result = ext;
                    HotfixLog.Info("[SMS.1.Y2] int->ShipType: arg=" + __0 + " -> '" + ext.name + "' (extended)");
                }
                else if (_callInt <= 40 || HotfixParams.Verbose)
                {
                    HotfixLog.Info("[SMS.1.Y2] int->ShipType: arg=" + __0 + " -> null (no extension)  built=" + _built);
                }
            }
            catch { }
        }

        // ② ShipType -> int（原版 -1 时才补）
        internal static void Postfix_GetNumFromType(ShipType __0, ref int __result)
        {
            try
            {
                _callNum++;
                string id = null;
                try { id = __0 != null ? __0.name : null; } catch { }
                // ★ 实测：原版对未知舰种返回 **0（= bb）**，不是 -1！
                //   因此这里不能只看 "< 0"：只要"名字不是原版 6 种"，就用扩展表覆盖。
                if (!string.IsNullOrEmpty(id) && !IsVanilla(id))
                {
                    EnsureTable();
                    int ext2;
                    if (_typeToInt.TryGetValue(id, out ext2))
                    {
                        if (__result != ext2)
                            HotfixLog.Info("[SMS.1.Y2] ShipType->int: '" + id + "' " + __result + " -> " + ext2 + " (override non-vanilla)");
                        __result = ext2;
                        return;
                    }
                }

                if (__result >= 0)
                {
                    if (_callNum <= 40 || HotfixParams.Verbose)
                        HotfixLog.Info("[SMS.1.Y2] ShipType->int: '" + id + "' -> " + __result + " (vanilla)  n=" + _callNum);
                    return;
                }
                if (string.IsNullOrEmpty(id)) return;

                EnsureTable();
                int ext;
                if (_typeToInt.TryGetValue(id, out ext))
                {
                    __result = ext;
                    HotfixLog.Info("[SMS.1.Y2] ShipType->int: '" + id + "' -> " + ext + " (extended)");
                }
                else if (_callNum <= 40 || HotfixParams.Verbose)
                {
                    HotfixLog.Info("[SMS.1.Y2] ShipType->int: '" + id + "' -> -1 (no extension)  built=" + _built);
                }
            }
            catch { }
        }

        // ③ 可用性：只对"扩展序号"补；原版序号保持原判
        internal static void Postfix_IsActive(int numberInList, ref bool __result)
        {
            if (__result) return;
            try
            {
                EnsureTable();
                if (_intToType.ContainsKey(numberInList))
                {
                    __result = true;
                    HotfixLog.Info("[SMS.1.Y2] IsShipTypeActive(" + numberInList + ") false -> true (extended type)");
                }
            }
            catch { }
        }

        private static bool IsVanilla(string id)
        {
            foreach (var v in Vanilla) if (v == id) return true;
            return false;
        }
        internal static void Postfix_ChangeState(GameManager.GameState newState)
        {
            try
            {
                string st = newState.ToString();
                if (st == "Menu" || st == "Loading" || st == "LoadingCustom")
                {
                    bool had = _built;
                    EnsureTable();
                    if (!had && _built) HotfixLog.Info("[SMS.1.Y2] table built at state '" + st + "' (early)");
                }
            }
            catch { }
        }

    }
}
