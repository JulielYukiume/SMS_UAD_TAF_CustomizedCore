using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Il2Cpp;

namespace TAFHotfixBatch1.Modules
{
    /// <summary>
    /// 【A 方案 · **dry-run 只读探针**】
    ///
    /// 目的：验证能否在**存档反序列化出口**（`CampaignController_StoreFormatter::Deserialize`，
    /// 游戏做"船/设计"分类**之前**）安全地拿到 `CampaignController.Store`。
    ///
    /// 背景：`_LoadCampaign_d__98::MoveNext` 实测到 **state=3** 才拿得到 store，此时分类已完成
    /// ⇒ store 层的 `designId` 修复会被"设计对象→再序列化"写回 0（第七/八轮实证）。
    /// Deserialize 是唯一更早的时机，但上一版在此打 Postfix 曾导致**读档崩溃**。
    ///
    /// 本版三重防护：
    ///   ① **不用 `AccessTools.TypeByName`**（它会遍历所有程序集，产生大量
    ///      `ReflectionTypeLoadException` 黄字并可能触发不稳定）——改为 `Assembly.GetType(名字)` 直取；
    ///   ② **只读**：不写任何字段，纯统计；
    ///   ③ 每次字段访问独立 try/catch，异常只丢一行日志。
    ///
    /// 判据：日志出现 `[A-probe] call #1 : entries=… designSlot=… shipLike=…` 即说明
    /// **时机可用**（能在此之前拿到完整 store）；若崩溃，则 A 路线否决、转 B（事后实例化）。
    /// </summary>
    internal static class DeserializeProbe
    {
        private static int _calls;
        private static HarmonyLib.Harmony _harmony;
        private static bool _patched;
        private static bool _gaveUp;
        private static int _attempts;

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            _harmony = harmony;
            if (!HotfixParams.DeserializeProbe)
            {
                HotfixLog.Info("[A-probe] disabled by config");
                return;
            }
            TryPatchNow("install");
        }

        /// <summary>
        /// 延迟安装：`Il2CppMessagePack` 在 OnInitializeMelon 时通常尚未加载，
        /// 因此每次状态机推进时再试一次；一旦打上，对**下一次读档**生效。
        /// </summary>
        internal static void TryPatchNow(string when)
        {
            // ★ 关键修复（2026-10-17 第 13 轮崩溃）：必须**每次**检查配置开关。
            //   此前只检查了一次（Install），导致被禁用的探针经"延迟重试"复活并再次引发读档崩溃。
            if (!HotfixParams.DeserializeProbe)
            {
                _gaveUp = true;      // 明确放弃，不再尝试
                return;
            }
            if (_patched || _gaveUp || _harmony == null) return;

            try
            {
                Type fmt = FindFormatterType();
                if (fmt == null)
                {
                    _attempts++;
                    if (_attempts <= 3)
                        HotfixLog.Info("[A-probe] formatter not resolvable yet (attempt " + _attempts
                                       + ", when=" + when + ")");
                    return;
                }

                MethodInfo m = fmt.GetMethod("Deserialize",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (m == null)
                {
                    _gaveUp = true;
                    HotfixLog.Warn("[A-probe] Deserialize not found on " + fmt.FullName);
                    return;
                }

                _harmony.Patch(m, postfix: new HarmonyMethod(
                    AccessTools.Method(typeof(DeserializeProbe), nameof(Postfix_Deserialize))));

                _patched = true;
                HotfixLog.Warn("[A-probe] patched " + fmt.FullName + "::Deserialize  (READ-ONLY dry-run)"
                               + "  [via " + when + "]  => active from the NEXT save load");
            }
            catch (Exception e)
            {
                HotfixLog.Error("[A-probe] patch attempt (" + when + ") failed: " + e.Message);
            }
        }

        /// <summary>
        /// 查找 formatter 类型。实测该类型位于 **Assembly-CSharp**（不是 Il2CppMessagePack）；
        /// 而"按全名直取"在此代理程序集上失手 ⇒ 退化为**只枚举 Assembly-CSharp 一个程序集**，
        /// 用 `ReflectionTypeLoadException.Types` 过滤加载失败的项（不影响其它程序集，不产生全量噪声）。
        /// </summary>
        private static Type FindFormatterType()
        {
            string[] fullNames = {
                "MessagePack.Formatters.CampaignController_StoreFormatter",
                "CampaignController_StoreFormatter"
            };

            Assembly cs = null;
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                try { if (a.GetName().Name == "Assembly-CSharp") { cs = a; break; } } catch { }
            }
            if (cs == null) { try { cs = typeof(CampaignController).Assembly; } catch { } }

            if (cs == null)
            {
                HotfixLog.Warn("[A-probe] Assembly-CSharp not loaded");
                return null;
            }

            // 1) 先按全名直取
            foreach (string n in fullNames)
            {
                try { Type t = cs.GetType(n, false); if (t != null) return t; } catch { }
            }

            // 2) 只枚举这一个程序集
            Type[] types = null;
            try { types = cs.GetTypes(); }
            catch (ReflectionTypeLoadException e) { types = e.Types; }
            catch (Exception e) { HotfixLog.Warn("[A-probe] GetTypes failed: " + e.Message); }

            if (types != null)
            {
                int seen = 0;
                foreach (Type t in types)
                {
                    if (t == null) continue;
                    try
                    {
                        if (t.Name == "CampaignController_StoreFormatter")
                        {
                            HotfixLog.Warn("[A-probe] found formatter by enumeration: " + t.FullName);
                            return t;
                        }
                        if (t.Name.Contains("StoreFormatter")) seen++;
                    }
                    catch { }
                }
                HotfixLog.Warn("[A-probe] enum of " + cs.GetName().Name + " : types=" + types.Length
                               + " , *StoreFormatter found=" + seen + " (none named CampaignController_StoreFormatter)");
            }

            return null;
        }

        internal static void Postfix_Deserialize(CampaignController.Store __result)
        {
            _calls++;
            try
            {
                if (__result == null)
                {
                    HotfixLog.Info("[A-probe] call #" + _calls + " -> null store");
                    return;
                }

                var ships = __result.Ships;
                if (ships == null)
                {
                    HotfixLog.Info("[A-probe] call #" + _calls + " -> null Ships");
                    return;
                }

                int n = ships.Count;
                int designSlot = 0, shipLike = 0, dupDesignIds = 0, withPort = 0;
                var seen = new HashSet<string>();

                for (int i = 0; i < n; i++)
                {
                    Ship.Store s = null;
                    try { s = ships[i]; } catch { continue; }
                    if (s == null) continue;

                    bool isDesign = true;
                    try { isDesign = s.designId == Il2CppSystem.Guid.Empty; } catch { }
                    if (!isDesign) continue;
                    designSlot++;

                    try
                    {
                        int st = (int)s.status;
                        if (st != 0 && st != 1) shipLike++;
                    }
                    catch { }

                    try { if (!string.IsNullOrEmpty(s.portLocation)) withPort++; } catch { }

                    try
                    {
                        string id = s.id.ToString();
                        if (id != "00000000-0000-0000-0000-000000000000" && !seen.Add(id)) dupDesignIds++;
                    }
                    catch { }
                }

                HotfixLog.Warn("[A-probe] call #" + _calls + " : entries=" + n
                               + " designSlot=" + designSlot + " shipLike=" + shipLike
                               + " withPort=" + withPort + " dupDesignIds=" + dupDesignIds);
            }
            catch (Exception e)
            {
                HotfixLog.Error("[A-probe] postfix: " + e.Message);
            }
        }
    }
}
