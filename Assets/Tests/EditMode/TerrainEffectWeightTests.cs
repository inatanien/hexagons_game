// 役割: Stage 8で導入した「演出用の重み付きカウント」の検証。
//       複合タイルが複数カテゴリで過剰にカウントされないこと、単一属性タイルの挙動が
//       変わらないこと、そしてクエスト進捗（実タイル数）が小数化しないことを確認する。

using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using ElfVillage.Core;
using ElfVillage.HexGrid;
using ElfVillage.Tiles;
using UnityEngine;

namespace ElfVillage.Tests
{
    public class TerrainEffectWeightTests
    {
        private static TerrainVariantDefinition MakeVariant(TileCategory category)
        {
            var v = ScriptableObject.CreateInstance<TerrainVariantDefinition>();
            v.category = category;
            return v;
        }

        /// <summary>areaWeightを明示指定して複合タイルを作る（実アセットの構成を再現するため）。</summary>
        private static TileType MakeTile(params (TileCategory category, float weight, bool visualOnly)[] spec)
        {
            var t = ScriptableObject.CreateInstance<TileType>();
            var elements = new TileElement[spec.Length];
            for (int i = 0; i < spec.Length; i++)
            {
                elements[i] = new TileElement
                {
                    variant    = MakeVariant(spec[i].category),
                    areaWeight = spec[i].weight,
                    visualOnly = spec[i].visualOnly,
                };
            }
            t.elements = elements;
            return t;
        }

        // ── 1. 単一属性タイルは従来と同じ重み（1.0）で扱われる ────────────────

        [Test]
        public void SingleCategoryTile_WeighsOne()
        {
            var forest = MakeTile((TileCategory.Forest, 1f, false));

            Assert.AreEqual(1f, TerrainEffectWeight.Of(forest, TileCategory.Forest), 0.0001f);
            Assert.AreEqual(0f, TerrainEffectWeight.Of(forest, TileCategory.Field), 0.0001f);
        }

        [Test]
        public void LegacyTileWithoutElements_WeighsOne()
        {
            // elements未設定のlegacyタイル（tileCategory文字列のみ）は従来どおり1枚として数える。
            var legacy = ScriptableObject.CreateInstance<TileType>();
            legacy.tileCategory = "Forest";

            Assert.AreEqual(1f, TerrainEffectWeight.Of(legacy, TileCategory.Forest), 0.0001f);
            Assert.AreEqual(0f, TerrainEffectWeight.Of(legacy, TileCategory.Field), 0.0001f);
        }

        // ── 2. 複合タイルが複数属性で過剰にカウントされない（合計1.0） ──────────

        [Test]
        public void CompositeTile_TotalWeightAcrossCategories_IsOne()
        {
            // 実アセット TileType_ForestFlower と同じ構成（Forest 0.7 / Field 0.3、Field側がvisualOnly）
            var forestFlower = MakeTile(
                (TileCategory.Forest, 0.7f, false),
                (TileCategory.Field,  0.3f, true));

            float forest = TerrainEffectWeight.Of(forestFlower, TileCategory.Forest);
            float field  = TerrainEffectWeight.Of(forestFlower, TileCategory.Field);

            Assert.AreEqual(0.7f, forest, 0.0001f);
            Assert.AreEqual(0.3f, field,  0.0001f);
            Assert.AreEqual(1f, forest + field, 0.0001f,
                "複合タイル1枚の寄与は全カテゴリ合計でちょうど1.0であるべき（従来は2.0になっていた）");
        }

        [Test]
        public void CompositeTile_FieldGroveConfiguration_MatchesAuthoredWeights()
        {
            // 実アセット TileType_FieldGrove と同じ構成（Field 0.75 / Forest 0.25、Forest側がvisualOnly）
            var fieldGrove = MakeTile(
                (TileCategory.Field,  0.75f, false),
                (TileCategory.Forest, 0.25f, true));

            Assert.AreEqual(0.75f, TerrainEffectWeight.Of(fieldGrove, TileCategory.Field),  0.0001f);
            Assert.AreEqual(0.25f, TerrainEffectWeight.Of(fieldGrove, TileCategory.Forest), 0.0001f);
        }

        [Test]
        public void ThreeCategoryTile_NormalizesToOne()
        {
            // 3属性以上へ拡張しても合計1.0に正規化されることを確認する。
            var triple = MakeTile(
                (TileCategory.Forest, 1f, false),
                (TileCategory.Field,  1f, false),
                (TileCategory.River,  1f, false));

            float sum = TerrainEffectWeight.Of(triple, TileCategory.Forest)
                      + TerrainEffectWeight.Of(triple, TileCategory.Field)
                      + TerrainEffectWeight.Of(triple, TileCategory.River);

            Assert.AreEqual(1f / 3f, TerrainEffectWeight.Of(triple, TileCategory.Forest), 0.0001f);
            Assert.AreEqual(1f, sum, 0.0001f);
        }

        // ── 7. 境界値・不正データ ──────────────────────────────────────────

        [Test]
        public void NullTile_WeighsZero()
        {
            Assert.AreEqual(0f, TerrainEffectWeight.Of(null, TileCategory.Forest), 0.0001f);
        }

        [Test]
        public void AllZeroAreaWeights_FallsBackToEqualSplit()
        {
            // 不正データ（全areaWeight=0）でも0除算せず、要素数で均等割りする。
            var broken = MakeTile(
                (TileCategory.Forest, 0f, false),
                (TileCategory.Field,  0f, false));

            Assert.AreEqual(0.5f, TerrainEffectWeight.Of(broken, TileCategory.Forest), 0.0001f);
            Assert.AreEqual(0.5f, TerrainEffectWeight.Of(broken, TileCategory.Field),  0.0001f);
        }

        [Test]
        public void SumFor_NullCollection_ReturnsZero()
        {
            Assert.AreEqual(0f, TerrainEffectWeight.SumFor(null, TileCategory.Forest), 0.0001f);
        }

        // ── 3・5. Evaluator経由：演出は重み付き、クエスト進捗は実タイル数 ────────

        private static Dictionary<HexCoord, HexTile> GetGrid(HexGridManager gridManager)
        {
            var field = typeof(HexGridManager).GetField("_grid", BindingFlags.NonPublic | BindingFlags.Instance);
            return (Dictionary<HexCoord, HexTile>)field.GetValue(gridManager);
        }

        private static void InvokeLifecycle(Component c, string methodName)
        {
            var method = c.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(method, $"{c.GetType().Name}に{methodName}メソッドが見つかりません");
            method.Invoke(c, null);
        }

        private static HexTile PlaceTile(Dictionary<HexCoord, HexTile> grid, HexCoord coord, TileType type)
        {
            var go   = new GameObject("TestTile_" + coord);
            var tile = go.AddComponent<HexTile>();
            tile.Initialize(coord, 1f);
            tile.Place(type, 0);
            grid[coord] = tile;
            return tile;
        }

        private static ForestGrowthEvaluator MakeForestEvaluator(HexGridManager gridManager)
        {
            var go        = new GameObject("TestForestEvaluator");
            var evaluator = go.AddComponent<ForestGrowthEvaluator>();
            typeof(ForestGrowthEvaluator)
                .GetField("_gridManager", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(evaluator, gridManager);
            InvokeLifecycle(evaluator, "OnEnable");
            return evaluator;
        }

        [Test]
        public void ForestCluster_CompositeTiles_WeightedIsLowerButQuestCountStaysInteger()
        {
            var gridManagerGo = new GameObject("TestGridManager");
            var gridManager   = gridManagerGo.AddComponent<HexGridManager>();
            var grid          = GetGrid(gridManager);
            var evaluator     = MakeForestEvaluator(gridManager);

            // 森0.7 / 花0.3 の複合タイルを3枚つなげる
            var forestFlower = MakeTile(
                (TileCategory.Forest, 0.7f, false),
                (TileCategory.Field,  0.3f, true));

            TerrainGrowthEvent<ForestGrowthMetrics> lastEvt = null;
            System.Action<TerrainGrowthEvent<ForestGrowthMetrics>> handler = e => lastEvt = e;
            EventBus.Subscribe(handler);
            try
            {
                var center = HexCoord.Zero;
                foreach (var c in new[] { center, center.Neighbor(0), center.Neighbor(1) })
                {
                    var t = PlaceTile(grid, c, forestFlower);
                    EventBus.Publish(new TilePlacedEvent(t, forestFlower, c));
                }
            }
            finally
            {
                EventBus.Unsubscribe(handler);
                InvokeLifecycle(evaluator, "OnDisable");
            }

            Assert.IsNotNull(lastEvt);

            // クエスト進捗用は実タイル数のまま（整数、小数化しない）
            Assert.AreEqual(3, lastEvt.Metrics.LargestClusterSize,
                "クエスト進捗に使うLargestClusterSizeは実タイル数の整数のままであるべき");

            // 演出用は重み付き（0.7×3 = 2.1）→ 葉VFXのしきい値3に届かない
            Assert.AreEqual(2.1f, lastEvt.Metrics.WeightedClusterSize, 0.0001f);
            Assert.Less(lastEvt.Metrics.WeightedClusterSize, 3f,
                "複合タイル3枚では葉VFXのしきい値(3)に到達せず、序盤の過密が抑えられるはず");
        }

        [Test]
        public void ForestCluster_SingleCategoryTiles_WeightedEqualsTileCount()
        {
            // 単一属性タイルのみのクラスターは従来と完全に同じ（重み = 実枚数）。
            var gridManagerGo = new GameObject("TestGridManager");
            var gridManager   = gridManagerGo.AddComponent<HexGridManager>();
            var grid          = GetGrid(gridManager);
            var evaluator     = MakeForestEvaluator(gridManager);

            var forest = MakeTile((TileCategory.Forest, 1f, false));

            TerrainGrowthEvent<ForestGrowthMetrics> lastEvt = null;
            System.Action<TerrainGrowthEvent<ForestGrowthMetrics>> handler = e => lastEvt = e;
            EventBus.Subscribe(handler);
            try
            {
                var center = HexCoord.Zero;
                foreach (var c in new[] { center, center.Neighbor(0), center.Neighbor(1) })
                {
                    var t = PlaceTile(grid, c, forest);
                    EventBus.Publish(new TilePlacedEvent(t, forest, c));
                }
            }
            finally
            {
                EventBus.Unsubscribe(handler);
                InvokeLifecycle(evaluator, "OnDisable");
            }

            Assert.IsNotNull(lastEvt);
            Assert.AreEqual(3, lastEvt.Metrics.LargestClusterSize);
            Assert.AreEqual(3f, lastEvt.Metrics.WeightedClusterSize, 0.0001f,
                "単一属性のみのクラスターは重みも実枚数と一致し、挙動が変わらないはず");
        }

        // ── 5. クエスト進捗が0.5刻みにならない（Relay経由の確認） ─────────────

        [Test]
        public void QuestProgress_StaysIntegerForCompositeTiles()
        {
            var relayGo = new GameObject("TestRelay");
            var relay   = relayGo.AddComponent<TerrainClusterProgressRelay>();
            InvokeLifecycle(relay, "OnEnable");

            TerrainClusterProgressEvent received = null;
            System.Action<TerrainClusterProgressEvent> handler = e => received = e;
            EventBus.Subscribe(handler);
            try
            {
                // 実タイル3枚・重み2.1 のメトリクスを直接流す
                var metrics = new ForestGrowthMetrics(
                    largestClusterSize: 3, totalForestTiles: 3, weightedClusterSize: 2.1f);
                EventBus.Publish(new TerrainGrowthEvent<ForestGrowthMetrics>(
                    terrainType: null, anchor: HexCoord.Zero,
                    affectedTiles: new List<HexTile>(), metrics: metrics));

                Assert.IsNotNull(received);
                Assert.AreEqual(3, received.ClusterSize,
                    "クエスト進捗は重み(2.1)ではなく実タイル数(3)を使うべき。0.5刻みになってはいけない");
            }
            finally
            {
                EventBus.Unsubscribe(handler);
                InvokeLifecycle(relay, "OnDisable");
                Object.DestroyImmediate(relayGo);
            }
        }
    }
}
