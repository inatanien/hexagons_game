// 役割: QuestTileFocusTracker（達成を祝う対象タイルの解決、Stage 1）を固定する。
//
//       ★「達成の直前に届いたイベントのタイルだろう」という暗黙の対応には依存しない。
//         Coreから届くQuestFocusが「盤面の何を見ているか」を明示するので、
//         それに対応する候補だけを選ぶ。ここではその対応表をテストで固定する。
//
//       ★見た目だけの花（visualOnly要素・landDecoration）が対象へ混ざらないことも守る。
//         混ざると「花を植えていないのに花畑クエストで光る」ことになる。
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
    public class QuestTileFocusTrackerTests
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

        private TileType MakeTileType(params (TileCategory category, bool visualOnly)[] spec)
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

        /// <summary>見た目だけの花を持つ川タイル（景観川）。花畑として数えてはいけない。</summary>
        private TileType MakeScenicRiverType()
        {
            var t = MakeTileType((TileCategory.River, false));
            var decoration = ScriptableObject.CreateInstance<TerrainVariantDefinition>();
            decoration.category = TileCategory.Field;
            _created.Add(decoration);

            t.landDecoration = decoration;
            t.landDecorationCandidateCount = 10;
            return t;
        }

        private HexTile MakeTile(int q, int r)
        {
            var go = new GameObject($"TestTile_{q}_{r}");
            _created.Add(go);
            var tile = go.AddComponent<HexTile>();
            tile.Initialize(new HexCoord(q, r), 1f);
            return tile;
        }

        private QuestTileFocusTracker MakeTracker()
        {
            var go = new GameObject("TestQuestTileFocusTracker");
            _created.Add(go);
            var tracker = go.AddComponent<QuestTileFocusTracker>();
            InvokeLifecycle(tracker, "OnEnable");
            return tracker;
        }

        /// <summary>解決結果を受け取るスコープ。Trackerは結果を保持しないのでイベントで見る。</summary>
        private sealed class Resolved : System.IDisposable
        {
            public readonly List<IReadOnlyList<HexTile>> Results = new();

            private readonly System.Action<QuestTileSelectionResolvedEvent> _handler;

            public Resolved()
            {
                _handler = e => Results.Add(e.Tiles);
                EventBus.Subscribe(_handler);
            }

            public IReadOnlyList<HexTile> Last => Results[Results.Count - 1];

            public void Dispose() => EventBus.Unsubscribe(_handler);
        }

        private static QuestFocus ClusterFocus(TerrainClusterCategory category)
            => new QuestFocus(QuestFocusSource.Cluster, category);

        private static QuestFocus PlacementFocus(TerrainClusterCategory category)
            => new QuestFocus(QuestFocusSource.TilePlacement, category);

        private static QuestFocus WorldEventFocus(string key)
            => new QuestFocus(QuestFocusSource.WorldEvent, default, key);

        private static void StartFocus(QuestFocus focus)
            => EventBus.Publish(new QuestFocusStartedEvent(focus));

        /// <summary>
        /// 祝いを要求し、そのフレームの同期イベント処理が終わった状態まで進める。
        /// Trackerは購読順に依存しないよう解決をLateUpdateまで遅らせるので、
        /// EditModeではそこまで明示的に呼ぶ。
        /// </summary>
        private static void Celebrate(QuestTileFocusTracker tracker, QuestFocus focus)
        {
            EventBus.Publish(new QuestCelebrationEvent(focus, "テストクエスト"));
            InvokeLifecycle(tracker, "LateUpdate");
        }

        private static void PublishForestCluster(params HexTile[] tiles)
            => EventBus.Publish(new TerrainGrowthEvent<ForestGrowthMetrics>(
                null, HexCoord.Zero, tiles, new ForestGrowthMetrics(tiles.Length, tiles.Length)));

        private static void PublishRiverCluster(params HexTile[] tiles)
            => EventBus.Publish(new TerrainGrowthEvent<RiverGrowthMetrics>(
                null, HexCoord.Zero, tiles, new RiverGrowthMetrics(tiles.Length, tiles.Length)));

        private static void PlaceTile(HexTile tile, TileType type)
            => EventBus.Publish(new TilePlacedEvent(tile, type, tile.Data.coord));

        // ── 1〜2. クラスター条件 ────────────────────────────────────────

        [Test]
        public void ForestCluster_ResolvesWholeCluster()
        {
            var tracker = MakeTracker();
            var tiles   = new[] { MakeTile(0, 0), MakeTile(1, 0), MakeTile(2, 0), MakeTile(3, 0), MakeTile(4, 0) };

            using (var r = new Resolved())
            {
                StartFocus(ClusterFocus(TerrainClusterCategory.Forest));
                PublishForestCluster(tiles);
                Celebrate(tracker, ClusterFocus(TerrainClusterCategory.Forest));
                InvokeLifecycle(tracker, "OnDisable");

                CollectionAssert.AreEquivalent(tiles, r.Last, "達成時点の森クラスター全体が対象になるはず");
            }
        }

        [Test]
        public void RiverCluster_ResolvesWholeCluster()
        {
            var tracker = MakeTracker();
            var tiles   = new[] { MakeTile(0, 0), MakeTile(1, 0), MakeTile(2, 0) };

            using (var r = new Resolved())
            {
                StartFocus(ClusterFocus(TerrainClusterCategory.River));
                PublishRiverCluster(tiles);
                Celebrate(tracker, ClusterFocus(TerrainClusterCategory.River));
                InvokeLifecycle(tracker, "OnDisable");

                CollectionAssert.AreEquivalent(tiles, r.Last, "達成時点の川クラスター全体が対象になるはず");
            }
        }

        // ── 3. 配置条件はフォーカス開始後に数えたタイルだけ ─────────────

        [Test]
        public void TilePlacement_ResolvesOnlyTilesPlacedAfterFocusStarted()
        {
            var tracker  = MakeTracker();
            var fieldType = MakeTileType((TileCategory.Field, false));

            var beforeFocus = MakeTile(-1, 0);
            var first       = MakeTile(0, 0);
            var second      = MakeTile(1, 0);

            using (var r = new Resolved())
            {
                // クエストが始まる前に置いた花畑は対象外
                PlaceTile(beforeFocus, fieldType);

                StartFocus(PlacementFocus(TerrainClusterCategory.Field));
                InvokeLifecycle(tracker, "LateUpdate");   // フレーム境界。ここから数え始める

                PlaceTile(first,  fieldType);
                PlaceTile(second, fieldType);

                Celebrate(tracker, PlacementFocus(TerrainClusterCategory.Field));
                InvokeLifecycle(tracker, "OnDisable");

                CollectionAssert.AreEquivalent(new[] { first, second }, r.Last,
                    "フォーカス開始後に置いたタイルだけが対象になるはず");
            }
        }

        // ── 4. 橋は対応する川のまとまり ─────────────────────────────────

        [Test]
        public void Bridge_ResolvesRiverClusterOfThatBridge()
        {
            var tracker = MakeTracker();
            var cluster = new[] { MakeTile(0, 0), MakeTile(1, 0), MakeTile(2, 0), MakeTile(3, 0), MakeTile(4, 0) };

            using (var r = new Resolved())
            {
                StartFocus(WorldEventFocus(WorldEventKeys.Bridge));
                EventBus.Publish(new RiverBridgeEvent(cluster[4], cluster, cluster.Length));
                Celebrate(tracker, WorldEventFocus(WorldEventKeys.Bridge));
                InvokeLifecycle(tracker, "OnDisable");

                CollectionAssert.AreEquivalent(cluster, r.Last, "橋が架かった川のまとまり全体が対象になるはず");
            }
        }

        // ── 5. シナジーは TilesA + TilesB ───────────────────────────────

        [Test]
        public void Synergy_ResolvesBothSides()
        {
            var tracker = MakeTracker();
            var forest  = new List<HexTile> { MakeTile(0, 0), MakeTile(1, 0) };
            var river   = new List<HexTile> { MakeTile(0, 1), MakeTile(1, 1), MakeTile(2, 1) };

            using (var r = new Resolved())
            {
                StartFocus(WorldEventFocus(WorldEventKeys.Synergy("ForestRiver")));
                EventBus.Publish(new TerrainSynergyEvent("ForestRiver", forest, river));
                Celebrate(tracker, WorldEventFocus(WorldEventKeys.Synergy("ForestRiver")));
                InvokeLifecycle(tracker, "OnDisable");

                var expected = new List<HexTile>(forest);
                expected.AddRange(river);
                CollectionAssert.AreEquivalent(expected, r.Last, "TilesAとTilesBの両方が対象になるはず");
            }
        }

        // ── 6. 見た目だけの花は数えない ─────────────────────────────────

        [Test]
        public void VisualOnlyAndLandDecoration_AreNotCountedAsFieldTiles()
        {
            var tracker = MakeTracker();

            var realField   = MakeTileType((TileCategory.Field, false));
            var forestFlower = MakeTileType((TileCategory.Forest, false), (TileCategory.Field, true)); // 花はvisualOnly
            var scenicRiver  = MakeScenicRiverType();                                                  // 花はlandDecoration

            var counted   = MakeTile(0, 0);
            var forestOne = MakeTile(1, 0);
            var riverOne  = MakeTile(2, 0);

            using (var r = new Resolved())
            {
                StartFocus(PlacementFocus(TerrainClusterCategory.Field));
                PlaceTile(counted,   realField);
                PlaceTile(forestOne, forestFlower);
                PlaceTile(riverOne,  scenicRiver);

                Celebrate(tracker, PlacementFocus(TerrainClusterCategory.Field));
                InvokeLifecycle(tracker, "OnDisable");

                CollectionAssert.AreEquivalent(new[] { counted }, r.Last,
                    "見た目だけの花（visualOnly / landDecoration）は花畑として数えないはず");
            }
        }

        // ── 7. 別クエスト用の候補と混線しない ───────────────────────────

        [Test]
        public void OtherCategoryCandidates_DoNotLeakIntoTheCelebration()
        {
            var tracker = MakeTracker();
            var forest  = new[] { MakeTile(0, 0), MakeTile(1, 0) };
            var river   = new[] { MakeTile(0, 1), MakeTile(1, 1) };

            using (var r = new Resolved())
            {
                StartFocus(ClusterFocus(TerrainClusterCategory.Forest));
                PublishForestCluster(forest);
                // 森クエストの最中に川も育っている
                PublishRiverCluster(river);

                Celebrate(tracker, ClusterFocus(TerrainClusterCategory.Forest));
                InvokeLifecycle(tracker, "OnDisable");

                CollectionAssert.AreEquivalent(forest, r.Last, "森の達成では森のタイルだけが対象になるはず");
            }
        }

        // ── 8. 祝ったら蓄積はリセットされる ─────────────────────────────

        [Test]
        public void AfterCelebration_AccumulationIsCleared()
        {
            var tracker   = MakeTracker();
            var fieldType = MakeTileType((TileCategory.Field, false));

            var firstRound  = new[] { MakeTile(0, 0), MakeTile(1, 0) };
            var secondRound = MakeTile(2, 0);

            using (var r = new Resolved())
            {
                StartFocus(PlacementFocus(TerrainClusterCategory.Field));
                foreach (var t in firstRound) PlaceTile(t, fieldType);
                Celebrate(tracker, PlacementFocus(TerrainClusterCategory.Field));

                // 2周目
                StartFocus(PlacementFocus(TerrainClusterCategory.Field));
                InvokeLifecycle(tracker, "LateUpdate");   // フレーム境界

                PlaceTile(secondRound, fieldType);
                Celebrate(tracker, PlacementFocus(TerrainClusterCategory.Field));
                InvokeLifecycle(tracker, "OnDisable");

                Assert.AreEqual(2, r.Results.Count);
                CollectionAssert.AreEquivalent(new[] { secondRound }, r.Last,
                    "2周目に1周目のタイルが残ってはいけない");
            }
        }

        // ── 9. フォーカスが切り替わっても古い蓄積が混ざらない ───────────

        [Test]
        public void ChangingFocus_DiscardsEarlierPlacementAccumulation()
        {
            var tracker   = MakeTracker();
            var fieldType = MakeTileType((TileCategory.Field, false));

            var underFieldFocus = MakeTile(0, 0);
            var underRiverFocus = MakeTile(1, 0);

            using (var r = new Resolved())
            {
                StartFocus(PlacementFocus(TerrainClusterCategory.Field));
                PlaceTile(underFieldFocus, fieldType);

                // 花畑クエストが達成されないまま次のクエストへ移った
                StartFocus(ClusterFocus(TerrainClusterCategory.River));
                InvokeLifecycle(tracker, "LateUpdate");   // フレーム境界。ここで蓄積がリセットされる

                PlaceTile(underRiverFocus, fieldType);

                // その状態で花畑の祝いが来ても、古いフォーカスの蓄積を使ってはいけない
                Celebrate(tracker, PlacementFocus(TerrainClusterCategory.Field));
                InvokeLifecycle(tracker, "OnDisable");

                CollectionAssert.DoesNotContain(r.Last, underFieldFocus,
                    "フォーカスが切り替わった時点で前の蓄積は捨てられているはず");
            }
        }

        // ── 10. eventKeyの一致はTrim・大小無視 ──────────────────────────

        [Test]
        public void WorldEventKey_MatchesIgnoringCaseAndPadding()
        {
            var tracker = MakeTracker();
            var cluster = new[] { MakeTile(0, 0), MakeTile(1, 0) };

            using (var r = new Resolved())
            {
                StartFocus(WorldEventFocus("  Bridge "));
                EventBus.Publish(new RiverBridgeEvent(cluster[1], cluster, cluster.Length));
                Celebrate(tracker, WorldEventFocus("  Bridge "));
                InvokeLifecycle(tracker, "OnDisable");

                CollectionAssert.AreEquivalent(cluster, r.Last,
                    "手入力の綴りの大小差・前後の空白で対象を見失わないはず");
            }
        }

        // ── 11. OnDisable後は何も収集も解決もしない ─────────────────────

        [Test]
        public void AfterOnDisable_CollectsAndResolvesNothing()
        {
            var tracker = MakeTracker();
            InvokeLifecycle(tracker, "OnDisable");

            using (var r = new Resolved())
            {
                StartFocus(ClusterFocus(TerrainClusterCategory.Forest));
                PublishForestCluster(MakeTile(0, 0), MakeTile(1, 0));
                Celebrate(tracker, ClusterFocus(TerrainClusterCategory.Forest));

                Assert.AreEqual(0, r.Results.Count, "OnDisable後は解決イベントを発行しないはず");
            }
        }
    }
}
