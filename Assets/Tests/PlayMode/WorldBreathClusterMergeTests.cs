// 役割: WorldBreathSystemのクラスター管理（Session 13でTileTypeキーのDictionaryから
//       タイル集合の重なり判定のみのListへ変更）の回帰テスト。
//       ParticleSystemRenderer.materialへの代入を伴うため、EditMode下では
//       マテリアルリークConsole Errorでテスト失敗扱いになる（既存のHexTileElementPropsTests等と
//       同じ理由）。PlayModeで実施する。
//       ForestGrowthEvaluator/HexGridManagerは経由せず、TerrainGrowthEvent<ForestGrowthMetrics>を
//       直接発行してWorldBreathSystem自身のクラスター統合ロジックだけを検証する。

using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using ElfVillage.Core;
using ElfVillage.HexGrid;
using ElfVillage.Tiles;
using UnityEngine;

namespace ElfVillage.Tests
{
    public class WorldBreathClusterMergeTests
    {
        // ── テストヘルパー ────────────────────────────────────────────

        private static HexTile MakeTile(HexCoord coord)
        {
            var go   = new GameObject("Tile_" + coord);
            var tile = go.AddComponent<HexTile>();
            tile.Initialize(coord, 1f);
            return tile;
        }

        private static TileType MakeTileType(string name)
        {
            var t = ScriptableObject.CreateInstance<TileType>();
            t.name = name;
            return t;
        }

        // PlayModeでもAddComponent直後にAwake/OnEnableが自動発火しない場合があるため明示的に呼ぶ。
        // メソッドが見つからない場合はAssert.Failで明確に失敗させる。
        private static void InvokeLifecycle(Component c, string methodName)
        {
            var method = c.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(method, $"{c.GetType().Name}に{methodName}メソッドが見つかりません（リフレクション対象名の変更を確認してください）");
            method.Invoke(c, null);
        }

        private static WorldBreathSystem MakeWorldBreathSystem()
        {
            var go     = new GameObject("TestWorldBreath");
            var system = go.AddComponent<WorldBreathSystem>();
            InvokeLifecycle(system, "Awake");    // _cachedParticleMatを構築
            InvokeLifecycle(system, "OnEnable"); // TerrainGrowthEvent<ForestGrowthMetrics>を購読
            return system;
        }

        private static void Teardown(WorldBreathSystem system)
        {
            InvokeLifecycle(system, "OnDisable");
            Object.Destroy(system.gameObject);
        }

        private static ForestGrowthMetrics MakeMetrics(int clusterSize)
            => new ForestGrowthMetrics(largestClusterSize: clusterSize, totalForestTiles: clusterSize);

        private static void PublishGrowth(TileType terrainType, HexCoord anchor, IReadOnlyList<HexTile> affectedTiles)
        {
            EventBus.Publish(new TerrainGrowthEvent<ForestGrowthMetrics>(
                terrainType:   terrainType,
                anchor:        anchor,
                affectedTiles: affectedTiles,
                metrics:       MakeMetrics(affectedTiles.Count)
            ));
        }

        private static int CountChildrenByPrefix(GameObject root, string prefix)
        {
            int count = 0;
            foreach (Transform child in root.transform)
                if (child.name.StartsWith(prefix))
                    count++;
            return count;
        }

        // ── テスト ────────────────────────────────────────────────────

        [Test]
        public void SingleLegacyCluster_ProducesExactlyOneGentleEffect()
        {
            var system = MakeWorldBreathSystem();
            try
            {
                var legacy = MakeTileType("Legacy");
                var tiles  = new List<HexTile> { MakeTile(HexCoord.Zero), MakeTile(HexCoord.Zero.Neighbor(0)), MakeTile(HexCoord.Zero.Neighbor(1)) };

                PublishGrowth(legacy, HexCoord.Zero, tiles);

                Assert.AreEqual(1, CountChildrenByPrefix(system.gameObject, "ForestGentle"));
            }
            finally
            {
                Teardown(system);
            }
        }

        [Test]
        public void MixedLegacyAndCompositeTileTypes_SamePhysicalCluster_ProducesOnlyOneGentleEffect()
        {
            var system = MakeWorldBreathSystem();
            try
            {
                var legacyForest = MakeTileType("TileType_Forest");
                var forestFlower = MakeTileType("TileType_ForestFlower");

                var tileA = MakeTile(HexCoord.Zero);
                var tileB = MakeTile(HexCoord.Zero.Neighbor(0));
                var tileC = MakeTile(HexCoord.Zero.Neighbor(1));

                // 1枚目: legacyForest起点、クラスターサイズ1（閾値未満、VFXなし）
                PublishGrowth(legacyForest, HexCoord.Zero, new List<HexTile> { tileA });
                Assert.AreEqual(0, CountChildrenByPrefix(system.gameObject, "ForestGentle"));

                // 2枚目: 起点のTileTypeがforestFlower（旧実装ではここで別キーの新規クラスター扱いになり
                // 重複が発生していた）。同じ物理クラスターとして3枚に成長し、閾値(3)を超える。
                PublishGrowth(forestFlower, tileB.Data.coord, new List<HexTile> { tileA, tileB, tileC });

                Assert.AreEqual(1, CountChildrenByPrefix(system.gameObject, "ForestGentle"),
                    "legacyタイルと複合タイルが混在する1つの物理クラスターに対し、Gentleエフェクトは1つだけであるべき");
            }
            finally
            {
                Teardown(system);
            }
        }

        [Test]
        public void TwoPhysicallySeparateClusters_ProduceTwoIndependentGentleEffects()
        {
            var system = MakeWorldBreathSystem();
            try
            {
                var typeA = MakeTileType("A");
                var typeB = MakeTileType("B");

                var clusterA = new List<HexTile> { MakeTile(new HexCoord(0, 0, 0)), MakeTile(new HexCoord(1, 0, -1)), MakeTile(new HexCoord(2, 0, -2)) };
                var clusterB = new List<HexTile> { MakeTile(new HexCoord(20, 0, -20)), MakeTile(new HexCoord(21, 0, -21)), MakeTile(new HexCoord(22, 0, -22)) };

                PublishGrowth(typeA, clusterA[0].Data.coord, clusterA);
                PublishGrowth(typeB, clusterB[0].Data.coord, clusterB);

                Assert.AreEqual(2, CountChildrenByPrefix(system.gameObject, "ForestGentle"),
                    "物理的に離れた2クラスターにはそれぞれ独立したGentleエフェクトが生成されるべき");
            }
            finally
            {
                Teardown(system);
            }
        }

        [Test]
        public void MergingTwoClusters_DoesNotLeaveDuplicateEffect()
        {
            var system = MakeWorldBreathSystem();
            try
            {
                var typeA = MakeTileType("A");
                var typeB = MakeTileType("B");
                var typeC = MakeTileType("C");

                var tileA1 = MakeTile(HexCoord.Zero);
                var tileA2 = MakeTile(HexCoord.Zero.Neighbor(0));
                var tileA3 = MakeTile(HexCoord.Zero.Neighbor(1));

                var bridge  = MakeTile(HexCoord.Zero.Neighbor(2));

                var tileB1 = MakeTile(HexCoord.Zero.Neighbor(2).Neighbor(2));
                var tileB2 = MakeTile(HexCoord.Zero.Neighbor(2).Neighbor(2).Neighbor(2));

                // 独立した2クラスター（片方は3枚で閾値到達、もう片方は2枚のみでまだ未到達）
                PublishGrowth(typeA, tileA1.Data.coord, new List<HexTile> { tileA1, tileA2, tileA3 });
                PublishGrowth(typeB, tileB1.Data.coord, new List<HexTile> { tileB1, tileB2 });
                Assert.AreEqual(1, CountChildrenByPrefix(system.gameObject, "ForestGentle"), "前提: 到達済みクラスターは1つのはず");

                // bridgeタイルが両クラスターを接続し、1つの物理クラスター(6枚)になる
                var merged = new List<HexTile> { tileA1, tileA2, tileA3, bridge, tileB1, tileB2 };
                PublishGrowth(typeC, bridge.Data.coord, merged);

                Assert.AreEqual(1, CountChildrenByPrefix(system.gameObject, "ForestGentle"),
                    "2クラスターが接続して1つになった後も、Gentleエフェクトは1つだけであるべき（重複が残ってはいけない）");
            }
            finally
            {
                Teardown(system);
            }
        }

        [Test]
        public void GentleAndWindTiers_StillActivateIndependentlyWithoutDuplication()
        {
            var system = MakeWorldBreathSystem();
            try
            {
                var typeA = MakeTileType("A");
                var typeB = MakeTileType("B");

                var t0 = MakeTile(new HexCoord(0, 0, 0));
                var t1 = MakeTile(new HexCoord(1, 0, -1));

                // 2枚: 閾値未満なので何も生成されない
                PublishGrowth(typeA, t0.Data.coord, new List<HexTile> { t0, t1 });
                Assert.AreEqual(0, CountChildrenByPrefix(system.gameObject, "ForestGentle"));
                Assert.AreEqual(0, CountChildrenByPrefix(system.gameObject, "ForestWind"));

                // 3枚（別TileType起点）: Gentle閾値(3)到達、Windはまだ
                var t2 = MakeTile(new HexCoord(2, 0, -2));
                PublishGrowth(typeB, t2.Data.coord, new List<HexTile> { t0, t1, t2 });
                Assert.AreEqual(1, CountChildrenByPrefix(system.gameObject, "ForestGentle"));
                Assert.AreEqual(0, CountChildrenByPrefix(system.gameObject, "ForestWind"));

                // 5枚（さらに別TileType起点）: Wind閾値(5)到達。Gentleは重複せず1つのまま
                var t3 = MakeTile(new HexCoord(3, 0, -3));
                var t4 = MakeTile(new HexCoord(4, 0, -4));
                PublishGrowth(typeA, t3.Data.coord, new List<HexTile> { t0, t1, t2, t3, t4 });
                Assert.AreEqual(1, CountChildrenByPrefix(system.gameObject, "ForestGentle"), "Gentleは重複せず1つのままであるべき");
                Assert.AreEqual(1, CountChildrenByPrefix(system.gameObject, "ForestWind"), "Windが新たに1つ生成されるべき");
            }
            finally
            {
                Teardown(system);
            }
        }
    }
}
