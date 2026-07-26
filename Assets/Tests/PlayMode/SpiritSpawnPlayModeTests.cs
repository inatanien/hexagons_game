// 役割: Stage 15「本編での生成条件とライフサイクル」を実ライフサイクルで検証する。
//       ★Phase1_v002.unity は開かない
//         本編Sceneを自動テストで開くと、テスト後にScene差分が残る危険があり、
//         UI・Audio・Questなど本題以外の理由でも壊れる。
//         代わりにPhase1相当の最小Hierarchy（森タイル＋Systems/Spirits相当）を
//         テスト内で構築し、本物のPhase1_v002は手動確認に使う。

using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ElfVillage.Core;
using ElfVillage.HexGrid;
using ElfVillage.Spirits;
using ElfVillage.Tiles;

namespace ElfVillage.Tests
{
    public class SpiritSpawnPlayModeTests
    {
        private const BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;

        private readonly List<GameObject> _spawned = new();

        private static void ClearEventBus()
        {
            var f = typeof(EventBus).GetField("_handlers", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(f, "EventBus._handlers が見つかりません");
            ((System.Collections.IDictionary)f.GetValue(null)).Clear();
        }

        private static int SubscriberCount<T>()
        {
            var f = typeof(EventBus).GetField("_handlers", BindingFlags.NonPublic | BindingFlags.Static);
            var dict = (System.Collections.IDictionary)f.GetValue(null);
            if (!dict.Contains(typeof(T))) return 0;
            return ((System.Delegate)dict[typeof(T)]).GetInvocationList().Length;
        }

        [SetUp]
        public void SetUp()
        {
            ClearEventBus();
            GameInteractionStateController.SetState(GameInteractionState.Playing);
        }

        [TearDown]
        public void TearDown()
        {
            GameInteractionStateController.SetState(GameInteractionState.Playing);
            foreach (var go in _spawned) if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
            ClearEventBus();
        }

        // ── Phase1相当の最小Hierarchy ─────────────────────────────────

        private GameObject Track(GameObject go) { _spawned.Add(go); return go; }

        /// <summary>指定枚数の連結した森クラスタを作る（本編のBFS結果に相当する集合）。</summary>
        private List<HexTile> MakeForest(string name, Vector3 origin, int count)
        {
            var root = Track(new GameObject(name));
            var tiles = new List<HexTile>();

            for (int i = 0; i < count; i++)
            {
                var go = new GameObject(name + "_Tile" + i);
                go.transform.SetParent(root.transform, true);
                var tile = go.AddComponent<HexTile>();
                tile.Initialize(new HexCoord(i, -i), 1f);
                go.transform.position = origin + new Vector3(i * 1.5f, 0f, (i % 2) * 0.866f);
                tiles.Add(tile);
            }
            return tiles;
        }

        /// <summary>本編と同じ構成（Spirits GameObject 1つに2 Component）を作る。</summary>
        private ForestSpiritSpawner MakeSpiritsSystem(int minClusterSize, bool relayFirst,
                                                       out SpiritStimulusRelay relay)
        {
            var go = Track(new GameObject("Spirits"));
            ForestSpiritSpawner spawner;

            if (relayFirst)
            {
                relay   = go.AddComponent<SpiritStimulusRelay>();
                spawner = go.AddComponent<ForestSpiritSpawner>();
            }
            else
            {
                spawner = go.AddComponent<ForestSpiritSpawner>();
                relay   = go.AddComponent<SpiritStimulusRelay>();
            }

            typeof(ForestSpiritSpawner).GetField("_minClusterSizeToSpawn", Priv)
                .SetValue(spawner, minClusterSize);
            return spawner;
        }

        private ForestSpiritSpawner MakeSpiritsSystem(int minClusterSize = 4)
            => MakeSpiritsSystem(minClusterSize, relayFirst: false, out _);

        private static void PublishForestGrowth(IReadOnlyList<HexTile> tiles)
            => EventBus.Publish(new TerrainGrowthEvent<ForestGrowthMetrics>(
                   null, HexCoord.Zero, tiles,
                   new ForestGrowthMetrics(tiles.Count, tiles.Count)));

        private static ForestSpirit SpiritOf(ForestSpiritSpawner s) => s.GetComponentInChildren<ForestSpirit>(true);
        private static object GetField(object t, string n) => t.GetType().GetField(n, Priv).GetValue(t);

        /// <summary>個体時計はproductionで公開する用途が無いためprivateのまま観測する。</summary>
        private static float SimulationTimeOf(ForestSpirit s) => (float)GetField(s, "_simulationTime");

        private static float FamiliarityOf(ForestSpirit s, SpiritStimulusKind kind)
        {
            var memory   = GetField(s, "_memory");
            var halfLife = (float)GetField(s, "_familiarityHalfLife");
            return (float)memory.GetType().GetMethod("GetFamiliarity")
                .Invoke(memory, new object[] { kind, SimulationTimeOf(s), halfLife });
        }

        private static float ExperienceOf(ForestSpirit s)
            => ((SpiritMemory)GetField(s, "_memory")).GetLifetimeExperience();

        private static IEnumerator WaitUntil(System.Func<bool> cond, float timeout, string msg)
        {
            float deadline = Time.realtimeSinceStartup + timeout;
            while (!cond())
            {
                Assert.Less(Time.realtimeSinceStartup, deadline, msg);
                yield return null;
            }
        }

        // ══ 生成条件 ════════════════════════════════════════════════════

        [UnityTest]
        public IEnumerator BelowMinimumCluster_DoesNotSpawn()
        {
            var spawner = MakeSpiritsSystem(minClusterSize: 4);
            yield return null;

            // 1枚 → 2枚 → 3枚と育てても、まだ誰も住み着かない。
            for (int size = 1; size <= 3; size++)
            {
                var forest = MakeForest("F" + size, new Vector3(size * 20f, 0f, 0f), size);
                PublishForestGrowth(forest);
                yield return null;

                Assert.IsNull(SpiritOf(spawner), $"クラスタ{size}枚で精霊が生成された");
            }
        }

        [UnityTest]
        public IEnumerator ReachingMinimumCluster_SpawnsExactlyOne()
        {
            var spawner = MakeSpiritsSystem(minClusterSize: 4);
            var forest  = MakeForest("Forest", Vector3.zero, 4);
            yield return null;

            PublishForestGrowth(forest);
            yield return null;

            var spirits = spawner.GetComponentsInChildren<ForestSpirit>(true);
            Assert.AreEqual(1, spirits.Length, "4枚到達でちょうど1体生成されるべき");
            Assert.AreEqual(SpiritGrowthStage.Sprout, spirits[0].GrowthStage);
        }

        [UnityTest]
        public IEnumerator SubsequentGrowth_DoesNotSpawnASecondSpirit()
        {
            var spawner = MakeSpiritsSystem(minClusterSize: 4);
            var forest  = MakeForest("Forest", Vector3.zero, 4);
            yield return null;

            PublishForestGrowth(forest);
            yield return null;

            // 同じ森が育ち続けても2体目は生まれない。
            for (int extra = 0; extra < 4; extra++)
            {
                var grown = new List<HexTile>(forest);
                grown.AddRange(MakeForest("Ext" + extra, new Vector3(-3f - extra * 1.5f, 0f, 0f), 2));
                PublishForestGrowth(grown);
                yield return null;

                Assert.AreEqual(1, spawner.GetComponentsInChildren<ForestSpirit>(true).Length,
                    $"{extra + 1}回目の成長で2体目が生成された");
            }
        }

        [UnityTest]
        public IEnumerator SmallClusterElsewhere_DoesNotSpawn_EvenWhenMetricsAreLarge()
        {
            // ★世界最大クラスタで判定していると、ここで1枚の森に精霊が生まれてしまう。
            var spawner = MakeSpiritsSystem(minClusterSize: 4);
            yield return null;

            var smallCluster = MakeForest("Small", new Vector3(50f, 0f, 50f), 1);

            EventBus.Publish(new TerrainGrowthEvent<ForestGrowthMetrics>(
                null, HexCoord.Zero, smallCluster,
                new ForestGrowthMetrics(largestClusterSize: 99, totalForestTiles: 99)));
            yield return null;

            Assert.IsNull(SpiritOf(spawner),
                "Metricsの数値が大きいだけで、1枚のクラスタへ精霊が生成されてしまった");
        }

        // ══ 生成時刺激と購読順非依存 ═════════════════════════════════════

        [UnityTest]
        public IEnumerator SpawnStimulus_IsRecordedExactlyOnce_RegardlessOfSubscriptionOrder()
        {
            var personalities = new List<SpiritPersonalityKind>();
            var experiences   = new List<float>();
            var familiarities = new List<float>();

            foreach (var relayFirst in new[] { false, true })
            {
                var spawner = MakeSpiritsSystem(4, relayFirst, out _);
                var forest  = MakeForest("Forest", new Vector3(3.5f, 0f, -2.5f), 4);
                yield return null;

                Assert.AreEqual(2, SubscriberCount<TerrainGrowthEvent<ForestGrowthMetrics>>(),
                    $"relayFirst={relayFirst}: SpawnerとRelayの両方が購読していない");

                PublishForestGrowth(forest);
                yield return null;

                var spirit = SpiritOf(spawner);
                Assert.IsNotNull(spirit, $"relayFirst={relayFirst}: 生成されなかった");

                personalities.Add(spirit.Personality);
                experiences.Add(ExperienceOf(spirit));
                familiarities.Add(FamiliarityOf(spirit, SpiritStimulusKind.ForestGrew));

                Assert.AreEqual(SpiritState.React, spirit.CurrentState,
                    $"relayFirst={relayFirst}: 生成時刺激でReactへ入っていない（0回になっている）");

                TearDown();
                SetUp();
            }

            Assert.AreEqual(personalities[0], personalities[1], "購読順で性格が変わった");

            // 正確に1回：0回でも2回でもない。
            Assert.AreEqual(1f, experiences[0], 0.0001f, "Spawner先: 累積体験が正確に1でない");
            Assert.AreEqual(1f, experiences[1], 0.0001f, "Relay先: 累積体験が正確に1でない");

            float expectedGain = SpiritPersonalityProfile.For(personalities[0]).FamiliarityGain;
            Assert.AreEqual(expectedGain, familiarities[0], 0.01f, "Spawner先: Familiarityが二重強化された");
            Assert.AreEqual(expectedGain, familiarities[1], 0.01f, "Relay先: Familiarityが二重強化された");
        }

        // ══ 入力Raycastとの非干渉 ════════════════════════════════════════

        private IEnumerator SpawnAndSettle(System.Action<ForestSpirit> onReady)
        {
            var spawner = MakeSpiritsSystem(4);
            var forest  = MakeForest("Forest", Vector3.zero, 4);
            yield return null;

            PublishForestGrowth(forest);
            yield return null;

            var spirit = SpiritOf(spawner);
            Assert.IsNotNull(spirit);

            yield return WaitUntil(() => spirit.CurrentState == SpiritState.Idle, 10f, "Idleへ戻らなかった");
            onReady(spirit);
        }

        [UnityTest]
        public IEnumerator Spirit_HasNoCollidersAnywhere_InEveryState()
        {
            ForestSpirit spirit = null;
            yield return SpawnAndSettle(s => spirit = s);

            void AssertNoColliders(string label)
            {
                var colliders = spirit.GetComponentsInChildren<Collider>(true);
                Assert.AreEqual(0, colliders.Length,
                    $"{label}: 精霊にColliderが{colliders.Length}個残っている" +
                    "（HexGridManagerのRaycastはLayerMaskなしで打ち切るため、タイル操作を妨げる）");
            }

            AssertNoColliders("生成直後");

            // 成長後も増えないこと。
            typeof(ForestSpirit).GetMethod("ApplyGrowthVisual", Priv)
                .Invoke(spirit, new object[] { SpiritGrowthStage.Bloom });
            AssertNoColliders("Bloom適用後");

            foreach (var state in new[] { SpiritState.React, SpiritState.Stretch,
                                          SpiritState.Sleep, SpiritState.Wander, SpiritState.ObserveTree })
            {
                typeof(ForestSpirit).GetMethod("EnterState", Priv).Invoke(spirit, new object[] { state });
                yield return null;
                AssertNoColliders(state.ToString());
            }
        }

        [UnityTest]
        public IEnumerator TileRaycast_ReachesTile_EvenWithSpiritInFront()
        {
            // 本編のHexGridManager.RaycastTile相当（LayerMaskなし・GetComponentInParentで打ち切り）を再現する。
            var tileGo = Track(new GameObject("RaycastTargetTile"));
            var tile   = tileGo.AddComponent<HexTile>();
            tile.Initialize(HexCoord.Zero, 1f);
            tileGo.transform.position = Vector3.zero;

            var box = tileGo.AddComponent<BoxCollider>();
            box.size = new Vector3(2f, 0.2f, 2f);

            ForestSpirit spirit = null;
            yield return SpawnAndSettle(s => spirit = s);

            // 精霊をタイルとカメラの間へ割り込ませる。
            spirit.transform.position = new Vector3(0f, 1f, 0f);
            yield return null;

            var ray = new Ray(new Vector3(0f, 5f, 0f), Vector3.down);
            Assert.IsTrue(Physics.Raycast(ray, out RaycastHit hit), "Raycastが何にも当たらなかった");

            var found = hit.collider.GetComponentInParent<HexTile>();
            Assert.IsNotNull(found,
                "精霊がRaycastを遮ってタイルへ到達できなかった（Colliderが残っている可能性）");
            Assert.AreSame(tile, found);
        }

        // ══ EventBusのライフサイクル ═════════════════════════════════════

        [UnityTest]
        public IEnumerator DestroyingSpiritsSystem_LeavesNoSubscriptions()
        {
            Assert.AreEqual(0, SubscriberCount<TerrainGrowthEvent<ForestGrowthMetrics>>(), "開始時に購読が残っている");

            var spawner = MakeSpiritsSystem(4);
            var forest  = MakeForest("Forest", Vector3.zero, 4);
            yield return null;

            PublishForestGrowth(forest);
            yield return null;

            Assert.IsNotNull(SpiritOf(spawner));
            Assert.AreEqual(1, SubscriberCount<SpiritStimulusEvent>(), "精霊が刺激を購読していない");
            Assert.AreEqual(2, SubscriberCount<TerrainGrowthEvent<ForestGrowthMetrics>>(),
                "SpawnerとRelayが購読していない");

            // ★Systems/Spirits ごと破棄する＝本編でSceneを閉じたときと同じ経路。
            //   テスト側でEventBusをリセットせず、productionのOnDisableだけで0になることを見る。
            Object.DestroyImmediate(spawner.gameObject);
            yield return null;

            Assert.AreEqual(0, SubscriberCount<TerrainGrowthEvent<ForestGrowthMetrics>>(),
                "破棄後もSpawner/Relayの購読が残っている");
            Assert.AreEqual(0, SubscriberCount<SpiritStimulusEvent>(),
                "破棄後も精霊の購読が残っている");
        }

        [UnityTest]
        public IEnumerator RebuildingSpiritsSystem_DoesNotDoubleSubscribe()
        {
            for (int cycle = 0; cycle < 3; cycle++)
            {
                var spawner = MakeSpiritsSystem(4);
                var forest  = MakeForest("Forest" + cycle, Vector3.zero, 4);
                yield return null;

                Assert.AreEqual(2, SubscriberCount<TerrainGrowthEvent<ForestGrowthMetrics>>(),
                    $"{cycle + 1}周目: 購読が二重化している");

                PublishForestGrowth(forest);
                yield return null;

                var spirits = spawner.GetComponentsInChildren<ForestSpirit>(true);
                Assert.AreEqual(1, spirits.Length, $"{cycle + 1}周目: 1体だけ生成されていない");
                Assert.AreEqual(1, SubscriberCount<SpiritStimulusEvent>(),
                    $"{cycle + 1}周目: 刺激の購読が二重化している");
                Assert.AreEqual(1f, ExperienceOf(spirits[0]), 0.0001f,
                    $"{cycle + 1}周目: 生成時刺激が1回でない（購読の残留で多重受信している）");

                Object.DestroyImmediate(spawner.gameObject);
                foreach (var t in forest) if (t != null) Object.DestroyImmediate(t.gameObject);
                yield return null;

                Assert.AreEqual(0, SubscriberCount<TerrainGrowthEvent<ForestGrowthMetrics>>(),
                    $"{cycle + 1}周目: 破棄後も購読が残っている");
            }
        }

        // ══ 生成後も通常どおり刺激へ反応する ═════════════════════════════

        [UnityTest]
        public IEnumerator SpawnedSpirit_StillRespondsToForestAndFlowerStimuli()
        {
            ForestSpirit spirit = null;
            yield return SpawnAndSettle(s => spirit = s);

            Assert.AreEqual(1f, ExperienceOf(spirit), 0.0001f);

            // 花へ反応する
            EventBus.Publish(new SpiritStimulusEvent(new SpiritStimulus(
                SpiritStimulusKind.FlowerBloomed, spirit.transform.position + new Vector3(0.5f, 0f, 0f), null)));
            yield return null;

            Assert.AreEqual(SpiritState.React, spirit.CurrentState, "花へ反応しなかった");
            Assert.AreEqual(2f, ExperienceOf(spirit), 0.0001f, "花の受理で累積体験が増えていない");

            // 遠方の花は無視する（Stage 11の保証）
            yield return WaitUntil(() => spirit.CurrentState == SpiritState.Idle, 10f, "Idleへ戻らなかった");
            EventBus.Publish(new SpiritStimulusEvent(new SpiritStimulus(
                SpiritStimulusKind.FlowerBloomed, spirit.transform.position + new Vector3(60f, 0f, 60f), null)));
            yield return null;

            Assert.AreEqual(2f, ExperienceOf(spirit), 0.0001f, "遠方の花で累積体験が増えた");
        }
    }
}
