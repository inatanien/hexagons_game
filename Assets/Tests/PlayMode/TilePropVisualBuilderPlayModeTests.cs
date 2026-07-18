// 役割: TilePropVisualBuilder（Session 11）のうち、木（プリミティブ生成でrenderer.materialを
//       使う）やCollider確認など、EditMode下では「マテリアルリーク」Console Errorでテスト失敗
//       扱いになる項目をPlayModeで検証する。legacy委譲の正しさ・複合要素の両要素表示・
//       Collider不残存を中心に確認する。

using System.Collections;
using System.Reflection;
using NUnit.Framework;
using ElfVillage.HexGrid;
using ElfVillage.Tiles;
using UnityEngine;
using UnityEngine.TestTools;

namespace ElfVillage.Tests
{
    public class TilePropVisualBuilderPlayModeTests
    {
        private static TerrainVariantDefinition MakeTreeVariant(int propCount)
        {
            var v = ScriptableObject.CreateInstance<TerrainVariantDefinition>();
            v.category  = TileCategory.Forest;
            v.propType  = TilePropType.Tree;
            v.propCount = propCount;
            return v;
        }

        private static TerrainVariantDefinition MakeFlowerVariant(int propCount)
        {
            var v = ScriptableObject.CreateInstance<TerrainVariantDefinition>();
            v.category  = TileCategory.Field;
            v.propType  = TilePropType.Flower;
            v.propCount = propCount;
            return v;
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

        private static TileType MakeElementsTile(params TileElement[] elements)
        {
            var t = ScriptableObject.CreateInstance<TileType>();
            t.elements = elements;
            return t;
        }

        // 複合要素タイルの生成物は"PreviewElementProps"ラッパーの下に、legacyタイルの
        // 生成物はparent直下に、それぞれ入る（両ケースを区別せず数えるため再帰的に探索する）。
        private static int CountTreePairs(Transform root)
        {
            int cylinders = 0;
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
                if (mf.name.Contains("Cylinder"))
                    cylinders++;
            return cylinders;
        }

        private static bool HasFlowerBillboards(Transform root) => root.Find("FlowerBillboards") != null
            || HasNestedFlowerBillboards(root);

        private static bool HasNestedFlowerBillboards(Transform root)
        {
            foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
                if (ps.gameObject.name == "FlowerBillboards") return true;
            return false;
        }

        // ── legacy委譲の正しさ ────────────────────────────────────────

        [Test]
        public void LegacyForest_OnlyTreesGenerated_NoFlowerBillboards()
        {
            var go = new GameObject();
            var type = MakeLegacyTreeTile(4);
            TilePropVisualBuilder.SpawnProps(type, go.transform, new HexCoord(0, 0, 0));

            Assert.Greater(CountTreePairs(go.transform), 0, "legacy Treeタイルは木が生成されるべき");
            Assert.IsFalse(HasFlowerBillboards(go.transform), "legacy Forestに花が出てはならない");
        }

        [Test]
        public void LegacyField_OnlyFlowersGenerated_NoTrees()
        {
            var go = new GameObject();
            var type = MakeLegacyFlowerTile(4);
            TilePropVisualBuilder.SpawnProps(type, go.transform, new HexCoord(1, -1, 0));

            Assert.IsTrue(HasFlowerBillboards(go.transform), "legacy Fieldは花が生成されるべき");
            Assert.AreEqual(0, CountTreePairs(go.transform), "legacy Fieldに木が出てはならない");
        }

        // ── 複合要素タイル：両方の要素が表示される ───────────────────────

        [Test]
        public void ForestFlowerLike_BothTreeAndFlowerGenerated()
        {
            var go = new GameObject();
            var type = MakeElementsTile(
                new TileElement { variant = MakeTreeVariant(10),   areaWeight = 0.7f, visualOnly = false },
                new TileElement { variant = MakeFlowerVariant(20), areaWeight = 0.3f, visualOnly = true  });
            TilePropVisualBuilder.SpawnProps(type, go.transform, new HexCoord(2, -1, -1));

            Assert.Greater(CountTreePairs(go.transform), 0, "ForestFlower相当は木が表示されるべき");
            Assert.IsTrue(HasFlowerBillboards(go.transform), "ForestFlower相当は花も表示されるべき");
        }

        [Test]
        public void FieldGroveLike_BothFlowerAndTreeGenerated()
        {
            var go = new GameObject();
            var type = MakeElementsTile(
                new TileElement { variant = MakeFlowerVariant(20), areaWeight = 0.75f, visualOnly = false },
                new TileElement { variant = MakeTreeVariant(10),   areaWeight = 0.25f, visualOnly = true  });
            TilePropVisualBuilder.SpawnProps(type, go.transform, new HexCoord(-1, 0, 1));

            Assert.IsTrue(HasFlowerBillboards(go.transform), "FieldGrove相当は花が表示されるべき");
            Assert.Greater(CountTreePairs(go.transform), 0, "FieldGrove相当は木も表示されるべき");
        }

        // ── Collider不残存 ────────────────────────────────────────────

        [UnityTest]
        public IEnumerator GeneratedProps_HaveNoColliders()
        {
            // SetPropMaterialが呼ぶObject.Destroy(collider)はフレーム末まで反映が遅延されるため、
            // 1フレーム待ってから確認する（既存HexTileElementPropsTestsの注記と同じ既知の挙動）。
            var go = new GameObject();
            var type = MakeElementsTile(
                new TileElement { variant = MakeTreeVariant(6),   areaWeight = 0.5f },
                new TileElement { variant = MakeFlowerVariant(6), areaWeight = 0.5f });
            TilePropVisualBuilder.SpawnProps(type, go.transform, new HexCoord(4, -2, -2));

            yield return null;

            var colliders = go.GetComponentsInChildren<Collider>();
            Assert.AreEqual(0, colliders.Length, "プレビュー生成物にColliderが残ってはならない");
        }

        // ── 生成数の一致確認（ComputeElementPropCountとの整合） ────────────

        [Test]
        public void TreeCount_MatchesComputeElementPropCount()
        {
            var go = new GameObject();
            var type = MakeElementsTile(
                new TileElement { variant = MakeTreeVariant(10), areaWeight = 1f });
            TilePropVisualBuilder.SpawnProps(type, go.transform, new HexCoord(5, -3, -2));

            // internal staticのためNonPublic|Staticで取得できる
            var method = typeof(HexTile).GetMethod("ComputeElementPropCount", BindingFlags.NonPublic | BindingFlags.Static);
            int expected = (int)method.Invoke(null, new object[] { 10, 1f });

            Assert.AreEqual(expected, CountTreePairs(go.transform));
        }
    }
}
