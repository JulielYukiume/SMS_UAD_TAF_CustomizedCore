using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Il2Cpp;

namespace TAFHotfixBatch1.Modules
{
    /// <summary>
    /// Module C-③ —— **读档期抢修：抢在 TAF 删除"无效船"之前补回 designId**。
    ///
    /// ## 为什么必须是 Prefix、且优先级要最高
    /// TAF 在 `GameManager._LoadCampaign_d__NN::MoveNext` 的 **Prefix（state 9）** 里做"读档修复"：
    /// <code>
    /// foreach (ship in store.Ships) {
    ///     if (!ship.isSharedDesign || status ∈ {Erased,Sunk,Scrapped} || ship.designId != Guid.Empty) continue;
    ///     if (ship.id == Guid.Empty) re-id &amp; designs.Add; else if (status == None) designs.Add; else nullIds.Add;
    /// }
    /// foreach (ship in nullIds) {
    ///     foreach (design in designs) {                 // ⚠ 候选池仅 **isSharedDesign** 且 status==None
    ///         if (parts.Count 不等) continue; for i: if (parts[i].Id != design.parts[i].Id) 不匹配;
    ///         if (匹配) { ship.designId = design.id; break; }
    ///     }
    ///     if (!匹配) store.Ships.Remove(ship);          // ★★ 删船
    /// }
    /// </code>
    /// ⇒ 任何 **Postfix 都来不及**（TAF 已经删完）；必须以更高优先级 **Prefix** 抢在它前面把
    ///   `designId` 补上，使 TAF 的 `designId != Guid.Empty ⇒ continue` 直接跳过这些船。
    ///
    /// ## 匹配规则（由强到弱）
    /// 1. **`parts[i].Id` 全序列相等** —— 即 TAF 自己用的判据，但**候选池放宽到全部设计条目**
    ///    （TAF 只查 `isSharedDesign` 的设计，这是它误删的根源）；
    /// 2. **改修链**：`refitDesignListID` 取**最后一条**（最接近当前状态的改修级），
    ///    取不到再取**第一条**（原始舰级）——"最不济假回退到上一级改修状态"；
    /// 3. **五项指纹**：`hullName + shipType + tonnage(标准吨位) + beam + draught
    ///    + instability_xx(横摇) + instability_zz(纵摇)`；`playerName` 仅作优先级（捕获/易主舰会不同）。
    ///
    /// 全部只写 `designId` 一个字段；幂等；`mode=log` 只打印。
    /// </summary>
    internal static class ZeroedShipRepair
    {
        private const float EPS = 0.05f;
        private static bool _done;

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            if (!HotfixParams.DuplicateDesignGuard)
            {
                HotfixLog.Info("[SMS.1.C3] ZeroedShipRepair: disabled by config");
                return;
            }

            try
            {
                Type smType = null;
                foreach (Type t in typeof(GameManager).GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (t.Name.StartsWith("_LoadCampaign_d__", StringComparison.Ordinal)
                        && t.GetMethod("MoveNext", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance) != null)
                    {
                        smType = t;
                        break;
                    }
                }

                if (smType == null)
                {
                    HotfixLog.Warn("[SMS.1.C3] load-campaign state machine not found -- repair unavailable");
                    return;
                }

                MethodInfo moveNext = smType.GetMethod("MoveNext",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                harmony.Patch(moveNext,
                    prefix: new HarmonyMethod(
                        AccessTools.Method(typeof(ZeroedShipRepair), nameof(Prefix_MoveNext)))
                    { priority = Priority.First + 100 });   // ★ 必须高于 TAF 的 Priority.First(=800)

                HotfixLog.Info("[SMS.1.C3] ZeroedShipRepair: patched " + smType.Name + "::MoveNext at Priority.First+100"
                               + " (outranks TAF's own prefix)  mode=" + HotfixParams.Mode);

                // ❌ 已回滚：给 MessagePack 的 CampaignController_StoreFormatter::Deserialize 打 Postfix
                //    会崩 —— 它是反序列化核心路径，Postfix 在 IL2CPP 域未就绪时抛错即导致读档崩溃；
                //    而且定位类型用的 AccessTools.TypeByName 会遍历所有程序集，产生大量
                //    "AccessTools.GetTypesFromAssembly ... ReflectionTypeLoadException" 黄字。
                //    实测（2026-10-17 15:54）：主界面黄字 + 读档直接崩溃。
                //    ⇒ 恢复"仅 MoveNext"方案（第六轮：零丢失、稳定）。
                //    ② 的正确做法另议（见 doc 21）：需要**真正实例化 Ship 对象**，
                //    而不是在 store 层改 designId（分类时会被游戏写回 0）。
            }
            catch (Exception e)
            {
                HotfixLog.Error("[SMS.1.C3] install failed: " + e);
            }
        }

        /// <summary>
        /// 未能匹配到"安全设计"的船形条目 ⇒ 记录其**自身 store 引用**。
        /// B2 阶段据此"以船自身的数据生成一份设计"（用户方案：船与设计同构，无需外部检索）。
        /// </summary>
        private static int _enterCount;
        private static readonly Dictionary<string, Ship.Store> _storeByShipId = new Dictionary<string, Ship.Store>();
        /// <summary>
        /// 为"找不到任何对应设计"的船**深拷贝**出的设计 store（设计态、带新 Guid）。
        /// B2 用它走 Ship.Create + FromStore，确保船与设计 id 一致且设计确实成形。
        /// </summary>
        private static readonly Dictionary<string, Ship.Store> _cloneStoreByShipId = new Dictionary<string, Ship.Store>();

        internal static bool TryGetCloneStore(string shipId, out Ship.Store store)
        {
            store = null;
            if (string.IsNullOrEmpty(shipId)) return false;
            return _cloneStoreByShipId.TryGetValue(shipId, out store);
        }

        internal static bool TryGetStoreForShip(string shipId, out Ship.Store store)
        {
            store = null;
            if (string.IsNullOrEmpty(shipId)) return false;
            return _storeByShipId.TryGetValue(shipId, out store);
        }
        private static int _missDiag;
        private static int _lastReid;

        /// <summary>
        /// 存档侧算出的 "船 id → 设计 id" 映射，供 <see cref="ShipDesignRelink"/>（运行时）直接使用。
        /// 理由：store 层有**完整字段**（parts/hull/tonnage/横纵摇），匹配率远高于运行时对象；
        /// 而运行时设计池只有"被引用的设计"（实测 88 个），会漏掉未被人引用的设计。
        /// </summary>
        private static readonly Dictionary<string, string> _designIdByShipId = new Dictionary<string, string>();

        internal static bool TryGetDesignIdFor(string shipId, out string designId)
        {
            designId = null;
            if (string.IsNullOrEmpty(shipId)) return false;
            return _designIdByShipId.TryGetValue(shipId, out designId);
        }

        /// <summary>
        /// 映射里出现过的**设计 id 集合**。这些 id 都对应存档里真实存在的设计条目
        /// ⇒ B2 只允许挂到这些设计上，避免产生"悬空 designId"（第 18 轮实测：256 条船因此被丢）。
        /// </summary>
        internal static IEnumerable<string> MappedDesignIds()
        {
            return _designIdByShipId.Values;
        }

        /// <summary>
        /// ★★ 首选入口：`CampaignController_StoreFormatter::Deserialize`（RVA 0x653D60）的 Postfix。
        /// 这是存档反序列化的唯一出口 —— 返回时 store 已完整、**游戏尚未做任何"船/设计"分类**。
        /// 在这里补 `designId`，游戏分类时看到的就是正确数据；
        /// 否则会出现"写了 designId 但存档重建时又被覆盖回 0"（第六轮实测：192 条只生效 7 条）。
        /// </summary>
        internal static void Postfix_Deserialize(CampaignController.Store __result)
        {
            if (_done || __result == null || __result.Ships == null) return;
            RepairStore(__result.Ships, "Deserialize");
        }

        /// <summary>退路：若 Deserialize 钩子未生效，仍在 LoadCampaign 状态机里尝试一次。</summary>
        internal static void Prefix_MoveNext(object __instance)
        {
            _enterCount++;
            if (_enterCount <= 3)
                HotfixLog.Debug("[SMS.1.C3] prefix entered #" + _enterCount + "  sm="
                               + (__instance == null ? "<null>" : __instance.GetType().Name));

            if (_done || __instance == null) return;

            // A 方案探针的延迟安装（MessagePack 程序集通常在初始化之后才加载）
            try { DeserializeProbe.TryPatchNow("MoveNext"); } catch { }

            try
            {
                // ★ 关键：Harmony 以 object 声明时，Il2CppInterop 只给"基类包装"，
                //   反射只能看到 isWrapped/pooledPtr。因此取出 Pointer，重建**具体代理类型**的强包装。
                object sm = AsProxy(__instance) ?? __instance;

                string diag;
                var ships = FindShips(sm, out diag);
                if (ships == null)
                {
                    if (_enterCount <= 3)
                        HotfixLog.Debug("[SMS.1.C3] store lookup failed: " + diag);
                    return;
                }
                if (ships.Count == 0) return;

                // 诊断：记录状态机当前 state（用于判断我们是否在"船/设计分类"之前拿到 store）
                if (_enterCount <= 8)
                {
                    int st = -999;
                    try
                    {
                        var pi = sm.GetType().GetProperty("__1__state",
                            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (pi != null) st = Convert.ToInt32(pi.GetValue(sm));
                    }
                    catch { }
                    HotfixLog.Debug("[SMS.1.C3] MoveNext state=" + st + "  ships=" + ships.Count);
                }

                RepairStore(ships, "MoveNext");
            }
            catch (Exception e)
            {
                HotfixLog.Error("[SMS.1.C3] Prefix_MoveNext: " + e.Message);
            }
        }

        /// <summary>去重 + 补 designId + 共享设计解耦；两个入口共用。</summary>
        private static void RepairStore(Il2CppSystem.Collections.Generic.List<Ship.Store> ships, string origin)
        {
            if (_done) return;
            try
            {
                HotfixLog.Debug("[SMS.1.C3] repair pass via " + origin + "  (" + ships.Count + " entries)");

                // ---- 0) 先去重：两份设计共用一个 id 时，保留第一份、其余换新 Guid ----
                //  必须早于"补 designId"，否则按 id 匹配可能命中错的那一份。
                try
                {
                    var seenIds = new HashSet<string>();
                    int dup = 0, reid = 0;
                    for (int i = 0; i < ships.Count; i++)
                    {
                        Ship.Store d = ships[i];
                        if (d == null || d.designId != Il2CppSystem.Guid.Empty) continue;
                        string did = d.id.ToString();
                        if (did == "00000000-0000-0000-0000-000000000000") continue;
                        if (seenIds.Add(did)) continue;
                        dup++;
                        if (HotfixParams.IsApply)
                        {
                            d.id = Il2CppSystem.Guid.NewGuid();
                            reid++;
                            HotfixLog.Error("[SMS.1.C][repair] duplicate design id " + did + " ('" + N(d) + "') -> re-id " + d.id);
                        }
                        else
                        {
                            HotfixLog.Error("[SMS.1.C][repair][log] duplicate design id " + did + " ('" + N(d) + "') -> would re-id");
                        }
                    }
                    if (dup > 0)
                    {
                        _lastReid = reid;
                        HotfixLog.Info("[SMS.1.C] dedupe: " + dup + " duplicate design id(s) found, " + reid + " re-id'd");
                    }
                }
                catch (Exception e1) { HotfixLog.Error("[SMS.1.C] dedupe: " + e1.Message); }

                // ---- 候选船（与 TAF 的 nullIds 判定一致）----
                var candidates = new List<Ship.Store>();
                for (int i = 0; i < ships.Count; i++)
                {
                    Ship.Store s = ships[i];
                    if (s == null) continue;
                    if (!s.isSharedDesign) continue;
                    if (s.designId != Il2CppSystem.Guid.Empty) continue;
                    if (s.status == VesselEntity.Status.Erased
                        || s.status == VesselEntity.Status.Sunk
                        || s.status == VesselEntity.Status.Scrapped) continue;
                    if (s.id == Il2CppSystem.Guid.Empty) continue;
                    if (s.status == VesselEntity.Status.None) continue;
                    candidates.Add(s);
                }

                if (candidates.Count == 0) return;   // 稍后状态可能变化，允许下一轮再试

                _done = true;
                HotfixLog.Warn("[SMS.1.C3] " + candidates.Count + " shared-design ship(s) would be deleted by TAF's state-9 cleanup");

                // ---- 设计池：**全部** designId==Empty 的条目（不再只看共享设计）----
                var byParts = new Dictionary<string, Ship.Store>();
                var byId = new Dictionary<string, Ship.Store>();
                var pool = new List<Ship.Store>();
                for (int i = 0; i < ships.Count; i++)
                {
                    Ship.Store d = ships[i];
                    if (d == null || d.designId != Il2CppSystem.Guid.Empty) continue;
                    pool.Add(d);
                    byId[d.id.ToString()] = d;
                    string pk = PartsKey(d);
                    if (pk != null && !byParts.ContainsKey(pk)) byParts[pk] = d;
                }

                int viaParts = 0, viaChain = 0, viaFp = 0, failed = 0, applied = 0;
                var rescued = new List<Ship.Store>();

                foreach (Ship.Store s in candidates)
                {
                    Ship.Store match = null;
                    string how = null;

                    // 1) parts 全序列（TAF 的判据，但池子放宽）
                    string pk = PartsKey(s);
                    if (pk != null)
                    {
                        Ship.Store d;
                        if (byParts.TryGetValue(pk, out d)) { match = d; how = "parts"; }
                    }

                    // 2) 改修链：最后一条 → 第一条
                    if (match == null && s.refitDesignListID != null && s.refitDesignListID.Count > 0)
                    {
                        for (int k = s.refitDesignListID.Count - 1; k >= 0 && match == null; k--)
                        {
                            Ship.Store d;
                            if (byId.TryGetValue(s.refitDesignListID[k].ToString(), out d)) { match = d; how = "chain[last]"; }
                        }
                        if (match == null)
                        {
                            Ship.Store d;
                            if (byId.TryGetValue(s.refitDesignListID[0].ToString(), out d)) { match = d; how = "chain[first]"; }
                        }
                    }

                    // 3) 五项指纹
                    if (match == null)
                    {
                        List<Ship.Store> hits = Fingerprint(s, pool);
                        if (hits.Count >= 1) { match = hits[0]; how = "fingerprint" + (hits.Count > 1 ? "(" + hits.Count + ")" : ""); }
                    }

                    if (match == null)
                    {
                        failed++;
                        HotfixLog.Warn("[SMS.1.C3][miss] '" + N(s) + "' type=" + Str(s.shipType)
                                       + " hull=" + Str(s.hullName) + " tonnage=" + F(s.tonnage)
                                       + " -> still unmatched (TAF would delete it)");
                        continue;
                    }

                    if (how == "parts") viaParts++;
                    else if (how.StartsWith("chain")) viaChain++;
                    else viaFp++;

                    if (HotfixParams.IsApply)
                    {
                        s.designId = match.id;
                        applied++;
                        rescued.Add(s);
                        HotfixLog.Info("[SMS.1.C3] rescued '" + N(s) + "' -> design '" + N(match) + "' via " + how);
                    }
                    else
                    {
                        HotfixLog.Info("[SMS.1.C3][log] would rescue '" + N(s) + "' -> design '" + N(match) + "' via " + how);
                    }
                }

                // ---- 4) C-④ 共享设计解耦 + 非法状态归一（用户定规则，2026-10-17）----
                //   规则①：`Erased(7)` 只应属于**设计**；**实存船只走 Sunk(5)/Scrapped(6)**
                //          ⇒ 救回的船若为 Erased，归一为 Sunk（同时进入 Module D 的缓冲范围）。
                //   规则②：`isSharedDesign` 在当前环境**仅有显示意义**
                //          （共享设计打包成预制、再作为预制菜抽出时该标记即丢失），
                //          且 UAD 把共享设计写进存档却仍强制要求外部文件存在 ⇒
                //          **对存档内设计清除该标记，与外部文件依存解耦**。
                //   ⭐ 附带收益：TAF 的 state-9 清理首句即 `if (!ship.isSharedDesign || …) continue;`
                //      ⇒ 标记清除后这些条目**根本不会进入它的候选**，从源头避免误删。
                int decoupled = 0, statusFixed = 0;
                if (HotfixParams.IsApply)
                {
                    var designIds = new HashSet<string>();
                    foreach (Ship.Store s in rescued)
                    {
                        try
                        {
                            if (s.isSharedDesign) { s.isSharedDesign = false; decoupled++; }
                            if ((int)s.status == 7)
                            {
                                s.status = VesselEntity.Status.Sunk;
                                statusFixed++;
                            }
                            // 记录其设计 id，稍后一并解耦
                            foreach (Ship.Store d in pool)
                            {
                                if (d.id == s.designId) { designIds.Add(d.id.ToString()); break; }
                            }
                        }
                        catch { }
                    }
                    for (int i = 0; i < ships.Count; i++)
                    {
                        Ship.Store d = ships[i];
                        if (d == null || d.designId != Il2CppSystem.Guid.Empty) continue;
                        if (!designIds.Contains(d.id.ToString())) continue;
                        try { if (d.isSharedDesign) { d.isSharedDesign = false; decoupled++; } } catch { }
                    }
                    HotfixLog.Info("[SMS.1.C4] shared-design decoupling: " + decoupled + " flag(s) cleared, "
                                   + statusFixed + " ship status normalized Erased->Sunk");
                }
                else if (rescued.Count > 0)
                {
                    HotfixLog.Info("[SMS.1.C4][log] would clear isSharedDesign on " + rescued.Count
                                   + " rescued ship(s) and their designs, and normalize Erased->Sunk");
                }

                // ---- 5) 为 B2（运行时 relink）准备 "船 id → 设计 id" 映射 ----
                //   store 层字段完整，能覆盖"运行时设计池"漏掉的未被引用设计。
                try
                {
                    _designIdByShipId.Clear();
                    _storeByShipId.Clear();
                    _cloneStoreByShipId.Clear();

                    // ★ Pass 0（用户建议，2026-10-17）：**有效设计若 id 为全零 ⇒ 强行发一个新 id**。
                    //   已量化：原始存档有 15 条这样的条目，全部是 status=7 的设计、parts 与 tonnage 都完整
                    //   ⇒ 内容有效、只是 id 丢了（重复抽选的副作用）。补号后它们才能被船引用。
                    {
                        int reid0 = 0;
                        for (int i = 0; i < ships.Count; i++)
                        {
                            Ship.Store d = ships[i];
                            if (d == null) continue;
                            if (d.designId != Il2CppSystem.Guid.Empty) continue;      // 只处理设计槽
                            if (d.id != Il2CppSystem.Guid.Empty) continue;             // 只处理全零 id
                            int np = 0; float ton = 0f; string hull = null;
                            try { np = d.parts != null ? d.parts.Count : 0; } catch { }
                            try { ton = d.tonnage; } catch { }
                            try { hull = d.hullName; } catch { }
                            if (np <= 0 || ton <= 0f || string.IsNullOrEmpty(hull)) continue;   // 无效设计：跳过
                            if (HotfixParams.IsApply)
                            {
                                d.id = Il2CppSystem.Guid.NewGuid();
                                reid0++;
                                if (reid0 <= 10)
                                    HotfixLog.Info("[SMS.1.C3] zero-id design reissued: '" + N(d) + "' -> " + d.id);
                            }
                            else reid0++;
                        }
                        if (reid0 > 0)
                            HotfixLog.Warn("[SMS.1.C3] reissued new ids for " + reid0 + " valid zero-id design(s) (mode="
                                           + HotfixParams.Mode + ")");
                    }

                    var poolByParts = new Dictionary<string, Ship.Store>();
                    var poolByFp = new Dictionary<string, Ship.Store>();
                    var poolByFpLoose = new Dictionary<string, Ship.Store>();

                    // 索引键：五项指纹 / 宽松三项（横摇纵摇为主，用户建议）
                    for (int i = 0; i < ships.Count; i++)
                    {
                        Ship.Store d = ships[i];
                        if (d == null || d.designId != Il2CppSystem.Guid.Empty) continue;
                        string pk = PartsKey(d);
                        if (pk != null && !poolByParts.ContainsKey(pk)) poolByParts[pk] = d;
                        string k5 = FpKey(d, true), k3 = FpKey(d, false);
                        if (k5 != null && !poolByFp.ContainsKey(k5)) poolByFp[k5] = d;
                        if (k3 != null && !poolByFpLoose.ContainsKey(k3)) poolByFpLoose[k3] = d;
                    }

                    int mapped = 0, unmatched = 0, reided = 0;
                    for (int i = 0; i < ships.Count; i++)
                    {
                        Ship.Store sh = ships[i];
                        if (sh == null || sh.designId != Il2CppSystem.Guid.Empty) continue;
                        int st = 0;
                        try { st = (int)sh.status; } catch { }
                        if (st == 0 || st == 1 || st == 7) continue;   // 只处理船形（Erased 属于设计）

                        Ship.Store d2 = null;
                        string pk2 = PartsKey(sh);
                        if (pk2 == null || !poolByParts.TryGetValue(pk2, out d2) || ReferenceEquals(d2, sh))
                        {
                            d2 = null;
                            string k5 = FpKey(sh, true);
                            if (k5 == null || !poolByFp.TryGetValue(k5, out d2) || ReferenceEquals(d2, sh))
                            {
                                d2 = null;
                                string k3 = FpKey(sh, false);
                                if (k3 != null) poolByFpLoose.TryGetValue(k3, out d2);
                                if (ReferenceEquals(d2, sh)) d2 = null;
                            }
                        }

                        // 记录船自身 store：只要 B2 发现映射到的设计"不安全"，就用它生成设计（用户方案）
                        _storeByShipId[sh.id.ToString()] = sh;

                        if (d2 == null)
                        {
                            unmatched++;
                            if (HotfixParams.ShipDesignRegen)
                            {
                                Ship.Store clone = TryCloneDesignStore(sh);
                                if (clone != null) _cloneStoreByShipId[sh.id.ToString()] = clone;
                            }
                            continue;
                        }

                        // 设计条目 id 为全零 ⇒ 补一个新 Guid，否则无法被引用
                        string did = d2.id.ToString();
                        if (did == "00000000-0000-0000-0000-000000000000")
                        {
                            if (HotfixParams.IsApply)
                            {
                                var fresh = Il2CppSystem.Guid.NewGuid();
                                d2.id = fresh;
                                did = fresh.ToString();
                                reided++;
                            }
                            else
                            {
                                did = "<would-new-guid>";
                            }
                        }

                        _designIdByShipId[sh.id.ToString()] = did;
                        mapped++;
                    }
                    if (unmatched > 0)
                    {
                        HotfixLog.Warn("[SMS.1.C3][LEGACY] " + unmatched + " ship(s) still have no resolvable design"
                                       + " (ghost entries preserved in the save, but not usable as ships)."
                                       + " They require an EXTERNAL save-repair tool -- see taf/docs/23 §6.4.");
                    }
                    HotfixLog.Info("[SMS.1.C3] design map for B2: mapped=" + mapped + " unmatched=" + unmatched
                                   + " zeroIdDesignReissued=" + reided);
                }
                catch (Exception e5) { HotfixLog.Warn("[SMS.1.C3] design map: " + e5.Message); }

                HotfixLog.Info("[SMS.1.C3] summary[" + origin + "]: candidates=" + candidates.Count + " viaParts=" + viaParts
                               + " viaChain=" + viaChain + " viaFingerprint=" + viaFp
                               + " failed=" + failed + " applied=" + applied + " (mode=" + HotfixParams.Mode + ")");

                // 计数交给 RepairNotifier，由它在 B2/S 完成后统一提示（时机更晚、更准确）
                int totalFixes = applied + _lastReid + decoupled;
                if (totalFixes > 0 && HotfixParams.IsApply)
                {
                    RepairNotifier.ChainFixes += applied;
                    RepairNotifier.DupIds += _lastReid;
                    RepairNotifier.SharedFlags += decoupled;
                }
                }
            catch (Exception e)
            {
                HotfixLog.Error("[SMS.1.C3] RepairStore(" + origin + "): " + e.Message);
            }
        }

        // ---------- 反射取 store.Ships（泛化：不硬编码 __8__1）----------

        /// <summary>把 Il2CppInterop 的基类包装升级为该 IL2CPP 类型的**具体代理类型**。</summary>
        private static object AsProxy(object wrapper)
        {
            try
            {
                var bo = wrapper as Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase;
                if (bo == null) return null;
                IntPtr ptr = bo.Pointer;

                Type proxy = null;
                foreach (Type t in typeof(GameManager).GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (t.Name.StartsWith("_LoadCampaign_d__", StringComparison.Ordinal)) { proxy = t; break; }
                }
                if (proxy == null) return null;

                ConstructorInfo ctor = proxy.GetConstructor(new Type[] { typeof(IntPtr) });
                if (ctor != null) return ctor.Invoke(new object[] { ptr });
                return Activator.CreateInstance(proxy, new object[] { ptr });
            }
            catch { return null; }
        }

        /// <summary>
        /// 有界广度搜索：从状态机出发，沿**字段与属性**逐层展开（最多 3 层），
        /// 找到第一个"含 List&lt;Ship.Store&gt; Ships 成员"的对象。
        /// 实测真实路径为：__8__1（属性） → store（属性） → Ships。
        /// </summary>
        private static Il2CppSystem.Collections.Generic.List<Ship.Store> FindShips(object sm, out string diag)
        {
            diag = "not found";
            const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

            // 这些是 Il2CppInterop 的包装器噪声，展开它们只会浪费时间和产生环
            var noise = new HashSet<string> { "ObjectClass", "Pointer", "WasCollected",
                                              "isWrapped", "pooledPtr", "Current" };

            try
            {
                var queue = new Queue<KeyValuePair<object, string>>();
                queue.Enqueue(new KeyValuePair<object, string>(sm, "sm"));

                int guard = 0;
                while (queue.Count > 0 && guard++ < 300)
                {
                    KeyValuePair<object, string> kv = queue.Dequeue();
                    object cur = kv.Key;
                    string path = kv.Value;
                    if (cur == null) continue;

                    var list = TryShips(cur);
                    if (list != null) { diag = path; return list; }

                    if (path.Split('.').Length > 3) continue;      // 深度上限

                    Type ct = cur.GetType();

                    foreach (FieldInfo f in ct.GetFields(F))
                    {
                        if (noise.Contains(f.Name)) continue;
                        object v;
                        try { v = f.GetValue(cur); } catch { continue; }
                        if (v == null) continue;
                        queue.Enqueue(new KeyValuePair<object, string>(v, path + "." + f.Name));
                    }

                    foreach (PropertyInfo p in ct.GetProperties(F))
                    {
                        if (noise.Contains(p.Name)) continue;
                        if (p.GetIndexParameters().Length > 0) continue;
                        object v;
                        try { v = p.GetValue(cur); } catch { continue; }
                        if (v == null) continue;
                        Type vt = v.GetType();
                        if (vt.IsPrimitive || vt == typeof(string) || vt.IsEnum) continue;
                        queue.Enqueue(new KeyValuePair<object, string>(v, path + "." + p.Name));
                    }
                }

                var names = new List<string>();
                foreach (PropertyInfo p in sm.GetType().GetProperties(F)) names.Add("P:" + p.Name);
                diag = "no Ships member within depth 3; root members = " + string.Join(",", names);
            }
            catch (Exception e)
            {
                diag = "exception: " + e.Message;
            }
            return null;
        }

        private static Il2CppSystem.Collections.Generic.List<Ship.Store> TryShips(object candidate)
        {
            if (candidate == null) return null;
            try
            {
                const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
                Type ct = candidate.GetType();

                FieldInfo f = ct.GetField("Ships", F);
                if (f != null)
                {
                    var l = f.GetValue(candidate) as Il2CppSystem.Collections.Generic.List<Ship.Store>;
                    if (l != null) return l;
                }
                PropertyInfo p = ct.GetProperty("Ships", F);
                if (p != null && p.CanRead)
                {
                    var l = p.GetValue(candidate) as Il2CppSystem.Collections.Generic.List<Ship.Store>;
                    if (l != null) return l;
                }
            }
            catch { }
            return null;
        }

        // ---------- 匹配工具 ----------

        private static string PartsKey(Ship.Store s)
        {
            try
            {
                if (s.parts == null || s.parts.Count == 0) return null;
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < s.parts.Count; i++)
                {
                    if (i > 0) sb.Append('|');
                    sb.Append(s.parts[i].Id);
                }
                return sb.ToString();
            }
            catch { return null; }
        }

        private static List<Ship.Store> Fingerprint(Ship.Store s, List<Ship.Store> pool)
        {
            var hits = new List<Ship.Store>();
            string hull = Str(s.hullName), type = Str(s.shipType);
            float ton = s.tonnage, beam = s.beam, dr = s.draught, roll = s.instability_xx, pitch = s.instability_zz;

            foreach (Ship.Store d in pool)
            {
                if (Str(d.hullName) != hull || Str(d.shipType) != type) continue;
                if (Math.Abs(d.tonnage - ton) > EPS) continue;
                if (Math.Abs(d.instability_xx - roll) > EPS) continue;
                if (Math.Abs(d.instability_zz - pitch) > EPS) continue;
                if (Math.Abs(d.beam - beam) > EPS) continue;
                if (Math.Abs(d.draught - dr) > EPS) continue;
                hits.Add(d);
            }

            if (hits.Count == 0)
            {
                foreach (Ship.Store d in pool)   // 放宽：改修会改船宽/吃水
                {
                    if (Str(d.hullName) != hull || Str(d.shipType) != type) continue;
                    if (Math.Abs(d.tonnage - ton) > EPS) continue;
                    if (Math.Abs(d.instability_xx - roll) > EPS) continue;
                    if (Math.Abs(d.instability_zz - pitch) > EPS) continue;
                    hits.Add(d);
                }
            }
            return hits;
        }

        /// <summary>
        /// 指纹键。五档 = 标准吨位+船宽+吃水+横摇+纵摇（用户实测：同设计完全一致）；
        /// 宽松档 = 标准吨位+横摇+纵摇（改装会改船宽/吃水）。
        /// </summary>
        private static string FpKey(Ship.Store s, bool five)
        {
            try
            {
                string t = s.tonnage.ToString("0.###");
                string xx = s.instability_xx.ToString("0.###");
                string zz = s.instability_zz.ToString("0.###");
                if (!five) return t + "|" + xx + "|" + zz;
                return t + "|" + s.beam.ToString("0.###") + "|" + s.draught.ToString("0.###") + "|" + xx + "|" + zz;
            }
            catch { return null; }
        }

        /// <summary>
        /// 把一条"船形"store 深拷贝成一份**设计态** store：
        /// MessagePack 往返得到深拷贝（避免共享 List 引用），再清成设计：
        /// 新 Guid / designId=空 / status=None / 清港口与进度 / 去掉共享与赔偿标记。
        /// </summary>
        private static Ship.Store TryCloneDesignStore(Ship.Store src)
        {
            try
            {
                var c = new Ship.Store();
                // ---- 身份：新 Guid、设计槽、惰性 ----
                c.id = Il2CppSystem.Guid.NewGuid();
                c.designId = Il2CppSystem.Guid.Empty;
                try { c.status = VesselEntity.Status.None; } catch { }

                // ---- 设计内容（逐字段复制；列表共享引用，之后不再修改）----
                try { c.vesselName = src.vesselName; } catch { }
                try { c.shipType = src.shipType; } catch { }
                try { c.hullName = src.hullName; } catch { }
                try { c.hullPartSizeZ = src.hullPartSizeZ; } catch { }
                try { c.hullPartSizeY = src.hullPartSizeY; } catch { }
                try { c.hullPartMinZ = src.hullPartMinZ; } catch { }
                try { c.hullPartMaxZ = src.hullPartMaxZ; } catch { }
                try { c.tonnage = src.tonnage; } catch { }
                try { c.beam = src.beam; } catch { }
                try { c.draught = src.draught; } catch { }
                try { c.instability_x = src.instability_x; } catch { }
                try { c.instability_z = src.instability_z; } catch { }
                try { c.instability_xx = src.instability_xx; } catch { }
                try { c.instability_zz = src.instability_zz; } catch { }
                try { c.overweight = src.overweight; } catch { }
                try { c.underweight = src.underweight; } catch { }
                try { c.survivability = src.survivability; } catch { }
                try { c.crewQuarters = src.crewQuarters; } catch { }
                try { c.components = src.components; } catch { }
                try { c.techs = src.techs; } catch { }
                try { c.parts = src.parts; } catch { }
                try { c.armor = src.armor; } catch { }
                try { c.turretArmors = src.turretArmors; } catch { }
                try { c.turretCalibers = src.turretCalibers; } catch { }
                try { c.AmmoTotal = src.AmmoTotal; } catch { }
                try { c.Ammo = src.Ammo; } catch { }
                try { c.CrewSkillDisperion = src.CrewSkillDisperion; } catch { }
                try { c.statWeaponAndHullDefect = src.statWeaponAndHullDefect; } catch { }
                try { c.techLevels = src.techLevels; } catch { }
                try { c.playerName = src.playerName; } catch { }
                try { c.playerOriginal = src.playerOriginal; } catch { }
                try { c.YearCreated = src.YearCreated; } catch { }

                // ---- 清成设计态 ----
                try { c.portLocation = ""; } catch { }
                try { c.location = ""; } catch { }
                try { c.sailingTo = ""; } catch { }
                try { c.prevPortLocation = ""; } catch { }
                try { c.buildingProgress = 0f; } catch { }
                try { c.repairingProgress = 0f; } catch { }
                try { c.isSharedDesign = false; } catch { }
                try { c.isReparationDesign = false; } catch { }
                try { c.refitDesignListID = null; } catch { }
                try { c.refitDesignName = null; } catch { }
                try { c.designRefitTime = 0f; } catch { }

                HotfixLog.Info("[SMS.1.C3] cloned design from ship '" + N(src) + "' -> " + c.id);
                return c;
            }
            catch (Exception e)
            {
                HotfixLog.Warn("[SMS.1.C3] clone design failed: " + e.Message);
                return null;
            }
        }
        private static string N(Ship.Store s) { try { return s.vesselName; } catch { return "<unnamed>"; } }        private static string Str(string x) { return x ?? ""; }
        private static string F(float x) { return x.ToString("0.##"); }
    }
}
