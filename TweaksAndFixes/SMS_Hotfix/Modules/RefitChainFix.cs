using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2Cpp;

namespace TAFHotfixBatch1.Modules
{
    /// <summary>
    /// Module A —— REP-1：赔偿舰 / 易主舰 的**改造链重建**（方案 A，用户采纳。
    /// 定性："TAF 有 TAF 的做法" —— 自建链数据重建，而非移植原版旧例或放宽判据）。
    ///
    /// 已定论的根因（见 taf/docs/21 §A.2.1）：
    ///   赔偿/易主路径 = CampaignController::TransferShipToNewOwner
    ///                   → PlayerController::CloneDesign/2（27 条，唯一调用者）
    ///                   → PlayerController::CloneShipRaw/6（130 条，1.5.1.6→1.7.0.0 逐指令未变）
    ///                   → Ship::Create → ToStore → **Guid::NewGuid** → FromStore
    ///   ⇒ 复制件是**用全新 Guid 造出的新设计，改造链保持空**（自 1.5.1.6 起即如此，非 1.7 引入）。
    ///   而 1.7 把"能否改造"的判据改成「**候选设计的 refitDesignListID 须含"该船当前设计的 id"**」
    ///   （CampaignFleetWindow::&lt;GetRefitShipFleet&gt;b__90_0, 0x66E2E0），
    ///   并**删除**了旧存档修链兜底 FixRefitDesognOnOldSaves（1.6.1.1 0x80E8D0）。
    ///   ⇒ 赔偿舰指向无链复制件 ⇒ 原始设计的任何派生改造都永远无法改造它 ⇒ **永久断链**。
    ///
    /// 本模块做法（doc 19 的链语义："改装设计会将其原设计（或前一级）的 ID 追加到末尾，
    /// 所以第一条通常是原造船设计"）：
    ///   copy.refitDesignListID = source.refitDesignListID + [source.id]
    ///
    /// ⚠ 安全红线（doc 21 §D.5）：
    ///   · **只追加、绝不删条目**；
    ///   · **绝不调用 TryToEraseVessel / Erase**、绝不触碰舰船对象集合；
    ///   · 克隆产物同样要过 Ship::FromStore（LoadShips 会用它验船，失败即静默擦除）
    ///     ⇒ 补链只动 refitDesignListID，**不改其它字段**；
    ///   · mode=log 时只记录不改。
    /// </summary>
    internal static class RefitChainFix
    {
        internal static void Install(HarmonyLib.Harmony harmony)
        {
            if (!HotfixParams.RefitChainFix)
            {
                HotfixLog.Info("[SMS.1.A] RefitChainFix: disabled by config");
                return;
            }

            try
            {
                var target = AccessTools.Method(typeof(PlayerController), "CloneDesign",
                                                new Type[] { typeof(Ship), typeof(Player) });
                if (target == null)
                {
                    HotfixLog.Error("[SMS.1.A] CloneDesign(Ship, Player) not found -- module A inactive");
                    return;
                }

                harmony.Patch(target, postfix: new HarmonyMethod(
                    AccessTools.Method(typeof(RefitChainFix), nameof(Postfix_CloneDesign))));

                HotfixLog.Info("[SMS.1.A] RefitChainFix: patched PlayerController::CloneDesign(Ship, Player)"
                               + "  (mode=" + HotfixParams.Mode + ")");
            }
            catch (Exception e)
            {
                HotfixLog.Error("[SMS.1.A] install failed: " + e);
            }
        }

        /// <summary>
        /// 赔偿/易主产生复制件后，把**源设计的血脉**补回复制件的改造链。
        /// </summary>
        internal static void Postfix_CloneDesign(Ship ship, Player overridePlayer, Ship __result)
        {
            if (ship == null || __result == null) return;

            try
            {
                int before = __result.refitDesignListID == null ? -1 : __result.refitDesignListID.Count;

                // 目标链 = 源链（若源为空则从空开始） + 源自身的 id
                var rebuilt = new Il2CppSystem.Collections.Generic.List<Il2CppSystem.Guid>();

                Il2CppSystem.Collections.Generic.List<Il2CppSystem.Guid> src = ship.refitDesignListID;
                if (src != null)
                {
                    for (int i = 0; i < src.Count; i++)
                    {
                        Il2CppSystem.Guid g = src[i];
                        if (!Contains(rebuilt, g)) rebuilt.Add(g);
                    }
                }

                Il2CppSystem.Guid srcId = ship.id;
                if (!Contains(rebuilt, srcId)) rebuilt.Add(srcId);

                if (HotfixParams.IsApply)
                {
                    __result.refitDesignListID = rebuilt;
                    HotfixLog.Info(string.Format("[SMS.1.A] chain rebuilt: '{0}' -> '{1}'  ({2} -> {3} entries)",
                        ship.vesselName, __result.vesselName, before, rebuilt.Count));

                    // ★ 2026-10-18：新建的**改造设计副本**把 status 统一为 None(0)。
                    //   原因：改造/赔偿设计的默认状态是 Normal(1)，而 B2 的"找回丢设计id的船"判定
                    //   曾把 status==1 的这类设计误当成实船重挂 ⇒ 存档里出现 designId!=0 的幽灵船
                    //   （内蒙古/海鹄/宵月/六連 等 12 条）。把副本状态置 0 后，任何按 status
                    //   区分"船 / 设计"的逻辑都不会再误判。
                    //   仅在对象确实是设计时执行；玩家视角不受影响（设计仍可查看/改造/建造）。
                    try
                    {
                        if (__result.isDesign && (int)__result.status != 0)
                        {
                            int oldStatus = (int)__result.status;
                            __result.status = (Ship.Status)0;
                            HotfixLog.Info(string.Format("[SMS.1.A] design status normalised: '{0}'  {1} -> 0",
                                __result.vesselName, oldStatus));
                        }
                    }
                    catch (Exception ex)
                    {
                        HotfixLog.Debug("[SMS.1.A] status normalise failed: " + ex.Message);
                    }
                    // ★ 2026-10-18（用户方案）：**复制赔偿舰设计**时改写 `playerOriginal`。
                    //   依据 `docs/30`：`playerOriginal` 决定"读取哪一势力的船体科技树、加载哪国炮模型"。
                    //   把副本的原始国别改成**复制者（玩家）**后：
                    //     · 不能再从原国别重选船体；
                    //     · 火炮等部件按玩家国别取模型；
                    //     · 其余（船体/部件/吨位）保持不变。
                    //   仅对"源是赔偿设计"的复制生效；`mode=log` 只打印。
                    try
                    {
                        if (ship.isReparationDesign)
                        {
                            Player target = overridePlayer;
                            if (target == null) { try { target = __result.player; } catch { } }

                            PlayerData pd = null;
                            if (target != null) { try { pd = target.data; } catch { } }

                            if (pd == null)
                            {
                                HotfixLog.Warn("[SMS.1.A] reparation copy: cannot resolve player data, playerOriginal NOT changed");
                            }
                            else if (HotfixParams.IsApply)
                            {
                                __result.playerOriginal = pd;
                                HotfixLog.Info("[SMS.1.A] reparation copy: playerOriginal -> '" + pd.name
                                               + "'  ('" + __result.vesselName + "')");
                            }
                            else
                            {
                                HotfixLog.Info("[SMS.1.A][log] would set playerOriginal -> '" + pd.name
                                               + "'  ('" + __result.vesselName + "')");
                            }
                        }
                    }
                    catch (Exception ex) { HotfixLog.Warn("[SMS.1.A] playerOriginal rewrite failed: " + ex.Message); }
                }
                else
                {
                    HotfixLog.Info(string.Format("[SMS.1.A][log] would rebuild chain: '{0}' -> '{1}'  ({2} -> {3} entries)",
                        ship.vesselName, __result.vesselName, before, rebuilt.Count));
                }
            }
            catch (Exception e)
            {
                HotfixLog.Error("[SMS.1.A] Postfix_CloneDesign: " + e.Message);
            }
        }

        private static bool Contains(Il2CppSystem.Collections.Generic.List<Il2CppSystem.Guid> list,
                                     Il2CppSystem.Guid value)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == value) return true;
            }
            return false;
        }
    }
}
