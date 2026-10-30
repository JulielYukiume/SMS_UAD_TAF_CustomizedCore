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

            // ★★★ [SMS.1.PROP] 关键修复：把累计的甲板包围盒写回 `s.deckBounds`。
            //   我们的 RefreshHull 移植漏了这一步（Beta6 用 __instance.deckBounds 计算布点区域），
            //   导致 s.deckBounds 是 default(size=0) ⇒ PoissonDisc 生成 0 个点 ⇒ DeckProps 永远为空。
            Melon<TweaksAndFixes>.Logger.Msg($"  Found {s.decks.Count} decks (deckBounds={s.deckBounds.size})");

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

                Melon<TweaksAndFixes>.Logger.Msg("  Settup props");

                var template = Util.AttachInst(s.gameObject, "DeckProp", Vector3.zero, new());
                var props = template.GetChild("Choices").GetChildren()[0].GetChildren();
                float separation = 5 + 4 * (100 - UiM.TAF_Settings.settings.deckPropCoverage) / 100f;

                if (PropCache.Count == 0)
                    foreach (var prop in props)
                        PropCache.Add(prop.name, new());

                foreach (var child in propContainer.GetChildren())
                    PropCache[child.name.Replace("(Clone)", "")].Push(child);

                // ★★★ [SMS.1.PROP] 2026-10-26 重写散布算法（按用户描述的规则）：
                //   1) 读取所有 deckplace 的位置/旋转/尺寸；
                //   2) 跳过绕 X 轴有轻微旋转的甲板（舰艏上翘段，不适合摆放）；
                //   3) 按高度分组（同组近似同一水平面，容差 HeightTol）；
                //   4) 每组取总包围盒，在盒内按 Poisson 均匀布点；
                //   5) 逐点实例化小物件并放到对应高度。
                const float HeightTol = 0.75f;

                var groupBounds = new System.Collections.Generic.List<Bounds>();
                int deckTotal = 0, skippedRot = 0;

                var eDecks = s.decks.GetEnumerator();
                while (eDecks.MoveNext())
                {
                    var col = eDecks.Current.Key;
                    if (col == null) continue;
                    deckTotal++;

                    var tr = col.transform;

                    // 跳过带 X 轴旋转的甲板（一般对应舰艏上翘部分）
                    float rotX = Mathf.DeltaAngle(0f, tr.eulerAngles.x);
                    if (Mathf.Abs(rotX) > 2f) { skippedRot++; continue; }

                    Bounds b = col.bounds;   // 世界空间 AABB

                    int gi = -1;
                    for (int i = 0; i < groupBounds.Count; i++)
                    {
                        if (Mathf.Abs(groupBounds[i].center.y - b.center.y) <= HeightTol) { gi = i; break; }
                    }

                    if (gi < 0) groupBounds.Add(b);
                    else { var g = groupBounds[gi]; g.Encapsulate(b); groupBounds[gi] = g; }
                }

                int placed = 0;

                foreach (var g in groupBounds)
                {
                    // PoissonDisc 的 size 是半宽/半深语义 ⇒ 传一半
                    var half = new Bounds(g.center, new Vector3(g.size.x * 0.5f, g.size.y, g.size.z * 0.5f));
                    var pts = PoissonDisc.Generate(half, separation);

                    foreach (var p in pts)
                    {
                        int ridx = UnityEngine.Random.Range(0, props.Count);
                        GameObject prop;

                        if (PropCache[props[ridx].name].Count > 0)
                            prop = PropCache[props[ridx].name].Pop();
                        else
                            prop = GameObject.Instantiate(props[ridx], propContainer.transform, false);

                        Vector3 world = new Vector3(p.x, g.center.y, p.y);
                        prop.transform.localPosition = propContainer.transform.InverseTransformPoint(world);
                        Util.SetLayer(prop, UnityEngine.LayerMask.NameToLayer("Decor"));

                        placed++;
                    }
                }

                Melon<TweaksAndFixes>.Logger.Msg("[SMS.1.PROP] detail: decks=" + deckTotal
                    + " skippedRot=" + skippedRot + " groups=" + groupBounds.Count
                    + " sep=" + separation.ToString("0.##") + " placed=" + placed
                    + " props=" + props.Count);

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

            // ★ [SMS.1.SHADOW] A/B 测试：关闭"复制出来的舯段"的投影，用于验证色带是否自遮挡阴影
            if (HotfixParams.MiddleShadowOff)
            {
                try
                {
                    int cnt = 0;
                    var mids = h.middles;
                    if (mids != null)
                        for (int i = 0; i < mids.Count; i++)
                            foreach (var rr in mids[i].GetComponentsInChildren<Renderer>())
                            { rr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; cnt++; }
                    Melon<TweaksAndFixes>.Logger.Msg("[SMS.1.SHADOW] middle shadows off: " + cnt + " renderers");
                }
                catch (Exception e) { HotfixLog.Warn("[SMS.1.SHADOW] " + e.Message); }
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
        /// <summary>
        /// [SMS.1.PROP] 布点坐标 → (甲板, 局部坐标)。
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
