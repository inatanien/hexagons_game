// 役割: TileDeck の複数要素タイル対応3連続防止ロジック（Session 3）の単体テスト。
//       集合の共通部分ベースの除外判定と、既存の重み付き抽選・フォールバックが
//       維持されていることを確認する。private メンバーへはリフレクションでアクセスする。

using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using ElfVillage.Tiles;
using UnityEngine;

namespace ElfVillage.Tests
{
    public class TileDeckCategoryTests
    {
        private static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;
        private static readonly BindingFlags NonPublicStatic   = BindingFlags.NonPublic | BindingFlags.Static;

        // ── テストヘルパー ────────────────────────────────────────────

        private static TileType MakeLegacyTile(string category)
        {
            var t = ScriptableObject.CreateInstance<TileType>();
            t.tileCategory = category;
            t.isActive = true;
            return t;
        }

        private static TileType MakeElementsTile(params TileCategory[] categories)
        {
            var t = ScriptableObject.CreateInstance<TileType>();
            t.isActive = true;
            var elems = new List<TileElement>();
            foreach (var c in categories)
            {
                var v = ScriptableObject.CreateInstance<TerrainVariantDefinition>();
                v.category = c;
                elems.Add(new TileElement { variant = v, areaWeight = 1f / categories.Length });
            }
            t.elements = elems.ToArray();
            return t;
        }

        private static HashSet<TileCategory> Cats(params TileCategory[] cats) => new HashSet<TileCategory>(cats);

        private static TileDeck MakeDeck(TileDeckEntry[] entries)
        {
            var go = new GameObject();
            var deck = go.AddComponent<TileDeck>();
            typeof(TileDeck).GetField("entries", NonPublicInstance).SetValue(deck, entries);
            return deck;
        }

        private static void SetRecentCategorySets(TileDeck deck, params HashSet<TileCategory>[] sets)
        {
            var field = typeof(TileDeck).GetField("_recentCategorySets", NonPublicInstance);
            var list  = (List<HashSet<TileCategory>>)field.GetValue(deck);
            list.Clear();
            list.AddRange(sets);
        }

        private static HashSet<TileCategory> InvokeGetExcludedCategories(TileDeck deck)
        {
            var method = typeof(TileDeck).GetMethod("GetExcludedCategories", NonPublicInstance);
            return (HashSet<TileCategory>)method.Invoke(deck, null);
        }

        private static bool InvokeIsExcluded(TileType tileType, HashSet<TileCategory> excluded)
        {
            var method = typeof(TileDeck).GetMethod("IsExcluded", NonPublicStatic);
            return (bool)method.Invoke(null, new object[] { tileType, excluded });
        }

        private static void InvokeDrawOne(TileDeck deck)
        {
            var method = typeof(TileDeck).GetMethod("DrawOne", NonPublicInstance);
            method.Invoke(deck, null);
        }

        private static List<TileType> GetHandList(TileDeck deck)
        {
            var field = typeof(TileDeck).GetField("_hand", NonPublicInstance);
            return (List<TileType>)field.GetValue(deck);
        }

        // ── 集合ベースの除外判定 ──────────────────────────────────────

        [Test]
        public void GetExcludedCategories_HistoryLessThanTwo_ReturnsNull()
        {
            var deck = MakeDeck(new TileDeckEntry[0]);
            SetRecentCategorySets(deck, Cats(TileCategory.Forest));
            Assert.IsNull(InvokeGetExcludedCategories(deck));

            SetRecentCategorySets(deck);
            Assert.IsNull(InvokeGetExcludedCategories(deck));
        }

        [Test]
        public void GetExcludedCategories_ForestForest_ExcludesForest()
        {
            var deck = MakeDeck(new TileDeckEntry[0]);
            SetRecentCategorySets(deck, Cats(TileCategory.Forest), Cats(TileCategory.Forest));

            var excluded = InvokeGetExcludedCategories(deck);
            Assert.IsNotNull(excluded);
            Assert.AreEqual(1, excluded.Count);
            Assert.IsTrue(excluded.Contains(TileCategory.Forest));
        }

        [Test]
        public void Forest_Then_ForestField_ExcludesForestRiverCandidate()
        {
            var deck = MakeDeck(new TileDeckEntry[0]);
            SetRecentCategorySets(deck, Cats(TileCategory.Forest), Cats(TileCategory.Forest, TileCategory.Field));
            var excluded = InvokeGetExcludedCategories(deck);

            var candidate = MakeElementsTile(TileCategory.Forest, TileCategory.River);
            Assert.IsTrue(InvokeIsExcluded(candidate, excluded));
        }

        [Test]
        public void Forest_Then_ForestField_AllowsFieldRiverCandidate()
        {
            var deck = MakeDeck(new TileDeckEntry[0]);
            SetRecentCategorySets(deck, Cats(TileCategory.Forest), Cats(TileCategory.Forest, TileCategory.Field));
            var excluded = InvokeGetExcludedCategories(deck);

            var candidate = MakeElementsTile(TileCategory.Field, TileCategory.River);
            Assert.IsFalse(InvokeIsExcluded(candidate, excluded));
        }

        [Test]
        public void ForestField_Then_ForestField_ExcludesForestCandidate()
        {
            var deck = MakeDeck(new TileDeckEntry[0]);
            SetRecentCategorySets(deck,
                Cats(TileCategory.Forest, TileCategory.Field),
                Cats(TileCategory.Forest, TileCategory.Field));
            var excluded = InvokeGetExcludedCategories(deck);

            var candidate = MakeElementsTile(TileCategory.Forest);
            Assert.IsTrue(InvokeIsExcluded(candidate, excluded));
        }

        [Test]
        public void ForestField_Then_FieldRiver_ExcludesFieldCandidate()
        {
            var deck = MakeDeck(new TileDeckEntry[0]);
            SetRecentCategorySets(deck,
                Cats(TileCategory.Forest, TileCategory.Field),
                Cats(TileCategory.Field, TileCategory.River));
            var excluded = InvokeGetExcludedCategories(deck);

            var candidate = MakeElementsTile(TileCategory.Field);
            Assert.IsTrue(InvokeIsExcluded(candidate, excluded));

            // Forestは共通部分に含まれないため許可される
            var forestCandidate = MakeElementsTile(TileCategory.Forest);
            Assert.IsFalse(InvokeIsExcluded(forestCandidate, excluded));
        }

        // ── フォールバック・既存挙動維持 ──────────────────────────────

        [Test]
        public void DrawOne_AllCandidatesExcluded_FallsBackAndStillDraws()
        {
            var forestOnly = MakeLegacyTile("Forest");
            var entries = new[] { new TileDeckEntry { tileType = forestOnly, weight = 5 } };
            var deck = MakeDeck(entries);
            SetRecentCategorySets(deck, Cats(TileCategory.Forest), Cats(TileCategory.Forest));

            InvokeDrawOne(deck);

            var hand = GetHandList(deck);
            Assert.AreEqual(1, hand.Count, "除外により候補ゼロでもフォールバックして必ず1枚引ける");
            Assert.AreEqual(forestOnly, hand[0]);
        }

        [Test]
        public void DrawOne_LegacyTilesOnly_NeverProducesThreeInARow()
        {
            var entries = new[]
            {
                new TileDeckEntry { tileType = MakeLegacyTile("Forest"), weight = 8 },
                new TileDeckEntry { tileType = MakeLegacyTile("Field"),  weight = 8 },
                new TileDeckEntry { tileType = MakeLegacyTile("River"),  weight = 8 },
                new TileDeckEntry { tileType = MakeLegacyTile("Village"),weight = 8 },
                new TileDeckEntry { tileType = MakeLegacyTile("Road"),   weight = 8 },
            };
            var deck = MakeDeck(entries);

            const int Draws = 500;
            for (int i = 0; i < Draws; i++)
                InvokeDrawOne(deck);

            var hand = GetHandList(deck);
            Assert.AreEqual(Draws, hand.Count);

            int run = 1, maxRun = 1;
            for (int i = 1; i < hand.Count; i++)
            {
                bool sameCategory = hand[i].tileCategory == hand[i - 1].tileCategory;
                run = sameCategory ? run + 1 : 1;
                if (run > maxRun) maxRun = run;
            }
            Assert.LessOrEqual(maxRun, 2, "legacyタイルのみの構成でも3連続が発生してはならない（既存挙動の維持）");
        }

        [Test]
        public void DrawOne_MixedLegacyAndCompositeTiles_NeverProducesThreeInARowForAnyCategory()
        {
            var entries = new[]
            {
                new TileDeckEntry { tileType = MakeLegacyTile("Forest"), weight = 8 },
                new TileDeckEntry { tileType = MakeLegacyTile("Field"),  weight = 8 },
                new TileDeckEntry { tileType = MakeLegacyTile("River"),  weight = 8 },
                new TileDeckEntry { tileType = MakeElementsTile(TileCategory.Forest, TileCategory.Field), weight = 3 },
                new TileDeckEntry { tileType = MakeElementsTile(TileCategory.Forest, TileCategory.River), weight = 3 },
            };
            var deck = MakeDeck(entries);

            const int Draws = 500;
            for (int i = 0; i < Draws; i++)
                InvokeDrawOne(deck);

            var hand = GetHandList(deck);

            // カテゴリごとの連続出現数を検証する（複合タイルは複数カテゴリにカウントされる）
            foreach (TileCategory category in System.Enum.GetValues(typeof(TileCategory)))
            {
                int run = 0, maxRun = 0;
                foreach (var tile in hand)
                {
                    bool has = new List<TileCategory>(tile.GetEffectiveCategories()).Contains(category);
                    run = has ? run + 1 : 0;
                    if (run > maxRun) maxRun = run;
                }
                Assert.LessOrEqual(maxRun, 2, $"{category} が3連続してはならない");
            }
        }
    }
}
