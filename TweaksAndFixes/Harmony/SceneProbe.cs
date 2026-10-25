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

            try { Probe(state); }
            catch (Exception e) { HotfixLog.Warn("[SMS.1.SCENE] probe: " + e.Message); }
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
