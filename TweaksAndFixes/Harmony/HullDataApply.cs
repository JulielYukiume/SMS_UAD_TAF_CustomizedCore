using System;
using System.Reflection;
using HarmonyLib;
using Il2Cpp;
using TAFHotfixBatch1;
using UnityEngine;

namespace TweaksAndFixes
{
    /// <summary>
    /// [SMS.1.WL] Beta4 的 hullData 应用层 —— 移植 3.22 Beta #4 的提交 `4090d68`：
    ///   · `waterline`：给"船体/分段在构造器里的 Y 位置"加偏移
    ///   · `stabilityForward`：写入 `hull.hullInfo.stabilityForward`
    ///
    /// 为什么这段重要：
    ///   Beta #4 的发布说明里写了 **"Fix miscolored fore/aft sections on some hulls in constructor"**，
    ///   而同一提交（`4090d68`）正是引入 `hullData.csv` 的提交 —— 也就是说这句"色差修复"
    ///   极可能就是 **waterline 垂直偏移** 让相邻分段不再共面（共面 ⇒ z-fighting/暗色带）。
    ///
    /// 之前我们只移植了 `HullData`（数据）与 `RefreshHull`（分段），**漏掉了这里的应用代码**，
    /// 所以 `waterline` / `stabilityForward` 一直是"有数据但不生效"。
    ///
    /// 实现说明：
    ///   原版补丁挂在 `Ship.__c__DisplayClass628_0._Update_b__6`（编译器生成的闭包方法），
    ///   类名里的数字在不同版本会变，所以这里改为**运行时按方法名查找**：
    ///   在 `Ship` 的所有嵌套类型里找名为 `<Update>b__6`、返回 `Vector3` 的方法再挂 Postfix。
    /// </summary>
    internal static class HullDataApply
    {
        internal static Ship UpdatingShip = null;

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            if (!HotfixParams.HullDataApply)
            {
                HotfixLog.Info("[SMS.1.WL] disabled");
                return;
            }

            int installed = 0;

            // ① Ship.Update 前缀：记录正在更新的船（供 ② 使用）
            try
            {
                var mUpdate = AccessTools.Method(typeof(Ship), nameof(Ship.Update));
                if (mUpdate != null)
                {
                    harmony.Patch(mUpdate, prefix: new HarmonyMethod(
                        AccessTools.Method(typeof(HullDataApply), nameof(Prefix_Update))));
                    installed++;
                }
            }
            catch (Exception e) { HotfixLog.Warn("[SMS.1.WL] Update: " + e.Message); }

            // ② `<Update>b__6`（Vector3 返回）：加 waterline 偏移
            try
            {
                MethodInfo target = null;
                foreach (var nt in typeof(Ship).GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
                {
                    foreach (var mi in nt.GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                                                    | BindingFlags.Instance | BindingFlags.Static))
                    {
                        if (mi.Name == "<Update>b__6" && mi.ReturnType == typeof(Vector3))
                        {
                            target = mi;
                            break;
                        }
                    }
                    if (target != null) break;
                }

                if (target != null)
                {
                    harmony.Patch(target, postfix: new HarmonyMethod(
                        AccessTools.Method(typeof(HullDataApply), nameof(Postfix_UpdateB6))));
                    installed++;
                    HotfixLog.Info("[SMS.1.WL] patched " + target.DeclaringType?.Name + "." + target.Name);
                }
                else
                {
                    HotfixLog.Warn("[SMS.1.WL] <Update>b__6 not found -- waterline offset inactive");
                }
            }
            catch (Exception e) { HotfixLog.Warn("[SMS.1.WL] b__6: " + e.Message); }

            // ③ CalcInstability 前缀：写入 stabilityForward
            try
            {
                var mCi = AccessTools.Method(typeof(Ship), nameof(Ship.CalcInstability));
                if (mCi != null)
                {
                    harmony.Patch(mCi, prefix: new HarmonyMethod(
                        AccessTools.Method(typeof(HullDataApply), nameof(Prefix_CalcInstability))));
                    installed++;
                }
            }
            catch (Exception e) { HotfixLog.Warn("[SMS.1.WL] CalcInstability: " + e.Message); }

            HotfixLog.Info("[SMS.1.WL] HullDataApply armed on " + installed + "/3 hook(s)");
        }

        internal static void Prefix_Update(Ship __instance)
        {
            try { UpdatingShip = __instance; } catch { }
        }

        internal static void Postfix_UpdateB6(ref Vector3 __result)
        {
            try
            {
                var s = UpdatingShip;
                if (s == null || s.hull == null || s.hull.data == null) return;

                string model = s.hull.data.model;
                if (string.IsNullOrEmpty(model)) return;
                if (!HullData._Data.ContainsKey(model)) return;

                float wl = HullData._Data[model].waterline;
                if (Math.Abs(wl) < 0.0001f) return;

                __result += new Vector3(0f, wl, 0f);
            }
            catch { }
        }

        internal static void Prefix_CalcInstability(Ship __instance)
        {
            try
            {
                if (__instance == null || __instance.hull == null) return;

                if (__instance.hull.hullInfo == null)
                    __instance.hull.hullInfo = new HullInfo();

                string model = __instance.hull.data != null ? __instance.hull.data.model : null;
                if (string.IsNullOrEmpty(model)) return;
                if (!HullData._Data.ContainsKey(model)) return;

                __instance.hull.hullInfo.stabilityForward = HullData._Data[model].stabilityForward;
            }
            catch (Exception e) { HotfixLog.Warn("[SMS.1.WL] CalcInstability prefix: " + e.Message); }
        }
    }
}
