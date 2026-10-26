using System;
using System.Collections.Generic;
using Il2Cpp;
using MelonLoader;
using TAFHotfixBatch1;
using UnityEngine;

namespace TweaksAndFixes
{
    /// <summary>
    /// [SMS.1.OCEAN] 海面外观参数表（`oceanGraphics.csv`）—— 移植 3.22 B6 的 `TweaksAndFixes.Data.OceanGraphicsData`。
    ///
    /// 来源：Beta6 反编译 `TweaksAndFixes.Data/OceanGraphicsData.cs`（124 行）。
    ///
    /// 数据结构：每行一组 `FFTSimParams`（Calm / Choppy / Stormy 三档），字段见 CSV 表头。
    /// `PostProcess()` 把该行写入对应档位的 `DayCycleAndWeather.oceanFFT.fftSimParams[i]`。
    ///
    /// 依赖：`Patch_SceneManagerPreview.DayCycleAndWeatherO`（战斗/构造器场景里的天气对象）。
    ///   **未移植静态海面/场景配置前该字段为 null ⇒ PostProcess 直接跳过**，
    ///   因此本类可以先落地、不会产生任何副作用。
    /// </summary>
    internal class OceanGraphicsData : Serializer.IPostProcess
    {
        [Serializer.Field] public string name = string.Empty;

        [Serializer.Field] public float ampFactor;
        [Serializer.Field] public float choppiness;
        [Serializer.Field] public float dirDependency;
        [Serializer.Field] public float normalScale;
        [Serializer.Field] public string scale = string.Empty;
        [Serializer.Field] public float scatterNormalScale;
        [Serializer.Field] public int simSize;
        [Serializer.Field] public float specularNormalScale;
        [Serializer.Field] public float timeScale;
        [Serializer.Field] public float windDirAngle;
        [Serializer.Field] public float windSpeed;

        /// <summary>把本行数据写入对应档位的 FFT 参数（Calm/Choppy/Stormy）。</summary>
        public void PostProcess()
        {
            try
            {
                var obj = Patch_SceneManagerPreview.DayCycleAndWeatherO;
                if (obj == null) return;   // 场景对象还不存在（未移植场景配置时的正常情况）

                var component = obj.GetComponent<DayCycleAndWeather>();
                if (component == null) return;

                int idx;
                switch (name)
                {
                    case "Calm": idx = 0; break;
                    case "Choppy": idx = 1; break;
                    case "Stormy": idx = 2; break;
                    default: return;
                }

                var oceanFFT = component.oceanFFT;
                if (oceanFFT == null) return;
                var arr = oceanFFT.fftSimParams;
                if (arr == null || idx >= arr.Length) return;

                var val = arr[idx];
                val.ampFactor = ampFactor;
                val.choppiness = choppiness;
                val.dirDependency = dirDependency;
                val.normalScale = normalScale;
                val.scatterNormalScale = scatterNormalScale;
                val.simSize = simSize;
                val.specularNormalScale = specularNormalScale;
                val.timeScale = timeScale;
                val.windDirAngle = windDirAngle;
                val.windSpeed = windSpeed;

                // scale 形如 "(0.0005,0.002)"
                if (!string.IsNullOrEmpty(scale) && scale.Length >= 5
                    && scale.StartsWith("(") && scale.EndsWith(")"))
                {
                    var parts = scale.Substring(1, scale.Length - 2).Split(',');
                    if (parts.Length == 2
                        && ModUtils.TryParse(parts[0].Trim(), out float sx)
                        && ModUtils.TryParse(parts[1].Trim(), out float sy))
                    {
                        val.scale = new Vector2(sx, sy);
                    }
                    else
                    {
                        Melon<TweaksAndFixes>.Logger.Error("[SMS.1.OCEAN] Invalid scale `" + scale + "`");
                    }
                }
                else
                {
                    Melon<TweaksAndFixes>.Logger.Error("[SMS.1.OCEAN] Invalid scale `" + scale + "`");
                }
            }
            catch (Exception e)
            {
                HotfixLog.Warn("[SMS.1.OCEAN] PostProcess: " + e.Message);
            }
        }


        internal static readonly List<OceanGraphicsData> Loaded = new List<OceanGraphicsData>();
        internal static bool _applied;

        /// <summary>
        /// [SMS.1.OCEAN] 把 oceanGraphics.csv 的 Calm/Choppy/Stormy 参数写入真实的海面 FFT。
        ///
        /// 探针结论（2026-10-26）：本版海面**不是 MeshRenderer 平面**（`opwOcean4OPW_00` 只是容器，
        /// scale=(0,0,0)、无 MeshFilter/MeshRenderer），实际渲染由 `DayCycleAndWeather.oceanFFT` 驱动。
        /// 因此 duke 的"静态海面（低质平面 + 滚动贴图）"在本版**不可直接照搬**；
        /// 但 `OceanGraphicsData` 的三档参数可以直接作用到 FFT 上 —— 这就是本方法的用途。
        /// </summary>
        public static void ApplyAll()
        {
            try
            {
                if (_applied) return;
                var obj = Patch_SceneManagerPreview.DayCycleAndWeatherO;
                if (obj == null) return;
                var dcw = obj.GetComponent<DayCycleAndWeather>();
                if (dcw == null) return;

                int n = 0;
                foreach (var d in Loaded)
                {
                    d.PostProcess();
                    n++;
                }

                _applied = true;
                HotfixLog.Info("[SMS.1.OCEAN] applied " + n + " ocean graphics preset(s) to FFT");
            }
            catch (Exception e) { HotfixLog.Warn("[SMS.1.OCEAN] ApplyAll: " + e.Message); }
        }

        /// <summary>场景切换时重置（不同场景的 DayCycleAndWeather 是不同实例）。</summary>
        public static void ResetApplied() { _applied = false; }

        /// <summary>读取 `Mods/oceanGraphics.csv`（文件不存在时静默跳过）。</summary>
        public static void LoadData()
        {
            try
            {
                var fp = new FilePath(FilePath.DirType.ModsDir, "oceanGraphics.csv");
                if (!fp.Exists)
                {
                    HotfixLog.Info("[SMS.1.OCEAN] oceanGraphics.csv not found -- skipped");
                    return;
                }

                string text = Serializer.CSV.GetTextFromFile(fp.path);
                if (text == null)
                {
                    HotfixLog.Warn("[SMS.1.OCEAN] failed to read oceanGraphics.csv");
                    return;
                }

                Loaded.Clear();
                Serializer.CSV.Read<List<OceanGraphicsData>, OceanGraphicsData>(text, Loaded, true, true);
                HotfixLog.Info("[SMS.1.OCEAN] oceanGraphics.csv loaded: " + Loaded.Count + " entries");
            }
            catch (Exception e)
            {
                HotfixLog.Warn("[SMS.1.OCEAN] LoadData: " + e.Message);
            }
        }
    }
}
