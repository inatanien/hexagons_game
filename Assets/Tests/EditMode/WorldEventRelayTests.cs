// 役割: WorldEventRelay（Tiles固有イベント → Coreイベントの翻訳）を固定する。
//
//       ★タイル配置の翻訳は TileType.GetEffectiveCategories（＝HasCategoryと同じ、
//         ゲームプレイ用の判定）を情報源とする。visualOnly要素とlandDecorationは数えない。
//         これが崩れると「見た目だけの花」で畑クエストが進んでしまう。
//
//       ★1枚のタイルにつき、同じカテゴリのイベントは1回だけ。
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
    public class WorldEventRelayTests
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

        private WorldEventRelay MakeRelay()
        {
            var go = new GameObject("TestWorldEventRelay");
            _created.Add(go);
            var relay = go.AddComponent<WorldEventRelay>();
            InvokeLifecycle(relay, "OnEnable");
            return relay;
        }

        /// <summary>翻訳結果を集めるスコープ。購読の後始末を必ず行う。</summary>
        private sealed class Collector : System.IDisposable
        {
            public readonly List<string>                 Keys       = new();
            public readonly List<TerrainClusterCategory> Categories = new();

            private readonly System.Action<WorldEventOccurredEvent> _onWorld;
            private readonly System.Action<TileCategoryPlacedEvent> _onPlaced;

            public Collector()
            {
                _onWorld  = e => Keys.Add(e.EventKey);
                _onPlaced = e => Categories.Add(e.Category);
                EventBus.Subscribe(_onWorld);
                EventBus.Subscribe(_onPlaced);
            }

            public void Dispose()
            {
                EventBus.Unsubscribe(_onWorld);
                EventBus.Unsubscribe(_onPlaced);
            }
        }

        // RiverBridgeEvent / TilePlacedEvent が持つHexTileは翻訳に使わないためnullで足りる
        // （Relayは盤面を一切見ない、という設計をテスト側でも表している）。
        private static void PublishPlacement(TileType type)
            => EventBus.Publish(new TilePlacedEvent(null, type, HexCoord.Zero));

        // ── 1〜3. 出来事の翻訳 ──────────────────────────────────────────

        [Test]
        public void Bridge_TranslatesToBridgeKey()
        {
            var relay = MakeRelay();
            using (var c = new Collector())
            {
                EventBus.Publish(new RiverBridgeEvent(null, null, 5));
                InvokeLifecycle(relay, "OnDisable");

                CollectionAssert.AreEqual(new[] { WorldEventKeys.Bridge }, c.Keys);
            }
        }

        [Test]
        public void Synergy_TranslatesToPrefixedKey()
        {
            var relay = MakeRelay();
            using (var c = new Collector())
            {
                EventBus.Publish(new TerrainSynergyEvent("ForestRiver", new List<HexTile>(), new List<HexTile>()));
                InvokeLifecycle(relay, "OnDisable");

                CollectionAssert.AreEqual(new[] { "synergy:ForestRiver" }, c.Keys);
            }
        }

        [Test]
        public void Synergy_WithBlankId_IsNotTranslated()
        {
            var relay = MakeRelay();
            using (var c = new Collector())
            {
                EventBus.Publish(new TerrainSynergyEvent(null,  new List<HexTile>(), new List<HexTile>()));
                EventBus.Publish(new TerrainSynergyEvent("   ", new List<HexTile>(), new List<HexTile>()));
                InvokeLifecycle(relay, "OnDisable");

                Assert.AreEqual(0, c.Keys.Count,
                    "SynergyIdが未入力のときは \"synergy:\" だけの無意味なキーを流さないはず");
            }
        }

        // ── 4〜8. タイル配置の翻訳 ──────────────────────────────────────

        [Test]
        public void FieldTile_TranslatesToFieldCategoryOnce()
        {
            var relay = MakeRelay();
            using (var c = new Collector())
            {
                PublishPlacement(MakeTile((TileCategory.Field, false)));
                InvokeLifecycle(relay, "OnDisable");

                CollectionAssert.AreEqual(new[] { TerrainClusterCategory.Field }, c.Categories);
            }
        }

        [Test]
        public void CompositeTile_TranslatesEachRealCategoryOnce()
        {
            var relay = MakeRelay();
            using (var c = new Collector())
            {
                PublishPlacement(MakeTile((TileCategory.Forest, false), (TileCategory.Field, false)));
                InvokeLifecycle(relay, "OnDisable");

                Assert.AreEqual(2, c.Categories.Count);
                CollectionAssert.Contains(c.Categories, TerrainClusterCategory.Forest);
                CollectionAssert.Contains(c.Categories, TerrainClusterCategory.Field);
            }
        }

        [Test]
        public void SameCategoryTwice_IsPublishedOnlyOnce()
        {
            var relay = MakeRelay();
            using (var c = new Collector())
            {
                // 同じカテゴリの要素が2つあるタイル（森の要素を2つ持つ複合タイルなど）
                PublishPlacement(MakeTile((TileCategory.Forest, false), (TileCategory.Forest, false)));
                InvokeLifecycle(relay, "OnDisable");

                CollectionAssert.AreEqual(new[] { TerrainClusterCategory.Forest }, c.Categories,
                    "1枚のタイルにつき同じカテゴリは1回だけのはず");
            }
        }

        [Test]
        public void VisualOnlyCategory_IsNotCounted()
        {
            var relay = MakeRelay();
            using (var c = new Collector())
            {
                // 景観タイル（森＋見た目だけの花）。花はゲームカテゴリではない
                PublishPlacement(MakeTile((TileCategory.Forest, false), (TileCategory.Field, true)));
                InvokeLifecycle(relay, "OnDisable");

                CollectionAssert.AreEqual(new[] { TerrainClusterCategory.Forest }, c.Categories,
                    "visualOnly要素のカテゴリは数えないはず（HasCategoryと同じ基準）");
            }
        }

        [Test]
        public void RoadAndVillage_AreNotTranslated()
        {
            var relay = MakeRelay();
            using (var c = new Collector())
            {
                PublishPlacement(MakeTile((TileCategory.Village, false)));
                PublishPlacement(MakeTile((TileCategory.Road, false)));
                InvokeLifecycle(relay, "OnDisable");

                Assert.AreEqual(0, c.Categories.Count,
                    "Road/VillageはCore側に対応するカテゴリが無いので翻訳しないはず");
            }
        }

        // ── 9. 購読／解除の対称性 ───────────────────────────────────────

        [Test]
        public void AfterOnDisable_TranslatesNothing()
        {
            var relay = MakeRelay();
            InvokeLifecycle(relay, "OnDisable");

            using (var c = new Collector())
            {
                EventBus.Publish(new RiverBridgeEvent(null, null, 5));
                EventBus.Publish(new TerrainSynergyEvent("ForestRiver", new List<HexTile>(), new List<HexTile>()));
                PublishPlacement(MakeTile((TileCategory.Field, false)));

                Assert.AreEqual(0, c.Keys.Count,       "OnDisable後は出来事を翻訳しないはず");
                Assert.AreEqual(0, c.Categories.Count, "OnDisable後はタイル配置を翻訳しないはず");
            }
        }
    }
}
