// 役割: 「川タイルかどうか」の判定が TileType.HasCategory(River) に一本化されたことを固定する。
//
//       ★以前は各システムがSceneのTileType[]へ登録されたアセット参照で判定しており、
//         川タイルを増やすたびに登録漏れで流れ・魚・橋が動かなくなった
//         （景観川6種の追加時に4システムすべてで発生）。
//         接続判定・デッキ抽選と同じ情報源へ揃えたので、
//         「Riverカテゴリを持つなら必ず川として扱われる」ことをここで固定する。
//
//       ★landDecoration（見た目だけの木・花）が判定へ混ざらないことも同時に守る。
//         混ざると RiverForest が森として、RiverFlower が花畑として扱われてしまう。

using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ElfVillage.Tiles;

namespace ElfVillage.Tests
{
    public class RiverCategoryJudgmentTests
    {
        private const string Dir = "Assets/_Game/ScriptableObjects/TileDefinitions/";

        /// <summary>川として扱われるべき9種（既存3 + 景観6）。</summary>
        public static readonly string[] RiverAssets =
        {
            "TileType_River_Straight", "TileType_River_Bend", "TileType_River_Wide_Bend",
            "TileType_RiverForest_Straight", "TileType_RiverForest_Bend", "TileType_RiverForest_WideBend",
            "TileType_RiverFlower_Straight", "TileType_RiverFlower_Bend", "TileType_RiverFlower_WideBend",
        };

        /// <summary>川として扱われてはいけないタイル。</summary>
        public static readonly string[] NonRiverAssets =
        {
            "TileType_Forest", "TileType_Field", "TileType_Village",
            "TileType_ForestFlower", "TileType_FieldGrove", "TileType_ForestFlower_Prototype",
        };

        private readonly List<Object> _created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
        }

        private static TileType Load(string name)
        {
            var t = UnityEditor.AssetDatabase.LoadAssetAtPath<TileType>(Dir + name + ".asset");
            Assert.IsNotNull(t, name + " が見つからない");
            return t;
        }

        // ══ Riverカテゴリ判定 ════════════════════════════════════════════

        [Test]
        public void AllRiverTiles_HaveTheRiverCategory([ValueSource(nameof(RiverAssets))] string assetName)
        {
            // ★これが false になると、そのタイルは流れも魚も橋もシナジーも失う。
            Assert.IsTrue(Load(assetName).HasCategory(TileCategory.River), assetName + " が River と判定されない");
        }

        [Test]
        public void NonRiverTiles_AreNotJudgedAsRiver([ValueSource(nameof(NonRiverAssets))] string assetName)
        {
            Assert.IsFalse(Load(assetName).HasCategory(TileCategory.River), assetName + " が River と誤判定される");
        }

        [Test]
        public void NullTileType_IsNotRiver()
        {
            // production側は t != null && t.HasCategory(...) の形。null で例外を出さないこと。
            TileType none = null;
            Assert.IsFalse(none != null && none.HasCategory(TileCategory.River));
        }

        // ══ 景観川は River のみを返す ════════════════════════════════════

        [Test]
        public void ScenicRiver_ReturnsRiverOnly(
            [Values("TileType_RiverForest_Straight", "TileType_RiverForest_Bend", "TileType_RiverForest_WideBend",
                    "TileType_RiverFlower_Straight", "TileType_RiverFlower_Bend", "TileType_RiverFlower_WideBend")]
            string assetName)
        {
            var t = Load(assetName);

            var categories = new List<TileCategory>();
            foreach (var c in t.GetEffectiveCategories()) categories.Add(c);

            Assert.AreEqual(1, categories.Count, $"{assetName}: カテゴリが1つでない [{string.Join(",", categories)}]");
            Assert.AreEqual(TileCategory.River, categories[0], $"{assetName}: River 以外を返している");
        }

        [Test]
        public void ScenicRiver_DoesNotJoinForestOrField(
            [Values("TileType_RiverForest_Straight", "TileType_RiverForest_Bend", "TileType_RiverForest_WideBend",
                    "TileType_RiverFlower_Straight", "TileType_RiverFlower_Bend", "TileType_RiverFlower_WideBend")]
            string assetName)
        {
            // ★木や花はあくまで landDecoration。Forest/Field としては数えない。
            var t = Load(assetName);
            Assert.IsFalse(t.HasCategory(TileCategory.Forest), $"{assetName}: Forest として扱われている");
            Assert.IsFalse(t.HasCategory(TileCategory.Field),  $"{assetName}: Field として扱われている");
            Assert.IsFalse(t.HasEffectCategory(TileCategory.Forest), $"{assetName}: Forest の成長演出へ参加している");
            Assert.IsFalse(t.HasEffectCategory(TileCategory.Field),  $"{assetName}: Field の成長演出へ参加している");
        }

        // ══ landDecoration は判定へ影響しない ════════════════════════════

        [Test]
        public void LandDecoration_DoesNotAffectRiverJudgment()
        {
            // 装飾を差し替えても、付け外ししても、River判定は変わらない。
            var river = ScriptableObject.CreateInstance<TileType>();
            _created.Add(river);
            river.propType     = TilePropType.Water;
            river.tileCategory = "River";
            river.edges        = new[] { EdgeType.River, EdgeType.Field, EdgeType.Field,
                                         EdgeType.River, EdgeType.Field, EdgeType.Field };
            river.elements = new TileElement[0];

            Assert.IsTrue(river.HasCategory(TileCategory.River), "装飾なしで River と判定されない");

            foreach (var cat in new[] { TileCategory.Forest, TileCategory.Field, TileCategory.Village, TileCategory.River })
            {
                var variant = ScriptableObject.CreateInstance<TerrainVariantDefinition>();
                _created.Add(variant);
                variant.category = cat;
                variant.propType = cat == TileCategory.Field ? TilePropType.Flower : TilePropType.Tree;

                river.landDecoration               = variant;
                river.landDecorationCandidateCount = 32;

                Assert.IsTrue(river.HasCategory(TileCategory.River),  $"装飾 {cat} で River 判定が消えた");
                Assert.IsFalse(river.HasCategory(TileCategory.Forest), $"装飾 {cat} で Forest 判定が生えた");
                Assert.IsFalse(river.HasCategory(TileCategory.Field),  $"装飾 {cat} で Field 判定が生えた");
            }
        }

        [Test]
        public void LandDecorationOnANonRiverTile_DoesNotMakeItARiver()
        {
            // 逆向きの固定。装飾を付けただけの森タイルが川になってはいけない。
            var forest = ScriptableObject.CreateInstance<TileType>();
            _created.Add(forest);
            forest.propType     = TilePropType.Tree;
            forest.tileCategory = "Forest";
            forest.edges        = new[] { EdgeType.Forest, EdgeType.Forest, EdgeType.Forest,
                                          EdgeType.Forest, EdgeType.Forest, EdgeType.Forest };
            forest.elements = new TileElement[0];

            var variant = ScriptableObject.CreateInstance<TerrainVariantDefinition>();
            _created.Add(variant);
            variant.category = TileCategory.River;   // 装飾のcategoryはRiverだが、判定には使われない
            variant.propType = TilePropType.Tree;
            forest.landDecoration               = variant;
            forest.landDecorationCandidateCount = 32;

            Assert.IsFalse(forest.HasCategory(TileCategory.River), "装飾のcategoryでRiverになってしまっている");
            Assert.IsTrue(forest.HasCategory(TileCategory.Forest));
        }

        // ══ 既存Riverとの同一性 ══════════════════════════════════════════

        [Test]
        public void ScenicRivers_JudgeIdenticallyToPlainRivers()
        {
            // 景観川は、川判定に関わるすべてのAPIで既存Riverと同じ答えを返す。
            var plain = Load("TileType_River_Straight");

            foreach (var name in RiverAssets)
            {
                var t = Load(name);
                foreach (TileCategory c in System.Enum.GetValues(typeof(TileCategory)))
                {
                    Assert.AreEqual(plain.HasCategory(c), t.HasCategory(c),
                        $"{name}: HasCategory({c}) が River_Straight と違う");
                    Assert.AreEqual(plain.HasEffectCategory(c), t.HasEffectCategory(c),
                        $"{name}: HasEffectCategory({c}) が River_Straight と違う");
                }
                Assert.AreEqual(plain.propType, t.propType, $"{name}: propType が違う");
                Assert.AreEqual(plain.tileCategory, t.tileCategory, $"{name}: tileCategory が違う");
            }
        }
    }
}
