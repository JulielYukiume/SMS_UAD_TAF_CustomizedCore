using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2Cpp;
using MelonLoader;
using TAFHotfixBatch1;
using UnityEngine;

namespace TweaksAndFixes
{
    /// <summary>
    /// [SMS.1.RH] 完整移植 Beta5 的 `Ship.RefreshHull` 重写（duke 的 31e3af3）。
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

            Melon<TweaksAndFixes>.Logger.Msg($"    Default ordering");
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

            Melon<TweaksAndFixes>.Logger.Msg($"  Sections {hModelName} {h.middles.Count} -> {middleCount}");

            if (!HullData.ModifiedSectionOrder(hModelName))
            {
                if (h.middles.Count != middleCount)
                    InitSectionsCyclical(s, middleCount);
            }
            else
            {
                Melon<TweaksAndFixes>.Logger.Msg($"    Custom ordering");
                var data = HullData._Data[hModelName].sectioningParsed;

                // Remove all, since order can be anything
                while (h.middles.Count > 0)
                {
                    var section = h.middles[^1];
                    h.middles.RemoveAt(h.middles.Count - 1);
                    GameObject.DestroyImmediate(section);
                }

                if (middleCount >= data.Count)
                {
                    // Melon<TweaksAndFixes>.Logger.Error(
                    //     $"Ship Sectioning: {s.hull.data.name} ({s.hull.model.name.Replace("Clone", "")}) has {data.Count} section lists defined, " +
                    //     $"but #{middleCount} (0-indexed) was requested!"
                    // );
                    InitSectionsCyclical(s, middleCount);
                    return;
                }
                
                var order = data[middleCount];

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

                    // Melon<TweaksAndFixes>.Logger.Msg($"    {index} : {h.middlesBase.Count}");
                    var section = Util.CloneGameObject(h.middlesBase[index]);
                    h.middles.Add(section);
                    Util.SetActiveX(section, true);
                }
            }
        }

        public static Dictionary<string, Stack<GameObject>> PropCache = new();

        [HarmonyPrefix]
        [HarmonyPatch(nameof(Ship.RefreshHull))]
        internal static bool Prefix_RefreshHull(Ship __instance, bool updateSections)
        {
            Melon<TweaksAndFixes>.Logger.Msg($"RefreshHull: {__instance.vesselName}");

            var s = __instance;
            var h = s.hull;
            var hModelName = h.data.model;
            var hd = s.hull.data;

            foreach (var m in h.middlesBase)
                Util.SetActiveX(m, false);

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

            Melon<TweaksAndFixes>.Logger.Msg($"    {hModelName} | {HullData._Data.ContainsKey(hModelName)}");

            Il2CppSystem.Collections.Generic.List<string> varSet = new();
            if (hd.paramx.ContainsKey("var"))
                varSet = hd.paramx["var"];

            foreach (var v in s.hullCont.GetComponentsInChildren<Variation>())
            {
                string active = string.Empty;

                foreach (var vString in v.variations)
                    // TODO: Check for multiple matches
                    if (varSet.Contains(vString))
                        active = vString;

                Melon<TweaksAndFixes>.Logger.Msg($"  Var {active}");

                v.Init(active, s);
            }

            // TODO: Check for var params that don't exist for hull

            Melon<TweaksAndFixes>.Logger.Msg($"  Refreshing...");

            h.Refresh(true);
            s.RefreshMounts();

            Melon<TweaksAndFixes>.Logger.Msg($"  Done!");

            Dictionary<GameObject, Bounds> sectionBounds = new();
            float totalLength = 0;
            var sectionsContainer = h.bow.GetParent();
            var sectionsList = new Il2CppSystem.Collections.Generic.List<GameObject>(h.hullSections);
            sectionsList.Reverse();
            foreach (var section in sectionsList)
            {
                Bounds total = new();
                bool inited = false;

                foreach (var visual in Part.GetVisualRenderers(section))
                {
                    if (!visual.gameObject.activeInHierarchy)
                        continue;

                    var bounds = Util.InverseTransformBounds(
                        sectionsContainer.transform,
                        Util.TransformBounds(
                            visual.transform,
                            visual.GetComponent<MeshFilter>().sharedMesh.bounds
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
                sectionBounds.Add(section, total);
            }
            totalLength *= -0.5f;

            Melon<TweaksAndFixes>.Logger.Msg($"  Total length: {totalLength}");

            foreach (var section in sectionsList)
            {
                Util.SetLocalZ(
                    section.transform,
                    sectionBounds[section].size.z * 0.5f + totalLength
                    + section.transform.localPosition.z - sectionBounds[section].center.z
                );

                var beam = s.beam != -1000f ? s.beam : 0f;
                Util.SetScaleX(section.transform, beam / 100f + 1f);

                var draught = s.draught != -1000f ? s.draught : 0f;
                Util.SetScaleY(section.transform, draught / 100f + 1f);

                totalLength += sectionBounds[section].size.z;
            }

            Melon<TweaksAndFixes>.Logger.Msg($"  Rescaled {s.beam} & {s.draught}");

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

            Melon<TweaksAndFixes>.Logger.Msg($"  Total hull size: {s.hullSize.size} : {s.hullSize.center}");

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

            Melon<TweaksAndFixes>.Logger.Msg($"  Found {s.decks.Count} decks");

            s.decksFake = Util.FindCustomChildren(h, new System.Func<GameObject, bool>((GameObject go) => {
                return go.name == "DeckFake";
            }));

            Melon<TweaksAndFixes>.Logger.Msg($"  Found {s.decksFake.Count} decksFake");

            s.sidesLeft = new();
            foreach (var c in hullBoxCols)
            {
                if (c.gameObject.name == "DeckBorderLeft")
                    continue;

                var f = c.size.z * c.transform.lossyScale.z;

                s.sidesLeft.Add(c, f);
            }

            Melon<TweaksAndFixes>.Logger.Msg($"  Found {s.sidesLeft.Count} sidesLeft");

            s.sidesRight = new();
            foreach (var c in hullBoxCols)
            {
                if (c.gameObject.name == "DeckBorderRight")
                    continue;

                var f = c.size.z * c.transform.lossyScale.z;

                s.sidesRight.Add(c, f);
            }

            Melon<TweaksAndFixes>.Logger.Msg($"  Found {s.sidesRight.Count} sidesRight");

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

            Melon<TweaksAndFixes>.Logger.Msg($"  Found camera plane");

            s.collision.GetComponent<Rigidbody>().mass = s.Weight() * 1.475f;
            s.collision.radius = s.hullSize.size.x * 1.023f * 0.5f;
            s.collision.height = s.hullSize.size.z * 1.35f;
            s.collisionRam = Util.GetOrAddComponent<CollisionArea>(s.collision);

            Melon<TweaksAndFixes>.Logger.Msg($"  Settup colision");

            Melon<TweaksAndFixes>.Logger.Msg("[SMS.1.PROP] enter: hull='" + hModelName
                + "' coverage=" + UiM.TAF_Settings.settings.deckPropCoverage
                + " updateSections=" + updateSections);

            if (UiM.TAF_Settings.settings.deckPropCoverage == 0)
            {
                Util.FindChild(h, "DeckProps")?.TryDestroy(true);
            }
            else
            {
                var propContainer = Util.FindOrCreate(h.gameObject, "DeckProps");

                Melon<TweaksAndFixes>.Logger.Msg($"  Settup props");

                var template = Util.AttachInst(s.gameObject, "DeckProp", Vector3.zero, new());
                var props = template.GetChild("Choices").GetChildren()[0].GetChildren();
                float separation = 5 + 4 * (100 - UiM.TAF_Settings.settings.deckPropCoverage) / 100f;
                Bounds area = new(s.deckBounds.center, new Vector3((s.deckBounds.size.x - separation / 4f) / 2f, s.deckBounds.size.y, s.deckBounds.size.z));
                var pts = PoissonDisc.Generate(area, separation);

                if (PropCache.Count == 0)
                    foreach (var prop in props)
                        PropCache.Add(prop.name, new());

                foreach (var child in propContainer.GetChildren())
                {
                    PropCache[child.name.Replace("(Clone)", "")].Push(child);
                }
            
                Melon<TweaksAndFixes>.Logger.Msg($"  Cache: {PropCache.Count}");

                foreach (var p in pts)
                {
                    GameObject prop;
                    BoxCollider deck;
                    int RandomIndex = UnityEngine.Random.Range(0, props.Count);

                    {
                        Vector3 rawPos = new(p.x - shipBounds.size.x / 2f, 0, p.y - shipBounds.size.z / 2);
                        deck = s.FindDeckAtPoint(rawPos);

                        if (deck == null)
                            continue;

                        if (PropCache[props[RandomIndex].name].Count > 0)
                        {
                            prop = PropCache[props[RandomIndex].name].Pop();
                        }
                        else
                            prop = GameObject.Instantiate(props[RandomIndex], propContainer.transform, false);

                        prop.transform.localPosition = rawPos + new Vector3(0, deck.transform.position.y, 0);
                        Util.SetLayer(prop, UnityEngine.LayerMask.NameToLayer("Decor"));
                    }

                    {
                        Vector3 rawPos = new(-(p.x - shipBounds.size.x / 2f), 0, p.y - shipBounds.size.z / 2);

                        if (deck == null)
                            continue;

                        if (PropCache[props[RandomIndex].name].Count > 0)
                        {
                            prop = PropCache[props[RandomIndex].name].Pop();
                        }
                        else
                            prop = GameObject.Instantiate(props[RandomIndex], propContainer.transform, false);

                        prop.transform.localPosition = rawPos + new Vector3(0, deck.transform.position.y, 0);
                        Util.SetLayer(prop, UnityEngine.LayerMask.NameToLayer("Decor"));
                    }
                }

                Melon<TweaksAndFixes>.Logger.Msg($"  Cache: {PropCache.Count}");

                foreach (var bucket in PropCache)
                    while (bucket.Value.Count > 0)
                        bucket.Value.Pop().TryDestroy(true);

                template.TryDestroy(true);
            }

            {
                int pc = -1;
                try { var dp = Util.FindChild(h, "DeckProps"); pc = dp != null ? dp.GetChildren().Count : -2; } catch { }
                Melon<TweaksAndFixes>.Logger.Msg("[SMS.1.PROP] done: hull='" + hModelName + "' DeckProps children=" + pc);
            }

            s.RefreshHullStats();
            s.RefreshMountedParts(null);
            h.OnRenderersWasChanged();

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

            return false;
        }
    }
}
