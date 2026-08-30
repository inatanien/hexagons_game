// 役割: 川のクエスト進捗経路（Step 1）を固定する。
//       RiverGrowthEvaluator が「クエスト進捗用の TerrainGrowthEvent<RiverGrowthMetrics>」を
//       閾値なしで毎回発行し、TerrainClusterProgressRelay がそれを
//       TerrainClusterProgressEvent(River) へ翻訳することを検証する。
//
//       ★同時に「魚などの演出の発生条件（RiverClusterEvent／threshold=8）が変わっていないこと」を
//         回帰として固定する。ここが崩れると、川3枚のクエストを足した途端に魚が3枚で湧く。
//
//       ★クエスト進捗はイベントの回数ではなく ClusterSize の値を読むため、
//         1配置につき発行が1回であること（二重購読による重複通知がないこと）も固定する。
//
//       注意: EditModeではAddComponent直後にOnEnable/OnDisableが自動発火しないため、
//       リフレクションで明示的に呼び出す（既存テストと同じ手法）。

using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using ElfVillage.Core;
using ElfVillage.HexGrid;
using ElfVillage.Tiles;
using UnityEngine;

namespace ElfVillage.Tests
{
    public class RiverGrowthProgressTests
    {
        private readonly List<Object> _created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created)
                if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
        }

        // ── ヘルパー ────────────────────────────────────────────────

        private static void InvokeLifecycle(Component c, string methodName)
        {
            var method = c.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(method, $"{c.GetType().Name}に{methodName}メソッドが見つかりません（リフレクション対象名の変更を確認してください）");
            method.Invoke(c, null);
        }

        private static readonly Dictionary<TileCategory, EdgeType> s_categoryToEdge = new()
        {
            { TileCategory.Forest, EdgeType.Forest },
            { TileCategory.Field,  EdgeType.Field  },
            { TileCategory.River,  EdgeType.River  },
        };

        /// <summary>テスト用のタイル種別を組み立てる。edgesはOnValidateの警告を避けるためだけに埋める。</summary>
        private TileType MakeTile(params (TileCategory category, bool visualOnly)[] spec)
        {
            var t = ScriptableObject.CreateInstance<TileType>();
            _created.Add(t);

            var elements = new TileElement[spec.Length];
            for (int i = 0; i < spec.Length; i++)
            {
                var v = ScriptableObject.CreateInstance<TerrainVariantDefinition>();
                v.category = spec[i].category;
                _created.Add(v);

                elements[i] = new TileElement
                {
                    variant    = v,
                    areaWeight = 1f / spec.Length,
                    visualOnly = spec[i].visualOnly,
                };
            }
            t.elements = elements;

            foreach (var (category, visualOnly) in spec)
            {
                if (visualOnly) continue;
                if (!s_categoryToEdge.TryGetValue(category, out var edgeType)) continue;
                for (int d = 0; d < 6; d++) t.edges[d] = edgeType;
            }
            return t;
        }

        private static Dictionary<HexCoord, HexTile> GetGrid(HexGridManager gridManager)
        {
            var field = typeof(HexGridManager).GetField("_grid", BindingFlags.NonPublic | BindingFlags.Instance);
            return (Dictionary<HexCoord, HexTile>)field.GetValue(gridManager);
        }

        private HexGridManager MakeGridManager()
        {
            var go = new GameObject("TestGridManager");
            _created.Add(go);
            return go.AddComponent<HexGridManager>();
        }

        private RiverGrowthEvaluator MakeRiverEvaluator(HexGridManager gridManager)
        {
            var go        = new GameObject("TestRiverEvaluator");
            _created.Add(go);
            var evaluator = go.AddComponent<RiverGrowthEvaluator>();
            var field     = typeof(RiverGrowthEvaluator).GetField("_gridManager", BindingFlags.NonPublic | BindingFlags.Instance);
            field.SetValue(evaluator, gridManager);
            InvokeLifecycle(evaluator, "OnEnable");
            return evaluator;
        }

        private TerrainClusterProgressRelay MakeRelay()
        {
            var go    = new GameObject("TestRelay");
            _created.Add(go);
            var relay = go.AddComponent<TerrainClusterProgressRelay>();
            InvokeLifecycle(relay, "OnEnable");
            return relay;
        }

        /// <summary>グリッドへタイルを置き、TilePlacedEventを発行する（実際の配置経路と同じ順序）。</summary>
        private void PlaceAndPublish(HexGridManager gridManager, HexCoord coord, TileType type)
        {
            var go = new GameObject("TestTile_" + coord);
            _created.Add(go);

            var tile = go.AddComponent<HexTile>();
            tile.Initialize(coord, 1f);
            tile.Place(type, 0);
            GetGrid(gridManager)[coord] = tile;

            EventBus.Publish(new TilePlacedEvent(tile, type, coord));
        }

        /// <summary>中心から数珠つなぎに連結したcount枚分の座標。BFSで必ず1クラスターになる。</summary>
        private static List<HexCoord> Chain(int count)
        {
            var list  = new List<HexCoord>();
            var coord = HexCoord.Zero;
            for (int i = 0; i < count; i++)
            {
                list.Add(coord);
                coord = coord.Neighbor(0);
            }
            return list;
        }

        // ── 1. 閾値なしで毎回発行される（川1枚でも観測できる） ──────────

        [Test]
        public void RiverGrowth_SingleTile_PublishesEventEvenBelowThreshold()
        {
            var gridManager = MakeGridManager();
            var evaluator   = MakeRiverEvaluator(gridManager);
            var river       = MakeTile((TileCategory.River, false));

            TerrainGrowthEvent<RiverGrowthMetrics> last = null;
            System.Action<TerrainGrowthEvent<RiverGrowthMetrics>> handler = e => last = e;
            EventBus.Subscribe(handler);
            try
            {
                PlaceAndPublish(gridManager, HexCoord.Zero, river);
            }
            finally
            {
                EventBus.Unsubscribe(handler);
                InvokeLifecycle(evaluator, "OnDisable");
            }

            Assert.IsNotNull(last, "川1枚でもクエスト進捗用のgrowthイベントは発行されるはず（閾値なし）");
            Assert.AreEqual(1, last.Metrics.LargestClusterSize);
            Assert.AreEqual(1, last.Metrics.TotalRiverTiles);
        }

        // ── 2. 連結3枚でClusterSize=3が観測できる ──────────────────────

        [Test]
        public void RiverGrowth_ThreeConnectedTiles_ReportsClusterSize3()
        {
            var gridManager = MakeGridManager();
            var evaluator   = MakeRiverEvaluator(gridManager);
            var river       = MakeTile((TileCategory.River, false));

            TerrainGrowthEvent<RiverGrowthMetrics> last = null;
            System.Action<TerrainGrowthEvent<RiverGrowthMetrics>> handler = e => last = e;
            EventBus.Subscribe(handler);
            try
            {
                foreach (var c in Chain(3)) PlaceAndPublish(gridManager, c, river);
            }
            finally
            {
                EventBus.Unsubscribe(handler);
                InvokeLifecycle(evaluator, "OnDisable");
            }

            Assert.IsNotNull(last);
            Assert.AreEqual(3, last.Metrics.LargestClusterSize, "連結3枚はClusterSize=3として観測できるはず");
            Assert.AreEqual(3, last.Metrics.TotalRiverTiles);
        }

        // ── 3. RelayがRiverをCoreイベントへ翻訳する ─────────────────────

        [Test]
        public void Relay_ConvertsRiverGrowthMetrics_ToRiverProgressEvent()
        {
            var relay = MakeRelay();

            TerrainClusterProgressEvent received = null;
            System.Action<TerrainClusterProgressEvent> handler = e => received = e;
            EventBus.Subscribe(handler);
            try
            {
                EventBus.Publish(new TerrainGrowthEvent<RiverGrowthMetrics>(
                    terrainType:   null,
                    anchor:        HexCoord.Zero,
                    affectedTiles: new List<HexTile>(),
                    metrics:       new RiverGrowthMetrics(3, 3)));
            }
            finally
            {
                EventBus.Unsubscribe(handler);
                InvokeLifecycle(relay, "OnDisable");
            }

            Assert.IsNotNull(received, "RelayはRiverのgrowthイベントを翻訳するはず");
            Assert.AreEqual(TerrainClusterCategory.River, received.Category);
            Assert.AreEqual(3, received.ClusterSize);
        }

        // ── 4. 景観川（複合タイル）も同じクラスターへ含まれる ───────────

        [Test]
        public void ScenicRiver_CompositeTile_CountsInSameCluster()
        {
            var gridManager = MakeGridManager();
            var evaluator   = MakeRiverEvaluator(gridManager);

            var plainRiver  = MakeTile((TileCategory.River, false));
            // 景観川（RiverForest / RiverFlower）: 川カテゴリ＋見た目だけの装飾
            var scenicRiver = MakeTile((TileCategory.River, false), (TileCategory.Forest, true));

            TerrainGrowthEvent<RiverGrowthMetrics> last = null;
            System.Action<TerrainGrowthEvent<RiverGrowthMetrics>> handler = e => last = e;
            EventBus.Subscribe(handler);
            try
            {
                var coords = Chain(3);
                PlaceAndPublish(gridManager, coords[0], plainRiver);
                PlaceAndPublish(gridManager, coords[1], scenicRiver);
                PlaceAndPublish(gridManager, coords[2], plainRiver);
            }
            finally
            {
                EventBus.Unsubscribe(handler);
                InvokeLifecycle(evaluator, "OnDisable");
            }

            Assert.IsNotNull(last);
            Assert.AreEqual(3, last.Metrics.LargestClusterSize,
                "景観川も HasCategory(River) で同じクラスターに数えるはず");
        }

        // ── 5. 川でないタイルではRiver growthが発行されない ─────────────

        [Test]
        public void NonRiverTile_DoesNotPublishRiverGrowth()
        {
            var gridManager = MakeGridManager();
            var evaluator   = MakeRiverEvaluator(gridManager);
            var forest      = MakeTile((TileCategory.Forest, false));

            int count = 0;
            System.Action<TerrainGrowthEvent<RiverGrowthMetrics>> handler = _ => count++;
            EventBus.Subscribe(handler);
            try
            {
                PlaceAndPublish(gridManager, HexCoord.Zero, forest);
            }
            finally
            {
                EventBus.Unsubscribe(handler);
                InvokeLifecycle(evaluator, "OnDisable");
            }

            Assert.AreEqual(0, count, "森タイルの配置で川の進捗イベントが飛んではいけない");
        }

        // ── 6. 既存仕様の回帰: 3枚ではRiverClusterEventが発行されない ───

        [Test]
        public void RiverClusterEvent_ThreeTiles_StillNotPublished()
        {
            var gridManager = MakeGridManager();
            var evaluator   = MakeRiverEvaluator(gridManager);
            var river       = MakeTile((TileCategory.River, false));

            int fishTriggers = 0;
            System.Action<RiverClusterEvent> handler = _ => fishTriggers++;
            EventBus.Subscribe(handler);
            try
            {
                foreach (var c in Chain(3)) PlaceAndPublish(gridManager, c, river);
            }
            finally
            {
                EventBus.Unsubscribe(handler);
                InvokeLifecycle(evaluator, "OnDisable");
            }

            Assert.AreEqual(0, fishTriggers, "threshold=8のまま。川3枚で魚の演出が湧いてはいけない");
        }

        // ── 7. 既存仕様の回帰: 8枚では従来どおり発行される ──────────────

        [Test]
        public void RiverClusterEvent_EightTiles_StillPublished()
        {
            var gridManager = MakeGridManager();
            var evaluator   = MakeRiverEvaluator(gridManager);
            var river       = MakeTile((TileCategory.River, false));

            RiverClusterEvent last = null;
            System.Action<RiverClusterEvent> handler = e => last = e;
            EventBus.Subscribe(handler);
            try
            {
                foreach (var c in Chain(8)) PlaceAndPublish(gridManager, c, river);
            }
            finally
            {
                EventBus.Unsubscribe(handler);
                InvokeLifecycle(evaluator, "OnDisable");
            }

            Assert.IsNotNull(last, "threshold=8に到達したらRiverClusterEventは従来どおり発行されるはず");
            Assert.AreEqual(8, last.ClusterSize);
        }

        // ── 8. 既存Forest経路への回帰がない ─────────────────────────────

        [Test]
        public void Relay_ForestConversion_Unchanged()
        {
            var relay = MakeRelay();

            var received = new List<TerrainClusterProgressEvent>();
            System.Action<TerrainClusterProgressEvent> handler = e => received.Add(e);
            EventBus.Subscribe(handler);
            try
            {
                EventBus.Publish(new TerrainGrowthEvent<ForestGrowthMetrics>(
                    terrainType:   null,
                    anchor:        HexCoord.Zero,
                    affectedTiles: new List<HexTile>(),
                    metrics:       new ForestGrowthMetrics(largestClusterSize: 5, totalForestTiles: 5)));
            }
            finally
            {
                EventBus.Unsubscribe(handler);
                InvokeLifecycle(relay, "OnDisable");
            }

            Assert.AreEqual(1, received.Count, "River購読を足してもForestの翻訳回数は変わらないはず");
            Assert.AreEqual(TerrainClusterCategory.Forest, received[0].Category);
            Assert.AreEqual(5, received[0].ClusterSize);
        }

        // ── 9. 1配置につき1回だけ。重複通知がない ───────────────────────

        [Test]
        public void SinglePlacement_PublishesGrowthAndProgressExactlyOnce()
        {
            var gridManager = MakeGridManager();
            var evaluator   = MakeRiverEvaluator(gridManager);
            var relay       = MakeRelay();
            var river       = MakeTile((TileCategory.River, false));

            int growthCount   = 0;
            int progressCount = 0;
            System.Action<TerrainGrowthEvent<RiverGrowthMetrics>> onGrowth = _ => growthCount++;
            System.Action<TerrainClusterProgressEvent> onProgress = e =>
            {
                if (e.Category == TerrainClusterCategory.River) progressCount++;
            };

            EventBus.Subscribe(onGrowth);
            EventBus.Subscribe(onProgress);
            try
            {
                PlaceAndPublish(gridManager, HexCoord.Zero, river);
            }
            finally
            {
                EventBus.Unsubscribe(onGrowth);
                EventBus.Unsubscribe(onProgress);
                InvokeLifecycle(evaluator, "OnDisable");
                InvokeLifecycle(relay, "OnDisable");
            }

            Assert.AreEqual(1, growthCount,   "1配置につきgrowthイベントは1回だけのはず");
            Assert.AreEqual(1, progressCount, "Relay経由の進捗通知も1回だけのはず（二重購読の検出）");
        }

        // ── 10. 購読／解除の対称性（解除後は両地形とも翻訳しない） ───────

        [Test]
        public void Relay_AfterOnDisable_StopsRelayingBothTerrains()
        {
            var relay = MakeRelay();
            InvokeLifecycle(relay, "OnDisable");

            int count = 0;
            System.Action<TerrainClusterProgressEvent> handler = _ => count++;
            EventBus.Subscribe(handler);
            try
            {
                EventBus.Publish(new TerrainGrowthEvent<RiverGrowthMetrics>(
                    null, HexCoord.Zero, new List<HexTile>(), new RiverGrowthMetrics(3, 3)));
                EventBus.Publish(new TerrainGrowthEvent<ForestGrowthMetrics>(
                    null, HexCoord.Zero, new List<HexTile>(), new ForestGrowthMetrics(3, 3)));
            }
            finally
            {
                EventBus.Unsubscribe(handler);
            }

            Assert.AreEqual(0, count,
                "OnDisable後はForest/Riverとも購読が残ってはいけない（購読と解除の対称性）");
        }
    }
}
