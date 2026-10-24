using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2Cpp;

namespace TAFHotfixBatch1.Modules
{
    /// <summary>
    /// Module B2 —— **把"被当成设计"的实存船重新挂回设计**（B 路线，主线程、事后修复）。
    ///
    /// ⚠ **触发时机（重要）**：用户定下"**修档绝不推进回合**"的原则 ——
    ///   推进回合会改变初始状态、还会随机刷出不得不打的战斗。
    ///   因此本模块**主触发点是 `GameManager::OnEnterState`（进入战役/载入完成）**，
    ///   `AdvanceShips` 仅作兜底；两者都在**主线程**，且**幂等一次性**。
    ///   ⇒ 玩家流程：读档 →（修复完成）→ **直接另存新档** → 重读，**全程不需要过回合**。
    ///
    /// 背景（第七/八/九/十轮实测）：
    ///   · 存档 `.Ships` 里约 319 条"船形条目"（有港口/建造/维修状态）却是 `designId = 0`
    ///     ⇒ 被游戏按"设计"载入 ⇒ 存档重建时 `designId` 又变回 0（store 层改了也没用）。
    ///   · 运行时对象上真正表达"这是设计还是船"的是 **`Ship.design`** 引用：
    ///     `isDesign` 只读属性由它推导；**`design` 有 setter** ✓
    ///   · 注册表是可直接遍历的 **`CampaignController.Instance.CampaignData.Vessels`**（`List<VesselEntity>`）。
    ///   · 匹配用 **`Ship.parts[i].Id`（Guid）序列** —— 与 TAF 自己的 store 侧判据一致。
    ///
    /// 做法：
    ///   ① 收集"真正的设计"：`isDesign == true` 且**非船形**（`status ∈ {None,Normal}`）；
    ///   ② 对 `isDesign == true` 但**船形**的对象（即丢了 design 的船），按 parts 序列找设计；
    ///   ③ `s.design = 匹配到的设计`（仅 `mode=apply`）⇒ `isDesign` 变为 false ⇒ 存档带上 designId。
    ///
    /// ⚠ 安全：不碰反序列化、不新建/删除对象；只改一个引用字段；`mode=log` 只打印。
    /// </summary>
    internal static class ShipDesignRelink
    {
        private static bool _done;
        /// <summary>`_done` 对应的战役实例；同一会话里换档（实例变化）时允许重跑一次。</summary>
        private static CampaignController _doneCampaign;
        internal static readonly System.Collections.Generic.List<Ship> Misses = new System.Collections.Generic.List<Ship>();
        internal static bool Finished;
        private static int _attempts;

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            if (!HotfixParams.ShipDesignRelink)
            {
                HotfixLog.Info("[SMS.1.B2] ShipDesignRelink: disabled by config");
                return;
            }

            int ok = 0;

            // ★ 主触发：ChangeState(GameState, bool) —— 必须**显式指定参数类型**，
            //   否则 AccessTools 会命中 `OnEnterState()` 这类没人调用的重载（第 11 轮实证）。
            try
            {
                var change = AccessTools.Method(typeof(GameManager), "ChangeState",
                    new Type[] { typeof(GameManager.GameState), typeof(bool) });
                if (change != null)
                {
                    harmony.Patch(change, postfix: new HarmonyMethod(
                        AccessTools.Method(typeof(ShipDesignRelink), nameof(Postfix_ChangeState))));
                    ok++;
                }
                else HotfixLog.Warn("[SMS.1.B2] GameManager::ChangeState(GameState,bool) not found");
            }
            catch (Exception e) { HotfixLog.Warn("[SMS.1.B2] ChangeState patch failed: " + e.Message); }

            // ★ 2026-10-18：**移除 AdvanceShips 兜底**（用户要求）。
            //   挂在 AdvanceShips 上会让修档逻辑每回合、以及从造船厂返回后反复执行，
            //   从而对"已废弃(status=7)的设计"产生二次误判，凭空造出无港口的"虚空船"。
            //   现在修档只在本存档的加载流程里跑一次，见 Postfix_ChangeState。

            HotfixLog.Info("[SMS.1.B2] ShipDesignRelink: armed on " + ok
                           + " trigger(s) (ChangeState: one run per save load only) -- NO turn advance needed");
        }

        /// <summary>
        /// ★ 2026-10-18：只在本存档的**加载流程**之后执行一次。
        ///   原先"任何非主菜单状态切换都安排一次"会导致：
        ///   · 从造船厂返回 World 时又跑一遍（用户实测：出造船厂后出现虚空船）；
        ///   · 每回合 AdvanceShips 再跑一遍。
        ///   现在：见到 LoadingCustom/Loading 打标记，随后进入 World 时消费该标记并执行；
        ///   造船厂 ←→ World 之类的切换不带标记，因此不会触发。
        /// </summary>
        private static bool _pendingLoad;

        /// <summary>
        /// ★ 2026-10-18：只在本存档的**加载流程**之后执行一次。
        ///   原先"任何非主菜单状态切换都安排一次"会导致：
        ///   · 从造船厂返回 World 时又跑一遍（用户实测：出造船厂后出现虚空船）；
        ///   · 每回合 AdvanceShips 再跑一遍。
        ///   现在：见到 LoadingCustom/Loading 打标记，随后进入 World 时消费该标记并执行；
        ///   造船厂 ←→ World 之类的切换不带标记，因此不会触发。
        /// </summary>
        internal static void Postfix_ChangeState(GameManager.GameState newState)
        {
            try
            {
                if (newState == GameManager.GameState.LoadingCustom || newState == GameManager.GameState.Loading)
                {
                    _pendingLoad = true;
                    HotfixLog.Info("[SMS.1.B2] save load detected (" + newState + ") -> relink armed for this load only");
                    return;
                }

                if (newState != GameManager.GameState.World) return;
                if (!_pendingLoad) return;      // 造船厂返回 / 普通状态切换 ⇒ 不执行

                _pendingLoad = false;
            }
            catch { return; }

            Schedule();
        }

        private static void Schedule()
        {
            // ⚠ 不能在状态切换的当下直接访问 CampaignData：此时可能仍是 LoadingCustom、
            //   战役对象正在卸载/重建（第 13 轮崩溃原因）。
            //   ⇒ 改为**延迟若干帧后**在主线程执行一次；不推进回合、也不产生每帧开销。
            try
            {
                var now = CampaignController.Instance;
                if (_done && !ReferenceEquals(now, _doneCampaign))
                {
                    _done = false;
                    HotfixLog.Info("[SMS.1.B2] new campaign detected -> relink will run again");
                }
            }
            catch { }
            if (_done || _scheduled) return;
            _scheduled = true;
            try
            {
                MelonLoader.MelonCoroutines.Start(DeferredRelink());
                HotfixLog.Info("[SMS.1.B2] deferred relink scheduled (waits for the campaign to finish loading)");
            }
            catch (Exception e)
            {
                _scheduled = false;
                HotfixLog.Warn("[SMS.1.B2] coroutine start failed: " + e.Message);
            }
        }

        private static bool _scheduled;

        /// <summary>
        /// 战役对象是否已存在（与"数据是否就绪"区分开）。
        /// 启动阶段（`ChangeState -> Loading`，还没到主菜单）**没有**战役对象，
        /// 此时必须**无限期等待**（有上限），不能消耗重试配额
        /// —— 第 23 轮 bug：启动时消耗完 10 次配额 ⇒ 真正读档时已放弃。
        /// </summary>
        private static bool CampaignExists()
        {
            try
            {
                var cc = CampaignController.Instance;
                return cc != null && cc.CampaignData != null;
            }
            catch { return false; }
        }

        private static System.Collections.IEnumerator DeferredRelink()
        {
            try
            {
                int waits = 0;          // 每 60 帧 ≈ 1 秒
                int prevCount = -1;
                int stableTicks = 0;

                while (true)
                {
                    for (int i = 0; i < 60; i++) yield return null;
                    waits++;

                    if (_done) yield break;

                    // ① 战役对象不存在 ⇒ 一直等（不设短上限；用户可能几分钟后才读档）
                    if (!CampaignExists())
                    {
                        if (waits % 30 == 0)
                            HotfixLog.Info("[SMS.1.B2] waiting for a campaign (" + waits + "s) — no retry budget consumed");
                        if (waits > 3600)   // 1 小时安全阀
                        {
                            HotfixLog.Warn("[SMS.1.B2] gave up waiting for a campaign after " + waits + "s");
                            yield break;
                        }
                        continue;
                    }

                    // ② 战役存在但数据量还在变化 ⇒ 仍在加载，继续等
                    int count = -1;
                    try { count = CampaignController.Instance.CampaignData.Vessels.Count; } catch { }
                    if (count < 100)
                    {
                        if (waits % 15 == 0)
                            HotfixLog.Info("[SMS.1.B2] campaign object present but data not loaded yet (vessels="
                                           + count + ", " + waits + "s)");
                        if (waits > 3600) { HotfixLog.Warn("[SMS.1.B2] gave up: data never loaded"); yield break; }
                        continue;
                    }
                    if (count != prevCount) { prevCount = count; stableTicks = 0; }

                    // ③ 数据量连续 2 秒不变 ⇒ 认为加载完成，执行
                    stableTicks++;
                    if (stableTicks < 2) continue;

                    bool ok = false;
                    try { ok = TryRelink(); } catch { }
                    if (ok) yield break;

                    // 失败则继续等待（不设次数上限，只有时间安全阀）
                    if (waits > 3600) { HotfixLog.Warn("[SMS.1.B2] gave up after " + waits + "s"); yield break; }
                    if (waits % 15 == 0) HotfixLog.Info("[SMS.1.B2] still not relinked (" + waits + "s), retrying");
                }
            }
            finally
            {
                // ★ 无论成功/放弃都必须复位，否则后续 ChangeState 无法再调度
                //   （第 14 轮 bug：_scheduled 永为 true）。
                _scheduled = false;
            }
        }

        /// <summary>返回 true 表示已成功处理（无论是否实际改动），false 表示数据未就绪需重试。</summary>
        private static bool TryRelink()
        {
            if (_done) return true;

            try
            {
                var cc = CampaignController.Instance;
                if (cc == null) return false;
                var data = cc.CampaignData;
                if (data == null) return false;
                var list = data.Vessels;
                // ⚠ 第 23 轮：不能只判 `Count == 0` —— 读档途中会短暂拿到**陈旧/半加载**的列表，
                //   若此时执行就会在将死的战役对象上动手（第 13 轮崩溃的同类风险），
                //   并且会把 `_done` 置真 ⇒ 真正读档时不再修。
                //   完整战役的 Vessels 恒为数千条，故以 100 为下限。
                if (list == null || list.Count < 100) return false;   // 数据还没就绪

                _attempts++;
                Run(list);
                _done = true;
                _doneCampaign = cc;      // 记住是哪一次战役（同一会话里换档要能重跑）
                return true;
            }
            catch (Exception e)
            {
                HotfixLog.Error("[SMS.1.B2] TryRelink: " + e.Message);
                return false;
            }
        }

        private static void Run(Il2CppSystem.Collections.Generic.List<VesselEntity> list)
        {
                // ---- ① 权威设计 id 集合（来自运行时设计注册表）----
                //   CampaignDesigns.shipsPerYear[*].Value.shipsPerPlayer[*].Value.shipsPerType[*].Value
                //   实测是 List<Ship.Store>（设计的序列化副本），其 id 即"合法设计 id"。
                //   ⇒ 用它区分"真设计"与"丢了设计的船"，不再依赖不可靠的 status
                //     （实测这批数据里多数"设计"也带着船态 status）。
                var designIds = new HashSet<string>();
                int rYears = 0, rPlayers = 0, rTypes = 0, rStores = 0, fromDesignRef = 0;
                try
                {
                    var cd = CampaignController.Instance._currentDesigns;
                    if (cd == null) HotfixLog.Warn("[SMS.1.B2] _currentDesigns is null");
                    else if (cd.shipsPerYear == null) HotfixLog.Warn("[SMS.1.B2] _currentDesigns.shipsPerYear is null");
                    else
                    {
                        rYears = cd.shipsPerYear.Count;
                        // 取法 A：Values
                        try
                        {
                            foreach (var sy in cd.shipsPerYear.Values)
                            {
                                if (sy == null || sy.shipsPerPlayer == null) continue;
                                rPlayers += sy.shipsPerPlayer.Count;
                                foreach (var sp in sy.shipsPerPlayer.Values)
                                {
                                    if (sp == null || sp.shipsPerType == null) continue;
                                    rTypes += sp.shipsPerType.Count;
                                    foreach (var lst in sp.shipsPerType.Values)
                                    {
                                        if (lst == null) continue;
                                        rStores += lst.Count;
                                        foreach (var st in lst) if (st != null) designIds.Add(st.id.ToString());
                                    }
                                }
                            }
                        }
                        catch (Exception ex) { HotfixLog.Warn("[SMS.1.B2] Values traversal failed: " + ex.Message); }

                        // 取法 B：Keys + 索引器（Values 在互操作层可能不可用）
                        if (designIds.Count == 0 && rYears > 0)
                        {
                            try
                            {
                                foreach (var ky in cd.shipsPerYear.Keys)
                                {
                                    var sy = cd.shipsPerYear[ky];
                                    if (sy == null || sy.shipsPerPlayer == null) continue;
                                    rPlayers += sy.shipsPerPlayer.Count;
                                    foreach (var kp in sy.shipsPerPlayer.Keys)
                                    {
                                        var sp = sy.shipsPerPlayer[kp];
                                        if (sp == null || sp.shipsPerType == null) continue;
                                        rTypes += sp.shipsPerType.Count;
                                        foreach (var kt in sp.shipsPerType.Keys)
                                        {
                                            var lst = sp.shipsPerType[kt];
                                            if (lst == null) continue;
                                            rStores += lst.Count;
                                            foreach (var st in lst) if (st != null) designIds.Add(st.id.ToString());
                                        }
                                    }
                                }
                                if (designIds.Count > 0) HotfixLog.Debug("[SMS.1.B2] registry traversal via Keys worked");
                            }
                            catch (Exception ex) { HotfixLog.Warn("[SMS.1.B2] Keys traversal failed: " + ex.Message); }
                        }
                    }
                }
                catch (Exception e) { HotfixLog.Warn("[SMS.1.B2] registry scan failed: " + e.Message); }

                // 取法 C：任何"被别人当作 design 引用"的对象，必然是设计
                try
                {
                    for (int i = 0; i < list.Count; i++)
                    {
                        Ship s = AsShip(list[i]);
                        if (s == null) continue;
                        Ship d = null;
                        try { d = s.design; } catch { }
                        if (d == null) continue;
                        fromDesignRef++;
                        string did = "";
                        try { did = d.id.ToString(); } catch { }
                        if (!string.IsNullOrEmpty(did)) designIds.Add(did);
                    }
                }
                catch (Exception e) { HotfixLog.Warn("[SMS.1.B2] designRef scan failed: " + e.Message); }

                HotfixLog.Debug("[SMS.1.B2] registry: years=" + rYears + " players=" + rPlayers
                               + " types=" + rTypes + " stores=" + rStores
                               + " | designRefs=" + fromDesignRef
                               + " | totalDistinctDesignIds=" + designIds.Count);

                // ---- ② 收集设计对象池 ----
                //   ⚠ 安全约束（第 18/19 轮教训）：**只有"被某艘船引用过"的设计才保证会被写入存档**；
                //   C-③ 映射里的设计 id 只是 store 层条目，GetStore 未必写 ⇒ 曾导致 184/256 条悬空引用
                //   （表现为重读时"存档里没有这条船"，那些船被丢弃）。
                //   ⇒ 白名单 = referenced 集合；不在其中的船走"用自身数据生成设计"的路线。
                var safeDesignIds = new HashSet<string>(designIds);
                HotfixLog.Debug("[SMS.1.B2] safe design ids: " + safeDesignIds.Count
                               + " (referenced=" + designIds.Count + ")");

                var byParts = new Dictionary<string, Ship>();
                var byPartsRank = new Dictionary<string, int>();
                var byTypeCount = new Dictionary<string, List<Ship>>();
                var byIdDesign = new Dictionary<string, Ship>();
                int designs = 0;
                for (int i = 0; i < list.Count; i++)
                {
                    Ship s = AsShip(list[i]);
                    if (s == null) continue;
                    if (!SafeIsDesign(s)) continue;

                    string sid = "";
                    try { sid = s.id.ToString(); } catch { }
                    if (!safeDesignIds.Contains(sid)) continue;      // ★ 只收"安全设计"

                    designs++;
                    if (!byIdDesign.ContainsKey(sid)) byIdDesign[sid] = s;

                    int rank = designIds.Contains(sid) ? 3 : 2;

                    string k = PartsKey(s);
                    if (k != null)
                    {
                        int old;
                        if (!byPartsRank.TryGetValue(k, out old) || rank > old)
                        {
                            byParts[k] = s;
                            byPartsRank[k] = rank;
                        }
                    }

                    string tk = TypeCountKey(s);
                    if (tk != null)
                    {
                        List<Ship> bucket;
                        if (!byTypeCount.TryGetValue(tk, out bucket)) { bucket = new List<Ship>(); byTypeCount[tk] = bucket; }
                        bucket.Add(s);
                    }
                }

                // ---- ③ 找回丢了设计的船：isDesign==true 且**不在**注册表里 ----
                HotfixLog.Debug("[SMS.1.B2] stage1 ok: vessels=" + list.Count + " designs=" + designs
                               + " designKeys=" + byParts.Count + " -- scanning ship-like entries");
                Misses.Clear(); Finished = false;
            try { RepairNotifier.ResetCounters(); SharedBackfillProbe.ResetCounters(); } catch { }
            int shipLike = 0, relinked = 0, miss = 0, fallbackUsed = 0, storeMapUsed = 0, madeDesign = 0;
                for (int i = 0; i < list.Count; i++)
                {
                    if (i % 100 == 0)
                        HotfixLog.Debug("[SMS.1.B2] progress " + i + "/" + list.Count
                                       + " shipLike=" + shipLike + " relinked=" + relinked + " miss=" + miss);
                    Ship s = AsShip(list[i]);
                    if (s == null) continue;
                    if (!SafeIsDesign(s)) continue;       // 已经是正常船

                    string sid = "";
                    try { sid = s.id.ToString(); } catch { }
                if (designIds.Count > 0 && designIds.Contains(sid)) continue;   // real design, skip

                // Hoisted out of the branch: ship-evidence check now always applies.
                if (!IsShipLike(s))
                {
                    HotfixLog.Debug("[SMS.1.B2] skip non-ship (" + DescribeEvidence(s) + ")");
                    continue;
                }

                    // ★ 赔偿舰**设计**绝不能当船重挂（2026-10-18 实锤 bug）：
                    //   赔偿设计（isReparationDesign=true）本身是设计，`designId` 必须全 0；
                    //   一旦给它 `design = 某设计`，存档会把它写成 `designId != 0` 的"船"
                    //   ⇒ 舰船列表里多出一条没有位置的幽灵船。
                    //   它的正确形态是：链里含原始设计 id（由 Module A 的 CloneDesign 负责补）。
                    bool isReparDesign = false;
                    try { isReparDesign = s.isReparationDesign; } catch { }
                    if (isReparDesign)
                    {
                        HotfixLog.Debug("[SMS.1.B2] skip reparation DESIGN '" + Name(s) + "' (never relink a design as ship)");
                        continue;
                    }

                    shipLike++;

                    // ---- 优先：使用 C-③ 在**存档层**算好的映射（字段完整、覆盖未被引用的设计）----
                    Ship d = null;
                    bool viaStoreMap = false;
                    {
                        string mappedId;
                        if (ZeroedShipRepair.TryGetDesignIdFor(sid, out mappedId) && !string.IsNullOrEmpty(mappedId))
                        {
                            Ship dd;
                            if (byIdDesign.TryGetValue(mappedId, out dd) && dd != null && !ReferenceEquals(dd, s))
                            {
                                d = dd;
                                viaStoreMap = true;
                            }
                        }
                    }

                    string k = d == null ? PartsKey(s) : null;
                    if (d == null && k != null) byParts.TryGetValue(k, out d);

                    // ---- 兜底：精确 parts 序列匹配不上时（改装过的船），用 "舰种+部件数" 归组 + 部件重合度 ----
                    bool viaFallback = false;
                    if (d == null || ReferenceEquals(d, s))
                    {
                        d = null;
                        string tk = TypeCountKey(s);
                        List<Ship> bucket;
                        if (tk != null && byTypeCount.TryGetValue(tk, out bucket) && bucket.Count > 0)
                        {
                            Ship best = null;
                            int bestScore = -1, second = -1;
                            var mine = PartIdSet(s);
                            foreach (Ship cand in bucket)
                            {
                                if (cand == null || ReferenceEquals(cand, s)) continue;
                                int sc = Overlap(mine, PartIdSet(cand));
                                if (sc > bestScore) { second = bestScore; bestScore = sc; best = cand; }
                                else if (sc > second) second = sc;
                            }
                            // 要求"最佳显著优于次佳"以免误配
                            if (best != null && bestScore > 0 && bestScore > second)
                            {
                                d = best;
                                viaFallback = true;
                            }
                        }
                    }

                    bool viaMadeDesign = false;
                    // ---- 最后兜底（默认关闭）：以"船自身数据"生成设计 ----
                    //   用户指示：仅在 parts/重叠都失败时使用，且**默认关闭** ——
                    //   否则会出现"存档膨胀 + 生成的设计未被写入 ⇒ 悬空 ⇒ 丢船"。
                    if ((d == null || ReferenceEquals(d, s)) && HotfixParams.ShipDesignRegen)
                    {
                        // 优先用"深拷贝出的设计态 store"；退而用船自身 store。
                        Ship.Store own = null;
                        bool has = ZeroedShipRepair.TryGetCloneStore(sid, out own);
                        if (!has || own == null) has = ZeroedShipRepair.TryGetStoreForShip(sid, out own);
                        if (has && own != null)
                        {
                            Ship made = TryMakeDesignFromStore(own);
                            if (made != null) { d = made; viaMadeDesign = true; }
                        }
                    }

                    if (d == null || ReferenceEquals(d, s))
                    {
                        miss++;
                        try { Misses.Add(s); } catch { }
                        if (miss <= 10)
                            HotfixLog.Warn("[SMS.1.B2][miss] '" + Name(s) + "' parts=" + (k == null ? "<none>" : "ok")
                                           + " -> no design matched");
                        continue;
                    }

                    // ★★ 2026-10-18 最终兜底门（用户要求）：真正写 `s.design` 之前，
                    //   无论从哪条分支走到这里，都必须再验一次"这确实是一艘实存船"。
                    //   实船标志（任一即成立）：
                    //     · status ∈ {2 建造, 3 海上, 4 修理, 10 改造, 11 舾装}
                    //     · portLocation 非空
                    //     · buildingProgress > 0
                    //     · dateFinished.turn > 0     ← 完成建造的船必然有值；设计恒为 0
                    //   全部不成立 ⇒ 是设计（含 status 7 的归档原始设计）⇒ 绝不重挂。
                    if (!HasShipEvidence(s))
                    {
                        HotfixLog.Debug("[SMS.1.B2] skip non-ship '" + Name(s) + "' (no ship evidence: "
                                        + DescribeEvidence(s) + ")");
                        continue;
                    }

                    if (HotfixParams.IsApply)
                    {
                        s.design = d;
                        relinked++;
                        if (viaFallback) fallbackUsed++;
                        if (viaStoreMap) storeMapUsed++;
                        if (viaMadeDesign) madeDesign++;
                        if (relinked <= 10)
                            HotfixLog.Info("[SMS.1.B2] relinked '" + Name(s) + "' -> design '" + Name(d) + "'"
                                           + (viaStoreMap ? "  (store-map)" : (viaFallback ? "  (fallback: type+parts overlap)" : "")));
                    }
                    else if (relinked + miss <= 10)
                    {
                        HotfixLog.Info("[SMS.1.B2][log] would relink '" + Name(s) + "' -> design '" + Name(d) + "'");
                    }
                }

                HotfixLog.Warn("[SMS.1.B2] summary: vessels=" + list.Count + " designs=" + designs
                               + " shipLike=" + shipLike + " relinked=" + relinked
                               + " (storeMap=" + storeMapUsed + ", fallback=" + fallbackUsed + ", madeDesign=" + madeDesign + ") miss=" + miss
                               + "  (mode=" + HotfixParams.Mode + ", attempt=" + _attempts + ")");
            Finished = true;
            try { TrueMixed.PrewarmIfNeeded(); } catch (Exception pe) { HotfixLog.Warn("[SMS.1.G] prewarm hook: " + pe.Message); }
            try { if (HotfixParams.SharedBackfillAggressive) SharedBackfillProbe.RunNow(); } catch { }
            try { RepairNotifier.NotifyOnLoadComplete(); } catch { }
        }

        // ---------- helpers ----------
        private static Ship AsShip(VesselEntity v)
        {
            if (v == null) return null;
            try { return v.TryCast<Ship>(); } catch { return null; }
        }

        private static bool SafeIsDesign(Ship s)
        {
            try { return s.isDesign; } catch { return false; }
        }

        private static bool IsShipLike(Ship s)
        {
            try
            {
                // ===== 2026-10-18 修订：只对"有船特征的条目"做重挂 =====
                // 事故：`宵月/六連/内蒙古/海鹄` 等**改造设计**被当成"丢了 designId 的实船"
                //   重挂 ⇒ 存档里被写成 `designId != 0` 的幽灵船（列表里无港口）。
                // 存档实测（753 条）：
                //   · 真实船：status ∈ {2 建造, 3 海上, 4 修理, 10 改造, 11 舾装}，
                //             或 status == 1 且 **portLocation 非空**（在港船，81 条）
                //   · 设计  ：status ∈ {0 None, 7 Erased}，或 status == 1 且 **无港口**
                //             （赔偿/改造设计的默认状态就是 1，正是被误判的那批）
                int st = (int)s.status;

                // ★ 2026-10-18（用户更正 2）：**去掉 repairingProgress / refitProgress 两个判据**。
                //   理由：设计对象上这两个值并不可靠（实测设计也带 repairingProgress=100），
                //   曾把废弃设计误判成"丢 designId 的船"⇒ 狸猫换太子式地变成虚空船。
                //   现在只认"船必有"的两项：港口归属、建造进度中的 >0；
                //   再叠加"只在海上/建造/修理/改造/舾装才可能没有港口"的状态白名单。
                //   TODO（待 API 确认）：dateFinished.turn != 0、refitDesignName 非空 亦可作为佐证。
                if (st == 2 || st == 3 || st == 4 || st == 10 || st == 11) return true;

                try
                {
                    var store = s.ToStore();
                    if (store != null)
                    {
                        string port = null;
                        float buildP = 0f;
                        try { port = store.portLocation; } catch { }
                        try { buildP = store.buildingProgress; } catch { }

                        if (!string.IsNullOrEmpty(port)) return true;
                        if (buildP > 0f) return true;
                    }
                }
                catch { }

                // 无任何实存船特征 ⇒ 一律视为设计（含 status 0/1/7），绝不重挂
                return false;
            }
            catch { return false; }
        }

        /// <summary>
        /// **实存船**的硬性证据（任一成立即为船）。设计一律不满足：
        /// 状态白名单 / 港口归属 / 建造进度 / 完成建造的回合（dateFinished.turn）。
        /// </summary>
        private static bool HasShipEvidence(Ship s)
        {
            try
            {
                int st = (int)s.status;
                if (st == 2 || st == 3 || st == 4 || st == 10 || st == 11) return true;

                var store = s.ToStore();
                if (store == null) return false;

                string port = null;
                float buildP = 0f;
                try { port = store.portLocation; } catch { }
                try { buildP = store.buildingProgress; } catch { }
                if (!string.IsNullOrEmpty(port)) return true;
                if (buildP > 0f) return true;

                try
                {
                    var df = store.dateFinished;
                    if (df.turn > 0) return true;
                }
                catch { }

                return false;
            }
            catch { return false; }
        }

        /// <summary>诊断用：输出当前条目的证据取值。</summary>
        private static string DescribeEvidence(Ship s)
        {
            try
            {
                string port = "?";
                float buildP = -1f;
                int turn = -1;
                try { var st = s.ToStore(); if (st != null) { port = st.portLocation; buildP = st.buildingProgress; turn = st.dateFinished.turn; } } catch { }
                return "st=" + (int)s.status + " port='" + (port ?? "") + "' buildP=" + buildP + " finishedTurn=" + turn;
            }
            catch { return "<unavailable>"; }
        }

        /// <summary>
        /// 以"船自身的数据"生成一份设计（用户方案）：船与设计同构 ⇒
        /// `Ship.Create` 造一个空设计对象，再用该船的 store 灌进去即可；
        /// 随后加入 `Data.Vessels`，使其在存档中被写成一条真正的设计。
        /// </summary>
        private static Ship TryMakeDesignFromStore(Ship.Store st)
        {
            try
            {
                string pname = null;
                try { pname = st.playerName; } catch { }
                Player p = null;
                try { if (!string.IsNullOrEmpty(pname)) p = LoadSave.GetPlayer(pname, false); } catch { }
                if (p == null) { HotfixLog.Warn("[SMS.1.B2] make design: player not resolved for '" + pname + "'"); return null; }

                Ship d = Ship.Create(null, p, false, false, false);
                if (d == null) { HotfixLog.Warn("[SMS.1.B2] make design: Ship.Create returned null"); return null; }

                var mid = new Il2CppSystem.Nullable<Il2CppSystem.Guid>(Il2CppSystem.Guid.NewGuid());
                bool ok = d.FromStore(st, mid, null, p, false);
                if (!ok) { HotfixLog.Warn("[SMS.1.B2] make design: FromStore failed"); return null; }

                try { d.SetStatus(VesselEntity.Status.None); } catch { }
                try { CampaignController.Instance.CampaignData.Vessels.Add(d); } catch { }
                return d;
            }
            catch (Exception e)
            {
                HotfixLog.Warn("[SMS.1.B2] make design failed: " + e.Message);
                return null;
            }
        }

        /// <summary>"舰种 + 部件数" 归组键；用于改装过的船（部件序列已变）。</summary>
        private static string TypeCountKey(Ship s)
        {
            try
            {
                var parts = s.parts;
                if (parts == null || parts.Count == 0) return null;
                string t = "?";
                try { t = s.shipType != null ? s.shipType.name : "?"; } catch { }
                return t + "|" + parts.Count;
            }
            catch { return null; }
        }

        /// <summary>该船/设计的部件 Id 集合（用于重合度比较）。</summary>
        private static HashSet<string> PartIdSet(Ship s)
        {
            var set = new HashSet<string>();
            try
            {
                var parts = s.parts;
                if (parts == null) return set;
                for (int i = 0; i < parts.Count; i++) set.Add(parts[i].Id.ToString());
            }
            catch { }
            return set;
        }

        private static int Overlap(HashSet<string> a, HashSet<string> b)
        {
            if (a == null || b == null || a.Count == 0) return 0;
            int n = 0;
            foreach (string x in a) if (b.Contains(x)) n++;
            return n;
        }

        private static string PartsKey(Ship s)
        {
            try
            {
                var parts = s.parts;
                if (parts == null || parts.Count == 0) return null;
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < parts.Count; i++)
                {
                    if (i > 0) sb.Append('|');
                    sb.Append(parts[i].Id.ToString());
                }
                return sb.ToString();
            }
            catch { return null; }
        }

        private static string Name(Ship s)
        {
            try { return s.vesselName; } catch { }
            try { return s.name; } catch { }
            return "<unnamed>";
        }
    }
}
