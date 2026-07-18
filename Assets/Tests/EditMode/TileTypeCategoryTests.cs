// 役割: TileType の有効カテゴリ取得API（Session 3 で追加）の単体テスト。
//       EffectiveElements / GetEffectiveCategories / HasCategory / TryGetLegacyCategory を検証する。

using System.Collections.Generic;
using NUnit.Framework;
using ElfVillage.Tiles;
using UnityEngine;
using UnityEngine.TestTools;

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

        // ── visualOnly（Session 7） ───────────────────────────────────

        [Test]
        public void VisualOnlyUnset_ElementIsIncludedInGameplayElements()
        {
            var tile = ScriptableObject.CreateInstance<TileType>();
            tile.elements = new[] { new TileElement { variant = MakeVariant(TileCategory.Forest) } };
            // visualOnlyを明示的に設定していない＝既定false（Gameplay参加）のはず

            var gameplay = new List<TileElement>(tile.GameplayElements);
            Assert.AreEqual(1, gameplay.Count, "visualOnly未設定の要素はGameplayElementsに含まれるべき");
        }

        [Test]
        public void VisualOnlyTrue_IncludedInEffectiveElements_ExcludedFromGameplayElements()
        {
            var tile = ScriptableObject.CreateInstance<TileType>();
            tile.elements = new[]
            {
                new TileElement { variant = MakeVariant(TileCategory.Forest), visualOnly = false },
                new TileElement { variant = MakeVariant(TileCategory.Field),  visualOnly = true },
            };

            var effective = new List<TileElement>(tile.EffectiveElements);
            var gameplay  = new List<TileElement>(tile.GameplayElements);

            Assert.AreEqual(2, effective.Count, "VisualOnly要素もEffectiveElementsには含まれる");
            Assert.AreEqual(1, gameplay.Count, "VisualOnly要素はGameplayElementsに含まれない");
            Assert.AreEqual(TileCategory.Forest, gameplay[0].variant.category);
        }

        [Test]
        public void GetEffectiveCategories_DoesNotReturnVisualOnlyCategory()
        {
            var tile = ScriptableObject.CreateInstance<TileType>();
            tile.elements = new[]
            {
                new TileElement { variant = MakeVariant(TileCategory.Forest), visualOnly = false },
                new TileElement { variant = MakeVariant(TileCategory.Field),  visualOnly = true },
            };

            var cats = new List<TileCategory>(tile.GetEffectiveCategories());
            Assert.AreEqual(1, cats.Count);
            Assert.AreEqual(TileCategory.Forest, cats[0]);
            Assert.IsFalse(tile.HasCategory(TileCategory.Field), "VisualOnlyのカテゴリはHasCategoryでも参加しない");
        }

        [Test]
        public void AllElementsVisualOnly_ReturnsEmptySet_DoesNotFallBackToLegacy()
        {
            // 推奨仕様: elementsに有効要素が存在する場合はelementsを正式データとみなし、
            // 全てvisualOnlyなら空集合を返す（legacyカテゴリを誤って復活させない）。
            var tile = ScriptableObject.CreateInstance<TileType>();
            tile.tileCategory = "Village"; // legacyにVillageを設定しておく
            tile.elements = new[]
            {
                new TileElement { variant = MakeVariant(TileCategory.Forest), visualOnly = true },
                new TileElement { variant = MakeVariant(TileCategory.Field),  visualOnly = true },
            };

            var cats = new List<TileCategory>(tile.GetEffectiveCategories());
            Assert.AreEqual(0, cats.Count, "有効要素が全てVisualOnlyの場合は空集合を返すべき");
            Assert.IsFalse(tile.HasCategory(TileCategory.Village), "legacyカテゴリへフォールバックしてはならない");
            Assert.IsFalse(tile.HasCategory(TileCategory.Forest));
        }

        [Test]
        public void OnValidate_GameplayPlusVisualOnly_NoWarningForMissingEdge()
        {
            var tile = ScriptableObject.CreateInstance<TileType>();
            tile.edges = new[]
            {
                EdgeType.Forest, EdgeType.Forest, EdgeType.Forest,
                EdgeType.Forest, EdgeType.Forest, EdgeType.Forest,
            };
            tile.elements = new[]
            {
                new TileElement { variant = MakeVariant(TileCategory.Forest), visualOnly = false },
                new TileElement { variant = MakeVariant(TileCategory.Field),  visualOnly = true },
            };

            var onValidate = typeof(TileType).GetMethod("OnValidate",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            onValidate.Invoke(tile, null);

            // VisualOnlyのFieldにedgeが対応していなくても警告が出ないことを確認する
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void OnValidate_GameplayElementMissingEdge_StillWarns()
        {
            var tile = ScriptableObject.CreateInstance<TileType>();
            tile.name = "T_MissingEdgeTest";
            tile.edges = new[]
            {
                EdgeType.Forest, EdgeType.Forest, EdgeType.Forest,
                EdgeType.Forest, EdgeType.Forest, EdgeType.Forest,
            };
            tile.elements = new[]
            {
                new TileElement { variant = MakeVariant(TileCategory.Forest), visualOnly = false },
                new TileElement { variant = MakeVariant(TileCategory.Field),  visualOnly = false }, // Gameplayのまま
            };

            var onValidate = typeof(TileType).GetMethod("OnValidate",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            onValidate.Invoke(tile, null);

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(".*Field.*edges.*"));
        }
    }
}
