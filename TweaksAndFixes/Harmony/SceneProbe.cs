using System;
using System.Text;
using HarmonyLib;
using Il2Cpp;
using TAFHotfixBatch1;
using UnityEngine;

namespace TweaksAndFixes
{
    /// <summary>
    /// [SMS.1.SCENE] 场景结构**只读探针**（移植 3.22 `Patch_SceneManager.ConfigureScene` 的前置侦察）。
    ///
    /// 目的：在真正接管场景之前，先确认我们这版游戏里
    ///   · 构造器场景根对象叫什么（Beta6 里是 `LevelConstructor`）
    ///   · 战斗场景根对象叫什么（Beta6 里是 `LevelBattle`）
    ///   · 它们下面是否存在 `Sun` / `Scene Lighting` / 海面对象（Beta6 的 ConfigureForPreview 依赖这些名字）
    ///
    /// 只读：不改任何对象，只打印一遍。
    /// 开关：`scene_probe`（默认 1）。
    /// </summary>
    [HarmonyPatch(typeof(GameManager))]
    internal static class Patch_SceneProbe
    {
        private static readonly string[] Roots =
        {
            "LevelConstructor", "LevelBattle", "Level", "Constructor", "Battle"
        };

        private static int _logged;

        [HarmonyPostfix]
        [HarmonyPatch("ChangeState")]
        internal static void Postfix_ChangeState(GameManager.GameState newState)
        {
            if (!HotfixParams.SceneProbe) return;
            if (_logged >= 6) return;

            string state = "";
            try { state = newState.ToString(); } catch { }

            // 只关心构造器/战斗相关状态
            if (state != "Constructor" && state != "Battle" && state != "LoadingBattle"
                && state != "LoadingConstructor" && state != "CustomBattle")
                return;

            _logged++;

            try
            {
                Probe(state);
                BindScene(state);
            }
            catch (Exception e) { HotfixLog.Warn("[SMS.1.SCENE] probe: " + e.Message); }
        }

        /// <summary>
        /// [SMS.1.SCENE] 把探针找到的对象登记到 `Patch_SceneManagerPreview`，
        /// 让 `ConfigureForPreview` 真正生效（预览时隐藏构造器 Sun、显示战斗 Scene Lighting）。
        ///
        /// 实测（2026-10-26，port37 探针）：
        ///   · `LevelConstructor` 的子对象含 `Sun`   ✅
        ///   · `LevelBattle`      的子对象含 `Scene Lighting` ✅
        ///   · `GameObject.Find("Sun")` 为 null（Sun 是失活对象，Find 只找激活对象）⇒ 必须用 Util.GetChild 从根上取
        ///   · 海面对象是 `opwOcean4OPW_00`（没有叫 OceanFFT 的对象）
        /// </summary>
        private static void BindScene(string state)
        {
            try
            {
                var lc = GameObject.Find("LevelConstructor");
                var lb = GameObject.Find("LevelBattle");

                if (lc != null) Patch_SceneManagerPreview.LevelConstructor = lc;
                if (lb != null) Patch_SceneManagerPreview.LevelBattle = lb;

                // DayCycleAndWeather：给 OceanGraphicsData 用（场景里唯一的天气组件）
                if (Patch_SceneManagerPreview.DayCycleAndWeatherO == null)
                {
                    try
                    {
                        var dcw = UnityEngine.Object.FindObjectOfType<DayCycleAndWeather>();
                        if (dcw != null) Patch_SceneManagerPreview.DayCycleAndWeatherO = dcw.gameObject;
                    }
                    catch { }
                }

                bool ready = Patch_SceneManagerPreview.LevelConstructor != null
                             && Patch_SceneManagerPreview.LevelBattle != null;
                Patch_SceneManagerPreview.PreviewInited = ready;

                try
                {
                    if (global::TAFHotfixBatch1.HotfixParams.StaticOceanProbe)
                        Patch_SceneManagerPreview.ProbeOcean();
                }
                catch { }

                // 把 oceanGraphics.csv 的三档参数应用到真实海面 FFT（延迟 3 秒，等 oceanFFT 初始化）
                try
                {
                    MelonLoader.MelonCoroutines.Start(DelayedOceanApply());
                }
                catch { }

                // ★ [SMS.1.OCEAN] 静态海面由**设置菜单的 Ocean Type** 驱动（与 duke 的设计一致，
                //   不再依赖外部 config 参数）：
                //     oceanType = 0 → 默认（原版 FFT 海面）
                //     oceanType = 1 → 静态海面（duke 的低质方案，用 TAFData 贴图）
                //     oceanType = 2 → 无海面（浮空/宇宙海战风格）
                try
                {
                    int oceanType = 0;
                    try { oceanType = global::TweaksAndFixes.UiM.TAF_Settings.settings.oceanType; } catch { }

                    Patch_SceneManagerPreview.SetOceanMode(oceanType);

                    HotfixLog.Info("[SMS.1.OCEAN] oceanType=" + oceanType);
                }
                catch { }

                HotfixLog.Info("[SMS.1.SCENE] bind state=" + state
                               + "  LevelConstructor=" + (Patch_SceneManagerPreview.LevelConstructor != null)
                               + "  LevelBattle=" + (Patch_SceneManagerPreview.LevelBattle != null)
                               + "  DayCycleAndWeather=" + (Patch_SceneManagerPreview.DayCycleAndWeatherO != null)
                               + "  previewInited=" + Patch_SceneManagerPreview.PreviewInited);
            }
            catch (Exception e) { HotfixLog.Warn("[SMS.1.SCENE] bind: " + e.Message); }
        }


        private static System.Collections.IEnumerator DelayedOceanApply()
        {
            for (int i = 0; i < 3; i++) yield return null;
            yield return null;
            try { global::TweaksAndFixes.OceanGraphicsData.ResetApplied(); } catch { }
            // 再等约 3 秒（180 帧 @60fps）
            for (int i = 0; i < 180; i++) yield return null;
            try { global::TweaksAndFixes.OceanGraphicsData.ApplyAll(); } catch { }
        }

        private static void Probe(string state)
        {
            var sb = new StringBuilder();
            sb.Append("[SMS.1.SCENE] state=").Append(state);

            // 1) 尝试若干候选根名
            foreach (var name in Roots)
            {
                GameObject go = null;
                try { go = GameObject.Find(name); } catch { }
                if (go == null) continue;

                sb.Append("\n  root '").Append(name).Append("' children: ");
                try
                {
                    var t = go.transform;
                    int n = t.childCount;
                    for (int i = 0; i < n && i < 25; i++)
                    {
                        var c = t.GetChild(i);
                        if (c == null) continue;
                        sb.Append(c.name).Append(i < n - 1 && i < 24 ? ", " : "");
                    }
                }
                catch (Exception e) { sb.Append("(err ").Append(e.Message).Append(")"); }
            }

            // 2) 直接找 Beta6 依赖的那几个名字
            foreach (var nm in new string[] { "Sun", "Scene Lighting" })
            {
                GameObject go = null;
                try { go = GameObject.Find(nm); } catch { }
                sb.Append("\n  find '").Append(nm).Append("': ").Append(go != null ? "FOUND" : "null");
            }

            // 3) 找海面/天气对象
            try
            {
                var fft = GameObject.Find("OceanFFT");
                sb.Append("\n  find 'OceanFFT': ").Append(fft != null ? "FOUND" : "null");
            }
            catch { }

            HotfixLog.Info(sb.ToString());
        }
    }
}
