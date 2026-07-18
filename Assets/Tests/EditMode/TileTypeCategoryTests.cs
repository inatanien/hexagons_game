// 役割: TileType の有効カテゴリ取得API（Session 3 で追加）の単体テスト。
//       EffectiveElements / GetEffectiveCategories / HasCategory / TryGetLegacyCategory を検証する。

using System.Collections.Generic;
using NUnit.Framework;
using ElfVillage.Tiles;
using UnityEngine;

namespace ElfVillage.Tests
{
    public class TileTypeCategoryTests
    {
        private static TerrainVariantDefinition MakeVariant(TileCategory category)
        {
            var v = ScriptableObject.CreateInstance<TerrainVariantDefinition>();
            v.category = category;
            return v;
        }

        [Test]
        public void HasCategory_ElementsWithForestAndField_BothTrue()
        {
            var tile = ScriptableObject.CreateInstance<TileType>();
            tile.elements = new[]
            {
                new TileElement { variant = MakeVariant(TileCategory.Forest), areaWeight = 0.5f },
                new TileElement { variant = MakeVariant(TileCategory.Field),  areaWeight = 0.5f },
            };

            Assert.IsTrue(tile.HasCategory(TileCategory.Forest));
            Assert.IsTrue(tile.HasCategory(TileCategory.Field));
            Assert.IsFalse(tile.HasCategory(TileCategory.River));
        }

        [Test]
        public void GetEffectiveCategories_DuplicateCategory_ReturnsOnce()
        {
            var tile = ScriptableObject.CreateInstance<TileType>();
            tile.elements = new[]
            {
                new TileElement { variant = MakeVariant(TileCategory.Forest) },
                new TileElement { variant = MakeVariant(TileCategory.Forest) },
            };

            var cats = new List<TileCategory>(tile.GetEffectiveCategories());
            Assert.AreEqual(1, cats.Count);
            Assert.AreEqual(TileCategory.Forest, cats[0]);
        }

        [Test]
        public void GetEffectiveCategories_NullVariant_Ignored()
        {
            var tile = ScriptableObject.CreateInstance<TileType>();
            tile.elements = new[]
            {
                new TileElement { variant = MakeVariant(TileCategory.Forest) },
                new TileElement { variant = null },
            };

            var cats = new List<TileCategory>(tile.GetEffectiveCategories());
            Assert.AreEqual(1, cats.Count);
        }

        [Test]
        public void GetEffectiveCategories_ElementsUnset_FallsBackToLegacy()
        {
            var tile = ScriptableObject.CreateInstance<TileType>();
            tile.elements = null;
            tile.tileCategory = "River";

            Assert.IsTrue(tile.HasCategory(TileCategory.River));
            Assert.IsFalse(tile.HasCategory(TileCategory.Forest));
        }

        [Test]
        public void GetEffectiveCategories_EmptyElements_FallsBackToLegacy()
        {
            var tile = ScriptableObject.CreateInstance<TileType>();
            tile.elements = new TileElement[0];
            tile.tileCategory = "Road";

            Assert.IsTrue(tile.HasCategory(TileCategory.Road));
        }

        [Test]
        public void GetEffectiveCategories_ElementsAllInvalid_FallsBackToLegacy()
        {
            var tile = ScriptableObject.CreateInstance<TileType>();
            tile.elements = new[] { new TileElement { variant = null }, new TileElement { variant = null } };
            tile.tileCategory = "Village";

            Assert.IsTrue(tile.HasCategory(TileCategory.Village));
        }

        [Test]
        public void GetEffectiveCategories_PartiallyInvalidElements_UsesOnlyValid_NoLegacyMix()
        {
            var tile = ScriptableObject.CreateInstance<TileType>();
            tile.tileCategory = "Village"; // legacyにVillageを設定しておく
            tile.elements = new[]
            {
                new TileElement { variant = MakeVariant(TileCategory.Forest) },
                new TileElement { variant = null },
            };

            var cats = new List<TileCategory>(tile.GetEffectiveCategories());
            Assert.AreEqual(1, cats.Count);
            Assert.AreEqual(TileCategory.Forest, cats[0]);
            Assert.IsFalse(tile.HasCategory(TileCategory.Village), "有効なelementsがある場合、legacyのカテゴリを混ぜてはならない");
        }

        [Test]
        public void TryGetLegacyCategory_InvalidString_ReturnsFalse_NotDefaultForest()
        {
            var tile = ScriptableObject.CreateInstance<TileType>();
            tile.tileCategory = "NotARealCategory";

            bool result = tile.TryGetLegacyCategory(out TileCategory category);

            Assert.IsFalse(result);
            Assert.IsFalse(tile.HasCategory(TileCategory.Forest), "変換不能時にdefault値(Forest)へ誤変換されてはならない");
        }

        [Test]
        public void TryGetLegacyCategory_EmptyString_ReturnsFalse()
        {
            var tile = ScriptableObject.CreateInstance<TileType>();
            tile.tileCategory = "";
            Assert.IsFalse(tile.TryGetLegacyCategory(out _));
        }

        [Test]
        public void TryGetLegacyCategory_ValidString_ReturnsTrue()
        {
            var tile = ScriptableObject.CreateInstance<TileType>();
            tile.tileCategory = "Forest";
            bool result = tile.TryGetLegacyCategory(out TileCategory category);
            Assert.IsTrue(result);
            Assert.AreEqual(TileCategory.Forest, category);
        }
    }
}
