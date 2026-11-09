using MelonLoader;
using HarmonyLib;
using UnityEngine;
using Il2Cpp;
using System.Reflection;
using TweaksAndFixes.Data;
using static Il2Cpp.Ship;
using UnityEngine.UI;
using TweaksAndFixes.Harmony;
using static TweaksAndFixes.UiM;

#pragma warning disable CS8625
using System;
using System.Collections.Generic;
using TAFHotfixBatch1;

namespace TweaksAndFixes
{

    // ########## HIT CHANCE OVERRIDES ########## //

    [HarmonyPatch(typeof(Ship.HitChanceCalc))]
    internal class Patch_Ship_HitChanceCalc
    {
        internal static MethodBase TargetMethod()
        {
            //return AccessTools.Method(typeof(Part), nameof(Part.CanPlace), new Type[] { typeof(string).MakeByRefType(), typeof(List<Part>).MakeByRefType(), typeof(List<Collider>).MakeByRefType() });

            // Do this manually
            var methods = AccessTools.GetDeclaredMethods(typeof(Ship.HitChanceCalc));
            foreach (var m in methods)
            {
                if (m.Name != nameof(Ship.HitChanceCalc.Add))
                    continue;

                if (m.GetParameters().Length == 3)
                    return m;
            }

            return null;
        }

        public static Dictionary<string, HashSet<string>> IgnoreList = new Dictionary<string, HashSet<string>>();

        static Patch_Ship_HitChanceCalc()
        {
        }

        internal static void Prefix(Ship.HitChanceCalc __instance, ref float mult, ref string reason, ref string value)
        {
            if (!AccuraciesExInfo.HasEntries())
            {
                return;
            }

            // Some multipliers are unamed, these have no real corrolation, so they are ignored.
            if (reason.Length == 0)
            {
                return;
            }

            if (IgnoreList.ContainsKey(reason) && (IgnoreList[reason].Contains(value) || IgnoreList[reason].Contains("All")))
            {
                return;
            }

            float modifiedMultiplier = mult;
            string modifiedName = reason;
            string modifiedSubname = value;

            bool changed = AccuraciesExInfo.UpdateAccuracyInfo(ref modifiedName, ref modifiedSubname, ref modifiedMultiplier);

            mult = modifiedMultiplier;
            reason = modifiedName;
            value = modifiedSubname;

            if (!changed)
            {
                if (!IgnoreList.ContainsKey(reason))
                {
                    IgnoreList[reason] = new HashSet<string>();
                }

                if (!IgnoreList[reason].Contains(value))
                {
                    IgnoreList[reason].Add(value);
                    Melon<TweaksAndFixes>.Logger.Error("Unknown accuracy modifier: " + reason + " : " + value + " : " + mult);
                }
            }
            else
            {
                // Melon<TweaksAndFixes>.Logger.Msg(reason + " : " + value + " : " + mult + " = " + __instance.multsCombined);
            }
        }
    }


    [HarmonyPatch(typeof(Ship))]
    internal class Patch_Ship
    {
        // ########## NEW CONSTRUCTOR LOGIC ########## //

        public static Ship LastCreatedShip;
        public static float LastClonedShipWeight = 0;

        [HarmonyPostfix]
        [HarmonyPatch(nameof(Ship.Create))]
        internal static void Postfix_Create(Ship __result, Ship design, Player player, bool isTempForBattle = false, bool isPrewarming = false, bool isSharedDesign = false)
        {
            // LastCreatedShip = __result;
            // 
            // if (LastCreatedShip == null) return;

            // Melon<TweaksAndFixes>.Logger.Msg($"{__result.id} : {__result.tonnage} + {(design != null ? (design.id + " : " + design.tonnage) : "NO DESIGN")}");

            LastClonedShipWeight = 0;
            if (design != null)
            {
                LastClonedShipWeight = design.tonnage;
            }

            // foreach (Mount mount in LastCreatedShip.mounts)
            // {
            //     Melon<TweaksAndFixes>.Logger.Msg(LastCreatedShip.Name(false, false) + ": " + mount.caliberMin + " - " + mount.caliberMax);
            // }
        }

        [HarmonyPostfix]
        [HarmonyPatch(nameof(Ship.GetRefitYearNameEnd))]
        internal static void Prefix_GetRefitYearNameEnd(Ship __instance, ref string __result)
        {
            // Melon<TweaksAndFixes>.Logger.Msg($"GetRefitYearNameEnd: {(__instance.designShipForRefit == null ? "NULL" : __instance.designShipForRefit.Name(false, false))}");
            // Melon<TweaksAndFixes>.Logger.Msg($"                     `{__instance.Name(true, false, false, false, false)}` `{__instance.Name(false, false, false, false, true)}` `{__instance.Name(true, false, false, false, true)}`");
            // Melon<TweaksAndFixes>.Logger.Msg($"                     Original `{__result}`");

            string prefix = 
                __result.Contains(__instance.Name(false, false, false, false, true))
                    ? $"{__instance.Name(false, false, false, false, true)}"
                    : "";

            // string prefix = __result[^1] != '2' ? $"{__result.Substring(0, __result.LastIndexOf('(') - 1)}" : "";
            int year = CampaignController.Instance.CurrentDate.AsDate().Year;
            int month = CampaignController.Instance.CurrentDate.AsDate().Month;
            string monthText;

            switch (TAF_Settings.settings.refitDateFormat)
            {
                case 0:
                    monthText = ModUtils.NumToMonth(month);
                    __result = $"{prefix} ({monthText} {year})";
                    break;
                case 1:
                    __result = $"{prefix} ({month:00}/{year})";
                    break;
                case 2:
                    monthText = ModUtils.NumToMonth(month);
                    __result = $"{prefix} ({year} {monthText})";
                    break;
                case 3:
                    __result = $"{prefix} ({year}/{month:00})";
                    break;
                default:
                    break;
            }

            // Melon<TweaksAndFixes>.Logger.Msg($"                     New      `{__result}`");
        }

        [HarmonyPrefix]
        [HarmonyPatch(nameof(Ship.RemovePart))]
        internal static bool Prefix_RemovePart(Ship __instance, Part part)
        {
            if (__instance == null) return false;

            if (part != Patch_Ui.SelectedPart && part.mount != null && part == Patch_Part.TrySkipDestroy || !__instance.parts.Contains(part))
            {
                Patch_Part.TrySkipDestroy = null;
                return false;
            }

            return true;
        }

        [HarmonyPostfix]
        [HarmonyPatch(nameof(Ship.RemovePart))]
        internal static void Postfix_RemovePart(Ship __instance, Part part)
        {
            // Melon<TweaksAndFixes>.Logger.Msg(part.Name() + ": Removed");

            if (!Patch_Ui.UseNewConstructionLogic()) return;

            if (part == Patch_Ui.PickupPart && Input.GetMouseButtonUp(0))
            {
                Patch_Ui.PickedUpPart = true;
                // Melon<TweaksAndFixes>.Logger.Msg(part.Name() + ": Might be a pickup");
            }

            if (!_IsInChangeHullWithHuman && Patch_Part.unmatchedParts.Contains(part)) Patch_Part.unmatchedParts.Remove(part);

            if (!_IsInChangeHullWithHuman && G.settings.autoMirror && Patch_Part.mirroredParts.ContainsKey(part))
            {
                Part A = part;
                Part B = Patch_Part.mirroredParts[part];
                Patch_Part.mirroredParts.Remove(A);
                Patch_Part.mirroredParts.Remove(B);

                if (Patch_Part.applyMirrorFromTo.ContainsKey(A))
                {
                    Patch_Part.applyMirrorFromTo.Remove(A);
                }
                else
                {
                    Patch_Part.applyMirrorFromTo.Remove(B);
                }

                if (part == A)
                {
                    __instance.RemovePart(B);
                }
                else
                {
                    __instance.RemovePart(A);
                }
            }

            foreach (var mount in part.mountsInside)
            {
                if (mount.employedPart != null)
                {
                    // Melon<TweaksAndFixes>.Logger.Msg($"  {mount.employedPart.Name()}");
                    __instance.RemovePart(mount.employedPart);
                }
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(nameof(Ship.CStats))]
        internal static void Postfix_CStats(Ship __instance)
        {
            if (__instance.stats_.ContainsKey(G.GameData.stats["floatability"]))
            {
                var stat = __instance.stats_[G.GameData.stats["floatability"]];

                // Melon<TweaksAndFixes>.Logger.Msg($"floatability:");
                // Melon<TweaksAndFixes>.Logger.Msg($"  {stat.basic}");
                // Melon<TweaksAndFixes>.Logger.Msg($"  {stat.misc}");
                // Melon<TweaksAndFixes>.Logger.Msg($"  {stat.tech}");
                // Melon<TweaksAndFixes>.Logger.Msg($"  {stat.modifiers}");
                // Melon<TweaksAndFixes>.Logger.Msg($"  {stat.total}");

                // Max = 150
                if (stat.total > Config.Param("taf_ship_stat_floatability_cap", 140f))
                {
                    stat.basic = Config.Param("taf_ship_stat_floatability_cap", 140f) - stat.modifiers;
                }
            }

            if (__instance.stats_.ContainsKey(G.GameData.stats["endurance"]))
            {
                var stat = __instance.stats_[G.GameData.stats["endurance"]];

                // Melon<TweaksAndFixes>.Logger.Msg($"endurance:");
                // Melon<TweaksAndFixes>.Logger.Msg($"  {stat.basic}");
                // Melon<TweaksAndFixes>.Logger.Msg($"  {stat.misc}");
                // Melon<TweaksAndFixes>.Logger.Msg($"  {stat.tech}");
                // Melon<TweaksAndFixes>.Logger.Msg($"  {stat.modifiers}");
                // Melon<TweaksAndFixes>.Logger.Msg($"  {stat.total}");

                // Max = 200
                if (stat.total > Config.Param("taf_ship_stat_endurance_cap", 175f))
                {
                    stat.basic = Config.Param("taf_ship_stat_endurance_cap", 175f) - stat.modifiers;
                }
            }
        }


        // PartMats

        [HarmonyPostfix]
        [HarmonyPatch(nameof(Ship.PartMats))]
        internal static void Prefix_PartMats(Ship __instance, ref Il2CppSystem.Collections.Generic.List<MatInfo> __result)
        {
            // Melon<TweaksAndFixes>.Logger.Msg($"Costs:");

            foreach (var mat in __result)
            {
                // Melon<TweaksAndFixes>.Logger.Msg($"  {mat.name} : {mat.cost}");

                if (__instance.shipType.paramx.ContainsKey(mat.name))
                {
                    foreach (var mod in __instance.shipType.paramx[mat.name])
                    {
                        var split = mod.Split(':');

                        if (split.Length != 2)
                        {
                            Melon<TweaksAndFixes>.Logger.Error($"Invalid cost modifier param for `{mat.name}`: `{mod}`. Invalid format. Should be name(cost:#;weight:#) or name(cost:#) or name(weight:#).");
                        }

                        string type = split[0];
                        string numRaw = split[1];

                        if (!float.TryParse(numRaw, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out float mult))
                        {
                            Melon<TweaksAndFixes>.Logger.Error($"Invalid cost modifier param for `{mat.name}`: `{numRaw}`. Invalid number.");
                        }

                        if (type == "cost")
                        {
                            // Melon<TweaksAndFixes>.Logger.Msg($"    {mat.cost} -> {mat.cost * mult}");
                            mat.cost *= mult;
                        }
                        else if (type == "weight")
                        {
                            // Melon<TweaksAndFixes>.Logger.Msg($"    {mat.weight} -> {mat.weight * mult}");
                            mat.weight *= mult;
                        }
                    }
                }
            }
        }

        [HarmonyPatch(nameof(Ship.EnterConstructor))]
        [HarmonyPrefix]
        internal static void Prefix_EnterConstructor(Ship __instance)
        {
            if (GameManager.IsWorld && __instance.player.isAi)
            {
                Melon<TweaksAndFixes>.Logger.Msg($"Making {__instance.vesselName} visible in world.");
                __instance.gameObject.transform.position += new Vector3(0, -1000, 0);
            }
        }

        [HarmonyPatch(nameof(Ship.Hover))]
        [HarmonyPrefix]
        internal static bool Prefix_Hover(Ship __instance, bool hover)
        {
            __instance.isHovered = hover;
            foreach (var p in __instance.hullAndParts)
                p.SetVisualMode(hover ? Part.VisualMode.Hover : Part.VisualMode.Normal);

            return false;
        }

        // ########## Ship Scuttling ########## //

        // CheckForSurrender

        [HarmonyPrefix]
        [HarmonyPatch(nameof(Ship.CheckForSurrender))]
        internal static bool Prefix_CheckForSurrender(Ship __instance, bool force)
        {
            float sinkThreashold = Config.Param("crew_percents_surrender_threshold", 0.3f);

            if (sinkThreashold <= __instance.CrewPercents && !force)
            {
                return true;
            }

            if (__instance.shipType.param.Contains("surrenders"))
            {
                Patch_Ui.replaceReportImportant = ModUtils.LocalizeF("$TAF_Ui_ReportShipSurrendered", __instance.Name());
                return true;
            }

            Patch_Ui.replaceReportImportant = ModUtils.LocalizeF("$TAF_Ui_ReportShipScuttled", __instance.Name());
            __instance.Sink("flooding");

            MelonCoroutines.Start(ExplodeCharges(
                __instance,
                BattleManager.Instance.CurrentBattle.Timer.leftTime,
                (float)System.Random.Shared.NextDouble() * 60 + 30
            ));

            // Effect.WaterSplash(10, __instance.gameObject.transform.position, new Quaternion());

            // Melon<TweaksAndFixes>.Logger.Msg($"Create Torp:");

            // Melon<TweaksAndFixes>.Logger.Msg($"Done:");

            return false;
        }

        private static bool IsBattleEnd(Ship ship)
        {
            return ship == null
                || Patch_SceneManager.sceneState != GameManager.GameState.Battle;
        }

        internal static System.Collections.IEnumerator ExplodeCharges(Ship ship, float startTime, float durration)
        {
            bool reportedWillScuttle = false;

            while (!IsBattleEnd(ship) && BattleManager.Instance.CurrentBattle.Timer.leftTime > startTime - durration)
            {
                if (!reportedWillScuttle && BattleManager.Instance.CurrentBattle.Timer.leftTime < startTime - durration + 5)
                {
                    G.ui.ReportImportant(ModUtils.LocalizeF("$TAF_Ui_ReportShipScuttling", ship.Name()), ship);
                    reportedWillScuttle = true;
                }

                yield return new WaitForSeconds(1);
            }

            if (IsBattleEnd(ship)) goto EXIT;

            Melon<TweaksAndFixes>.Logger.Msg($"Beginning scuttle effect for ship {ship.Name(false, false)}...");

            var mockPart = new Part();
            mockPart.data = G.GameData.parts["torpedo_x0"];
            mockPart.ship = ship;
            mockPart._ship_k__BackingField = ship;

            var sectionsGo = ship.hull.model.gameObject.GetChild("Visual").GetChild("Sections");

            if (sectionsGo == null)
            {
                Melon<TweaksAndFixes>.Logger.Msg($"  Error! Could not find `Sections` game object in ship! Aborting charge effect!\n{ModUtils.DumpHierarchy(ship.hull.gameObject)}");
                goto EXIT;
            }

            float foreZ = 0;
            float rearZ = 0;

            foreach (var section in sectionsGo.GetChildren())
            {
                if (!section.active) continue;

                foreach (var mesh in section.transform.GetComponentsInChildren<MeshRenderer>())
                {
                    if (!mesh.gameObject.name.ToLower().Contains("hull")) continue;

                    float lengthZ = mesh.bounds.size.z;

                    if (section.transform.localPosition.z + lengthZ > foreZ)
                    {
                        foreZ = section.transform.localPosition.z + lengthZ;
                    }

                    if (section.transform.localPosition.z - lengthZ < rearZ)
                    {
                        rearZ = section.transform.localPosition.z - lengthZ;
                    }
                }
            }

            float lastCharge = 0;

            for (float i = 0.25f; i < 1f; i += 0.25f)
            {
                lastCharge = BattleManager.Instance.CurrentBattle.Timer.leftTime;

                var leftCharge = Torpedo.Create(
                    mockPart, ship.gameObject.transform.position,
                    Vector3.right, 9999999, 0
                );
                leftCharge.gameObject.SetParent(sectionsGo);

                leftCharge.transform.localPosition = new Vector3(-ship.collision.radius, 0, Mathf.Lerp(foreZ, rearZ, i));// i * ship.collision.height / 4);
                leftCharge.torpedoEffectScale = ship.collision.height / 100f + (float)(System.Random.Shared.NextDouble() - 0.5);
                leftCharge.Explode();

                leftCharge.transform.localPosition = new Vector3(ship.collision.radius, 0, leftCharge.transform.localPosition.z);// i * ship.collision.height / 4);
                leftCharge.torpedoEffectScale = ship.collision.height / 100f + (float)(System.Random.Shared.NextDouble() - 0.5);
                leftCharge.Explode();

                leftCharge.RemoveSelf();

                while (!IsBattleEnd(ship) && BattleManager.Instance.CurrentBattle.Timer.leftTime > lastCharge - 2)
                {
                    yield return new WaitForSeconds(0.25f);
                }

                if (IsBattleEnd(ship)) goto EXIT;
            }

            List<Part> guns = new();

            foreach (var part in ship.mainGuns)
            {
                guns.Add(part);
            }

            foreach (var part in ship.parts)
            {
                if (!part.data.isGun || ship.mainGuns.Contains(part)) continue;

                guns.Add(part);
            }

            float lastFlash = 0;
            float flashDurration = 0;

            foreach (var gun in guns)
            {
                lastFlash = BattleManager.Instance.CurrentBattle.Timer.leftTime;
                flashDurration = (float)System.Random.Shared.NextDouble() * 2.5f + 2.5f;

                ship.StartFire(ship.GetSectionFromPositions(gun.transform.position), gun.transform.position);

                yield return new WaitForEndOfFrame();
                yield return new WaitForEndOfFrame();

                ship.AddSound("flash_fire", Vector3.zero, null, false);
                Effect.FlashFire(gun, 0);

                while (!IsBattleEnd(ship) && BattleManager.Instance.CurrentBattle.Timer.leftTime > lastFlash - flashDurration)
                {
                    yield return new WaitForSeconds(0.25f);
                }

                if (IsBattleEnd(ship)) goto EXIT;
            }

        EXIT:
            Melon<TweaksAndFixes>.Logger.Msg($"Exiting effect");

            yield break;
        }

        // ########## Fixes by Crux10086 ########## //

        // Fix for broken deck hits

        // public static float percentDeck = 0.1f;

        [HarmonyPrefix]
        [HarmonyPatch(nameof(Ship.GetSectionFromPositions))]
        internal static void Fix(Ship __instance, ref Vector3 tempPos)
        {
            if (Patch_Shell.updating == null)
            {
                return;
            }

            Vector3 startPos = Patch_Shell.shellTargetData[Patch_Shell.updating];
            Vector3 endPos = Patch_Shell.updating.transform.position;

            float distance = ModUtils.distance(startPos, endPos);

            // Should be impossible, but you never know
            if (distance <= 0)
            {
                Melon<TweaksAndFixes>.Logger.Msg($"Error: Invalid shell distance!");
                return;
            }

            float range = Patch_Shell.updating.from.ship.weaponRangesCache.GetValueOrDefault(Patch_Shell.updating.from.data);

            // Should be impossible, but you never know
            if (range <= 0)
            {
                Melon<TweaksAndFixes>.Logger.Msg($"Error: Invalid shell range!");
                return;
            }

            // Melon<TweaksAndFixes>.Logger.Msg($"{startPos} -> {endPos} = {distance} / {range} [AP = {Patch_Shell.updating.from.ship.weaponRangesAPCache.GetValueOrDefault(Patch_Shell.updating.from.data)}, HE = {Patch_Shell.updating.from.ship.weaponRangesHECache.GetValueOrDefault(Patch_Shell.updating.from.data)}");

            float percentDeckModifier = distance / range;

            int mark = Patch_Shell.updating.from.ship.TechGunGrade(Patch_Shell.updating.from.data);

            float min = Config.Param("taf_shell_deck_hit_percent_min", 0f);
            float max = Config.Param("taf_shell_deck_hit_percent_max", 1.2f);

            float deckPercent = ((max - min) * (percentDeckModifier * ((float)mark / (float)Config.MaxGunGrade))) + min;

            // Melon<TweaksAndFixes>.Logger.Msg($"{distance/1000:N2}km / {range/1000:N2}km = {percentDeckModifier * 100:N2}% | {mark} / {Config.MaxGunGrade} | deck width -> {deckPercent * 100:N2}% | {(deckPercent * deckPercent) / 3 * 100} deck hit chance.");

            // predictedDeckHits += (deckPercent * deckPercent) / 3;

            Bounds hullSize = __instance.hullSize;
            float y = hullSize.min.y * deckPercent;
            tempPos.y -= y;
        }

        // public static int total = 0;
        // public static int totalDeckHits = 0;
        // public static int totalBeltHits = 0;
        // public static int totalOtherHits = 0;
        // public static float predictedDeckHits = 0;
        // 
        // [HarmonyPrefix]
        // [HarmonyPatch(nameof(Ship.Report))]
        // internal static void Prefix_Report(Ship __instance, Ui.RImportance importance, string text, string tooltip, Ship otherShip)
        // {
        //     if (text.ToLower().Contains("deck"))
        //     {
        //         ++totalDeckHits;
        //         total++;
        //     }
        //     else if (text.ToLower().Contains("belt"))
        //     {
        //         ++totalBeltHits;
        //         total++;
        //     }
        //     else if (text.ToLower().Contains("hit"))
        //     {
        //         ++totalOtherHits;
        //         total++;
        //     }
        // }





        public static HashSet<string> rangeNameSet = new HashSet<string>();

        [HarmonyPostfix]
        [HarmonyPatch(nameof(Ship.Update))]
        internal static void Postfix_Update(Ship __instance)
        {
            // Melon<TweaksAndFixes>.Logger.Msg($"{__instance.Name(false, false, false, false, true)} : Show deck props {UiM.TAF_Settings.settings.deckPropCoverage != float.MaxValue}");

            if (__instance.floatUpsCont != null && __instance.floatUpsCont.transform.localPosition.y < 100)
            {
                __instance.floatUpsCont.transform.localPosition = new Vector3(0, 120, 0);
            }

            // [SMS.2.WAKE] 战斗中的船只可能不经过 RefreshHull，这里补注册（幂等）
            try { ShipWakeHint.Register(__instance); } catch { }

            if (__instance.uiRangesCont == null) return;
            if (__instance.uiRangesCont.active == false) return;

            // if (Input.GetKeyDown(KeyCode.J))
            // {
            //     Melon<TweaksAndFixes>.Logger.Msg($"{__instance.Name(false, false, false, false, true)}");
            //     // Melon<TweaksAndFixes>.Logger.Msg($"{__instance.Name(false, false, false, false, true)} - {range.name}:");
            //     // Melon<TweaksAndFixes>.Logger.Msg($"  {Input.mousePosition} : {cam.WorldToScreenPoint(rect.transform.position)} : {worldMin} : {worldMax}");
            //     // Melon<TweaksAndFixes>.Logger.Msg($"  {Input.mousePosition.x > worldMin.x} {Input.mousePosition.x < worldMax.x} {Input.mousePosition.y > worldMin.y} {Input.mousePosition.y < worldMax.y}");
            // }

            rangeNameSet.Clear();

            foreach (GameObject range in __instance.uiRangesCont.GetChildren())
            {
                if (range.active == false) continue;
                // ShipsActive/CA Alkmaar (Alkmaar) [netherlands]/ShipIngameUi(Clone)/GunRanges/GunRange:Torp/RangeCanvas/RangeLayout/RangeText

                GameObject rangeCanvas = range.GetChild("RangeCanvas");

                if (!rangeCanvas.GetComponent<Canvas>().enabled) continue;

                RectTransform rect = rangeCanvas.GetComponent<RectTransform>();
                GameObject txtObj = ModUtils.GetChildAtPath("RangeCanvas/RangeLayout/RangeText", range);
                Text txt = txtObj.GetComponent<Text>(); // Text is monospace, each char is 30 wide and 100 tall.

                if (rangeNameSet.Contains(txt.text))
                {
                    Melon<TweaksAndFixes>.Logger.Msg($"Disabling duplicate range: {txt.text}");
                    range.SetActive(false);
                    continue;
                }

                rangeNameSet.Add(txt.text);

                txtObj.TryDestroyComponent<OnEnter>();
                txtObj.TryDestroyComponent<OnLeave>();

                RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, Input.mousePosition, G.cam.cameraComp, out Vector2 outpoint);
                outpoint.x += txt.text.Length * 30 / 2;

                if (outpoint.x > 0 && -outpoint.y > 0 && outpoint.x < txt.text.Length * 30 && -outpoint.y < 100)
                {
                    // Melon<TweaksAndFixes>.Logger.Msg($"{range.name}: INSIDE");
                    if (rect.gameObject.active) rect.gameObject.SetActive(false);
                }
                else
                {
                    if (!rect.gameObject.active) rect.gameObject.SetActive(true);
                }

                // if (Input.GetKeyDown(KeyCode.J))
                // {
                //     Melon<TweaksAndFixes>.Logger.Msg($"  {range.name}: {outpoint}");
                // }
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(nameof(Ship.FindEnemyForBowSternGuns))]
        internal static void Postfix_FindEnemyForBowSternGuns(Ship __instance, ref Ship __result, PartData gunGroup, Dictionary<PartData, Aim> tempEnemies, List<Part> gunsOnNeededSide, ref Aim aim, ref Ship enemy)
        {
            if (__instance.torpedoMode == ShootMode.Aggressive) return;

            if (__instance.torpedoesAll.Count == 0) return;

            PartData torpData = __instance.torpedoesAll[0].data;

            if (gunGroup.name != torpData.name) return;

            if (__result == null) return;

            if (!__instance.weaponRangesCache.ContainsKey(torpData)) return;

            float rangeToEnemy = __instance.transform.position.GetDistanceXZ(__result.transform.position);

            // Melon<TweaksAndFixes>.Logger.Msg($"  {__instance.Name(false, false)} : {rangeToEnemy} > {__instance.weaponRangesCache[torpData]}");

            if (rangeToEnemy > __instance.weaponRangesCache[torpData] * Config.Param("taf_torpedo_max_launch_range_percent", 0.9f))
            {
                // Melon<TweaksAndFixes>.Logger.Msg($"{__instance.Name(false, false)} : {rangeToEnemy} > {__instance.weaponRangesCache[torpData] * Config.Param("taf_torpedo_max_launch_range_percent", 0.8f)} : {__result.Name(false, false)} : {enemy?.Name(false, false) ?? "NULL"}");

                aim.target = null;
                __result = null;
                enemy = null;
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(nameof(Ship.FindEnemyForOtherGuns))]
        internal static void Postfix_FindEnemyForOtherGuns(Ship __instance, ref Ship __result, PartData gunGroup, Dictionary<PartData, Aim> tempEnemies, List<Part> gunsOnNeededSide, ref Aim aim, ref Ship enemy)
        {
            if (__instance.torpedoMode == ShootMode.Aggressive) return;

            if (__instance.torpedoesAll.Count == 0) return;

            PartData torpData = __instance.torpedoesAll[0].data;

            if (gunGroup.name != torpData.name) return;

            if (__result == null) return;

            if (!__instance.weaponRangesCache.ContainsKey(torpData)) return;

            float rangeToEnemy = __instance.transform.position.GetDistanceXZ(__result.transform.position);

            // Melon<TweaksAndFixes>.Logger.Msg($"  {__instance.Name(false, false)} : {rangeToEnemy} > {__instance.weaponRangesCache[torpData]}");

            if (rangeToEnemy > __instance.weaponRangesCache[torpData] * Config.Param("taf_torpedo_max_launch_range_percent", 0.9f))
            {
                // Melon<TweaksAndFixes>.Logger.Msg($"{__instance.Name(false, false)} : {rangeToEnemy} > {__instance.weaponRangesCache[torpData] * Config.Param("taf_torpedo_max_launch_range_percent", 0.8f)} : {__result.Name(false, false)} : {enemy?.Name(false, false) ?? "NULL"}");

                aim.target = null;
                __result = null;
                enemy = null;
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(nameof(Ship.HitChanceTorpedoEst))]
        internal static void Prefix_HitChanceTorpedoEst(Ship ally, Ship enemy, ref float rangeToEnemy, float torpedoRange, ref float __result)
        {
            rangeToEnemy = ally.transform.position.GetDistanceXZ(enemy.transform.position);
        }

        // ########## MODIFIED SHIP GENERATION ########## //

        internal static int _GenerateShipState = -1;
        internal static bool _IsLoading = false;
        internal static Ship _ShipForLoading = null;
        internal static Ship.Store _StoreForLoading = null;
        internal static Ship._GenerateRandomShip_d__573 _GenerateRandomShipRoutine = null;
        internal static Ship._AddRandomPartsNew_d__591 _AddRandomPartsRoutine = null;
        internal static RandPart _LastRandPart = null;
        internal static bool _LastRPIsGun = false;
        internal static ShipM.BatteryType _LastBattery = ShipM.BatteryType.main;
        internal static ShipM.GenGunInfo _GenGunInfo = new ShipM.GenGunInfo();

        internal static bool UpdateRPGunCacheOrSkip(RandPart rp)
        {
            if (rp != _LastRandPart)
            {
                _LastRandPart = rp;
                _LastRPIsGun = rp.type == "gun";
                if (_LastRPIsGun)
                    _LastBattery = rp.condition.Contains("main_cal") ? ShipM.BatteryType.main : (rp.condition.Contains("sec_cal") ? ShipM.BatteryType.sec : ShipM.BatteryType.ter);
            }
            return !_LastRPIsGun;
        }

        [HarmonyPostfix]
        [HarmonyPatch(nameof(Ship.ToStore))]
        internal static void Postfix_ToStore(Ship __instance, ref Ship.Store __result)
        {
            __instance.TAFData().ToStore(__result, false);
        }

        // We can't patch FromStore because it has a nullable argument.
        // It has multiple early-outs. We're skipping:
        // * shipType can't be found in GameData
        // * tech not in GameData.
        // * part hull not in GameData
        // * can't find design
        // But we will patch the regular case
        internal static void Postfix_FromStore(Ship __instance)
        {
            if (__instance != null && _StoreForLoading != null)
                __instance.TAFData().ToStore(_StoreForLoading, true);

            _IsLoading = false;
            _ShipForLoading = null;
            _StoreForLoading = null;
        }

        // Successful FromStore
        [HarmonyPostfix]
        [HarmonyPatch(nameof(Ship.Init))]
        internal static void Postfix_Init(Ship __instance)
        {
            if (_IsLoading)
                Postfix_FromStore(__instance);
        }

        [HarmonyPostfix]
        [HarmonyPatch(nameof(Ship.TechGunGrade))]
        internal static void Postfix_TechGunGrade(Ship __instance, PartData gun, bool requireValid, ref int __result)
        {
            // Let's hope the gun grade cache is only used in this method!
            // If it's used elsewhere, we won't catch that case. The reason
            // is that we can't patch the cache if we want to use it at all,
            // because we need to preserve the _real_ grade but we also
            // don't want to cache-bust every time.

            __result = __instance.TAFData().GunGrade(gun, __result);
        }

        [HarmonyPostfix]
        [HarmonyPatch(nameof(Ship.TechTorpedoGrade))]
        internal static void Postfix_TechTorpedoGrade(Ship __instance, PartData torpedo, bool requireValid, ref int __result)
        {
            // Let's hope the torp grade cache is only used in this method!
            // If it's used elsewhere, we won't catch that case. The reason
            // is that we can't patch the cache if we want to use it at all,
            // because we need to preserve the _real_ grade but we also
            // don't want to cache-bust every time.

            __result = __instance.TAFData().TorpedoGrade(__result);
        }

        [HarmonyPatch(nameof(Ship.AddedAdditionalTonnageUsage))]
        [HarmonyPrefix]
        internal static bool Prefix_AddedAdditionalTonnageUsage(Ship __instance)
        {
            ShipM.AddedAdditionalTonnageUsage(__instance);
            return false;
        }

        [HarmonyPatch(nameof(Ship.ReduceWeightByReducingCharacteristics))]
        [HarmonyPrefix]
        internal static bool Prefix_ReduceWeightByReducingCharacteristics(Ship __instance, Il2CppSystem.Random rnd, float tryN, float triesTotal, float randArmorRatio = 0, float speedLimit = 0)
        {
            ShipM.ReduceWeightByReducingCharacteristics(__instance, rnd, tryN, triesTotal, randArmorRatio, speedLimit);
            return false;
        }

        [HarmonyPatch(nameof(Ship.GenerateArmor))]
        [HarmonyPrefix]
        internal static bool Prefix_GenerateArmor(float armorMaximal, Ship shipHint, ref Il2CppSystem.Collections.Generic.Dictionary<Ship.A, float> __result)
        {
            __result = ShipM.GenerateArmorNew(armorMaximal, shipHint);
            return false;
        }

        internal static bool _IsInChangeHullWithHuman = false;
        // Work around difficulty in patching AdjustHullStats
        [HarmonyPatch(nameof(Ship.ChangeHull))]
        [HarmonyPrefix]
        internal static void Prefix_ChangeHull(Ship __instance, ref bool byHuman)
        {
            if (byHuman)
            {
                byHuman = false;
                _IsInChangeHullWithHuman = true;
            }
        }

        [HarmonyPatch(nameof(Ship.ChangeHull))]
        [HarmonyPostfix]
        internal static void Postfix_ChangeHull(Ship __instance)
        {
            // Patch_Ui.NeedsConstructionListsClear = true;
            // Melon<TweaksAndFixes>.Logger.Msg($"Changed: {LastCreatedShip?.Name(false, false)} to {__instance.Name(false, false)}");
            // Melon<TweaksAndFixes>.Logger.Msg($"Changed: {LastCreatedShip?.id} to {__instance.id}");
            // LastCreatedShip = __instance;
            _IsInChangeHullWithHuman = false;

            // LastClonedShipWeight

            Patch_Ui.UpdateActiveShip = true;

            if (Patch_GameManager._IsRefreshSharedDesign)
            {
                // Melon<TweaksAndFixes>.Logger.Msg($"Change Hull in Refresh Shared Design");

                if (!G.GameData.sharedDesignsPerNation.ContainsKey(__instance.player.data.name))
                {
                    // Melon<TweaksAndFixes>.Logger.Warning($"Failed to find nation {__instance.player.data.name} for Shared Design {__instance.Name(false, false)}.");
                }
                else
                {
                    foreach (var ship in G.GameData.sharedDesignsPerNation[__instance.player.data.name])
                    {
                        if (ship.Item1.id != __instance.id) continue;

                        // Melon<TweaksAndFixes>.Logger.Msg($"  Stored: {ship.Item1.vesselName}: {ship.Item1.tonnage}");
                        __instance.tonnage = ship.Item1.tonnage;
                    }
                }
            }
            else if (LastClonedShipWeight != 0)
            {
                // Melon<TweaksAndFixes>.Logger.Msg($"Change Hull outside Refresh Shared Design: {LastClonedShipWeight} : {__instance.tonnage}");
                __instance.tonnage = LastClonedShipWeight;
                LastClonedShipWeight = 0;
            }

            // if (G.ui.isConstructorRefitMode)
            // {
            //     Player player = ExtraGameData.MainPlayer();
            // 
            //     if (player == null)
            //     {
            //         Melon<TweaksAndFixes>.Logger.Error("Failed to get main player in Refit Mode. Build mode will be broken.");
            //         return;
            //     }
            // 
            //     if (player.designs.Count() < 2)
            //     {
            //         Melon<TweaksAndFixes>.Logger.Error("Design count less than 2. Failed to find refit ship reference. Build mode will be broken.");
            //         return;
            //     }
            // 
            //     LastCreatedShip = new Il2CppSystem.Collections.Generic.List<Ship>(player.designs)[^2];
            // }
        }

        [HarmonyPatch(nameof(Ship.SetDraught))]
        [HarmonyPostfix]
        internal static void Postfix_SetDraught(Ship __instance)
        {
            // Do what ChangeHull would do in the byHuman block
            if (_IsInChangeHullWithHuman)
            {
                float tonnageLimit = Mathf.Min(__instance.tonnage, __instance.TonnageMax());
                float tonnageToSet = Mathf.Lerp(__instance.TonnageMin(), tonnageLimit, UnityEngine.Random.Range(0f, 1f));
                __instance.SetTonnage(tonnageToSet);
                var designYear = __instance.GetYear(__instance);
                var origTargetWeightRatio = 1f - Util.Remap(designYear, 1890f, 1940f, 0.63f, 0.52f, true);
                var stopFunc = new System.Func<bool>(() =>
                {
                    return (__instance.Weight() / __instance.Tonnage()) <= (1f - Util.Remap(designYear, 1890f, 1940f, 0.63f, 0.52f, true));
                });
                ShipM.AdjustHullStats(__instance, -1, origTargetWeightRatio, stopFunc, true, true, true, true, true, null, -1f, -1f);
            }
        }

        [HarmonyPatch(nameof(Ship.ChangeRefitShipTech))]
        [HarmonyPostfix]
        internal static void Postfix_ChangeRefitShipTech(Ship __instance, Ship newDesign)
        {
            __instance.TAFData().OnRefit(newDesign);
        }

        private static List<PartData> _TempDatas = new List<PartData>();

        // Hook this just so we can run this after a random gun is added. Bleh.
        // We need to do this because, if we place a part of a _new_ caliber,
        // we need to check if we are now at the limit for caliber counts for
        // that battery, and if so remove all other-caliber datas from being
        // chosen.
        [HarmonyPatch(nameof(Ship.AddShipTurretArmor), new Type[] { typeof(Part) })]
        [HarmonyPostfix]
        internal static void Postfix_AddShipTurretArmor(Part part)
        {
            if (_AddRandomPartsRoutine == null || !_GenGunInfo.isLimited || UpdateRPGunCacheOrSkip(_AddRandomPartsRoutine.__8__1.randPart))
                return;

            // Register reports true iff we're at the count limit
            if (_GenGunInfo.RegisterCaliber(_LastBattery, part.data))
            {
                // Ideally we'd do RemoveAll, but we can't use a managed predicate
                // on the native list. We could reimplement RemoveAll, but I don't trust
                // calling RuntimeHelpers across the boundary. This should still be faster
                // than the O(n^2) of doing RemoveAts, because we don't have to copy
                // back to compress the array each time.
                for (int i = _AddRandomPartsRoutine._chooseFromParts_5__11.Count; i-- > 0;)
                    if (_GenGunInfo.CaliberOK(_LastBattery, _AddRandomPartsRoutine._chooseFromParts_5__11[i]))
                        _TempDatas.Add(_AddRandomPartsRoutine._chooseFromParts_5__11[i]);

                _AddRandomPartsRoutine._chooseFromParts_5__11.Clear();
                for (int i = _TempDatas.Count; i-- > 0;)
                    _AddRandomPartsRoutine._chooseFromParts_5__11.Add(_TempDatas[i]);

                _TempDatas.Clear();
            }
        }

        public static void UpdateDeckClutter(Ship ship)
        {
            // guard added: 冷启动/场景切换时 RefreshHull 可能处于半构造状态
            try { if (ship == null || ship.hull == null) return; } catch { return; }
            // ★ NRE 真因：部分船（无甲板装饰/早期加载阶段）**没有 "DeckProps" 子物体**，
            //   GetChild 返回 null 后直接 SetActive → NullReferenceException，
            //   发生在 RefreshHull 的 postfix 里，导致每船每次刷新都报一次。
            GameObject deckProps = null;
            try { deckProps = ship.hull.gameObject.GetChild("DeckProps"); } catch { }
            if (deckProps == null)
            {
                global::TAFHotfixBatch1.HotfixLog.Dbg("[SMS.2.PROP] no DeckProps child on '" + (ship != null ? ship.vesselName : "?") + "'");
                return;
            }

            if (UiM.TAF_Settings.settings.deckPropCoverage == 0)
            {
                deckProps.SetActive(false);
            }
            else
            {
                deckProps.SetActive(true);

                var props = deckProps.GetChildren();

                // ★ [SMS.2.PROP] 健壮化：3.22 的新布点算法生成数量是**任意**的（不再是成对 + 镜像），
                //   旧实现按 `i += 2` 并访问 `props[i + 1]`，遇到奇数个会抛
                //   ArgumentOutOfRangeException（2026-10-26 实测，异常会让整个装饰流程中断）。
                //   这里改为逐项判定，但保留"每 2 个共享同一状态"的原有语义。
                int bucketSize = UiM.TAF_Settings.settings.deckPropCoverage / 25;   // 0..4
                for (int i = 0; i < props.Count; i++)
                {
                    bool on = ((i / 2) % 4) < bucketSize;
                    props[i].SetActive(on);
                }
            }
        }

        // SizeRatio
        // chosen.
        [HarmonyPatch(nameof(Ship.RefreshHull))]
        [HarmonyPostfix]
        internal static void Postfix_RefreshHull(Ship __instance)
        {
            UpdateDeckClutter(__instance);
        }

        // [HarmonyPatch(typeof(Ship.__c__DisplayClass870_1))]
        // [HarmonyPatch("_RefreshHull_b__21")]
        // [HarmonyPrefix]
        // internal static bool Prefix__RefreshHull_b__21(__c__DisplayClass870_1 __instance, DeckProp p, ref bool __result)
        // {
        //     __result = UiM.TAF_Settings.settings.deckPropCoverage == float.MaxValue ||
        //         Vector3.SqrMagnitude(p.transform.position - __instance.pos1) <= UiM.TAF_Settings.settings.deckPropCoverage * UiM.TAF_Settings.settings.deckPropCoverage;
        // 
        //     return false;
        // }
    }

    // We can't target ref arguments in an attribute, so
    // we have to make this separate class to patch with a
    // TargetMethod call.
    [HarmonyPatch(typeof(Ship))]
    internal class Patch_Ship_IsComponentAvailable
    {
        internal static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(Ship), nameof(Ship.IsComponentAvailable), new Type[] { typeof(ComponentData), typeof(string).MakeByRefType() });
        }

        internal static bool Prefix(Ship __instance, ComponentData component, ref string reason, ref bool __result, out float __state)
        {
            __state = component.weight;

            var weight = ComponentDataM.GetWeight(component, __instance.shipType);

            //if (weight == component.weight)
            //    return true;
            //Melon<TweaksAndFixes>.Logger.Msg($"For component {component.name} and shipType {__instance.shipType.name}, overriding weight to {weight:F0}");

            if (weight <= 0f)
            {
                __result = false;
                reason = "Ship Type";
                return false;
            }
            component.weight = weight;
            return true;
        }

        internal static void Postfix(ComponentData component, float __state)
        {
            component.weight = __state;
        }
    }

    [HarmonyPatch(typeof(Ship.__c))]
    internal class Patch_Ship_c
    {
        // This method is called by the component selection process to set up
        // the weighted-random dictionary. So we need to patch it too. But
        // it doesn't know the ship in question. So we have to patch the calling
        // method to pass that on.
        // ALSO, it's code that's shared with IsComponentAvailable. But we
        // patch that by changing weight before and after the method. So there's
        // no need to do so here. So we abort if we're not in GenerateRandomShip.
        [HarmonyPatch(nameof(Ship.__c._GetComponentsToInstall_b__574_3))]
        [HarmonyPrefix]
        internal static bool Prefix_GetComponentsToInstall_b__565_3(ComponentData c, ref float __result)
        {
            if (Patch_Ship._GenerateRandomShipRoutine == null)
                return true;

            __result = ComponentDataM.GetWeight(c, Patch_Ship._GenerateRandomShipRoutine.__4__this.shipType);
            //if(__result != c.weight)
            //    Melon<TweaksAndFixes>.Logger.Msg($"Gen: For component {c.name} and shipType {Patch_Ship._GenerateRandomShipRoutine.__4__this.shipType.name}, overriding weight to {__result:F0}");
            return false;
        }
    }

    // This runs when selecting all possible parts for a RP
    // but once an RP is having parts placed, we also need to
    // knock options out whenever a caliber is picked. See
    // AddTurretArmor above.
    [HarmonyPatch(typeof(Ship.__c__DisplayClass590_0))]
    internal class Patch_Ship_c_GetParts
    {
        [HarmonyPatch(nameof(Ship.__c__DisplayClass590_0._GetParts_b__0))]
        [HarmonyPrefix]
        internal static bool Prefix_b0(Ship.__c__DisplayClass590_0 __instance, PartData a, ref bool __result)
        {
            // Super annoying we can't prefix GetParts itself to do the RP caching
            if (!Patch_Ship._GenGunInfo.isLimited || Patch_Ship.UpdateRPGunCacheOrSkip(__instance.randPart))
                return true;

            int partCal = (int)((a.caliber + 1f) * (1f / 25.4f));
            if (!Patch_Ship._GenGunInfo.CaliberOK(Patch_Ship._LastBattery, partCal))
            {
                __result = false;
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(Ship._GenerateRandomShip_d__573))]
    internal class Patch_ShipGenRandom
    {
        //static string lastName = string.Empty;
        //static int shipCount = 0;

        internal struct GRSData
        {
            public int state;
            public int tryNum;
            public float speed;
            public float beam;
            public float beamMin;
            public float beamMax;
            public float draught;
            public float draughtMin;
            public float draughtMax;
        }

        public static GRSData lastState = new();

        public static bool shipGenActive = false;

        public static float Reratio(float v, float a1, float a2, float b1, float b2)
        {
            // Mapping onto a single point range always gives the same answer
            //   Also prevents a devide-by-zero edge case
            if (b1 == b2) return b1;
            if (a1 == a2) return a1;

            return b1 + (b2 - b1) * ((v - a1) / (a2 - a1));
        }

        private static void ClampShipStats(Ship ship)
        {
            bool modified = false;

            var sd = ship.hull.data;
            var st = ship.shipType;

            float speed_min = st.speedMin;
            float speed_max = st.speedMax;
            float beam_min = sd.beamMin;
            float beam_max = sd.beamMax;
            float draught_min = sd.draughtMin;
            float draught_max = sd.draughtMax;

            if (st.paramx.ContainsKey("shipgen_clamp"))
            {
                modified = true;

                foreach (var stat in st.paramx["shipgen_clamp"])
                {
                    var split = stat.Split(':');

                    if (split.Length != 2)
                    {
                        Melon<TweaksAndFixes>.Logger.Error($"Invalid `shipTypes.csv` `shipgen_clamp` param: `{stat}` for ID `{st.name}`. Must be formatted `shipgen_clamp(stat:number; stat:number; ...)`.");
                        continue;
                    }

                    string tag = split[0];

                    if (!float.TryParse(split[1], System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out float val))
                    {
                        Melon<TweaksAndFixes>.Logger.Error($"Invalid `shipTypes.csv` `shipgen_clamp` param: `{stat}` for ID `{st.name}`. Must be valid number.");
                        continue;
                    }

                    switch (tag)
                    {
                        case "speed_min": speed_min = Math.Clamp(val, st.speedMin, st.speedMax); break;
                        case "speed_max": speed_max = Math.Clamp(val, st.speedMin, st.speedMax); break;
                        case "beam_min": beam_min = Math.Clamp(val, sd.beamMin, sd.beamMax); break;
                        case "beam_max": beam_max = Math.Clamp(val, sd.beamMin, sd.beamMax); break;
                        case "draught_min": draught_min = Math.Clamp(val, sd.draughtMin, sd.draughtMax); break;
                        case "draught_max": draught_max = Math.Clamp(val, sd.draughtMin, sd.draughtMax); break;
                        default:
                            Melon<TweaksAndFixes>.Logger.Error($"Invalid `shipTypes.csv` `shipgen_clamp` param: `{stat}` for ID `{st.name}`. Unsuported stat. Can only be [speed_min, speed_max, beam_min, beam_max, draught_min, draught_max]");
                            break;
                    }
                }
            }

            if (sd.paramx.ContainsKey("shipgen_clamp"))
            {
                modified = true;

                foreach (var stat in sd.paramx["shipgen_clamp"])
                {
                    var split = stat.Split(':');

                    if (split.Length != 2)
                    {
                        Melon<TweaksAndFixes>.Logger.Error($"Invalid `parts.csv` `shipgen_clamp` param: `{stat}` for ID `{sd.name}`. Must be formatted `shipgen_clamp(stat:number; stat:number; ...)`.");
                        continue;
                    }

                    string tag = split[0];

                    if (!float.TryParse(split[1], System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out float val))
                    {
                        Melon<TweaksAndFixes>.Logger.Error($"Invalid `parts.csv` `shipgen_clamp` param: `{stat}` for ID `{sd.name}`. Must be valid number.");
                        continue;
                    }

                    switch (tag)
                    {
                        case "speed_min":   speed_min   = Math.Clamp(val, st.speedMin, st.speedMax); break;
                        case "speed_max":   speed_max   = Math.Clamp(val, st.speedMin, st.speedMax); break;
                        case "beam_min":    beam_min    = Math.Clamp(val, sd.beamMin, sd.beamMax); break;
                        case "beam_max":    beam_max    = Math.Clamp(val, sd.beamMin, sd.beamMax); break;
                        case "draught_min": draught_min = Math.Clamp(val, sd.draughtMin, sd.draughtMax); break;
                        case "draught_max": draught_max = Math.Clamp(val, sd.draughtMin, sd.draughtMax); break;
                        default:
                            Melon<TweaksAndFixes>.Logger.Error($"Invalid `parts.csv` `shipgen_clamp` param: `{stat}` for ID `{sd.name}`. Unsuported stat. Can only be [speed_min, speed_max, beam_min, beam_max, draught_min, draught_max]");
                            break;
                    }
                }
            }

            if (!modified) return;

            speed_min = speed_min > speed_max ? speed_max : speed_min;
            beam_min = beam_min > beam_max ? beam_max : beam_min;
            draught_min = draught_min > draught_max ? draught_max : draught_min;

            // Melon<TweaksAndFixes>.Logger.Msg($"Mod stats for ship {ship.Name(false, false)}:");

            // if (lastState.speed != ship.SpeedMax())
            {
                float val =
                    ModUtils.roundToInc(
                        Math.Clamp(ship.speedMax, speed_min * 0.5144444f, speed_max * 0.5144444f),
                        Config.Param("speed_step", 0.1f)
                    );

                // Melon<TweaksAndFixes>.Logger.Msg($"  {lastState.speed} != {ship.SpeedMax()}");
                // Melon<TweaksAndFixes>.Logger.Msg($"  {speed_min} / {st.speedMin} - {speed_max} / {st.speedMax}");
                // if (speed_min != float.MinValue)
                //     Melon<TweaksAndFixes>.Logger.Msg(
                //         $"  speed:     {ship.speedMax * 1.943844f,10} -> {val * 1.943844f}"
                //     );

                ship.SetSpeedMax(val);
                lastState.speed = ship.SpeedMax();
            }
            
            if (lastState.beam != ship.Beam())
            {
                float val =
                    ModUtils.roundToInc(
                        Reratio(ship.beam, sd.beamMin, sd.beamMax, beam_min, beam_max),
                        0.1f
                    );

                // Melon<TweaksAndFixes>.Logger.Msg($"  {lastState.beam} != {ship.Beam()}");
                // Melon<TweaksAndFixes>.Logger.Msg($"  {beam_min} / {sd.beamMin} - {beam_max} / {sd.beamMax}");
                // if (beam_min != float.MinValue)
                //     Melon<TweaksAndFixes>.Logger.Msg($"  beam:      {ship.beam,10} -> {val}");

                ship.SetBeam(val);
                lastState.beam = ship.Beam();
            }

            if (lastState.draught != ship.Draught())
            {
                float val =
                    ModUtils.roundToInc(
                        Reratio(ship.draught, sd.draughtMin, sd.draughtMax, draught_min, draught_max),
                        0.1f
                    );

                // Melon<TweaksAndFixes>.Logger.Msg($"  {lastState.draught} != {ship.Draught()}");
                // Melon<TweaksAndFixes>.Logger.Msg($"  {draught_min} / {sd.draughtMin} - {draught_max} / {sd.draughtMax}");
                // if (draught_min != float.MinValue)
                //     Melon<TweaksAndFixes>.Logger.Msg($"  draught:   {ship.draught,10} -> {val}");

                ship.SetDraught(val);
                lastState.draught = ship.Draught();
            }
        }

        private static bool CanModCompType(Ship ship, string key)
        {
            if (!G.GameData.compTypes.ContainsKey(key))
                return false;

            if (!ship.components.ContainsKey(G.GameData.compTypes[key]))
                return false;

            return true;
        }

        private static bool CanModComp(Ship ship, string key)
        {
            if (!G.GameData.components.ContainsKey(key))
                return false;

            if (!ship.components.ContainsKey(G.GameData.components[key].typex))
                return false;

            return true;
        }

        public static void OptimizeComponents(Ship ship)
        {
            var _this = ship;

            if (CanModCompType(ship, "boilers") && CanModCompType(ship, "engine") && CanModCompType(ship, "fuel"))
            {
                float bestWeight = _this.Weight();
                ComponentData bestEngine = _this.components[G.GameData.compTypes["boilers"]];
                ComponentData bestBoiler = _this.components[G.GameData.compTypes["engine"]];
                ComponentData bestFuel = _this.components[G.GameData.compTypes["fuel"]];

                // Melon<TweaksAndFixes>.Logger.Msg($"  Start: {bestEngine.name} x {bestBoiler.name} x {bestFuel.name}: {_this.weight} t. / {_this.Tonnage()}");

                foreach (var engine in G.GameData.technologies)
                {
                    if (engine.Value.componentx == null
                        || engine.Value.componentx.type != "engine"
                        || !_this.IsComponentAvailable(engine.Value.componentx)) continue;

                    foreach (var boiler in G.GameData.technologies)
                    {
                        if (boiler.Value.componentx == null
                            || boiler.Value.componentx.type != "boilers"
                            || !_this.IsComponentAvailable(boiler.Value.componentx)) continue;

                        foreach (var fuel in G.GameData.technologies)
                        {
                            if (fuel.Value.componentx == null
                                || fuel.Value.componentx.type != "fuel"
                                || !_this.IsComponentAvailable(fuel.Value.componentx)) continue;

                            // Melon<TweaksAndFixes>.Logger.Msg($"    {engine.Key} x {boiler.Key} x {fuel.Key}");

                            _this.InstallComponent(engine.Value.componentx);
                            _this.InstallComponent(boiler.Value.componentx);
                            _this.InstallComponent(fuel.Value.componentx);

                            if (bestWeight > _this.Weight())
                            {
                                bestWeight = _this.Weight();
                                bestEngine = engine.Value.componentx;
                                bestBoiler = boiler.Value.componentx;
                                bestFuel = fuel.Value.componentx;
                            }
                        }
                    }
                }

                _this.InstallComponent(bestEngine);
                _this.InstallComponent(bestBoiler);
                _this.InstallComponent(bestFuel);
                // Melon<TweaksAndFixes>.Logger.Msg($"  Best Combo: {bestEngine.name} x {bestBoiler.name} x {bestFuel.name}: {_this.weight} t. / {_this.Tonnage()}");
            }

            if (CanModCompType(ship, "torpedo_prop") &&
                CanModComp(ship, "torpedo_prop_fast") &&
                CanModComp(ship, "torpedo_prop_normal") &&
                _this.components[G.GameData.compTypes["torpedo_prop"]] == G.GameData.components["torpedo_prop_fast"])
            {
                _this.InstallComponent(G.GameData.components["torpedo_prop_normal"]);
            }

            if (CanModCompType(ship, "shell") &&
                CanModComp(ship, "shell_light") &&
                CanModComp(ship, "shell_normal") &&
                _this.components[G.GameData.compTypes["shell"]] == G.GameData.components["shell_light"])
            {
                _this.InstallComponent(G.GameData.components["shell_normal"]);
            }
        }

        public static void OnShipgenStart()
        {
            PrintShipgenStart(Patch_Ship._GenerateRandomShipRoutine, Patch_Ship._GenerateRandomShipRoutine.__4__this);

            // [SMS.1.SHIPGEN] 每次生成前清空"鱼雷尺寸偏好"缓存（Beta6 同款）
            try { global::TweaksAndFixes.Modules.ShipGen.DesiredTorpSize = null; } catch { }

            shipGenActive = true;
        }

        public static void OnShipgenEnd()
        {
            PrintShipgenEnd(Patch_Ship._GenerateRandomShipRoutine);

            // ★ [SMS.1.SHIPGEN] 生成后审计（shipgen_limits >= 1 时启用）
            if (global::TAFHotfixBatch1.HotfixParams.ShipGenLimits >= 1)
            {
                try
                {
                    var routine = Patch_Ship._GenerateRandomShipRoutine;
                    var ship = routine != null ? routine.__4__this : null;
                    if (ship != null)
                    {
                        // ① 先把速度/船宽/吃水夹回 shipgen_clamp 区间（force：生成结束必须夹一次）
                        global::TweaksAndFixes.Modules.ShipGen.ClampShipStats(ship, true);
                        // ② 再做 shipgen_limit 审计（仅日志）
                        global::TweaksAndFixes.Modules.ShipGen.ValidateGenerated(ship);
                    }
                }
                catch (Exception e) { global::TAFHotfixBatch1.HotfixLog.Warn("[SMS.1.SHIPGEN] audit: " + e.Message); }
            }

            shipGenActive = false;
        }

        private static void PrintShipgenIssues(Ship._GenerateRandomShip_d__573 __instance, Ship ship)
        {
            if (Config.Param("taf_debug_shipgen_info", 0) == 0) return;

            if (Config.Param("taf_debug_shipgen_info", 0) == 1)
            {
                Melon<TweaksAndFixes>.Logger.Msg($"Attempt {__instance._tryN_5__5} / {__instance._triesTotal_5__4}");

                return;
            }

            int numMainTurrets = 0;
            int numMainBarrels = 0;

            foreach (var part in ship.parts)
            {
                if (!part.data.isGun) continue;

                if (!ship.IsMainCal(part)) continue;

                numMainTurrets++;
                numMainBarrels += part.data.barrels;
            }

            bool hasMinMainTurrets = ship.hull.data.minMainTurrets == -1 || ship.hull.data.minMainTurrets <= numMainTurrets;
            bool hasMinMainBarrels = ship.hull.data.minMainBarrels == -1 || ship.hull.data.minMainBarrels <= numMainBarrels;

            bool isValidCostReqParts = ship.IsValidCostReqParts(
                out string isValidCostReqPartsReason,
                out Il2CppSystem.Collections.Generic.List<ShipType.ReqInfo> notPassed,
                out Il2CppSystem.Collections.Generic.Dictionary<Part, string> badParts);

            bool isValidCostWeightBarbette = ship.IsValidCostWeightBarbette(
                out string isValidCostWeightBarbetteReason,
                out Il2CppSystem.Collections.Generic.List<Part> errorBarbettePart);

            bool isTonnageAllowedByTech = ship.player.IsTonnageAllowedByTech(ship.Tonnage(), ship.shipType);

            bool isValidWeightOffset = ship.IsValidWeightOffset();

            Melon<TweaksAndFixes>.Logger.Msg($"Attempt {__instance._tryN_5__5} / {__instance._triesTotal_5__4}");

            if (!hasMinMainTurrets && !hasMinMainBarrels)
            {
                Melon<TweaksAndFixes>.Logger.Msg($"  Insufficent main turret or barrel count: (only one needs to be true)");
                Melon<TweaksAndFixes>.Logger.Msg($"    Turret Cnt. = {numMainTurrets} / {ship.hull.data.minMainTurrets}");
                Melon<TweaksAndFixes>.Logger.Msg($"    Barrel Cnt. = {numMainBarrels} / {ship.hull.data.minMainBarrels}");
            }

            if (!isValidCostReqParts)
            {
                if (notPassed.Count > 0)
                {
                    Melon<TweaksAndFixes>.Logger.Msg($"  Unmet Requirements:");
                    foreach (var req in notPassed)
                    {
                        Melon<TweaksAndFixes>.Logger.Msg($"    {req.stat.name,10} : {ship.stats[req.stat].total} ({req.min} ~ {req.max})");
                    }
                }

                if (badParts.Count > 0)
                {
                    Melon<TweaksAndFixes>.Logger.Msg($"  Invalid parts:");
                    foreach (var part in badParts)
                    {
                        Melon<TweaksAndFixes>.Logger.Msg($"    {part.Key.data.name,10} : {part.Value}");
                    }
                }
            }

            if (ship.Weight() > ship.Tonnage())
            {
                Melon<TweaksAndFixes>.Logger.Msg($"  Ship Overweight: {(int)ship.Weight()}t / {(int)ship.Tonnage()}t");
            }

            if (!isValidCostWeightBarbette)
            {
                if (errorBarbettePart.Count > 0)
                {
                    Melon<TweaksAndFixes>.Logger.Msg($"  Empty barbettes:");

                    foreach (var barbette in errorBarbettePart)
                    {
                        Melon<TweaksAndFixes>.Logger.Msg($"    {barbette.data.name}");
                    }
                }
            }

            if (!isTonnageAllowedByTech)
            {
                Melon<TweaksAndFixes>.Logger.Msg($"  Tonnage outside tech range.");
            }

            if (!isValidWeightOffset)
            {
                float inst_x = ship.stats_[G.GameData.stats["instability_x"]].total;
                float inst_z = ship.stats_[G.GameData.stats["instability_z"]].total;

                Melon<TweaksAndFixes>.Logger.Msg($"  Invalid weight offset(s):");
                if (inst_x > 0) Melon<TweaksAndFixes>.Logger.Msg($"    instability_x: {inst_x} > 0");
                if (inst_z > 100) Melon<TweaksAndFixes>.Logger.Msg($"    instability_y: {inst_z} > 100");
            }
        }

        private static void PrintShipgenStart(Ship._GenerateRandomShip_d__573 __instance, Ship ship)
        {
            if (Config.Param("taf_debug_shipgen_info", 0) == 0) return;

            if (Config.Param("taf_debug_shipgen_info", 0) == 1)
            {
                Melon<TweaksAndFixes>.Logger.Msg($"Begin shipgen:");
                Melon<TweaksAndFixes>.Logger.Msg($"  Hull   : {ship.hull.data.nameUi}");
                Melon<TweaksAndFixes>.Logger.Msg($"  Nation : {ship.player.data.nameUi}");
                Melon<TweaksAndFixes>.Logger.Msg($"  Year   : {ship.dateCreated.AsDate().Year}");
                return;
            }

            Melon<TweaksAndFixes>.Logger.Msg($"Begin shipgen:");
            Melon<TweaksAndFixes>.Logger.Msg($"  Hull   : {ship.hull.data.name} ({ship.hull.data.nameUi})");
            Melon<TweaksAndFixes>.Logger.Msg($"  Model  : {ship.hull.data.model}");
            Melon<TweaksAndFixes>.Logger.Msg($"  Nation : {ship.player.data.name} ({ship.player.data.nameUi})");
            Melon<TweaksAndFixes>.Logger.Msg($"  Year   : {ship.dateCreated.AsDate().Year}");
            Melon<TweaksAndFixes>.Logger.Msg($"First pass:");
        }

        private static void PrintShipgenEnd(Ship._GenerateRandomShip_d__573 __instance)
        {
            if (Config.Param("taf_debug_shipgen_info", 0) == 0) return;

            if (Config.Param("taf_debug_shipgen_info", 0) == 1)
            {
                Melon<TweaksAndFixes>.Logger.Msg($"Shipgen halted");

                if (__instance == null)
                {
                    Melon<TweaksAndFixes>.Logger.Msg($"  Result   : Interrupted");
                }
                else
                {
                    Melon<TweaksAndFixes>.Logger.Msg($"  Attempts : {__instance._tryN_5__5} / {__instance._triesTotal_5__4}");
                    Melon<TweaksAndFixes>.Logger.Msg($"  Result   : {(__instance._tryN_5__5 != __instance._triesTotal_5__4 ? "Success" : "Failure")}");
                }

                return;
            }

            Melon<TweaksAndFixes>.Logger.Msg($"Shipgen halted");

            if (__instance == null)
            {
                Melon<TweaksAndFixes>.Logger.Msg($"  Result   : Interrupted");
            }
            else
            {
                Melon<TweaksAndFixes>.Logger.Msg($"  Attempts : {__instance._tryN_5__5} / {__instance._triesTotal_5__4}");
                Melon<TweaksAndFixes>.Logger.Msg($"  Result   : {(__instance._tryN_5__5 != __instance._triesTotal_5__4 ? "Success" : "Failure")}");
            }

        }

        [HarmonyPatch(nameof(Ship._GenerateRandomShip_d__573.MoveNext))]
        [HarmonyPrefix]
        internal static bool Prefix_MoveNext(Ship._GenerateRandomShip_d__573 __instance, out GRSData __state, ref bool __result)
        {
            Patch_Ship._GenerateRandomShipRoutine = __instance;

            // TODO:
            //   Remove whole screen blocker, block part selector & side menus
            //   Add pause/step buttons

            // So we know what state we started in.
            __state = new GRSData();
            __state.state = __instance.__1__state;
            __state.tryNum = __instance._tryN_5__5;
            Patch_Ship._GenerateShipState = __state.state;
            var ship = __instance.__4__this;
            var hd = ship.hull.data;
            __state.speed = ship.SpeedMax();
            __state.beam = ship.Beam();
            __state.beamMin = hd.beamMin;
            __state.beamMax = hd.beamMax;
            __state.draught = ship.Draught();
            __state.draughtMin = hd.draughtMin;
            __state.draughtMax = hd.draughtMax;
            lastState = __state;

            if (__instance.__1__state > 1)
            {
                ClampShipStats(ship);
                OptimizeComponents(ship);
            }

            switch (__state.state)
            {
                case 0:
                    __instance.__4__this.TAFData().ResetAllGrades();
                    break;

                case 6:
                    float weightTargetRand = Util.Range(0.875f, 1.075f, __instance.__8__1.rnd);
                    var designYear = ship.GetYear(ship);
                    float yearRemapToFreeTng = Util.Remap(designYear, 1890f, 1940f, 0.6f, 0.4f, true);
                    float weightTargetRatio = 1f - Mathf.Clamp(weightTargetRand * yearRemapToFreeTng, 0.45f, 0.65f);
                    var stopFunc = new System.Func<bool>(() =>
                    {
                        float targetRand = Util.Range(0.875f, 1.075f, __instance.__8__1.rnd);
                        return (ship.Weight() / ship.Tonnage()) <= (1.0f - Mathf.Clamp(targetRand * yearRemapToFreeTng, 0.45f, 0.65f));
                    });

                    // We can't access the nullable floats on this object
                    // so we cache off their values at the callsite (the
                    // only one that sets them).

                    ShipM.AdjustHullStats(
                      ship,
                      -1,
                      weightTargetRatio,
                      stopFunc,
                      Patch_BattleManager_d115._ShipGenInfo.customSpeed <= 0f,
                      Patch_BattleManager_d115._ShipGenInfo.customArmor <= 0f,
                      true,
                      true,
                      true,
                      __instance.__8__1.rnd,
                      Patch_BattleManager_d115._ShipGenInfo.limitArmor,
                      __instance._savedSpeedMinValue_5__3);

                    // We can't do the frame-wait thing easily, let's just advance straight-away
                    __instance.__1__state = 7;
                    break;

                case 10:
                    // We can't access the nullable floats on this object
                    // so we cache off their values at the callsite (the
                    // only one that sets them).

                    ShipM.AdjustHullStats(
                      ship,
                      1,
                      1f,
                      null,
                      Patch_BattleManager_d115._ShipGenInfo.customSpeed <= 0f,
                      Patch_BattleManager_d115._ShipGenInfo.customArmor <= 0f,
                      true,
                      true,
                      true,
                      __instance.__8__1.rnd,
                      Patch_BattleManager_d115._ShipGenInfo.limitArmor,
                      __instance._savedSpeedMinValue_5__3);

                    ship.UpdateHullStats();

                    foreach (var p in ship.parts)
                        p.UpdateCollidersSize(ship);

                    foreach (var p in ship.parts)
                        Part.GunBarrelLength(p.data, ship, true);

                    // We can't do the frame-wait thing easily, let's just advance straight-away
                    __instance.__1__state = 11;
                    break;
            }
            return true;
        }

        [HarmonyPatch(nameof(Ship._GenerateRandomShip_d__573.MoveNext))]
        [HarmonyPostfix]
        internal static void Postfix_MoveNext(Ship._GenerateRandomShip_d__573 __instance, GRSData __state, ref bool __result)
        {
            var ship = __instance.__4__this;
            var hd = ship.hull.data;
            hd.beamMin = __state.beamMin;
            hd.beamMax = __state.beamMax;
            hd.draughtMin = __state.draughtMin;
            hd.draughtMax = __state.draughtMax;
            // For now, we're going to reset all grades regardless.
            //if (__state == 1 && (!__instance._isRefitMode_5__2 || !__instance.isSimpleRefit))
            //    __instance.__4__this.TAFData().ResetAllGrades();

            if (__instance.__1__state > 1)
            {
                ClampShipStats(ship);
                OptimizeComponents(ship);
            }

            switch (__state.state)
            {
                case 0:
                    if (Config.ShipGenTweaks)
                    {
                        Patch_Ship._GenGunInfo.FillFor(__instance.__4__this);

                        if (!G.ui.isConstructorRefitMode)
                        {
                            //__instance._savedSpeedMinValue_5__3 = Mathf.Max(__instance.__4__this.shipType.speedMin,
                            //    Mathf.Min(__instance.__4__this.hull.data.speedLimiter - 2f, __instance.__4__this.hull.data.speedLimiter * G.GameData.parms.GetValueOrDefault("taf_genship_minspeed_mult")))
                            //    * ShipM.KnotsToMS;

                            // For now, let each method handle it.
                            __instance._savedSpeedMinValue_5__3 = -1f;
                        }
                    }
                    break;

                case 8: // Add parts
                    break;
            }

            Patch_Ship._GenerateRandomShipRoutine = null;
            Patch_Ship._GenerateShipState = -1;

            if (__state.tryNum != __instance._tryN_5__5 && __instance._tryN_5__5 != __instance._triesTotal_5__4)
            {
                PrintShipgenIssues(__instance, ship);
            }
        }
    }


    [HarmonyPatch(typeof(Ship._AddRandomPartsNew_d__591))]
    internal class Patch_Ship_AddRandParts
    {
        [HarmonyPatch(nameof(Ship._AddRandomPartsNew_d__591.MoveNext))]
        [HarmonyPrefix]
        internal static void Prefix_MoveNext(Ship._AddRandomPartsNew_d__591 __instance, out int __state)
        {
            Patch_Ship._AddRandomPartsRoutine = __instance;
            __state = __instance.__1__state;
            //Melon<TweaksAndFixes>.Logger.Msg($"Iteraing AddRandomPartsNew, state {__state}");
            //switch (__state)
            //{
            //    case 2: // pick a part and place it
            //            // The below is a colossal hack to get the game
            //            // to stop adding funnels past a certain point.
            //            // This patch doesn't really work, because components are selected
            //            // AFTER parts. Durr.
            //            if (!Config.ShipGenTweaks)
            //        return;

            //    var _this = __instance.__4__this;
            //    if (!_this.statsValid)
            //        _this.CStats();
            //    var eff = _this.stats.GetValueOrDefault(G.GameData.stats["smoke_exhaust"]);
            //    if (eff == null)
            //        return;
            //    if (eff.total < Config.Param("taf_generate_funnel_maxefficiency", 150f))
            //        return;

            //    foreach (var p in G.GameData.parts.Values)
            //    {
            //        if (p.type == "funnel")
            //            _this.badData.Add(p);
            //    }
            //    break;
            //}
        }

        [HarmonyPatch(nameof(Ship._AddRandomPartsNew_d__591.MoveNext))]
        [HarmonyPostfix]
        internal static void Postfix_MoveNext(Ship._AddRandomPartsNew_d__591 __instance, int __state)
        {
            Patch_Ship._AddRandomPartsRoutine = null;
            //Melon<TweaksAndFixes>.Logger.Msg($"AddRandomPartsNew Iteration for state {__state} ended, new state {__instance.__1__state}");
        }
    }

    [HarmonyPatch(typeof(VesselEntity))]
    internal class Patch_VesselEntityFromStore
    {
        // Harmony can't patch methods that take nullable arguments.
        // So instead of patching Ship.FromStore() we have to patch
        // this, which it calls near the start.
        [HarmonyPrefix]
        [HarmonyPatch(nameof(VesselEntity.FromBaseStore))]
        internal static void Prefix_FromBaseStore(VesselEntity __instance, VesselEntity.VesselEntityStore store, bool isSharedDesign)
        {
            Ship ship = __instance.GetComponent<Ship>();
            if (ship == null)
                return;

            var sStore = store.TryCast<Ship.Store>();
            if (sStore == null)
                return;

            if (sStore.mission != null && LoadSave.Get(sStore.mission, G.GameData.missions) == null)
                return;

            Patch_Ship._IsLoading = true;
            Patch_Ship._ShipForLoading = ship;
            Patch_Ship._StoreForLoading = sStore;
            ship.TAFData().FromStore(sStore);
        }
    }

    // ===================================================================
    // 以下代码原本在 Harmony/HullDataApply.cs，2026-11-05 合并到本文件，
    // 目的是与 duke 的 Harmony/Ship.cs 结构对齐（便于后续 diff / 上游合并）。
    // ===================================================================

    /// <summary>
    /// [SMS.1.WL] Beta4 的 hullData 应用层 —— 移植 3.22 Beta #4 的提交 `4090d68`：
    ///   · `waterline`：给"船体/分段在构造器里的 Y 位置"加偏移
    ///   · `stabilityForward`：写入 `hull.hullInfo.stabilityForward`
    ///
    /// 为什么这段重要：
    ///   Beta #4 的发布说明里写了 **"Fix miscolored fore/aft sections on some hulls in constructor"**，
    ///   而同一提交（`4090d68`）正是引入 `hullData.csv` 的提交 —— 也就是说这句"色差修复"
    ///   极可能就是 **waterline 垂直偏移** 让相邻分段不再共面（共面 ⇒ z-fighting/暗色带）。
    ///
    /// 之前我们只移植了 `HullData`（数据）与 `RefreshHull`（分段），**漏掉了这里的应用代码**，
    /// 所以 `waterline` / `stabilityForward` 一直是"有数据但不生效"。
    ///
    /// 实现说明：
    ///   原版补丁挂在 `Ship.__c__DisplayClass628_0._Update_b__6`（编译器生成的闭包方法），
    ///   类名里的数字在不同版本会变，所以这里改为**运行时按方法名查找**：
    ///   在 `Ship` 的所有嵌套类型里找名为 `<Update>b__6`、返回 `Vector3` 的方法再挂 Postfix。
    /// </summary>
    internal static class HullDataApply
    {
        internal static Ship UpdatingShip = null;

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            if (!HotfixParams.HullDataApply)
            {
                HotfixLog.Info("[SMS.1.WL] disabled");
                return;
            }

            int installed = 0;

            // ① Ship.Update 前缀：记录正在更新的船（供 ② 使用）
            try
            {
                var mUpdate = AccessTools.Method(typeof(Ship), nameof(Ship.Update));
                if (mUpdate != null)
                {
                    harmony.Patch(mUpdate, prefix: new HarmonyMethod(
                        AccessTools.Method(typeof(HullDataApply), nameof(Prefix_Update))));
                    installed++;
                }
            }
            catch (Exception e) { HotfixLog.Warn("[SMS.1.WL] Update: " + e.Message); }

            // ② `<Update>b__6`（Vector3 返回）：加 waterline 偏移
            try
            {
                MethodInfo target = null;
                // Il2Cpp 的嵌套类型（编译器闭包 Ship.<>c__DisplayClassXXX）在代理类型上不可枚举，
                // 因此改为扫描所有已加载类型，找返回 Vector3 的 "<Update>b__6"/"_Update_b__6"。
                try
                {
                    foreach (var t in AccessTools.AllTypes())
                    {
                        if (t == null) continue;
                        string fn = t.FullName ?? t.Name ?? "";
                        if (fn.IndexOf("Ship", System.StringComparison.Ordinal) < 0) continue;
                        if (fn.IndexOf("DisplayClass", System.StringComparison.Ordinal) < 0) continue;

                        foreach (var mi in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                                                        | BindingFlags.Instance | BindingFlags.Static))
                        {
                            if (mi == null) continue;
                            if (mi.ReturnType != typeof(Vector3)) continue;
                            string mn = mi.Name ?? "";
                            if (mn.IndexOf("_Update_b__6", System.StringComparison.Ordinal) >= 0
                                || mn.IndexOf("Update>b__6", System.StringComparison.Ordinal) >= 0)
                            {
                                target = mi;
                                break;
                            }
                        }
                        if (target != null) break;
                    }
                }
                catch (Exception e) { HotfixLog.Warn("[SMS.1.WL] b__6 scan: " + e.Message); }


                if (target != null)
                {
                    harmony.Patch(target, postfix: new HarmonyMethod(
                        AccessTools.Method(typeof(HullDataApply), nameof(Postfix_UpdateB6))));
                    installed++;
                    HotfixLog.Info("[SMS.1.WL] patched " + target.DeclaringType?.Name + "." + target.Name);
                }
                else
                {
                    // ★ 显式 Error：这是"静默失效"的关键点 —— 找不到闭包方法时
                    //   waterline 偏移完全不会生效，而游戏不会给出任何提示。
                    HotfixLog.Error("[SMS.1.WL] <Update>b__6 NOT FOUND -- waterline offsets INACTIVE (update the lookup if the game version changed)");
                }
            }
            catch (Exception e) { HotfixLog.Warn("[SMS.1.WL] b__6: " + e.Message); }

            // ③ CalcInstability 前缀：写入 stabilityForward
            try
            {
                var mCi = AccessTools.Method(typeof(Ship), nameof(Ship.CalcInstability));
                if (mCi != null)
                {
                    harmony.Patch(mCi, prefix: new HarmonyMethod(
                        AccessTools.Method(typeof(HullDataApply), nameof(Prefix_CalcInstability))));
                    installed++;
                }
            }
            catch (Exception e) { HotfixLog.Warn("[SMS.1.WL] CalcInstability: " + e.Message); }

            HotfixLog.Info("[SMS.1.WL] HullDataApply armed on " + installed + "/3 hook(s)");
        }

        internal static void Prefix_Update(Ship __instance)
        {
            try { UpdatingShip = __instance; } catch { }
        }

        internal static void Postfix_UpdateB6(ref Vector3 __result)
        {
            try
            {
                var s = UpdatingShip;
                if (s == null || s.hull == null || s.hull.data == null) return;

                string model = s.hull.data.model;
                if (string.IsNullOrEmpty(model)) return;
                if (!HullData._Data.ContainsKey(model)) return;

                float wl = HullData._Data[model].waterline;
                if (Math.Abs(wl) < 0.0001f) return;

                __result += new Vector3(0f, wl, 0f);
            }
            catch { }
        }

        internal static void Prefix_CalcInstability(Ship __instance)
        {
            try
            {
                if (__instance == null || __instance.hull == null) return;

                if (__instance.hull.hullInfo == null)
                    __instance.hull.hullInfo = new HullInfo();

                string model = __instance.hull.data != null ? __instance.hull.data.model : null;
                if (string.IsNullOrEmpty(model)) return;
                if (!HullData._Data.ContainsKey(model)) return;

                __instance.hull.hullInfo.stabilityForward = HullData._Data[model].stabilityForward;
            }
            catch (Exception e) { HotfixLog.Warn("[SMS.1.WL] CalcInstability prefix: " + e.Message); }
        }
    }

    // ===================================================================
    // 以下代码原本在 Harmony/ShipRefreshHull.cs，2026-11-05 合并到本文件，
    // 目的是与 duke 的 Harmony/Ship.cs 结构对齐（便于后续 diff / 上游合并）。
    // ===================================================================

    /// <summary>
    /// [SMS.2.RH] 完整移植 Beta5 的 `Ship.RefreshHull` 重写（duke 的 31e3af3）。
    ///
    /// 目的：修复"部分船体首/尾段材质错色（miscolored fore/aft sections）"。
    /// 该问题的根因在段（section）重排后未正确重建渲染器与 hull 尺寸，
    /// duke 用整段重写 RefreshHull（含 UpdateSections / InitSectionsCyclical）解决。
    /// 本文件为**逐行移植**（来源：GitHub 提交 31e3af3 的真实源码，非反编译），
    /// 仅改动：命名空间、日志前缀、以及适配我们树的少量 API。
    /// </summary>
    [HarmonyPatch(typeof(Ship))]
    internal class Patch_Ship_RefreshHull
    {
        private static void InitSectionsCyclical(Ship s, int middleCount)
        {
            var h = s.hull;

            HotfixLog.Dbg($"    Default ordering");
            while (middleCount - h.middles.Count != 0)
            {
                if (middleCount > h.middles.Count)
                {
                    var section = Util.CloneGameObject(h.middlesBase[h.middles.Count % h.middlesBase.Count]);
                    h.middles.Add(section);
                    Util.SetActiveX(section, true);
                }
                else if (middleCount < h.middles.Count)
                {
                    var section = h.middles[^1];
                    h.middles.RemoveAt(h.middles.Count - 1);
                    GameObject.DestroyImmediate(section);
                }
            }
        }

        private static void UpdateSections(Ship s, int middleCount)
        {
            var h = s.hull;
            var hModelName = h.data.model;

            HotfixLog.Dbg($"  Sections {hModelName} {h.middles.Count} -> {middleCount}");

            if (!HullData.ModifiedSectionOrder(hModelName))
            {
                if (h.middles.Count != middleCount)
                    InitSectionsCyclical(s, middleCount);
            }
            else
            {
                HotfixLog.Dbg($"    Custom ordering");
                var hullData = HullData._Data[hModelName];

                // Remove all, since order can be anything
                while (h.middles.Count > 0)
                {
                    var section = h.middles[^1];
                    h.middles.RemoveAt(h.middles.Count - 1);
                    GameObject.DestroyImmediate(section);
                }

                // ★ 与 duke 对齐（docs/75）：先查 hullData.csv 的全局段序（按 middleCount 索引），
                //   再查 parts.csv 的按部件段序（sect_<n> → sectioningParsedPart）。
                List<int> order;

                if (middleCount < hullData.sectioningParsed.Count)
                {
                    order = hullData.sectioningParsed[middleCount];
                }
                else
                {
                    string partName = h.data.name;

                    if (!hullData.sectioningParsedPart.ContainsKey(partName) ||
                        !hullData.sectioningParsedPart[partName].ContainsKey(middleCount))
                    {
                        // Melon<TweaksAndFixes>.Logger.Error(
                        //     $"Ship Sectioning: {s.hull.data.name} ({s.hull.model.name.Replace("Clone", "")}) has {data.Count} section lists defined, " +
                        //     $"but #{middleCount} (0-indexed) was requested!"
                        // );
                        InitSectionsCyclical(s, middleCount);
                        return;
                    }

                    order = hullData.sectioningParsedPart[partName][middleCount];
                }

                // Force default ordering for this section count
                if (order.Count == 1 && order[0] == -1)
                {
                    InitSectionsCyclical(s, middleCount);
                    return;
                }

                // Create in order
                foreach (var index in order)
                {
                    if (index >= h.middlesBase.Count)
                    {
                        Melon<TweaksAndFixes>.Logger.Error(
                            $"Ship Sectioning: {s.hull.data.name} ({s.hull.model.name.Replace("Clone", "")}) section order for {middleCount} sections " +
                            $"requested section #{index} (0-indexed), but there are only {h.middlesBase.Count} section models availible for this hull!"
                        );
                        continue;
                    }

                    // HotfixLog.Dbg($"    {index} : {h.middlesBase.Count}");
                    var section = Util.CloneGameObject(h.middlesBase[index]);
                    h.middles.Add(section);
                    Util.SetActiveX(section, true);
                }
            }
        }

        public static Dictionary<string, Stack<GameObject>> PropCache = new();

        // ★ 重入保护：L451 的 h.OnRenderersWasChanged() 会再触发 Ship.RefreshHull，
        //   冷启动封面船那次重入时船体尚在构造中 → NullReferenceException（两次/回）。
        [ThreadStatic] private static bool _inRefreshHull;

        [HarmonyPrefix]
        [HarmonyPatch(nameof(Ship.RefreshHull))]
        internal static bool Prefix_RefreshHull(Ship __instance, bool updateSections)
        {
            if (_inRefreshHull)
            {
                HotfixLog.Warn("[SMS.2.RH] re-entrant RefreshHull skipped (inner call -> vanilla)");
                return true;   // 内层交回原版，避免在半构造状态上跑我们的逻辑
            }
            _inRefreshHull = true;
            try
            {
            // ★ [SMS.2.RH] 色差二分开关：refresh_hull=0 时完全走原版
            if (!global::TAFHotfixBatch1.HotfixParams.RefreshHull)
                return true;

            HotfixLog.Dbg($"RefreshHull: {__instance.vesselName}");

            // 防御性判空（理论上不会发生；发生即为上游状态异常，交回原版更安全）
            if (__instance == null || __instance.hull == null || __instance.hull.data == null)
            {
                HotfixLog.Warn("[SMS.2.RH] RefreshHull: hull/data null, fallback to vanilla");
                return true;
            }

            var s = __instance;
            var h = s.hull;
            var hModelName = h.data.model;
            var hd = s.hull.data;

            if (h.middlesBase != null)
            {
                foreach (var m in h.middlesBase)
                {
                    if (m != null) Util.SetActiveX(m, false);
                }
            }
            else HotfixLog.Warn("[SMS.2.RH] middlesBase null @ " + s.vesselName);

            if (updateSections)
            {
                int desiredMiddleCount = Mathf.RoundToInt(
                    Mathf.Lerp(
                        hd.sectionsMin - 0.499f,
                        hd.sectionsMax + 0.499f,
                        Mathf.InverseLerp(
                            s.TonnageMin() / s.beamDraughtBonus,
                            s.TonnageMax() / s.beamDraughtBonus,
                            s.tonnage
                        )
                    )
                );

                UpdateSections(s, desiredMiddleCount);
            }

            HotfixLog.Dbg($"    {hModelName} | {HullData._Data.ContainsKey(hModelName)}");

            Il2CppSystem.Collections.Generic.List<string> varSet = new();
            if (hd.paramx != null && hd.paramx.ContainsKey("var"))
                varSet = hd.paramx["var"];
            else if (hd.paramx == null)
                HotfixLog.Warn("[SMS.2.RH] paramx null @ " + s.vesselName);

            var variations = s.hullCont != null ? s.hullCont.GetComponentsInChildren<Variation>() : null;
            if (variations == null)
                HotfixLog.Warn("[SMS.2.RH] hullCont null @ " + s.vesselName);
            foreach (var v in variations ?? new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Variation>(0))
            {
                string active = string.Empty;

                foreach (var vString in v.variations)
                    // TODO: Check for multiple matches
                    if (varSet.Contains(vString))
                        active = vString;

                HotfixLog.Dbg($"  Var {active}");

                v.Init(active, s);
            }

            // TODO: Check for var params that don't exist for hull

            HotfixLog.Dbg($"  Refreshing...");

            try { h.Refresh(true); }
            catch (Exception e) { HotfixLog.Warn("[SMS.2.RH] h.Refresh threw: " + e.GetType().Name + " " + e.Message); }
            try { s.RefreshMounts(); }
            catch (Exception e) { HotfixLog.Warn("[SMS.2.RH] RefreshMounts threw: " + e.GetType().Name + " " + e.Message); }

            HotfixLog.Dbg($"  Done!");

            // ★ 用 InstanceID 作键：Il2Cpp 的 GameObject 包装器做字典键会随机 KeyNotFoundException
            Dictionary<int, Bounds> sectionBounds = new();
            float totalLength = 0;
            var sectionsContainer = h.bow != null ? h.bow.GetParent() : null;
            if (sectionsContainer == null)
            {
                HotfixLog.Warn("[SMS.2.RH] sectionsContainer null, skip section-bounds pass");
                return false;
            }
            var sectionsList = new Il2CppSystem.Collections.Generic.List<GameObject>(h.hullSections);
            sectionsList.Reverse();
            foreach (var section in sectionsList)
            {
                if (section == null) continue;            // 段可能已被销毁/替换
                Bounds total = new();
                bool inited = false;

                foreach (var visual in Part.GetVisualRenderers(section))
                {
                    if (!visual.gameObject.activeInHierarchy)
                        continue;

                    // ★ 真因：部分 Renderer 在此阶段可能没有 MeshFilter / sharedMesh
                    //   （或本身是 SkinnedMeshRenderer），直接解引用会 NRE。
                    var mf = visual.GetComponent<MeshFilter>();
                    if (mf == null || mf.sharedMesh == null)
                        continue;

                    var bounds = Util.InverseTransformBounds(
                        sectionsContainer.transform,
                        Util.TransformBounds(
                            visual.transform,
                            mf.sharedMesh.bounds
                        )
                    );

                    if (!inited)
                        total = bounds;
                    else
                        total.Encapsulate(bounds);

                    inited = true;
                }

                var info = section.GetComponent<SectionInfo>()
                    ?? section.GetComponentInChildren<SectionInfo>();

                if (info != null)
                {
                    total.size += Vector3.forward * (info.sizeFrontMod + info.sizeBackMod);
                    total.center += Vector3.forward * (info.sizeFrontMod - info.sizeBackMod) * 0.5f;
                }

                var lod = section.GetComponentInChildren<AutomaticLOD>();
                lod.ComputeAllLODMeshes(false);
                lod.SwitchToLOD(0, false);

                totalLength += total.size.z;
                sectionBounds[section.GetInstanceID()] = total;
            }
            totalLength *= -0.5f;

            HotfixLog.Dbg($"  Total length: {totalLength}");

            foreach (var section in sectionsList)
            {
                Util.SetLocalZ(
                    section.transform,
                    sectionBounds[section.GetInstanceID()].size.z * 0.5f + totalLength
                    + section.transform.localPosition.z - sectionBounds[section.GetInstanceID()].center.z
                );

                var beam = s.beam != -1000f ? s.beam : 0f;
                Util.SetScaleX(section.transform, beam / 100f + 1f);

                var draught = s.draught != -1000f ? s.draught : 0f;
                Util.SetScaleY(section.transform, draught / 100f + 1f);

                totalLength += sectionBounds[section.GetInstanceID()].size.z;
            }

            HotfixLog.Dbg($"  Rescaled {s.beam} & {s.draught}");

            h.RegrabDeckSizes();

            Bounds shipBounds = new();
            bool initedShipBounds = false;
            foreach (var r in h.visualRenderers)
            {
                if (!r.gameObject.activeInHierarchy)
                    continue;

                if (!initedShipBounds)
                    shipBounds = r.bounds;
                else
                    shipBounds.Encapsulate(r.bounds);

                initedShipBounds = true;
            }
            
            s.hullSize = shipBounds;

            HotfixLog.Dbg($"  Total hull size: {s.hullSize.size} : {s.hullSize.center}");

            if (s.hullSize.center.x != 0)
            {
                var newCenter = Util.ChangeX(s.hullSize.center, 0);
                var size = s.hullSize.size;
                s.hullSize = new(newCenter, size);
            }

            s.CheckHullSizeMaxMin();
            s.decks = new();

            var hullBoxCols = h.GetComponentsInChildren<BoxCollider>();
            Bounds totalDeckBounds = new();
            bool initedTotalDeckBounds = false;
            foreach (var c in hullBoxCols)
            {
                if (c.gameObject.layer != LayerMask.NameToLayer("Deck"))
                    continue;

                var center = c.center;
                center.Scale(c.transform.lossyScale);
                var size = c.size;
                size.Scale(c.transform.lossyScale);
                Bounds adjusted = new(center, size);

                if (!initedTotalDeckBounds)
                    totalDeckBounds = adjusted;
                else
                    totalDeckBounds.Encapsulate(adjusted);

                var f = c.size.x * c.transform.lossyScale.x * c.size.z * c.transform.lossyScale.z;

                s.decks.Add(c, f);

                initedTotalDeckBounds = true;
            }

            // ★★★ [SMS.2.PROP] 关键修复：把累计的甲板包围盒写回 `s.deckBounds`。
            //   我们的 RefreshHull 移植漏了这一步（Beta6 用 __instance.deckBounds 计算布点区域），
            //   导致 s.deckBounds 是 default(size=0) ⇒ PoissonDisc 生成 0 个点 ⇒ DeckProps 永远为空。
            HotfixLog.Dbg($"  Found {s.decks.Count} decks (deckBounds={s.deckBounds.size})");

            s.decksFake = Util.FindCustomChildren(h, new System.Func<GameObject, bool>((GameObject go) => {
                return go.name == "DeckFake";
            }));

            HotfixLog.Dbg($"  Found {s.decksFake.Count} decksFake");

            s.sidesLeft = new();
            foreach (var c in hullBoxCols)
            {
                if (c.gameObject.name == "DeckBorderLeft")
                    continue;

                var f = c.size.z * c.transform.lossyScale.z;

                s.sidesLeft.Add(c, f);
            }

            HotfixLog.Dbg($"  Found {s.sidesLeft.Count} sidesLeft");

            s.sidesRight = new();
            foreach (var c in hullBoxCols)
            {
                if (c.gameObject.name == "DeckBorderRight")
                    continue;

                var f = c.size.z * c.transform.lossyScale.z;

                s.sidesRight.Add(c, f);
            }

            HotfixLog.Dbg($"  Found {s.sidesRight.Count} sidesRight");

            s.cameraPlane = Util.GetChild<BoxCollider>(h, "CameraPlane", true);

            if (s.cameraPlane == null)
            {
                var plane = Util.AttachNew(h.gameObject, "CameraPlane");
                s.cameraPlane = plane.AddComponent<BoxCollider>();
                s.cameraPlane.isTrigger = true;
                Util.SetActiveX(s.cameraPlane, false);
                s.cameraPlane.center = s.hullSize.center;
                s.cameraPlane.size = Util.ChangeY(s.hullSize.size, 0.000001f);
            }

            HotfixLog.Dbg($"  Found camera plane");

            s.collision.GetComponent<Rigidbody>().mass = s.Weight() * 1.475f;
            s.collision.radius = s.hullSize.size.x * 1.023f * 0.5f;
            s.collision.height = s.hullSize.size.z * 1.35f;
            s.collisionRam = Util.GetOrAddComponent<CollisionArea>(s.collision);

            HotfixLog.Dbg($"  Settup colision");

            HotfixLog.Dbg("[SMS.2.PROP] enter: hull='" + hModelName
                + "' coverage=" + UiM.TAF_Settings.settings.deckPropCoverage
                + " updateSections=" + updateSections);

            // ★ [SMS.2.PROP] 默认**不在 RefreshHull 内布点**（prop_in_refresh_hull=0）。
            //   实测：走这里的布点会得到"一坨"的结果（段重建后 deck 包围盒/坐标未就绪）；
            //   而 refresh_hull=0 时由另一条 deck-clutter 路径布点是正常的（100/50/25% 均正确）。
            //   因此默认交给那条路径；需要对照实验时再把它设为 1。
            if (!global::TAFHotfixBatch1.HotfixParams.PropInRefreshHull)
            {
                HotfixLog.Dbg("[SMS.2.PROP] skipped in RefreshHull (prop_in_refresh_hull=0)");
            }
            else if (UiM.TAF_Settings.settings.deckPropCoverage == 0)
            {
                Util.FindChild(h, "DeckProps")?.TryDestroy(true);
            }
            else
            {
                PlacePropsDuke(s, h);
            }

            {
                int pc = -1;
                try { var dp = Util.FindChild(h, "DeckProps"); pc = dp != null ? dp.GetChildren().Count : -2; } catch { }
                HotfixLog.Dbg("[SMS.2.PROP] done: hull='" + hModelName + "' DeckProps children=" + pc);
            }

            try { ShipWakeHint.Register(s); } catch { }

            s.RefreshHullStats();
            s.RefreshMountedParts(null);
            try { h.OnRenderersWasChanged(); }
            catch (Exception e)
            {
                HotfixLog.Warn("[SMS.2.RH] OnRenderersWasChanged threw (cold-start re-entry?): "
                    + e.GetType().Name + " " + e.Message);
            }

            // ★ [SMS.2.OCEAN] RefreshHull 之后重放一次海面模式：
            //   场景绑定时的 SetOceanMode 早于本次重建，之后水面对象可能被重建回来
            //   （表现为 oceanType=None 时仍有水）。这里只做很轻的开关动作。
            try
            {
                int __ot = 0;
                try { __ot = global::TweaksAndFixes.UiM.TAF_Settings.settings.oceanType; } catch { }
                if (__ot != 0)
                    Patch_SceneManagerPreview.SetOceanMode(__ot);
            }
            catch { }

            foreach (var p in s.hullAndParts)
            {
                if (p.visualRenderers == null)
                    continue;

                for (int i = p.visualRenderers.Count - 1; i >= 0; i--)
                {
                    if (p.visualRenderers[i] != null)
                        continue;

                    p.visualRenderers.RemoveAt(i);
                }
            }

            }
            finally { _inRefreshHull = false; }

            return false;
        }
        /// <summary>

        // ======================================================================
        //  ★ duke 的原版甲板装饰物布点（Beta6 Patch_Ship L1105–1182 直译）
        //    与我们的旧实现差别：
        //      · 用 Ship.deckBounds（不是 decks 遍历 + 自写三维回退）
        //      · 用 ModUtils.GeneratePoissonDiscPoints（Bridson 采样）
        //      · 密度由 deckPropCoverage 换算 separation = 5 + 4*(100-cov)/100
        //      · 每个点放一个 prop，并在 x 取反处放一个**镜像**
        //      · prop 图层设为 "Decor"
        // ======================================================================
        private static void PlacePropsDuke(Ship s, Part hull)
        {
            try
            {
                var container = Util.FindOrCreate(hull.gameObject, "DeckProps");

                // 模板：DeckProp → Choices[0] 的子物体就是可选 prop 列表
                var template = Util.AttachInst(s.gameObject, "DeckProp", Vector3.zero, new Quaternion());
                var choices = Util.GetChildren(Util.GetChildren(Util.GetChild(template, "Choices", false))[0]);

                int coverage = UiM.TAF_Settings.settings.deckPropCoverage;
                float separation = 5f + (4f * (100 - coverage)) / 100f;
                var db = s.deckBounds;

                float halfX = (db.size.x - separation / 4f) / 2f;
                var poissonArea = new Bounds(db.center, new Vector3(halfX, db.size.y, db.size.z));
                var points = PoissonDisc.Generate(poissonArea, separation);

                // 缓存：把已存在的 DeckProps 子物体按名字压栈，复用而不是无限增长
                if (PropCache.Count == 0)
                {
                    for (int i = 0; i < choices.Count; i++)
                        PropCache[choices[i].name] = new Stack<GameObject>();
                }

                var existing = Util.GetChildren(container);
                for (int i = 0; i < existing.Count; i++)
                {
                    string nm = existing[i].name.Replace("(Clone)", "");
                    if (!PropCache.ContainsKey(nm)) PropCache[nm] = new Stack<GameObject>();
                    PropCache[nm].Push(existing[i]);
                }

                int layer = LayerMask.NameToLayer("Decor");
                int placed = 0;

                foreach (var pt in points)
                {
                    int pick = UnityEngine.Random.Range(0, choices.Count);
                    string pickName = choices[pick].name;

                    float px = pt.x - s.hullSize.size.x / 2f;
                    float pz = pt.z - s.hullSize.size.z / 2f;

                    var deck = s.FindDeckAtPoint(new Vector3(px, 0f, pz));
                    if (deck == null) continue;

                    float deckY = deck.transform.position.y;

                    // 正向
                    GameObject prop;
                    if (PropCache[pickName].Count <= 0)
                        prop = UnityEngine.Object.Instantiate(choices[pick], container.transform);
                    else
                        prop = PropCache[pickName].Pop();

                    prop.transform.localPosition = new Vector3(px, deckY, pz);
                    Util.SetLayer(prop, layer);
                    placed++;

                    // 镜像（x 取反）
                    float mx = 0f - px;
                    var deck2 = s.FindDeckAtPoint(new Vector3(mx, 0f, pz));
                    if (deck2 == null) continue;

                    GameObject prop2;
                    if (PropCache[pickName].Count <= 0)
                        prop2 = UnityEngine.Object.Instantiate(choices[pick], container.transform);
                    else
                        prop2 = PropCache[pickName].Pop();

                    prop2.transform.localPosition = new Vector3(mx, deck2.transform.position.y, pz);
                    Util.SetLayer(prop2, layer);
                    placed++;
                }

                // 回收多余缓存 + 销毁模板
                foreach (var kv in PropCache)
                    while (kv.Value.Count > 0) Util.TryDestroy(kv.Value.Pop(), true);

                Util.TryDestroy(template, true);

                HotfixLog.Dbg("[SMS.2.PROP] duke placed=" + placed + " points=" + points.Count
                              + " separation=" + separation.ToString("0.##"));
            }
            catch (Exception e) { HotfixLog.Warn("[SMS.2.PROP] PlacePropsDuke: " + e.Message); }
        }

        /// [SMS.2.PROP] 布点坐标 → (甲板, 局部坐标)。
        /// PoissonDisc 返回世界 XZ；原移植代码又减了 shipBounds/2 后直接喂 FindDeckAtPoint，
        /// 导致每个点都判定不到甲板（port48：props=27 pts=61 但 children=0）。
        /// 这里三级回退：世界坐标 → hull 局部 → 旧相对坐标。
        /// </summary>
        private static BoxCollider ResolveDeck(Ship s, Transform hullTr, Vector2 p, out Vector3 usedLocal)
        {
            usedLocal = Vector3.zero;
            Vector3 world = new Vector3(p.x, hullTr != null ? hullTr.position.y : 0f, p.y);

            try { var d = s.FindDeckAtPoint(world); if (d != null) { usedLocal = hullTr.InverseTransformPoint(world); return d; } } catch { }
            try { var local = hullTr.InverseTransformPoint(world); var d2 = s.FindDeckAtPoint(local); if (d2 != null) { usedLocal = local; return d2; } } catch { }
            try
            {
                var rel = new Vector3(p.x - s.hullSize.size.x / 2f, 0f, p.y - s.hullSize.size.z / 2f);
                var d3 = s.FindDeckAtPoint(rel); if (d3 != null) { usedLocal = rel; return d3; }
            }
            catch { }
            return null;
        }

    }

}
