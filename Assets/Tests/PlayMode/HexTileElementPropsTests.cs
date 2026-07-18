// 役割: HexTile の複数要素タイル対応プロップ生成（Session 4）のテスト。
//       legacy生成との切り替え・二重生成防止・areaWeightの安全な扱いを検証する。
//       PlayModeテストとして実装している理由: EditMode下ではSetPropMaterialの
//       renderer.material呼び出しがEditor専用の「マテリアルリーク」警告を[Error]として
//       Consoleに出し、テスト失敗として扱われてしまうため（Play Modeでは発生しない）。

using System.Reflection;
using NUnit.Framework;
using ElfVillage.HexGrid;
using ElfVillage.Tiles;
using UnityEngine;

namespace ElfVillage.Tests
{
    public class HexTileElementPropsTests
    {
        // ── テストヘルパー ────────────────────────────────────────────

        private static HexTile MakeTile(HexCoord coord)
        {
            var go = new GameObject();
            var tile = go.AddComponent<HexTile>();
            tile.Initialize(coord, 1f);
            return tile;
        }

        private static TileType MakeLegacyTreeTile(int propCount)
        {
            var t = ScriptableObject.CreateInstance<TileType>();
            t.propType  = TilePropType.Tree;
            t.propCount = propCount;
            return t;
        }

        private static TileType MakeLegacyFlowerTile(int propCount)
        {
            var t = ScriptableObject.CreateInstance<TileType>();
            t.propType  = TilePropType.Flower;
            t.propCount = propCount;
            return t;
        }

        private static TerrainVariantDefinition MakeTreeVariant(int propCount, string name = "TreeVariant")
        {
            var v = ScriptableObject.CreateInstance<TerrainVariantDefinition>();
            v.category   = TileCategory.Forest;
            v.propType   = TilePropType.Tree;
            v.propCount  = propCount;
            v.variantName = name;
            return v;
        }

        private static TerrainVariantDefinition MakeFlowerVariant(int propCount, string name = "FlowerVariant")
        {
            var v = ScriptableObject.CreateInstance<TerrainVariantDefinition>();
            v.category   = TileCategory.Field;
            v.propType   = TilePropType.Flower;
            v.propCount  = propCount;
            v.variantName = name;
            return v;
        }

        private static TileType MakeElementsTile(params TileElement[] elements)
        {
            var t = ScriptableObject.CreateInstance<TileType>();
            t.elements = elements;
            return t;
        }

        // 直下のプリミティブ木（Cylinder+Sphereのペア）の数を数える
        private static int CountPrimitiveTreePairs(Transform root)
        {
            int cylinders = 0;
            foreach (Transform child in root)
                if (child.GetComponent<MeshFilter>() != null && child.name.Contains("Cylinder"))
                    cylinders++;
            return cylinders;
        }

        private static Transform FindElementPropsRoot(HexTile tile) => tile.transform.Find("ElementProps");

        // Object.Destroy は実際の破棄がフレーム末まで遅延されるため、同一フレーム内で
        // 「破棄されたはず」を同期的に検証する場合は transform.Find ではなく、
        // HexTile自身が同期的にnullへ戻す private フィールドを直接見る。
        private static GameObject GetElementPropsRootField(HexTile tile)
        {
            var field = typeof(HexTile).GetField("_elementPropsRoot", BindingFlags.NonPublic | BindingFlags.Instance);
            return (GameObject)field.GetValue(tile);
        }

        // ── legacy⇔複合の切り替え ────────────────────────────────────

        [Test]
        public void ElementsUnset_UsesLegacyGeneration_NoElementPropsRoot()
        {
            var tile = MakeTile(HexCoord.Zero);
            tile.Place(MakeLegacyTreeTile(3), 0);

            Assert.IsNull(FindElementPropsRoot(tile), "legacyタイルではElementPropsルートを作らない");
            Assert.AreEqual(3, CountPrimitiveTreePairs(tile.transform), "legacyの木の生成数はSession4前と同じであるべき");
        }

        [Test]
        public void ValidElements_DoesNotRunLegacyGeneration()
        {
            var tile = MakeTile(HexCoord.Zero);
            var elementsType = MakeElementsTile(
                new TileElement { variant = MakeTreeVariant(2), areaWeight = 1f });
            tile.Place(elementsType, 0);

            var elementRoot = FindElementPropsRoot(tile);
            Assert.IsNotNull(elementRoot, "有効なelementsがあればElementPropsルートを作る");
            // legacy生成はTileType.propType(未設定=None)を見ないため、legacy側の生成物は無いはず
            Assert.AreEqual(0, CountPrimitiveTreePairs(tile.transform), "transform直下にlegacy生成の木があってはならない");
        }

        // ── 複数要素の生成 ────────────────────────────────────────────

        [Test]
        public void ForestAndFieldVariants_BothGenerate()
        {
            var tile = MakeTile(HexCoord.Zero);
            var elementsType = MakeElementsTile(
                new TileElement { variant = MakeTreeVariant(4),   areaWeight = 0.5f },
                new TileElement { variant = MakeFlowerVariant(4), areaWeight = 0.5f });
            tile.Place(elementsType, 0);

            var root = FindElementPropsRoot(tile);
            Assert.IsNotNull(root);
            Assert.Greater(CountPrimitiveTreePairs(root), 0, "森要素の木が生成されているべき");
            Assert.IsNotNull(root.Find("FlowerBillboards"), "花要素のBillboardが生成されているべき");
        }

        [Test]
        public void NullVariantElement_Ignored()
        {
            var tile = MakeTile(HexCoord.Zero);
            var elementsType = MakeElementsTile(
                new TileElement { variant = MakeTreeVariant(2), areaWeight = 0.5f },
                new TileElement { variant = null,               areaWeight = 0.5f });

            Assert.DoesNotThrow(() => tile.Place(elementsType, 0));
            var root = FindElementPropsRoot(tile);
            Assert.Greater(CountPrimitiveTreePairs(root), 0);
        }

        [Test]
        public void OneElementFailsInternally_OtherValidElementsStillGenerate()
        {
            // propTypeがswitchのどのcaseにも該当しない不正な値でも、他の有効要素の生成が
            // 止まらないことを確認する。
            var tile = MakeTile(HexCoord.Zero);
            var brokenVariant = ScriptableObject.CreateInstance<TerrainVariantDefinition>();
            brokenVariant.category  = TileCategory.Village;
            brokenVariant.propType  = (TilePropType)999; // switchのどのcaseにも該当しない未知の値
            brokenVariant.propCount = 1;

            var elementsType = MakeElementsTile(
                new TileElement { variant = MakeTreeVariant(2), areaWeight = 0.5f },
                new TileElement { variant = brokenVariant,       areaWeight = 0.5f });

            Assert.DoesNotThrow(() => tile.Place(elementsType, 0));
            var root = FindElementPropsRoot(tile);
            Assert.Greater(CountPrimitiveTreePairs(root), 0, "不正な要素があっても他の有効要素は生成される");
        }

        // ── areaWeightの正規化 ────────────────────────────────────────

        [Test]
        public void EqualWeights_BothVariantsGenerateRoughlyBalanced()
        {
            var tile = MakeTile(HexCoord.Zero);
            var elementsType = MakeElementsTile(
                new TileElement { variant = MakeTreeVariant(10),   areaWeight = 0.5f },
                new TileElement { variant = MakeFlowerVariant(10), areaWeight = 0.5f });
            tile.Place(elementsType, 0);

            var root = FindElementPropsRoot(tile);
            int treeCount = CountPrimitiveTreePairs(root);
            Assert.AreEqual(5, treeCount, "propCount10 * weight0.5 = 5本のはず");
        }

        [Test]
        public void ZeroWeights_FallsBackToEqualSplit()
        {
            var tile = MakeTile(HexCoord.Zero);
            var elementsType = MakeElementsTile(
                new TileElement { variant = MakeTreeVariant(10), areaWeight = 0f },
                new TileElement { variant = MakeFlowerVariant(10), areaWeight = 0f });
            tile.Place(elementsType, 0);

            var root = FindElementPropsRoot(tile);
            int treeCount = CountPrimitiveTreePairs(root);
            Assert.AreEqual(5, treeCount, "重み合計0なら均等配分(1/2ずつ)になるはず");
        }

        [TestCase(-1f)]
        [TestCase(2.5f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void InvalidAreaWeight_DoesNotThrow(float badWeight)
        {
            var tile = MakeTile(HexCoord.Zero);
            var elementsType = MakeElementsTile(
                new TileElement { variant = MakeTreeVariant(3), areaWeight = badWeight },
                new TileElement { variant = MakeFlowerVariant(3), areaWeight = 0.5f });

            Assert.DoesNotThrow(() => tile.Place(elementsType, 0));
            var root = FindElementPropsRoot(tile);
            Assert.IsNotNull(root);
        }

        [Test]
        public void SinglePropCountWithPositiveWeight_GeneratesAtLeastOne()
        {
            var tile = MakeTile(HexCoord.Zero);
            var elementsType = MakeElementsTile(
                new TileElement { variant = MakeTreeVariant(1),   areaWeight = 0.1f },
                new TileElement { variant = MakeFlowerVariant(1), areaWeight = 0.9f });
            tile.Place(elementsType, 0);

            var root = FindElementPropsRoot(tile);
            Assert.Greater(CountPrimitiveTreePairs(root), 0, "propCount1・重み>0の要素は丸めで0にならず最低1個生成される");
        }

        // ── 再生成・破棄 ──────────────────────────────────────────────

        [Test]
        public void RePlaceSameElementsType_DoesNotDoublePropCount()
        {
            var tile = MakeTile(HexCoord.Zero);
            var elementsType = MakeElementsTile(
                new TileElement { variant = MakeTreeVariant(4), areaWeight = 1f });

            tile.Place(elementsType, 0);
            int firstCount = CountPrimitiveTreePairs(FindElementPropsRoot(tile));

            tile.Place(elementsType, 0); // 同じTileTypeで再配置
            int secondCount = CountPrimitiveTreePairs(FindElementPropsRoot(tile));

            Assert.AreEqual(firstCount, secondCount, "再生成でプロップ数が倍増してはならない");
        }

        [Test]
        public void ChangeFromCompositeToLegacy_OldElementPropsRootReferenceCleared()
        {
            var tile = MakeTile(HexCoord.Zero);
            var elementsType = MakeElementsTile(
                new TileElement { variant = MakeTreeVariant(4), areaWeight = 1f });
            tile.Place(elementsType, 0);
            Assert.IsNotNull(FindElementPropsRoot(tile));

            tile.Place(MakeLegacyFlowerTile(2), 0);
            // Object.Destroyは実際の破棄がフレーム末まで遅延されるため、同一フレーム内ではtransform.Findで
            // 即座に確認できない。ClearElementProps()が_elementPropsRootの参照を同期的にnullへ戻す
            // ことを直接確認する（これは実際にDestroy対象になったことの確実な裏付けになる）。
            Assert.IsNull(GetElementPropsRootField(tile), "複合→legacyへ切り替えた際、旧ElementPropsルートへの参照が残ってはならない");
        }

        [Test]
        public void ChangeFromLegacyToComposite_ElementsSideHasNoLeftover_LegacyOwnPropsPreExistingLimitation()
        {
            // 既知の制約: legacy生成（SpawnTrees等）は自身が生成したプロップを一切追跡しないため、
            // Place()を複数回呼ぶとlegacy由来のプロップ自体はtransform直下に残り続ける。
            // これはSession 4で変更していない既存の挙動であり（実際のゲームプレイではPlace()は
            // 1タイルにつき1回しか呼ばれないため到達しない経路）、今回はこの制約を修正しない。
            // ここでは「elements側の新しい生成が正しく行われ、ElementPropsルートが1つだけ存在する」
            // ことだけを確認する。
            var tile = MakeTile(HexCoord.Zero);
            tile.Place(MakeLegacyTreeTile(3), 0);
            Assert.Greater(CountPrimitiveTreePairs(tile.transform), 0);

            var elementsType = MakeElementsTile(
                new TileElement { variant = MakeFlowerVariant(3), areaWeight = 1f });
            tile.Place(elementsType, 0);

            var root = FindElementPropsRoot(tile);
            Assert.IsNotNull(root, "複合タイルへの切り替え後、ElementPropsルートが生成されているべき");
            Assert.IsNotNull(root.Find("FlowerBillboards"), "新しいFlower要素が正しく生成されているべき");
        }
    }
}
