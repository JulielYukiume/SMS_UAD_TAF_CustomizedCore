using System.Collections;
using Il2Cpp;
using MelonLoader;
using UnityEngine;

namespace TweaksAndFixes.Modules
{
    /// <summary>
    /// [SMS.1.CON] Constructor 辅助模块 —— 移植 3.22 B2 的 `TweaksAndFixes.Modules.Constructor`。
    ///
    /// 来源：Beta6 反编译 `TweaksAndFixes.Modules/Constructor.cs`（141 行，2026-08-18 构建）。
    ///
    /// 作用（按 duke 的意图）：
    ///   · `CanPlaceSoftCached(Part)`：软判定一个武器部件当前的射击扇面是否"可用"
    ///     （炮塔类、非鱼雷、无炮座/炮廓、扇面角 ≥ 75°）；
    ///   · `CheckWarnParts()`：在造船厂里逐帧检查部件，把不可用的塞进 `Ui.placedPartsWarn`，
    ///     可用的移除；带 `CheckForWarningsAfterDelay` 的延迟启动；
    ///   · `UpdateHull()` / `DelayedUpdateHull()`：把"正在检查错误"置位后延迟一帧复位。
    ///
    /// 说明：Beta6 里只有一个调用点（`Patch_Ui` 的每帧更新里调 `Constructor.Update()`），
    /// 而 `Update()` 本体只有一句 `GameManager.IsConstructor`（无副作用）。
    /// 也就是说该模块在 Beta6 中基本是**预留/半成品**；这里按原样移植以保持行为一致，
    /// 不额外发挥（不在我们这边新增调用点）。
    ///
    /// 日志沿用 `[SMS.1.CON]` 前缀（原版 duke 用裸 Logger.Msg，这里统一标签便于排查）。
    /// </summary>
    internal class Constructor
    {
        public static float CheckForWarningsAfterDelay = -1f;
        public static bool CheckForWarnings = false;
        public static int CheckForWarningsIdx = 0;

        public static float CheckForErrorsAfterDelay = -1f;
        public static bool CheckForErrors = false;
        public static bool CheckingForErrors = false;

        public static Il2CppSystem.Collections.Generic.List<Part> CheckForErrorsQueue
            = new Il2CppSystem.Collections.Generic.List<Part>();

        public static Il2CppSystem.Collections.Generic.Dictionary<Part, float> CheckForErrorsDistances
            = new Il2CppSystem.Collections.Generic.Dictionary<Part, float>();

        public static Vector3 poi = Vector3.zero;

        /// <summary>当前正在建造的船（PlayerController.Ship）。</summary>
        public static Ship CurrShip
        {
            get
            {
                try
                {
                    var pc = PlayerController.Instance;
                    if (pc == null) return null;
                    return pc.Ship;
                }
                catch { return null; }
            }
        }

        /// <summary>软判定：该部件当前是否可以放置（扇面角度足够 / 有炮座 / 炮廓 / 非炮类）。</summary>
        public static bool CanPlaceSoftCached(Part p)
        {
            try
            {
                if (p == null || p.data == null) return true;

                // 非火炮、或鱼雷 ⇒ 不参与判定
                if (!p.data.isGun || p.data.isTorpedo) return true;

                // 有炮座（含炮廓）⇒ 视为可用
                if (p.mount != null) return true;

                // 扇面总角度不足 75° ⇒ 不可用（会挡住其他部件/毫无射界）
                if (p.fireSector != null && p.fireSector.shootableAngleTotal < 75f) return false;

                return true;
            }
            catch { return true; }
        }

        /// <summary>造船厂逐帧检查：维护 Ui.placedPartsWarn 集合。</summary>
        public static void CheckWarnParts()
        {
            try
            {
                var ship = CurrShip;
                if (ship == null) return;

                var parts = ship.parts;
                if (parts == null) return;

                // 缺射击扇面信息的部件先补齐（否则 CanPlaceSoftCached 读不到角度）
                for (int i = 0; i < parts.Count; i++)
                {
                    Part part = null;
                    try { part = parts[i]; } catch { }
                    if (part == null) continue;

                    if (part.fireSector == null)
                    {
                        part.fireSector = new Part.FireSectorInfo();
                        part.CalcFireSectorNonAlloc(part.fireSector);
                    }
                }

                if (!CheckForWarnings && CheckForWarningsAfterDelay == -1f) return;

                if (CheckForWarningsAfterDelay > 0f)
                {
                    CheckForWarningsAfterDelay -= Time.deltaTime;
                    return;
                }

                if (CheckForWarningsAfterDelay != -1f)
                {
                    CheckForWarningsAfterDelay = -1f;
                    CheckForWarnings = true;
                }

                if (CheckForWarnings && CheckForWarningsIdx >= parts.Count - 1)
                {
                    Melon<TweaksAndFixes>.Logger.Msg("[SMS.1.CON] check finished");
                    CheckForWarnings = false;
                    CheckForWarningsIdx = 0;
                    CheckForWarningsAfterDelay = -1f;
                }
                else
                {
                    var ui = G.ui;
                    Part p = null;
                    try { p = parts[CheckForWarningsIdx]; } catch { }
                    if (p != null && ui != null && ui.placedPartsWarn != null)
                    {
                        if (CanPlaceSoftCached(p))
                            ui.placedPartsWarn.Remove(p);
                        else
                            ui.placedPartsWarn.Add(p);
                    }

                    CheckForWarningsIdx++;
                }
            }
            catch { }
        }

        /// <summary>延迟一帧复位"正在检查"标志（Beta6 的协程语义）。</summary>
        internal static IEnumerator DelayedUpdateHull()
        {
            yield return null;
            CheckingForErrors = false;
        }

        public static void UpdateHull()
        {
            if (CheckingForErrors) return;

            CheckingForErrors = true;
            try { MelonCoroutines.Start(DelayedUpdateHull()); }
            catch { CheckingForErrors = false; }
        }

        /// <summary>Beta6 原样：只有一句取值，无副作用（保留以对齐 IL 行为）。</summary>
        public static void Update()
        {
            try { bool _ = GameManager.IsConstructor; } catch { }
        }
    }
}
