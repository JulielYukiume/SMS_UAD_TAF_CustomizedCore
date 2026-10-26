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
            LevelConstructor.GetChild("Probes").active = true;
            LevelBattle.GetChild("WaterLevel").active = true;

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
    // ★ [SMS.1.SCENE] 3.22 B6 的"预览模式"开关（移植自 Beta6 Patch_SceneManager）。
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

    /// <summary>[SMS.1.OCEAN] 战斗/构造器场景里的天气对象（由场景配置阶段赋值）。</summary>
    internal static GameObject DayCycleAndWeatherO;


        /// <summary>
        /// [SMS.1.OCEAN] 静态海面可行性探针：打印 LevelBattle 下海面对象的组件/材质/贴图信息。
        ///
        /// 背景：duke 的"静态海面"是**降质替代方案**（低质量海水渲染）。
        /// 我们这版没有 `OceanFFT` 对象（探针显示是 `opwOcean4OPW_00`），
        /// 所以先确认这个对象能否被当作"平面 + 滚动贴图"来驱动：
        ///   · 是否有 MeshRenderer / MeshFilter（能不能缩放成平面）
        ///   · 材质有哪些贴图属性（_MainTex / _BumpMap ...）
        /// 开关：`static_ocean_probe`（默认 1，只读）
        /// </summary>
        internal static void ProbeOcean()
        {
            try
            {
                var lb = Patch_SceneManagerPreview.LevelBattle;
                if (lb == null) { HotfixLog.Info("[SMS.1.OCEAN] LevelBattle=null"); return; }

                var tr = lb.transform;
                for (int i = 0; i < tr.childCount; i++)
                {
                    var c = tr.GetChild(i);
                    if (c == null) continue;
                    string nm = c.name;
                    if (!(nm.Contains("Ocean") || nm.Contains("ocean") || nm.Contains("Water") || nm.Contains("water")))
                        continue;

                    var sb = new StringBuilder();
                    sb.Append("[SMS.1.OCEAN] ocean obj '").Append(nm).Append("'")
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

                    HotfixLog.Info(sb.ToString());
                }

                var dcw = Patch_SceneManagerPreview.DayCycleAndWeatherO;
                HotfixLog.Info("[SMS.1.OCEAN] DayCycleAndWeather=" + (dcw != null ? dcw.name : "null"));
            }
            catch (Exception e) { HotfixLog.Warn("[SMS.1.OCEAN] probe: " + e.Message); }
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
            HotfixLog.Warn("[SMS.1.SCENE] ConfigureForPreview: " + e.Message);
        }
    }

    }
}

#endif