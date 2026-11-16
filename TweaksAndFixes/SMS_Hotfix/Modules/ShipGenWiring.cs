using System;
using System.Reflection;
using HarmonyLib;
using Il2Cpp;
using TAFHotfixBatch1;

namespace TweaksAndFixes
{
    /// <summary>
    /// [SMS.1.SHIPGEN] ShipGen 限制的**真正接入点**（移植 Beta6 的两个小钩子）。
    ///
    /// 重要发现（2026-10-26）：Beta6 的 `Patch_ShipGenRandom.cs`（762 行）**并不是**重写 AI 随机选件流程，
    /// 它只是给 `Ship._GenerateRandomShip_d__573.MoveNext` 加 Prefix/Postfix，用于：
    ///   · 记录状态（速度/船宽/吃水）
    ///   · 在特定状态做 `ClampShipStats` / `OptimizeComponents`（部件降级优化、鱼雷尺寸偏好）
    ///   · 生成结束打印诊断
    /// 而 `shipgen_limit` 的**约束生效点**只有两个：
    ///   ① `Part.CanPlace(...)`（3 参数重载）→ `ShipGen.CanAdd(part, ref denyReason)`
    ///   ② `Ship.IsPartAvailable(PartData, Player, ShipType, Ship)` → `ShipGen.PartAvailible(ship, part, ref __result)`
    /// 两者都只在 `GameManager.IsAutodesignActive`（AI 自动设计）时拦截。
    ///
    /// 本文件实现 ②① 两个钩子，由参数 `shipgen_limits` 控制（默认 0 = 不介入）。
    /// </summary>
    internal static class ShipGenWiring
    {
        internal static bool Enabled()
        {
            try { return HotfixParams.ShipGenLimits >= 1; } catch { return false; }
        }

        internal static bool Autodesign()
        {
            try { return GameManager.IsAutodesignActive; } catch { return false; }
        }
    }

    /// <summary>① 部件放置检查：AI 自动设计时用 shipgen_limit 决定能不能放。</summary>
    [HarmonyPatch(typeof(Part))]
    internal static class Patch_Part_CanPlaceShipGen
    {
        /// <summary>Part.CanPlace 有多个重载，取参数个数为 3 的那个（Beta6 同款做法）。</summary>
        internal static MethodBase TargetMethod()
        {
            try
            {
                foreach (var m in AccessTools.GetDeclaredMethods(typeof(Part)))
                {
                    if (m.Name != "CanPlace") continue;
                    if (m.GetParameters().Length == 3)
                    {
                        try
                        {
                            var ps = m.GetParameters();
                            HotfixLog.Info("[SMS.1.SHIPGEN] hooking Part.CanPlace("
                                + ps[0].ParameterType.Name + ", "
                                + ps[1].ParameterType.Name + ", "
                                + ps[2].ParameterType.Name + ")");
                        }
                        catch { }
                        return m;
                    }
                }
            }
            catch (Exception e) { HotfixLog.Warn("[SMS.1.SHIPGEN] CanPlace lookup: " + e.Message); }
            return null;
        }

        internal static bool Prefix(Part __instance, ref string denyReason, ref bool __result)
        {
            try
            {
                if (!ShipGenWiring.Enabled()) return true;
                if (!ShipGenWiring.Autodesign()) return true;

                // ★ 关键修正：只有**我们拒绝**时才跳过原版；
                //   允许时返回 true 让原版继续做碰撞/放置检查，
                //   否则会绕过游戏的碰撞判定（实测：三个烟囱重叠在一起）。
                if (!Modules.ShipGen.CanAdd(__instance, ref denyReason))
                {
                    __result = false;
                    return false;
                }
                return true;
            }
            catch (Exception e)
            {
                HotfixLog.Warn("[SMS.1.SHIPGEN] CanPlace prefix: " + e.Message);
                return true;
            }
        }
    }

    /// <summary>② 部件可用性检查（船体级）：AI 自动设计时过滤掉不允许的部件。</summary>
    [HarmonyPatch(typeof(Ship))]
    internal static class Patch_Ship_IsPartAvailableShipGen
    {
        [HarmonyPrefix]
        [HarmonyPatch("IsPartAvailable", new Type[]
        {
            typeof(PartData), typeof(Player), typeof(ShipType), typeof(Ship)
        })]
        internal static bool Prefix_IsPartAvailable(PartData part, Player player, ShipType shipType, Ship ship,
                                                    ref bool __result)
        {
            try
            {
                if (!ShipGenWiring.Enabled()) return true;
                if (!ShipGenWiring.Autodesign()) return true;

                bool allowed = true;
                bool ret = Modules.ShipGen.PartAvailible(ship, part, ref allowed);
                // 只有"我们明确不允许"时才短路；其余交回原版判定
                if (ret == false || allowed == false)
                {
                    __result = false;
                    return false;
                }
                return true;
            }
            catch (Exception e)
            {
                HotfixLog.Warn("[SMS.1.SHIPGEN] IsPartAvailable prefix: " + e.Message);
                return true;
            }
        }
    }
}
