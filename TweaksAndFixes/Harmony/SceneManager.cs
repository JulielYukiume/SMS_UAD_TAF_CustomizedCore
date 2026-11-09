using System;
using System.Text;
using HarmonyLib;
using Il2Cpp;
using Il2CppNavalAction.Common;
using MelonLoader;
using TweaksAndFixes.Modified;
using UnityEngine;
using UnityEngine.SceneManagement;
using TAFHotfixBatch1;

#if true

namespace TweaksAndFixes
{
    [HarmonyPatch(typeof(SceneManager))]
    internal class Patch_SceneManager
    {
        public static void SetConstructorWeather(int idx = -1)
        {
            var dcaw = DayCycleAndWeatherO.GetComponent<DayCycleAndWeather>();

            if (idx == -1)
                idx = UnityEngine.Random.RandomRangeInt(0, 3);

            if (idx == 2 && sceneState == GameManager.GameState.Constructor)
                idx = 0;

            dcaw.DesiredStormIntensity = 0;
            dcaw.StormIntensity = 0;
            dcaw.Storm.Intensity = 0;
            dcaw.Storm.gameObject.active = false;

            Patch_DayCycleAndWeather.inited = true;

            switch (idx)
            {
                // Noon & clear
                case 0:
                    dcaw.WorldTime = new() { Time = 60 * 60 * 36, TimeScale = 1 };
                    dcaw.InitWeatherStateFromTime();

                    dcaw.SetDaytime(DayCycleAndWeather.TimesOfDay.Day);
                    dcaw.SetWeather(DayCycleAndWeather.WeatherType.Clear);
                    dcaw.LateUpdate();
                    break;

                // Sunset & clear
                case 1:
                    dcaw.WorldTime = new() { Time = 60 * 60 * 40, TimeScale = 1 };
                    dcaw.InitWeatherStateFromTime();

                    dcaw.SetDaytime(DayCycleAndWeather.TimesOfDay.Day);
                    dcaw.SetWeather(DayCycleAndWeather.WeatherType.Clear);
                    dcaw.LateUpdate();
                    break;

                // Noon & Foggy
                case 2:
                    dcaw.WorldTime = new() { Time = 60 * 60 * 36, TimeScale = 1 };
                    dcaw.InitWeatherStateFromTime();
                    dcaw.SetDaytime(DayCycleAndWeather.TimesOfDay.Day);
                    dcaw.SetWeather(DayCycleAndWeather.WeatherType.Overcast);
                    dcaw.LateUpdate();
                    HeightVolumetricFog.Instance.fogColorBackFar += new Color(0.2f, 0.2f, 0.2f, 0.0f);
                    HeightVolumetricFog.Instance.fogColorBackNear += new Color(0.2f, 0.2f, 0.2f, 0.0f);
                    HeightVolumetricFog.Instance.fogColorFrontFar += new Color(0.2f, 0.2f, 0.2f, 0.0f);
                    HeightVolumetricFog.Instance.fogColorFrontNear += new Color(0.2f, 0.2f, 0.2f, 0.0f);

                    break;
            }
        }

        public static void SetBattleWeather()
        {
            var dcaw = DayCycleAndWeatherO.GetComponent<DayCycleAndWeather>();

            if (G.ui.skirmishSetup != null)
            {
                dcaw.SetDaytime(G.ui.skirmishSetup.daytime);
                dcaw.SetWeather(G.ui.skirmishSetup.weather);
            }
            else
            {
                dcaw.CheckDesiredParams();
            }

            dcaw.LateUpdate();
        }

        public static GameManager.GameState sceneState;
        public static bool hasCleanedUpBattle = true;
        public static bool wasInMainMenu = false;

        /// <summary>[SMS.2.OCEAN] 按 oceanType 在场景配置时统一应用海面模式。</summary>
        internal static void ApplyOceanModeForScene(GameManager.GameState state)
        {
            try
            {
                int __ot = 0;
                try { __ot = UiM.TAF_Settings.settings.oceanType; } catch { }
                int __daW = Config.Param("taf_disable_all_water", 0) == 1 ? 1 : 0;
                int __m = (__daW == 1) ? 2 : __ot;
                HotfixLog.Info("[SMS.2.OCEAN] ApplyOceanModeForScene state=" + state + " mode=" + __m);

                if (__m == 1 || __m == 2)
                {
                    var ocean = LevelBattle != null ? LevelBattle.GetChild("opwOcean4OPW_00") : null;
                    if (ocean != null) { ocean.active = false; HotfixLog.Info("[SMS.2.OCEAN] ocean root off (scene=" + state + ")"); }
                    else HotfixLog.Warn("[SMS.2.OCEAN] ocean root null (LevelBattle=" + (LevelBattle != null) + ")");
                }

                if (__m == 2)
                {
                    var mws = LevelConstructor != null ? LevelConstructor.GetChild("MoveWithShip") : null;
                    if (mws != null) mws.active = false;
                    var dock = LevelConstructor != null ? LevelConstructor.GetChild("Dock") : null;
                    if (dock != null)
                    {
                        string[] names = { "water_in_huge_dock_002", "water_in_big_dock_002", "water_in_middle_dock_002", "water_in_smoll_dock_002" };
                        for (int i = 0; i < names.Length; i++) { var d = dock.GetChild(names[i]); if (d != null) d.active = false; }
                    }
                }

                // 原版水底 FakePlaneToFixWiresGlitch（空水模式重着色/关闭/下移）
                try { NoneWaterBottom.Apply(__m); } catch { }


                // 雷迹补偿（mode 1/2 都启用；mode 0 由循环内部自行清理）
                if (__m == 1 || __m == 2) TorpedoWakeHint.EnsureRunning();

                if (__m == 1)
                {
                    if (Patch_SceneManagerPreview.StaticOcean == null) Patch_SceneManagerPreview.CreateStaticOcean();
                    Patch_SceneManagerPreview.EnsureTexture();
                }
            }
            catch (Exception e) { HotfixLog.Warn("[SMS.2.OCEAN] ApplyOceanModeForScene: " + e.Message); }
        }

        private static bool _probesLogged;


        /// <summary>
        /// [SMS.2.SEC] 诊断：`LevelConstructor/Probes` 到底是什么。
        /// 注意：**不能用 `GetComponent&lt;T&gt;()`** —— Il2Cpp 的泛型方法存储在
        /// `MethodInfoStoreGeneric_GetComponent_Public_T_0` 处会抛
        /// "type initializer threw an exception"，整段 dump 会中断。
        /// 因此这里只用非泛型 API：Transform 子节点遍历 + FindObjectsOfType（各自 try/catch）。
        /// </summary>

        private static string ParentChain(Transform t)
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                int guard = 0;
                while (t != null && guard++ < 6)
                {
                    if (sb.Length > 0) sb.Insert(0, "/");
                    sb.Insert(0, t.name);
                    t = t.parent;
                }
                return sb.ToString();
            }
            catch { return "?"; }
        }

        private static void DumpProbes(GameObject probesGo, bool enabled)
        {
            // 仅在 verbose=1 时输出（这些是排查用细节，不是运行期信息）
            if (!HotfixParams.Verbose) return;

            HotfixLog.Info("[SMS.2.SEC] Probes mode=" + HotfixParams.ConstructorProbeMode
                           + " active=" + (probesGo != null && probesGo.activeSelf)
                           + " param=" + enabled + " obj=" + (probesGo != null ? probesGo.name : "NULL"));
            if (probesGo == null) return;

            try
            {
                var t = probesGo.transform;
                for (int i = 0; i < t.childCount; i++)
                {
                    var c = t.GetChild(i);
                    if (c == null) continue;
                    HotfixLog.Dbg("[SMS.2.SEC]   child[" + i + "] '" + c.name + "' active=" + c.gameObject.activeSelf);
                }
            }
            catch { }

            // 注意：`FindObjectsOfType<LightProbeGroup>()` 在 Il2Cpp 下必然抛
            //   "MethodInfoStoreGeneric_FindObjectsOfType_... threw an exception"，
            //   且该场景不含 LightProbeGroup（Probes 下只有 1 全局 + 9 局部反射探针），故不再尝试。

            bool lcWasActive = false;
            try
            {
                lcWasActive = LevelConstructor.activeSelf;
                if (!lcWasActive) LevelConstructor.active = true;

                var rps = UnityEngine.Object.FindObjectsOfType<ReflectionProbe>();
                if (rps != null)
                {
                    for (int k = 0; k < rps.Length; k++)
                    {
                        var rp = rps[k];
                        if (rp == null) continue;
                        HotfixLog.Dbg("[SMS.2.SEC]   RP '" + rp.name + "' box=" + rp.boxProjection
                                      + " size=" + rp.size + " blend=" + rp.blendDistance
                                      + " intensity=" + rp.intensity + " pos=" + rp.transform.position
                                      + " chain=" + ParentChain(rp.transform));
                    }
                }
            }
            catch { }
            finally { try { if (!lcWasActive) LevelConstructor.active = false; } catch { } }
        }

        public static void ConfigureScene(GameManager.GameState state)
        {
            if (!inited)
                return;

            if (state != GameManager.GameState.MainMenu)
                MainMenuM.ClearScene();
            else if (state == GameManager.GameState.MainMenu)
                MainMenuM.InitRandomScene();

            // Seems to cause problems, maybe?
            if (state == GameManager.GameState.Loading
                || state == GameManager.GameState.LoadingCustom)
                return;

            // These are just full-screen menus pasted over the main menu.
            //   No point in disabling the main menu scene if we're just gonna
            //   load it back up again if/when they back out of the menu.
            if (state == GameManager.GameState.NewCampaignSetup
                || state == GameManager.GameState.CustomBattleSetup
                || state == GameManager.GameState.AcademyMissionSelect)
                state = GameManager.GameState.MainMenu;

            if (state == GameManager.GameState.MainMenu)
                wasInMainMenu = true;

            if (state == sceneState)
            {
                Melon<TweaksAndFixes>.Logger.Msg($"Skipping config: new `{state}` == current `{sceneState}`");
                return;
            }

            Melon<TweaksAndFixes>.Logger.Msg($"ConfigureScene: {state}");

            if (Config.Param("taf_use_old_constructor_lighting", 0) == 0
                || state == GameManager.GameState.Battle)
            {
                LevelConstructor.GetChild("Sun").active = false;
                LevelBattle.GetChild("Scene Lighting").active = true;
            }
            else
            {
                LevelConstructor.GetChild("Sun").active = true;
                LevelBattle.GetChild("Scene Lighting").active = false;
            }

            if (state == GameManager.GameState.Battle)
                hasCleanedUpBattle = false;

            if (state != GameManager.GameState.Battle
                && state != GameManager.GameState.Loading
                && state != GameManager.GameState.LoadingCustom
                && !hasCleanedUpBattle)
            {
                Melon<TweaksAndFixes>.Logger.Msg($"  Clean up after leaving battle...");

                foreach (var torp in Torpedo.AllTorpedo.ToArray())
                {
                    torp.RemoveSelf();
                    Melon<TweaksAndFixes>.Logger.Msg($"    Deleting torpedo");
                }

                List<Sound.Playing> toDelete = new();

                foreach (var sfx in G.sound.currentlyPlaying)
                {
                    if (sfx.group == "music")
                        continue;

                    // Seems to get reused, rather than duplicated
                    if (sfx.audio.name == "torpedo_engine:Torpedo_Engine")
                        continue;

                    toDelete.Add(sfx);
                }

                foreach (var sfx in toDelete.ToArray())
                {
                    Melon<TweaksAndFixes>.Logger.Msg($"    Deleting audio: {sfx.group} / {sfx.audio.name}");
                    G.sound.Stop(sfx.audio.gameObject);
                }

                DecalsManager.Instance().parentsArr.Clear();
                DecalsManager.Instance().enabled = false;
                DecalsManager.Instance().enabled = true;

                hasCleanedUpBattle = true;
            }

            Melon<TweaksAndFixes>.Logger.Msg($"  Disable all scene objects");

            // LevelBattle.name = "_LevelBattle";
            LevelConstructor.name = "_LevelConstructor";
            LevelConstructor.active = false;
            ReflectionProbesCamera.active = false;
            Mesh0.active = false;
            Mesh2.active = false;
            BordersMesh.active = false;
            try
            {
                // ★★★ [SMS.2.SEC] 色差根因（2026-11-05 实测确认）★★★
                //   `LevelConstructor/Probes` 就是"舯段色带"的根因：
                //   · 打开它 ⇒ 原版就存在的舯段色差（duke 在 69a529a "Fixes rare visual bug in constructor" 中关闭）；
                //   · 关闭它 ⇒ 色差消失（代价：整船色调/光效会变化，因为它同时提供了场景光照基础）。
                //   控制参数：`taf_constructor_probes`（默认 0 = 关闭，与 duke 一致；设 1 可复现 bug 对照）。
                var probesGo = LevelConstructor.GetChild("Probes");
                // ★★★ [SMS.2.SEC] 色差根因与三档修复（2026-11-05 实测确认）★★★
                //   Probes = 1 个全局反射探针 + 9 个局部探针（size=250、z=-50/0/+50、intensity=1.75、box=false、blend=1）。
                //   局部探针互相大幅重叠且 box=false ⇒ 无法混合 ⇒ Unity 按"最近探针"整对象二选一
                //   ⇒ 沿船长切换探针 ⇒ 各段反射不同 ⇒ 舯段色带。且探针位于船体内部时会拍进内壁。
                //   档位（constructor_probe_mode）：
                //     0 = duke 做法：整组关闭（最"死"，兼容旧行为）
                //     1 = 只保留全局探针（亮度可调）——供 modder 调画面
                //     2 = 保留全部探针，但局部探针下移到 constructor_probe_y（默认 -30m）——默认推荐
                int probeMode = HotfixParams.ConstructorProbeMode;
                float gi = HotfixParams.ConstructorProbeGlobalIntensity;

                if (probesGo != null)
                {
                    bool lcWas = LevelConstructor.activeSelf;
                    try
                    {
                        if (!lcWas) LevelConstructor.active = true;   // 便于 FindObjectsOfType 扫到 inactive 子对象

                        if (probeMode == 0)
                        {
                            probesGo.active = false;
                        }
                        else
                        {
                            probesGo.active = true;
                            var rps = UnityEngine.Object.FindObjectsOfType<ReflectionProbe>();
                            if (rps != null)
                            {
                                for (int k = 0; k < rps.Length; k++)
                                {
                                    var rp = rps[k];
                                    if (rp == null) continue;
                                    string chain = ParentChain(rp.transform);
                                    if (chain.IndexOf("Probes", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                                    bool isGlobal = chain.IndexOf("Global", System.StringComparison.OrdinalIgnoreCase) >= 0;

                                    if (isGlobal)
                                    {
                                        rp.intensity = gi;
                                        continue;
                                    }

                                    if (probeMode == 1) rp.gameObject.SetActive(false);
                                    else
                                    {
                                        var t = rp.transform;
                                        t.position = new Vector3(t.position.x, HotfixParams.ConstructorProbeY, t.position.z);
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception e) { HotfixLog.Warn("[SMS.2.SEC] probes: " + e.Message); }
                    finally { try { if (!lcWas) LevelConstructor.active = false; } catch { } }
                }                if (!_probesLogged)
                {
                    _probesLogged = true;
                    DumpProbes(probesGo, HotfixParams.ConstructorProbeMode == 0 ? false : true);
                }
            }
            catch { }
            LevelBattle.GetChild("WaterLevel").active = true;

            // ★ [SMS.2.OCEAN] 放在 WaterLevel 处理**之后**、switch(state) 之前：
            //   此时 LevelBattle 已就绪，且不会被后续 WaterLevel 操作覆盖。
            try { ApplyOceanModeForScene(state); }
            catch (Exception __e) { HotfixLog.Warn("[SMS.2.OCEAN] ConfigureScene: " + __e.Message); }

            switch (state)
            {
                case GameManager.GameState.MainMenu:
                    // MainMenu
                    //   >< ContrastEnhance
                    //   ./ Bloom
                    //   >< ColorCorrectionCurves
                    //   ./ DepthOfField
                    //   >< MotionBlur
                    //   ./ NoiseAndScratches
                    
                    sceneState = GameManager.GameState.MainMenu;

                    LevelConstructor.GetChild("Dock").transform.localPosition = Vector3.zero;

                    // G.cam.enabled = false;
                    // G.cam.lookingAt = new();
                    // G.cam.distance = 200;
                    // G.cam.distanceDesired = 200;
                    // G.cam.rotationX = 20;
                    // G.cam.rotationY = 225;
                    // UiM.SettupMainMenuCam(G.cam);

                    G.cam.DepthOfField(true);
                    G.cam.NoiseGrain(true);
                    G.cam.MotionBlur(false);

                    if (Config.Param("taf_enable_main_menu_vignette", 1) == 0)
                        G.ui.mainMenuUi.GetChild("BgX").active = false;

                    LevelConstructor.GetChild("Probes").active = false;
                    LevelConstructor.name = "LevelConstructor";
                    LevelConstructor.active = true;
                    ReflectionProbesCamera.active = true;

                    SetConstructorWeather();

                    break;


                case GameManager.GameState.Constructor:
                    // Constr
                    //   >< ContrastEnhance
                    //   ./ Bloom
                    //   >< ColorCorrectionCurves
                    //   ./ DepthOfField
                    //   ./ MotionBlur
                    //   >< NoiseAndScratches

                    sceneState = GameManager.GameState.Constructor;

                    G.cam.enabled = true;

                    G.cam.DepthOfField(true);
                    G.cam.NoiseGrain(false);
                    G.cam.MotionBlur(true);

                    G.cam.lookingAt = new();
                    G.cam.distance = 200;
                    G.cam.distanceDesired = 200;
                    G.cam.rotationX = 45;
                    G.cam.rotationY = 270;
                    UiM.SettupMainMenuCam(G.cam);

                    LevelConstructor.name = "LevelConstructor";
                    LevelConstructor.active = true;
                    ReflectionProbesCamera.active = true;

                    SetConstructorWeather();

                    break;


                case GameManager.GameState.Battle:
                    // Battle
                    //   >< PostEffect
                    //   ./ VAOEffectCommandBuffer
                    //   >< ContrastEnhance
                    //   ./ Bloom
                    //   >< ColorCorrectionCurves
                    //   >< DepthOfField
                    //   ./ MotionBlur
                    //   >< NoiseAndScratches

                    sceneState = GameManager.GameState.Battle;

                    Melon<TweaksAndFixes>.Logger.Msg($"  Configure camera");

                    G.cam.enabled = true;

                    G.cam.gameObject.GetComponent<PostEffect>().enabled = false;
                    G.cam.DepthOfField(false);
                    G.cam.NoiseGrain(false);
                    G.cam.MotionBlur(true);

                    Melon<TweaksAndFixes>.Logger.Msg($"  Enable scene objects");

                    LevelBattle.name = "LevelBattle";

                    Melon<TweaksAndFixes>.Logger.Msg($"  Configure weather");

                    SetBattleWeather();

                    break;


                case GameManager.GameState.World:
                    // Battle
                    //   >< PostEffect
                    //   ./ VAOEffectCommandBuffer
                    //   >< ContrastEnhance
                    //   ./ Bloom
                    //   >< ColorCorrectionCurves
                    //   >< DepthOfField
                    //   ./ MotionBlur
                    //   >< NoiseAndScratches

                    sceneState = GameManager.GameState.World;

                    G.cam.enabled = true;
                    
                    G.cam.DepthOfField(false);
                    G.cam.NoiseGrain(false);
                    G.cam.MotionBlur(false);

                    LevelBattle.GetChild("WaterLevel").active = false;
                    Mesh0.active = true;
                    Mesh2.active = true;
                    BordersMesh.active = true;

                    SetConstructorWeather(0);

                    break;

                default:
                    sceneState = GameManager.GameState.Loading;
                    break;
            }

            Melon<TweaksAndFixes>.Logger.Msg($"  Update camera");

            UiM.CamUpdate(G.cam);

            Melon<TweaksAndFixes>.Logger.Msg($"  Done!");

        }

        public static bool inited = false;

        public static GameObject LevelConstructor;
        public static GameObject LevelConstructorEnvMinimal;
        public static GameObject ReflectionProbesCamera;
        public static GameObject DayCycleAndWeatherO;
        public static GameObject LevelBattle;
        public static GameObject WaterSurfaceCam;
        public static GameObject Mesh0;
        public static GameObject Mesh2;
        public static GameObject BordersMesh;
        public static GameObject From;
        public static GameObject To;

        internal static GameObject BasicInit(
            string name, GameObject parent,
            Vector3 pos = default, Vector3 scale = default, Vector3 rot = default,
            bool local = false)
        {
            var go = new GameObject(name);
            go.SetParent(parent);
            if (!local)
            {
                go.transform.position = pos;
            }
            else
            {
                go.transform.localPosition = pos;
            }
            go.transform.SetScale(scale.x, scale.y, scale.z);
            go.transform.eulerAngles = rot;
            return go;
        }
        
        [HarmonyPatch(nameof(SceneManager.LoadSceneAsync))]
        [HarmonyPrefix]
        [HarmonyPatch(new Type[] { typeof(string) })]
        internal static bool Prefix_LoadSceneAsync(string sceneName, ref AsyncOperation __result)
        {
            Melon<TweaksAndFixes>.Logger.Msg($"Loading: {sceneName}");

            GameManager.GameState newState = GameManager.GameState.Loading;

            switch (sceneName)
            {
                case "Battle":
                    newState = GameManager.GameState.Battle;
                    break;
                case "MainMenu":
                    newState = GameManager.GameState.MainMenu;
                    break;
                case "Constructor":
                    newState = GameManager.GameState.Constructor;
                    break;
                case "World":
                    newState = GameManager.GameState.World;
                    break;
                default:
                    newState = GameManager.GameState.Loading;
                    break;
            }

            ConfigureScene(newState);

            __result = Resources.LoadAsync("techGroups");
            
            return false;
        }

        public static bool bypass = false;

        [HarmonyPatch(nameof(SceneManager.LoadScene))]
        [HarmonyPrefix]
        [HarmonyPatch(new Type[] { typeof(string) })]
        internal static bool Prefix_LoadScene(string sceneName)
        {
            if (bypass)
                return true;

            // Melon<TweaksAndFixes>.Logger.Msg($"Loading:");
            // Melon<TweaksAndFixes>.Logger.Msg($"  {G.level.mainMenuScene}");
            // Melon<TweaksAndFixes>.Logger.Msg($"  {G.level.battleScene}");
            // Melon<TweaksAndFixes>.Logger.Msg($"  {G.level.constructorScene}");
            // Melon<TweaksAndFixes>.Logger.Msg($"  {G.level.worldScene}");
        
            GameManager.GameState newState = GameManager.GameState.Loading;
        
            switch (sceneName)
            {
                case "Battle":
                    newState = GameManager.GameState.Battle;
                    break;
                case "MainMenu":
                    newState = GameManager.GameState.MainMenu;
                    break;
                case "Constructor":
                    newState = GameManager.GameState.Constructor;
                    break;
                case "World":
                    newState = GameManager.GameState.World;
                    break;
            }
        
            ConfigureScene(newState);
        
            return false;
        }
    }
    
    [HarmonyPatch(typeof(Level))]
    internal class Patch_Level
    {
    
        [HarmonyPatch(nameof(Level.Refresh))]
        [HarmonyPrefix]
        internal static bool Prefix_Refresh(Level __instance)
        {
            if (GameManager.IsConstructor)
            {
                __instance.OnConShipChanged();

                __instance.Wind = Vector3.back;

                __instance.RefreshWeather();
            }

            return false;
        }
    }


    [HarmonyPatch(typeof(Resources))]
    internal class Patch_Resources
    {
        [HarmonyPatch(nameof(Resources.UnloadUnusedAssets))]
        [HarmonyPrefix]
        internal static bool Prefix_UnloadUnusedAssets(ref AsyncOperation __result)
        {
            Melon<TweaksAndFixes>.Logger.Msg($"Skipping asset unload.");

            __result = Resources.LoadAsync("techGroups");

            return false;
        }
    }


    [HarmonyPatch(typeof(DayCycleAndWeather))]
    internal class Patch_DayCycleAndWeather
    {
        public static bool inited = false;

        [HarmonyPatch(nameof(DayCycleAndWeather.LateUpdate))]
        [HarmonyPrefix]
        internal static bool Prefix_LateUpdate(DayCycleAndWeather __instance)
        {
            return inited;
        }

        [HarmonyPatch(nameof(DayCycleAndWeather.UpdateCycle))]
        [HarmonyPostfix]
        internal static void Prefix_UpdateCycle()
        {
            // ★ [SMS.2.OCEAN] 先更新简化海面（跟随 + 滚动），再处理波高：
            //   原来这行在 Battle 早退之后，导致**战斗中简化海面不滚动**。
            try { Patch_SceneManagerPreview.UpdateStaticOcean(); } catch { }
            try { UiM.RefreshCampaignRows(); } catch { }

            if (Patch_SceneManager.sceneState == GameManager.GameState.Battle)
                return;
        
            var dcaw = Patch_SceneManager.DayCycleAndWeatherO.GetComponent<DayCycleAndWeather>();

            if (Patch_SceneManager.sceneState == GameManager.GameState.World)
                dcaw.oceanFFT.globalWaveScale = 0;
            else
                dcaw.oceanFFT.globalWaveScale = 0.01f;

            CPUWaterSimulator.instance.UpdateGlobalWaveScale(dcaw.oceanFFT.globalWaveScale);

        }
    }

internal static class Patch_SceneManagerPreview
{
    // ★ [SMS.2.SCENE] 3.22 B6 的"预览模式"开关（移植自 Beta6 Patch_SceneManager）。
    //
    // 作用：在生成舰船/部件预览图、以及刷新分段计划 UI 时，暂时把场景切到"预览模式"
    //   —— 隐藏构造器场景的 Sun、显示战斗场景的 Scene Lighting（若静态海面启用则同时缩放海面）。
    //
    // 说明：这些字段由 ConfigureScene 建立；**在我们还没移植 ConfigureScene / 静态海面之前，
    //   inited 恒为 false ⇒ 本方法是无副作用的空操作**，可以安全先接上钩子。
    //   等到移植静态海面时，只需在 ConfigureScene 里设置 inited / LevelConstructor / LevelBattle。
    internal static bool PreviewInited = false;
    internal static bool StaticOceanEnabled = false;
    internal static GameObject StaticOcean;
    internal static GameObject LevelConstructor;
    internal static GameObject LevelBattle;

    /// <summary>[SMS.2.OCEAN] 战斗/构造器场景里的天气对象（由场景配置阶段赋值）。</summary>
    internal static GameObject DayCycleAndWeatherO;



        // ======================================================================
        //  [SMS.2.OCEAN] 静态海面（移植 duke 的低质海面方案）
        //
        //  贴图来自 mod 包：Mods/TAFData/Tileable_Ocean.png (+ _Bump)
        //  做法：自建一个超大四边形，跟随相机 XZ、Y 固定在水面；每帧滚动 _MainTex。
        //  开关：static_ocean（默认 0，避免默认改变观感）
        // ======================================================================

        internal static Material StaticOceanMat;
        private static GameObject _oceanRoot;
        private static Texture2D _oceanTex;
        private static Texture2D _oceanBump;
        private static float _oceanDt;
        private static bool _oceanLooping;

        private static Texture2D LoadPng(string fileName)
        {
            try
            {
                string root = MelonLoader.Utils.MelonEnvironment.ModsDirectory;
                string[] candidates =
                {
                    System.IO.Path.Combine(root, "TAFData", fileName),
                    System.IO.Path.Combine(root, fileName),
                };

                foreach (var path in candidates)
                {
                    if (!System.IO.File.Exists(path)) continue;
                    var bytes = System.IO.File.ReadAllBytes(path);
                    var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true);
                    if (tex.LoadImage(bytes, false)) return tex;
                }

                HotfixLog.Warn("[SMS.2.OCEAN] texture not found: " + fileName);
            }
            catch (Exception e) { HotfixLog.Warn("[SMS.2.OCEAN] load png: " + e.Message); }
            return null;
        }

        /// <summary>创建静态海面（只在 static_ocean=1 时调用）。</summary>

        /// <summary>
        /// [SMS.2.OCEAN] 诊断：打印 opwOcean4OPW_00 的父链 + 我们平面的状态 + 相机位置。
        /// 只读，Info 级（每次场景绑定打一次），用于定位"为什么构造器里看不到简化海面"。
        /// </summary>

        private static string TexName(GameObject go)
        {
            try
            {
                var mr = go.GetComponent<MeshRenderer>();
                if (mr == null || mr.sharedMaterial == null) return "no-mat";
                var m = mr.sharedMaterial;
                if (!m.HasProperty("_MainTex")) return "no-prop";
                var t = m.mainTexture;
                return t == null ? "null" : t.name;
            }
            catch { return "?"; }
        }


        /// <summary>
        /// [SMS.2.OCEAN] 贴图守护：换场景后运行时创建的 Texture2D 会被销毁，
        /// 表现为平面在但 `tex=''`（白/透明）。这里按需重新加载并挂回材质。
        /// </summary>
        internal static void EnsureTexture()
        {
            try
            {
                if (StaticOceanMat == null) return;
                if (StaticOceanMat.mainTexture != null) return;

                var tex = LoadPng("Tileable_Ocean.png");
                if (tex == null) return;
                tex.wrapMode = TextureWrapMode.Repeat;
                StaticOceanMat.mainTexture = tex;
                StaticOceanMat.mainTextureScale = new Vector2(200f, 200f);
                _oceanTex = tex;

                if (_oceanBump == null)
                {
                    var bump = LoadPng("Tileable_Ocean_Bump.png");
                    if (bump != null && StaticOceanMat.HasProperty("_BumpMap"))
                    {
                        bump.wrapMode = TextureWrapMode.Repeat;
                        StaticOceanMat.SetTexture("_BumpMap", bump);
                        _oceanBump = bump;
                    }
                }

                HotfixLog.Info("[SMS.2.OCEAN] texture reloaded for static ocean");
            }
            catch (Exception e) { HotfixLog.Warn("[SMS.2.OCEAN] EnsureTexture: " + e.Message); }
        }

        internal static void DiagOcean(string tag)
        {
            try
            {
                var lb = LevelBattle;
                var root = lb != null ? lb.GetChild("opwOcean4OPW_00") : null;

                if (root != null)
                {
                    var sb = new StringBuilder("[SMS.2.OCEAN] diag ").Append(tag).Append(": ocean '").Append(root.name).Append("' active=").Append(root.activeSelf);
                    var t = root.transform;
                    var chain = new StringBuilder();
                    int guard = 0;
                    while (t != null && guard++ < 8)
                    {
                        chain.Append(t.name).Append("(").Append(t.gameObject.activeSelf ? "on" : "off").Append(") < ");
                        t = t.parent;
                    }
                    sb.Append(" | chain: ").Append(chain);
                    HotfixLog.Dbg(sb.ToString());
                }
                else
                {
                    HotfixLog.Dbg("[SMS.2.OCEAN] diag " + tag + ": ocean root NOT FOUND (LevelBattle=" + (lb != null) + ")");
                }

                var so = StaticOcean;
                if (so == null)
                {
                    HotfixLog.Dbg("[SMS.2.OCEAN] diag " + tag + ": StaticOcean = null");
                }
                else
                {
                    string matName = "?";
                    bool rendererOn = false;
                    try
                    {
                        var mr = so.GetComponent<MeshRenderer>();
                        if (mr != null) { rendererOn = mr.enabled; if (mr.sharedMaterial != null) matName = mr.sharedMaterial.name; }
                    }
                    catch { }

                    string camPos = "?";
                    try { if (G.cam != null) camPos = G.cam.transform.position.ToString("0.#"); } catch { }

                    HotfixLog.Dbg("[SMS.2.OCEAN] diag " + tag + ": plane active=" + so.activeSelf
                                   + " pos=" + so.transform.position.ToString("0.#")
                                   + " lossyScale=" + so.transform.lossyScale.ToString("0.##")
                                   + " parent=" + (so.transform.parent != null ? so.transform.parent.name : "<root>")
                                   + " layer=" + so.layer
                                   + " renderer=" + rendererOn
                                   + " mat='" + matName + "'"
                                   + " tex='" + TexName(so) + "'"
                                   + " cam=" + camPos);
                }
            }
            catch (Exception e) { HotfixLog.Warn("[SMS.2.OCEAN] diag: " + e.Message); }
        }

        internal static float _totalDt;

        /// <summary>
        /// [SMS.2.OCEAN] 照搬 Beta6 UpdateStaticOcean()：平面吸附相机（100 对齐）+ `_MainTex` 滚动。
        /// 由 Patch_DayCycleAndWeather 每帧钩子调用。
        /// </summary>
        internal static void UpdateStaticOcean()
        {
            try
            {
                if (StaticOcean == null || !StaticOceanEnabled) return;
                _totalDt = (_totalDt + Time.deltaTime) % 100f;
                // ★ 海面是**世界固定**的参考平面，不跟随相机。
                //   （跟随会让"海面相对舰船"随视点移动而滑动；duke 的 100 吸附在战斗里会跳变。）
                //   平面尺寸 100000 ⇒ 覆盖 ±50 km，足够所有战斗区域。
                StaticOcean.transform.position = Vector3.zero;
                if (StaticOceanMat != null)
                    StaticOceanMat.SetTextureOffset("_MainTex", new Vector2(_totalDt / 50f, _totalDt / 50f));
            }
            catch { }
        }

        internal static void CreateStaticOcean()
        {
            // ★ 我们自己的简化海面（观感优于 duke 的 cubeVisualizer 方案，见 docs/82 的四点问题）。
            //   触发与调用沿用 duke 的结构（oceanType==1 时创建、StaticOceanEnabled 控制更新）。
            try
            {
                if (StaticOcean != null) return;

                var go = new GameObject("SMS_StaticOcean");
                // 根对象（DontDestroyOnLoad 才有效），世界坐标由 OceanLoop 跟随相机
                try { UnityEngine.Object.DontDestroyOnLoad(go); } catch { }

                go.transform.position = Vector3.zero;
                go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);   // 四边形躺平
                go.transform.localScale = new Vector3(100000f, 100000f, 0.05f);

                var mf = go.AddComponent<MeshFilter>();
                mf.sharedMesh = BuildQuadMesh();

                var mr = go.AddComponent<MeshRenderer>();
                try { mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false; } catch { }

                var shader = Shader.Find("Standard");
                if (shader == null) shader = Shader.Find("Unlit/Texture");
                var mat = new Material(shader);

                var tex = LoadPng("Tileable_Ocean.png");
                if (tex != null)
                {
                    tex.wrapMode = TextureWrapMode.Repeat;
                    if (mat.HasProperty("_MainTex")) mat.mainTexture = tex;
                    float __tiling = TAFHotfixBatch1.HotfixParams.StaticOceanTiling;
                    mat.mainTextureScale = new Vector2(__tiling, __tiling);
                }
                var bump = LoadPng("Tileable_Ocean_Bump.png");
                if (bump != null && mat.HasProperty("_BumpMap"))
                {
                    bump.wrapMode = TextureWrapMode.Repeat;
                    mat.SetTexture("_BumpMap", bump);
                }
                try
                {
                    if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", TAFHotfixBatch1.HotfixParams.StaticOceanGloss);
                    if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", TAFHotfixBatch1.HotfixParams.StaticOceanMetallic);
                    float __a = TAFHotfixBatch1.HotfixParams.StaticOceanAlpha;
                    mat.color = new Color(TAFHotfixBatch1.HotfixParams.StaticOceanR, TAFHotfixBatch1.HotfixParams.StaticOceanG, TAFHotfixBatch1.HotfixParams.StaticOceanB, __a);
                    if (__a < 0.999f)
                    {
                        // Standard 透明模式（Blend SrcAlpha OneMinusSrcAlpha, ZWrite Off）
                        mat.SetOverrideTag("RenderType", "Transparent");
                        mat.SetInt("_SrcBlend", 5);
                        mat.SetInt("_DstBlend", 10);
                        mat.SetInt("_ZWrite", 0);
                        mat.DisableKeyword("_ALPHATEST_ON");
                        mat.EnableKeyword("_ALPHABLEND_ON");
                        mat.renderQueue = 2500;   // 早于曳光/标线等透明效果（它们约 3000）
                    }
                }
                catch { }

                mr.sharedMaterial = mat;

                StaticOcean = go;
                StaticOceanMat = mat;
                StaticOceanEnabled = true;
                _oceanTex = tex;

                HotfixLog.Info("[SMS.2.OCEAN] static ocean created (SMS quad path, tex=" + (tex != null) + ")");
            }
            catch (Exception e) { HotfixLog.Warn("[SMS.2.OCEAN] CreateStaticOcean: " + e.Message); }
        }

        /// <summary>1x1 四边形（朝 +Z，旋转 90° 后躺平）。</summary>
        private static Mesh BuildQuadMesh()
        {
            var m = new Mesh();
            m.vertices = new Vector3[]
            {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3( 0.5f, -0.5f, 0f),
                new Vector3( 0.5f,  0.5f, 0f),
                new Vector3(-0.5f,  0.5f, 0f),
            };
            m.uv = new Vector2[]
            {
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f),
            };
            m.triangles = new int[] { 0, 2, 1, 0, 3, 2 };
            m.RecalculateNormals();
            return m;
        }

        /// <summary>每帧：跟随相机 XZ、滚动贴图。</summary>
        private static System.Collections.IEnumerator OceanLoop()
        {
            while (true)
            {
                yield return null;

                try
                {
                    // Simple 模式：每帧确保"通用海面关、我们的平面开"
                    // （构造器场景在进入时会把 opwOcean4OPW_00 重新激活，只在绑定时机处理会漏掉）
                    int __ot2 = 0;
                    try { __ot2 = global::TweaksAndFixes.UiM.TAF_Settings.settings.oceanType; } catch { }
                    if (__ot2 == 1)
                    {
                        try
                        {
                            if (_oceanRoot == null)
                            {
                                var lb2 = LevelBattle;
                                if (lb2 != null) _oceanRoot = lb2.GetChild("opwOcean4OPW_00");
                            }
                            if (_oceanRoot != null && _oceanRoot.activeSelf)
                            {
                                _oceanRoot.SetActive(false);
                                HotfixLog.Dbg("[SMS.2.OCEAN] simple: re-hide ocean root");
                            }
                        }
                        catch { }

                        if (StaticOcean != null && !StaticOcean.activeSelf)
                            StaticOcean.SetActive(true);

                        EnsureTexture();
                    }

                    if (!StaticOceanEnabled || StaticOcean == null) continue;

                    _oceanDt = (_oceanDt + Time.deltaTime) % 100f;

                    var cam = G.cam;
                    if (cam != null)
                    {
                        var p = cam.transform.position;
                        StaticOcean.transform.position = new Vector3(p.x, 0f, p.z);
                    }

                    if (StaticOceanMat != null)
                        StaticOceanMat.SetTextureOffset("_MainTex", new Vector2(_oceanDt / 50f, _oceanDt / 50f));
                }
                catch { }
            }
        }

        /// <summary>
        /// [SMS.2.OCEAN] 静态海面可行性探针：打印 LevelBattle 下海面对象的组件/材质/贴图信息。
        ///
        /// 背景：duke 的"静态海面"是**降质替代方案**（低质量海水渲染）。
        /// 我们这版没有 `OceanFFT` 对象（探针显示是 `opwOcean4OPW_00`），
        /// 所以先确认这个对象能否被当作"平面 + 滚动贴图"来驱动：
        ///   · 是否有 MeshRenderer / MeshFilter（能不能缩放成平面）
        ///   · 材质有哪些贴图属性（_MainTex / _BumpMap ...）
        /// 开关：`static_ocean_probe`（默认 1，只读）
        /// </summary>

        /// <summary>
        /// [SMS.2.OCEAN] 按 Ocean Type 设置海面模式：
        ///   0 = 默认（原版 FFT 海面，若之前建了静态海面则销毁）
        ///   1 = 静态海面（由 CreateStaticOcean 负责）
        ///   2 = 无海面（隐藏静态海面；原版海面本身在构造器里已 scale=0 的占位对象不影响）
        /// </summary>

        // ======================================================================
        //  [SMS.2.OCEAN] 空水 = **照搬 duke 的 `taf_disable_all_water` 行为**
        //  来源：duke `TweaksAndFixes/Harmony/GameManager.cs`（ToMainMenuCoroutine，我们已有同段代码）
        //        if (Config.Param("taf_disable_all_water", 0) == 1)
        //        {
        //            LevelBattle.GetChild("opwOcean4OPW_00").active = false;
        //            LevelConstructor.GetChild("MoveWithShip").active = false;
        //            waterHC/BC/MC/SC.active = false;      // 4 个船渠水
        //        }
        //  我们此前只关了 oceanFFT，没关这些 ⇒ 空水模式仍有水/波光。
        // ======================================================================
        private static readonly string[] DockWaterNames =
        {
            "water_in_huge_dock_002", "water_in_big_dock_002",
            "water_in_middle_dock_002", "water_in_smoll_dock_002",
        };

        private static GameObject RootOf(GameObject level, string child)
        {
            try { return level != null ? level.transform.Find(child)?.gameObject : null; } catch { return null; }
        }

        internal static void ApplyDukeAllWater(bool disable)
        {
            try
            {
                var battle = LevelBattle;
                var ctor = LevelConstructor;

                var oceanRoot = RootOf(battle, "opwOcean4OPW_00");
                if (oceanRoot != null) { oceanRoot.SetActive(!disable); HotfixLog.Dbg("[SMS.2.OCEAN] opwOcean4OPW_00 active=" + (!disable)); }

                var moveWithShip = RootOf(ctor, "MoveWithShip");
                if (moveWithShip != null) { moveWithShip.SetActive(!disable); HotfixLog.Dbg("[SMS.2.OCEAN] MoveWithShip active=" + (!disable)); }

                // 船渠水：duke 建在 Dock/DryDock_001 下
                GameObject dock = null;
                try { dock = ctor != null ? ctor.transform.Find("Dock/DryDock_001")?.gameObject : null; } catch { }

                int n = 0;
                if (dock != null)
                {
                    for (int i = 0; i < DockWaterNames.Length; i++)
                    {
                        var tr = dock.transform.Find(DockWaterNames[i]);
                        if (tr == null) continue;
                        tr.gameObject.SetActive(!disable);
                        n++;
                    }
                }

                HotfixLog.Info("[SMS.2.OCEAN] ApplyDukeAllWater disable=" + disable + " dockWater=" + n);
            }
            catch (Exception e) { HotfixLog.Warn("[SMS.2.OCEAN] ApplyDukeAllWater: " + e.Message); }
        }

        internal static readonly System.Collections.Generic.List<GameObject> _disabledForNone
            = new System.Collections.Generic.List<GameObject>();

        internal static void SetOceanMode(int mode)
        {
            try
            {
                bool wantStatic = (mode == 1);
                bool wantVanilla = (mode == 0);
                bool wantNone = (mode == 2);

                // ---- 1) 原版 FFT 海面（战斗/港口的主海面）----
                var dcwObj = DayCycleAndWeatherO;
                if (dcwObj != null)
                {
                    var dcw = dcwObj.GetComponent<DayCycleAndWeather>();
                    var fft = dcw != null ? dcw.oceanFFT : null;
                    if (fft != null && fft.gameObject != null)
                    {
                        fft.gameObject.SetActive(wantVanilla);
                        HotfixLog.Dbg("[SMS.2.OCEAN] vanilla FFT ocean -> active=" + wantVanilla);
                    }
                    else HotfixLog.Warn("[SMS.2.OCEAN] oceanFFT is null (cannot toggle vanilla ocean)");
                }

                // ---- 2) 我们的静态平面 ----
                if (wantStatic)
                {
                    // ★ Simple：只关通用海面（duke 写法），船渠水/MoveWithShip 保持
                    try
                    {
                        var lb = LevelBattle;
                        if (lb != null)
                        {
                            var oceanRoot = lb.GetChild("opwOcean4OPW_00");
                            if (oceanRoot != null) { oceanRoot.active = false; }
                        }
                        var lc = LevelConstructor;
                        if (lc != null)
                        {
                            var mws = lc.GetChild("MoveWithShip");
                            if (mws != null) mws.active = true;
                        }
                        HotfixLog.Dbg("[SMS.2.OCEAN] simple: ocean root off");
                    }
                    catch (Exception e) { HotfixLog.Warn("[SMS.2.OCEAN] simple: " + e.Message); }

                    // ★ 每次场景绑定都要重建（构造器/共享设计是不同场景，主菜单建的平面不会跟过来）
                    if (StaticOcean == null)
                        CreateStaticOcean();
                    else
                    {
                        try
                        {
                            if (!StaticOcean.activeSelf) StaticOcean.SetActive(true);
                            StaticOcean.transform.localScale = new Vector3(100000f, 100000f, 0.05f);
                        }
                        catch { }
                    }
                }
                else if (StaticOcean != null)
                {
                    UnityEngine.Object.Destroy(StaticOcean);
                    StaticOcean = null;
                    StaticOceanMat = null;
                    StaticOceanEnabled = false;
                    HotfixLog.Dbg("[SMS.2.OCEAN] static ocean removed (mode=" + mode + ")");
                }

                // ---- 3) None 模式：关掉"船渠里的水"与"水下光效/雾" ----
                // ★ 只有 None（空水）才走 duke 的整组关闭；
                //   Simple 绝不能调用它——它会把 opwOcean4OPW_00 重新打开（port92/98 的干扰源）。
                // ★ port92 的分叉方案：默认**不启用**（现由 GameManager 的 __oceanMode 分支统一负责）。
                //   需要对照实验时设 ocean_duke_all_water=1。
                if (global::TAFHotfixBatch1.HotfixParams.OceanDukeAllWater)
                {
                    if (wantNone) ApplyDukeAllWater(true);
                    else if (wantVanilla) ApplyDukeAllWater(false);
                }



                if (wantNone)
                {
                    DisableWaterAndUnderwaterEffects();
                    // ⚠️ 守夜人方案在 port90 里被证实会误伤船体（waterline 等），默认关闭，仅作实验保留。
                    if (global::TAFHotfixBatch1.HotfixParams.OceanKeeper)
                        OceanNoneKeeper.EnsureRunning();
                }
                else
                {
                    RestoreWaterAndUnderwaterEffects();
                }
                DiagOcean("after SetOceanMode mode=" + mode);
            }
            catch (Exception e) { HotfixLog.Warn("[SMS.2.OCEAN] SetOceanMode: " + e.Message); }
        }

        /// <summary>None 模式：关闭船渠水面 + 水下光效/雾。</summary>
        private static void DisableWaterAndUnderwaterEffects()
        {
            try
            {
                if (_disabledForNone.Count > 0) return;   // 已处理

                // ★ 用全场景扫描代替"猜层级"：把所有"看起来像水"的 MeshRenderer 关掉
                //   判据：对象名 或 材质名 或 shader 名 含 water/ocean/sea/pool 等关键词
                int scanned = 0, hidden = 0;
                try
                {
                    var all = UnityEngine.Object.FindObjectsOfType<MeshRenderer>();
                    if (all != null)
                    {
                        for (int k = 0; k < all.Length; k++)
                        {
                            var mr = all[k];
                            if (mr == null) continue;
                            scanned++;

                            string goName = "";
                            string matName = "";
                            string shaderName = "";
                            try { goName = mr.gameObject.name; } catch { }
                            try { if (mr.sharedMaterial != null) matName = mr.sharedMaterial.name; } catch { }
                            try { if (mr.sharedMaterial != null && mr.sharedMaterial.shader != null) shaderName = mr.sharedMaterial.shader.name; } catch { }

                            // ★ 3.22.0-SMS.2：**废弃按关键字模糊匹配**。
                            //   历史上用 "water/ocean/sea/..." 匹配曾误伤舰船的 waterline 材质
                            //   （Pola/Zara/Fiume 冷启动 NRE）。现在只用**已知名称白名单**。
                            string gn = goName.ToLowerInvariant();
                            bool isWater = gn.StartsWith("opwocean4opw_00")
                                        || gn.StartsWith("water_in_")
                                        || gn.StartsWith("water_all_")
                                        || gn.StartsWith("drydock_water")
                                        || gn == "water_all_001";

                            if (!isWater) continue;

                            // 不要把我们自己的静态海面也列进来
                            try { if (StaticOcean != null && mr.gameObject == StaticOcean) continue; } catch { }

                            if (_debugLog++ < 40)
                                HotfixLog.Dbg("[SMS.2.OCEAN] water candidate: go='" + goName
                                               + "' mat='" + matName + "' shader='" + shaderName + "'");

                            mr.gameObject.SetActive(false);
                            _disabledForNone.Add(mr.gameObject);
                            hidden++;
                        }
                    }
                }
                catch (Exception e) { HotfixLog.Warn("[SMS.2.OCEAN] scan water: " + e.Message); }

                // 反射探针（"水面反光"观感的主要来源之一）
                try
                {
                    var probes = UnityEngine.Object.FindObjectsOfType<ReflectionProbe>();
                    if (probes != null)
                    {
                        for (int k = 0; k < probes.Length; k++)
                        {
                            var rp = probes[k];
                            if (rp == null) continue;
                            rp.gameObject.SetActive(false);
                            _disabledForNone.Add(rp.gameObject);
                        }
                    }
                }
                catch { }

                // 全局雾
                try { RenderSettings.fog = false; } catch { }

                try { LogNoneCandidates("after-hide"); } catch { }
                HotfixLog.Dbg("[SMS.2.OCEAN] none mode: scanned=" + scanned + " waterHidden=" + hidden
                               + " totalDisabled=" + _disabledForNone.Count);
            }
            catch (Exception e) { HotfixLog.Warn("[SMS.2.OCEAN] DisableWater: " + e.Message); }
        }

        // [SMS.2.OCEAN] none-mode: list candidate water/fog renderers (log only, no modification)
        private static bool _noneScanLogged;

        internal static void LogNoneCandidates(string tag)
        {
            if (_noneScanLogged) return;
            _noneScanLogged = true;
            try
            {
                int n = 0;
                foreach (var mr in UnityEngine.Object.FindObjectsOfType<MeshRenderer>())
                {
                    if (mr == null) continue;
                    string gn = "", mn = "", sn = "";
                    try { gn = mr.gameObject.name; } catch { }
                    try { if (mr.sharedMaterial != null) mn = mr.sharedMaterial.name; } catch { }
                    try { if (mr.sharedMaterial != null && mr.sharedMaterial.shader != null) sn = mr.sharedMaterial.shader.name; } catch { }
                    string all = (gn + "|" + mn + "|" + sn).ToLowerInvariant();
                    if (!(all.Contains("water") || all.Contains("ocean") || all.Contains("sea")
                          || all.Contains("wave") || all.Contains("fog") || all.Contains("underwater")
                          || all.Contains("mist") || all.Contains("haze")))
                        continue;

                    Bounds b = default;
                    try { b = mr.bounds; } catch { }
                    HotfixLog.Info("[SMS.2.OCEAN] none-candidate(" + tag + ") go='" + gn
                                   + "' mat='" + mn + "' shader='" + sn
                                   + "' active=" + mr.gameObject.activeInHierarchy
                                   + " bounds{" + b.center + " | " + b.size + "}");
                    if (++n >= 40) break;
                }
                HotfixLog.Info("[SMS.2.OCEAN] none-candidate scan done: " + n);
            }
            catch (System.Exception e) { HotfixLog.Warn("[SMS.2.OCEAN] none-candidate: " + e.Message); }
        }

        private static int _debugLog;

        private static void RestoreWaterAndUnderwaterEffects()
        {
            try
            {
                if (_disabledForNone.Count == 0) return;
                foreach (var go in _disabledForNone)
                {
                    try { if (go != null) go.SetActive(true); } catch { }
                }
                _disabledForNone.Clear();
                try { RenderSettings.fog = true; } catch { }
                HotfixLog.Dbg("[SMS.2.OCEAN] restored water/fog objects");
            }
            catch { }
        }

        internal static void ProbeOcean()
        {
            try
            {
                var lb = Patch_SceneManagerPreview.LevelBattle;
                if (lb == null) { HotfixLog.Dbg("[SMS.2.OCEAN] LevelBattle=null"); return; }

                var tr = lb.transform;
                for (int i = 0; i < tr.childCount; i++)
                {
                    var c = tr.GetChild(i);
                    if (c == null) continue;
                    string nm = c.name;
                    if (!(nm.Contains("Ocean") || nm.Contains("ocean") || nm.Contains("Water") || nm.Contains("water")))
                        continue;

                    var sb = new StringBuilder();
                    sb.Append("[SMS.2.OCEAN] ocean obj '").Append(nm).Append("'")
                      .Append(" pos=").Append(c.position.ToString("0.#"))
                      .Append(" scale=").Append(c.lossyScale.ToString("0.##"));

                    var mf = c.GetComponent<MeshFilter>();
                    var mr = c.GetComponent<MeshRenderer>();
                    sb.Append(" meshFilter=").Append(mf != null)
                      .Append(" meshRenderer=").Append(mr != null);

                    if (mr != null && mr.sharedMaterial != null)
                    {
                        var mat = mr.sharedMaterial;
                        sb.Append(" mat='").Append(mat.name).Append("'");
                        sb.Append(" shader='").Append(mat.shader != null ? mat.shader.name : "?").Append("'");
                        try
                        {
                            if (mat.HasProperty("_MainTex") && mat.mainTexture != null)
                                sb.Append(" mainTex='").Append(mat.mainTexture.name).Append("'");
                        }
                        catch { }
                    }

                    HotfixLog.Dbg(sb.ToString());
                }

                var dcw = Patch_SceneManagerPreview.DayCycleAndWeatherO;
                HotfixLog.Dbg("[SMS.2.OCEAN] DayCycleAndWeather=" + (dcw != null ? dcw.name : "null"));
            }
            catch (Exception e) { HotfixLog.Warn("[SMS.2.OCEAN] probe: " + e.Message); }
        }

    /// <summary>切换"预览模式"（见上）。未初始化时为空操作。</summary>
    public static void ConfigureForPreview(bool active)
    {
        if (!PreviewInited) return;

        try
        {
            if (StaticOceanEnabled && StaticOcean != null)
            {
                StaticOcean.transform.localScale = active
                    ? new Vector3(100000f, 0.05f, 100000f)
                    : new Vector3(0f, 0f, 0f);
            }

            var sun = LevelConstructor != null ? Util.GetChild(LevelConstructor, "Sun", false) : null;
            if (sun != null) sun.active = !active;

            var lighting = LevelBattle != null ? Util.GetChild(LevelBattle, "Scene Lighting", false) : null;
            if (lighting != null) lighting.active = active;
        }
        catch (Exception e)
        {
            HotfixLog.Warn("[SMS.2.SCENE] ConfigureForPreview: " + e.Message);
        }
    }

    }
}

#endif

    // ======================================================================
    //  [SMS.2.REFL] 反射探针强度（移植 Beta6 Patch_SceneManager/ConfigureScene）
    //    构造器/预览：taf_constructor_reflection_intensity（默认 1.5）
    //    战斗场景  ：taf_battle_reflection_intensity（默认 0.5）
    //  反射强度直接影响弧形舯段的色调；缺失时会出现"某些段颜色与其它段不同"。
    // ======================================================================
    internal static class ReflectionIntensity
    {
        internal static ReflectionProbe Probe;

        internal static void Apply(bool battle)
        {
            try
            {
                if (Probe == null)
                {
                    var probes = UnityEngine.Object.FindObjectsOfType<ReflectionProbe>();
                    if (probes != null && probes.Length > 0)
                        Probe = probes[0];
                }

                if (Probe == null)
                {
                    HotfixLog.Warn("[SMS.2.REFL] no ReflectionProbe found");
                    return;
                }

                float def = battle ? 0.5f : 1.5f;
                float v = global::TweaksAndFixes.Config.Param(battle ? "taf_battle_reflection_intensity"
                                             : "taf_constructor_reflection_intensity", def);

                Probe.intensity = v;
                HotfixLog.Dbg("[SMS.2.REFL] probe='" + Probe.name
                               + "' battle=" + battle + " intensity=" + v.ToString("0.##"));
            }
            catch (Exception e) { HotfixLog.Warn("[SMS.2.REFL] apply: " + e.Message); }
        }
    }

// ======================================================================
//  [SMS.2.OCEAN] None 模式"守夜人"：周期性把水面类 renderer 关掉。
//
//  背景：oceanType=None 时我们在场景绑定时关了水，但之后水面对象会被重建/重新激活
//  （主菜单、设计模式、战斗加载都可能），所以表现成"水又回来了"。
//  做法：每 1 秒扫一次场景，把名字/材质/shader 含 water/ocean/sea/wave/pool/basin/canal/harbour
//  的 MeshRenderer 关掉（跳过我们自己的 StaticOcean）。
// ======================================================================
internal static class OceanNoneKeeper
{
    private static bool _running;
    internal static int Disabled;

    internal static void EnsureRunning()
    {
        if (_running) return;
        _running = true;
        try { MelonLoader.MelonCoroutines.Start(Loop()); }
        catch (Exception e) { HotfixLog.Warn("[SMS.2.OCEAN] keeper start: " + e.Message); }
    }

    private static System.Collections.IEnumerator Loop()
    {
        int logged = 0;
        while (true)
        {
            for (int i = 0; i < 60; i++) yield return null;   // ~1 秒

            try
            {
                int ot = 0;
                try { ot = TweaksAndFixes.UiM.TAF_Settings.settings.oceanType; } catch { }
                if (ot != 2) continue;

                var all = UnityEngine.Object.FindObjectsOfType<MeshRenderer>();
                if (all == null) continue;

                for (int k = 0; k < all.Length; k++)
                {
                    var mr = all[k];
                    if (mr == null || !mr.gameObject.activeSelf) continue;

                    string blob = "";
                    try { blob += mr.gameObject.name; } catch { }
                    try { if (mr.sharedMaterial != null) blob += "|" + mr.sharedMaterial.name; } catch { }
                    try { if (mr.sharedMaterial != null && mr.sharedMaterial.shader != null) blob += "|" + mr.sharedMaterial.shader.name; } catch { }
                    blob = blob.ToLowerInvariant();

                    if (blob.Contains("waterline") || blob.Contains("hull") || blob.Contains("ship")) continue;

                    if (!(blob.Contains("water") || blob.Contains("ocean") || blob.Contains("sea")
                          || blob.Contains("wave") || blob.Contains("pool") || blob.Contains("basin")
                          || blob.Contains("canal") || blob.Contains("harbour") || blob.Contains("harbor")))
                        continue;

                    try
                    {
                        if (global::TweaksAndFixes.Patch_SceneManagerPreview.StaticOcean != null &&
                            mr.gameObject == global::TweaksAndFixes.Patch_SceneManagerPreview.StaticOcean) continue;
                    }
                    catch { }

                    if (logged++ < 25)
                        HotfixLog.Info("[SMS.2.OCEAN] keeper off '" + blob + "'");

                    mr.gameObject.SetActive(false);
                    Disabled++;
                }
            }
            catch { }
        }
    }
}
