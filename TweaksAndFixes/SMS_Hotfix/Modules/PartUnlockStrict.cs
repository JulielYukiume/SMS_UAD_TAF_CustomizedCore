using System;
using HarmonyLib;
using Il2Cpp;

namespace TAFHotfixBatch1.Modules
{
    /// <summary>
    /// [SMS.2.PU] 非战役上下文补回 `needunlock(keyword)` 检查。
    ///
    /// 根因（docs/05-design-mechanics/111-vanilla-part-availability-tech-unlock.md §9）：
    ///   `Player::IsPartUnlocked` 开头 `if (!GameManager.IsCampaign) return true;`
    ///   ⇒ 共享设计 / 自定义战斗里，部件解锁检查被整体跳过，后期舰桥/烟囱从一开始就可用。
    ///
    /// 做法（方案 A，docs/06-delivery/112-sms3-16-plan-a-design.md）：
    ///   Postfix `Ship.IsPartAvailable(PartData, Player, ShipType, Ship)`：
    ///   非战役 + 部件有 `NeedUnlock` + 原版判可用 时，改用**设计自己的科技名表**（`Ship.techs`）
    ///   经 `GameData.technologies[name].effects["unlockpart"]` 比对，找不到则判不可用。
    ///
    /// 默认 `part_unlock_strict=0`（原版行为）；打开后才改变可用部件集合。
    /// </summary>
    internal static class PartUnlockStrict
    {
        private static int _denied;

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            if (!HotfixParams.PartUnlockStrict) { HotfixLog.Info("[SMS.2.PU] PartUnlockStrict: disabled"); return; }
            try
            {
                var m = AccessTools.Method(typeof(Ship), "IsPartAvailable",
                    new Type[] { typeof(PartData), typeof(Player), typeof(ShipType), typeof(Ship) });
                if (m == null) { HotfixLog.Warn("[SMS.2.PU] static IsPartAvailable(4) not found"); return; }
                harmony.Patch(m, postfix: new HarmonyMethod(AccessTools.Method(typeof(PartUnlockStrict), nameof(Postfix))));
                HotfixLog.Info("[SMS.2.PU] PartUnlockStrict armed (non-campaign needunlock check, default off in vanilla terms)");
            }
            catch (Exception e) { HotfixLog.Error("[SMS.2.PU] install failed: " + e); }
        }

        internal static void Postfix(PartData part, Player player, ShipType shipType, Ship ship, ref bool __result)
        {
            try
            {
                if (!__result) return;                                  // 原版已拒
                if (!HotfixParams.PartUnlockStrict) return;
                if (part == null) return;
                string need = part.NeedUnlock;
                if (string.IsNullOrEmpty(need)) return;                  // 无关键词限制

                var gm = GameManager.Instance;
                if (gm == null || gm.isCampaign) return;                 // 战役走原版

                Ship s = ship;
                if (s == null) { try { s = global::TweaksAndFixes.Patch_Ship.LastCreatedShip; } catch { } }
                if (s == null) return;

                var techs = s.techs;                                     // HashSet<TechnologyData>（VesselEntity.techs）
                if (techs == null || techs.Count == 0) return;           // fail-open

                if (!HasUnlock(techs, need))
                {
                    __result = false;
                    if (_denied++ < 8)
                        HotfixLog.Info("[SMS.2.PU] deny part='" + SafeName(part) + "' needunlock='" + need + "'");
                }
            }
            catch { }
        }

        /// <summary>在设计自己的科技集合里找 unlockpart(need)。</summary>
        private static bool HasUnlock(Il2CppSystem.Collections.Generic.HashSet<TechnologyData> techs, string need)
        {
            foreach (TechnologyData td in techs)
            {
                if (td == null) continue;
                var fx = td.effects;
                if (fx == null) continue;
                Il2CppSystem.Collections.Generic.List<Il2CppSystem.Collections.Generic.List<string>> rows = null;
                try { if (fx.ContainsKey("unlockpart")) rows = fx["unlockpart"]; } catch { }
                if (rows == null) continue;

                for (int r = 0; r < rows.Count; r++)
                {
                    var row = rows[r];
                    if (row == null) continue;
                    for (int c = 0; c < row.Count; c++)
                        if (string.Equals(row[c], need, StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
            return false;
        }

        private static string SafeName(PartData p)
        {
            try { return p.name ?? "?"; } catch { return "?"; }
        }
    }
}
