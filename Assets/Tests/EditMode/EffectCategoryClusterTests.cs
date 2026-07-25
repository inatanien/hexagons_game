// 役割: TileType.HasEffectCategory（Session 12、visualOnly要素も含むエフェクト用カテゴリ判定）と、
//       それを使うForestGrowthEvaluator/FlowerClusterEvaluatorのクラスター判定を検証する。
//       legacy単一タイルと複合タイル（TileType.elements、Flower要素がvisualOnlyのケース含む）が
//       同じ成長クラスターとして正しく繋がること、既存の単一カテゴリのみのクラスター挙動が
//       変わらないことを確認する。
//       注意: EditModeのAddComponent直後はOnEnable/OnDisableが自動発火しないため、
//       リフレクションで明示的に呼び出す（EventBus購読の残留を防ぐためテスト末尾でOnDisableも呼ぶ）。

using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using ElfVillage.Core;
using ElfVillage.HexGrid;
using ElfVillage.Tiles;
using UnityEngine;

namespace ElfVillage.Tests
{
    public class EffectCategoryClusterTests
    {
        // ── TileType.HasCategory / HasEffectCategory ────────────────────────

        private static TerrainVariantDefinition MakeVariant(TileCategory category)
        {
            var v = ScriptableObject.CreateInstance<TerrainVariantDefinition>();
            v.category = category;
            return v;
        }

        // クラスターBFS自体はedges[]を見ないが、TileType.OnValidateが「elementsにあるが
        // edgesにないカテゴリ」を警告するため、テスト用タイルでも非visualOnly要素のカテゴリに
        // 対応するEdgeTypeで6辺を埋めておく（警告ノイズを避けるだけで、テストの意図には無関係）。
        private static readonly Dictionary<TileCategory, EdgeType> s_categoryToEdge = new()
        {
            { TileCategory.Forest, EdgeType.Forest },
            { TileCategory.Field,  EdgeType.Field  },
            { TileCategory.River,  EdgeType.River  },
        };

        private static TileType MakeCompositeTile(params (TileCategory category, bool visualOnly)[] spec)
        {
            var t = ScriptableObject.CreateInstance<TileType>();
            var elements = new TileElement[spec.Length];
            for (int i = 0; i < spec.Length; i++)
            {
                elements[i] = new TileElement
                {
                    variant    = MakeVariant(spec[i].category),
                    areaWeight = 1f / spec.Length,
                    visualOnly = spec[i].visualOnly,
                };
            }
            t.elements = elements;

            foreach (var (category, visualOnly) in spec)
            {
                if (visualOnly) continue;
                if (!s_categoryToEdge.TryGetValue(category, out var edgeType)) continue;
                for (int d = 0; d < 6; d++)
                    t.edges[d] = edgeType;
            }

            return t;
        }

        [Test]
        public void HasCategory_ExcludesVisualOnlyElement()
        {
            var tile = MakeCompositeTile((TileCategory.Forest, false), (TileCategory.Field, true));

            Assert.IsTrue(tile.HasCategory(TileCategory.Forest));
            Assert.IsFalse(tile.HasCategory(TileCategory.Field), "HasCategoryはvisualOnly要素のカテゴリを含めないはず（ゲームプレイ判定の既存挙動）");
        }

        [Test]
        public void HasEffectCategory_IncludesVisualOnlyElement()
        {
            var tile = MakeCompositeTile((TileCategory.Forest, false), (TileCategory.Field, true));

            Assert.IsTrue(tile.HasEffectCategory(TileCategory.Forest));
            Assert.IsTrue(tile.HasEffectCategory(TileCategory.Field), "HasEffectCategoryはvisualOnly要素のカテゴリも含めるはず");
        }

        [Test]
        public void HasEffectCategory_UnrelatedCategory_ReturnsFalse()
        {
            var tile = MakeCompositeTile((TileCategory.Forest, false));
            Assert.IsFalse(tile.HasEffectCategory(TileCategory.River));
        }

        // ── ForestGrowthEvaluator / FlowerClusterEvaluator のクラスター統合 ──────

        private static Dictionary<HexCoord, HexTile> GetGrid(HexGridManager gridManager)
        {
            var field = typeof(HexGridManager).GetField("_grid", BindingFlags.NonPublic | BindingFlags.Instance);
            return (Dictionary<HexCoord, HexTile>)field.GetValue(gridManager);
        }

        private static HexGridManager MakeGridManager()
        {
            var go = new GameObject("TestGridManager");
            return go.AddComponent<HexGridManager>();
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

        // EditModeではAddComponent直後にOnEnable/OnDisableが自動発火しないため、明示的に呼び出す。
        // メソッドが見つからない場合はAssert.Failで明確に失敗させる（対象クラスのリファクタリング等で
        // メソッド名が変わった際に、「イベントが飛ばない」という分かりにくい失敗ではなく、
        // ここで直接原因が分かるようにするため）。
        private static void InvokeLifecycle(Component c, string methodName)
        {
            var method = c.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(method, $"{c.GetType().Name}に{methodName}メソッドが見つかりません（リフレクション対象名の変更を確認してください）");
            method.Invoke(c, null);
        }

        private static ForestGrowthEvaluator MakeForestEvaluator(HexGridManager gridManager)
        {
            var go        = new GameObject("TestForestEvaluator");
            var evaluator = go.AddComponent<ForestGrowthEvaluator>();
            var field     = typeof(ForestGrowthEvaluator).GetField("_gridManager", BindingFlags.NonPublic | BindingFlags.Instance);
            field.SetValue(evaluator, gridManager);
            InvokeLifecycle(evaluator, "OnEnable");
            return evaluator;
        }

        private static FlowerClusterEvaluator MakeFlowerEvaluator(HexGridManager gridManager)
        {
            var go        = new GameObject("TestFlowerEvaluator");
            var evaluator = go.AddComponent<FlowerClusterEvaluator>();
            var field     = typeof(FlowerClusterEvaluator).GetField("_gridManager", BindingFlags.NonPublic | BindingFlags.Instance);
            field.SetValue(evaluator, gridManager);
            InvokeLifecycle(evaluator, "OnEnable");
            return evaluator;
        }

        [Test]
        public void ForestCluster_LegacyForestPlusComposite_CountTogether()
        {
            var gridManager = MakeGridManager();
            var grid        = GetGrid(gridManager);
            var evaluator   = MakeForestEvaluator(gridManager);

            var legacyForest = MakeCompositeTile((TileCategory.Forest, false));
            var forestFlower = MakeCompositeTile((TileCategory.Forest, false), (TileCategory.Field, true));

            var center   = HexCoord.Zero;
            var neighbor = center.Neighbor(0);

            TerrainGrowthEvent<ForestGrowthMetrics> lastEvt = null;
            System.Action<TerrainGrowthEvent<ForestGrowthMetrics>> handler = e => lastEvt = e;
            EventBus.Subscribe(handler);
            try
            {
                var tileA = PlaceTile(grid, center, legacyForest);
                EventBus.Publish(new TilePlacedEvent(tileA, legacyForest, center));

                var tileB = PlaceTile(grid, neighbor, forestFlower);
                EventBus.Publish(new TilePlacedEvent(tileB, forestFlower, neighbor));
            }
            finally
            {
                EventBus.Unsubscribe(handler);
                InvokeLifecycle(evaluator, "OnDisable");
            }

            Assert.IsNotNull(lastEvt);
            Assert.AreEqual(2, lastEvt.AffectedTiles.Count, "legacy Forestタイルと複合ForestFlowerタイルは同じForestクラスターとして繋がるはず");
        }

        [Test]
        public void FlowerCluster_LegacyFieldPlusComposite_CountTogether_EvenThoughVisualOnly()
        {
            // Stage 8で発生条件が重み付きになったため、テストの組み立てを更新している。
            // 以前は「legacy 2枚 + 複合1枚 = 3枚」で閾値に到達する前提だったが、
            // 複合タイルのField寄与はareaWeightで按分され0.5になったため合計2.5で閾値未満になる。
            // このテストの本来の意図は「visualOnlyのField要素を持つ複合タイルも花クラスターに
            // 参加する」ことなので、閾値はlegacyだけで満たしたうえで、複合タイルが
            // クラスターへ加わることを検証する形へ変更した（意図は同じ、前提だけ更新）。
            var gridManager = MakeGridManager();
            var grid        = GetGrid(gridManager);
            var evaluator   = MakeFlowerEvaluator(gridManager);

            var legacyField  = MakeCompositeTile((TileCategory.Field, false));
            var forestFlower = MakeCompositeTile((TileCategory.Forest, false), (TileCategory.Field, true)); // Field側がvisualOnly

            var c0 = HexCoord.Zero;
            var c1 = c0.Neighbor(0);
            var c2 = c0.Neighbor(1);
            var c3 = c0.Neighbor(2);

            FlowerClusterEvent lastEvt = null;
            System.Action<FlowerClusterEvent> handler = e => lastEvt = e;
            EventBus.Subscribe(handler);
            try
            {
                // legacy 3枚（重み1.0×3=3.0）で閾値に到達させる。
                foreach (var c in new[] { c0, c1, c2 })
                {
                    var t = PlaceTile(grid, c, legacyField);
                    EventBus.Publish(new TilePlacedEvent(t, legacyField, c));
                }
                Assert.IsNotNull(lastEvt, "前提: legacy 3枚で閾値に到達すること");
                Assert.AreEqual(3, lastEvt.Tiles.Count);

                // 4枚目にvisualOnlyのField要素を持つ複合タイルを隣接配置する。
                var t3 = PlaceTile(grid, c3, forestFlower);
                EventBus.Publish(new TilePlacedEvent(t3, forestFlower, c3));

                Assert.AreEqual(4, lastEvt.Tiles.Count,
                    "visualOnlyのField要素を持つ複合タイルも同じ花クラスターに参加するはず");
                CollectionAssert.Contains(lastEvt.Tiles, t3);
            }
            finally
            {
                EventBus.Unsubscribe(handler);
                InvokeLifecycle(evaluator, "OnDisable");
            }
        }

        [Test]
        public void ForestCluster_UnrelatedCategoryNeighbor_NotIncluded()
        {
            var gridManager = MakeGridManager();
            var grid        = GetGrid(gridManager);
            var evaluator   = MakeForestEvaluator(gridManager);

            var forest = MakeCompositeTile((TileCategory.Forest, false));
            var river  = MakeCompositeTile((TileCategory.River, false));

            var center   = HexCoord.Zero;
            var neighbor = center.Neighbor(0);

            TerrainGrowthEvent<ForestGrowthMetrics> lastEvt = null;
            System.Action<TerrainGrowthEvent<ForestGrowthMetrics>> handler = e => lastEvt = e;
            EventBus.Subscribe(handler);
            try
            {
                PlaceTile(grid, neighbor, river); // 森より先に無関係カテゴリを置いておく
                var tileForest = PlaceTile(grid, center, forest);
                EventBus.Publish(new TilePlacedEvent(tileForest, forest, center));
            }
            finally
            {
                EventBus.Unsubscribe(handler);
                InvokeLifecycle(evaluator, "OnDisable");
            }

            Assert.IsNotNull(lastEvt);
            Assert.AreEqual(1, lastEvt.AffectedTiles.Count, "Riverカテゴリの隣接タイルはForestクラスターに含まれないはず");
        }

        [Test]
        public void ForestCluster_LegacyOnly_RegressionSizeUnchanged()
        {
            var gridManager = MakeGridManager();
            var grid        = GetGrid(gridManager);
            var evaluator   = MakeForestEvaluator(gridManager);

            var forest = MakeCompositeTile((TileCategory.Forest, false));
            var center = HexCoord.Zero;
            var coords = new List<HexCoord> { center, center.Neighbor(0), center.Neighbor(1) };

            TerrainGrowthEvent<ForestGrowthMetrics> lastEvt = null;
            System.Action<TerrainGrowthEvent<ForestGrowthMetrics>> handler = e => lastEvt = e;
            EventBus.Subscribe(handler);
            try
            {
                foreach (var c in coords)
                {
                    var tile = PlaceTile(grid, c, forest);
                    EventBus.Publish(new TilePlacedEvent(tile, forest, c));
                }
            }
            finally
            {
                EventBus.Unsubscribe(handler);
                InvokeLifecycle(evaluator, "OnDisable");
            }

            Assert.AreEqual(3, lastEvt.AffectedTiles.Count, "legacy単一タイルのみのクラスターサイズは従来どおりであるべき");
        }

        [Test]
        public void FlowerCluster_LegacyOnly_RegressionSizeUnchanged()
        {
            var gridManager = MakeGridManager();
            var grid        = GetGrid(gridManager);
            var evaluator   = MakeFlowerEvaluator(gridManager);

            var field  = MakeCompositeTile((TileCategory.Field, false));
            var center = HexCoord.Zero;
            var coords = new List<HexCoord> { center, center.Neighbor(0), center.Neighbor(1) };

            FlowerClusterEvent lastEvt = null;
            System.Action<FlowerClusterEvent> handler = e => lastEvt = e;
            EventBus.Subscribe(handler);
            try
            {
                foreach (var c in coords)
                {
                    var tile = PlaceTile(grid, c, field);
                    EventBus.Publish(new TilePlacedEvent(tile, field, c));
                }
            }
            finally
            {
                EventBus.Unsubscribe(handler);
                InvokeLifecycle(evaluator, "OnDisable");
            }

            Assert.AreEqual(3, lastEvt.Tiles.Count, "legacy単一タイルのみのクラスターサイズは従来どおりであるべき");
        }
    }
}
