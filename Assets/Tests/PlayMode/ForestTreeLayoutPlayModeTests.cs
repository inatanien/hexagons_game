// 役割: Forestタイルの木の「本数・並び・絵柄」が、実配置と配置ゴーストで一致することを
//       実際にGameObjectを生成して確認する。Collider残存もここで見る。

using System.Collections;
using System.Reflection;
using NUnit.Framework;
using ElfVillage.HexGrid;
using ElfVillage.Tiles;
using UnityEngine;
using UnityEngine.TestTools;

namespace ElfVillage.Tests
{
    public class ForestTreeLayoutPlayModeTests
    {
        private GameObject _systemsGO;
        private GameObject _cameraGO;
        private GameObject _root;

        private TreeBillboardSystem _billboards;

        [SetUp]
        public void SetUp()
        {
            _cameraGO = new GameObject("TestCamera");
            _cameraGO.AddComponent<Camera>();
            _cameraGO.tag = "MainCamera";
            _cameraGO.transform.position = new Vector3(0f, 6f, -8f);
            _cameraGO.transform.rotation = Quaternion.Euler(40f, 0f, 0f);

            // ★非アクティブで作ってからフィールドを入れ、最後に有効化する。
            //   PlayModeではAddComponentの時点でAwakeが走るため、そのままだと
            //   画像未設定のままMaterialが組まれてしまう。
            _systemsGO = new GameObject("TestSystems");
            _systemsGO.SetActive(false);
            _billboards = _systemsGO.AddComponent<TreeBillboardSystem>();
            SetPrivate(_billboards, "_treeTextures", MakeTestTextures());
            _systemsGO.SetActive(true);

            _root = new GameObject("TestTileRoot");
        }

        [TearDown]
        public void TearDown()
        {
            if (_root      != null) Object.DestroyImmediate(_root);
            if (_systemsGO != null) Object.DestroyImmediate(_systemsGO);
            if (_cameraGO  != null) Object.DestroyImmediate(_cameraGO);
        }

        // TreeVariantWeights が名前で重みを引くので、名前も本番と同じにしておく。
        private static Texture2D[] MakeTestTextures()
        {
            string[] names =
            {
                "Tree_01_Rounded_Layered", "Tree_02_Columnar",        "Tree_03_Wide_Dome",
                "Tree_04_Compact_Conifer", "Tree_05_Airy_Branches",   "Tree_06_Asymmetric",
                "Tree_07_Slender_Conifer", "Tree_08_Yellow_Clusters", "Tree_09_Deep_Green_Oval",
                "Tree_10_Lime_Egg",
            };
            var list = new Texture2D[names.Length];
            for (int i = 0; i < names.Length; i++)
                list[i] = new Texture2D(4, 4) { name = names[i] };
            return list;
        }

        private static void SetPrivate(object target, string field, object value)
        {
            var f = target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(f, field + " が見つからない");
            f.SetValue(target, value);
        }

        private static TileType MakeForestTile(int propCount)
        {
            var v = ScriptableObject.CreateInstance<TerrainVariantDefinition>();
            v.category    = TileCategory.Forest;
            v.variantName = "TestForest";
            v.propType    = TilePropType.Tree;
            v.propCount   = propCount;

            var t = ScriptableObject.CreateInstance<TileType>();
            t.elements = new[] { new TileElement { variant = v, areaWeight = 1f } };
            return t;
        }

        private static int CountByName(Transform root, string name)
        {
            int n = 0;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) n++;
            return n;
        }

        // ══ 本数 ═════════════════════════════════════════════════════════

        [UnityTest]
        public IEnumerator Preview_ForestTile_Spawns24Billboards()
        {
            var type = MakeForestTile(24);
            TilePropVisualBuilder.SpawnProps(type, _root.transform, new HexCoord(2, -1, -1));
            yield return null;

            Assert.AreEqual(24, CountByName(_root.transform, "TreeBillboard"), "ゴーストの木が24本ではない");
        }

        [UnityTest]
        public IEnumerator Preview_SameCoord_ProducesIdenticalTreeLayout()
        {
            // 実配置とゴーストは同じ関数・同じseedを通るので、同じ座標なら完全一致する。
            var type  = MakeForestTile(24);
            var coord = new HexCoord(3, -2, -1);

            var a = new GameObject("A"); var b = new GameObject("B");
            TilePropVisualBuilder.SpawnProps(type, a.transform, coord);
            TilePropVisualBuilder.SpawnProps(type, b.transform, coord);
            yield return null;

            var ta = a.GetComponentsInChildren<MeshRenderer>();
            var tb = b.GetComponentsInChildren<MeshRenderer>();
            Assert.AreEqual(ta.Length, tb.Length, "生成数が一致しない");

            for (int i = 0; i < ta.Length; i++)
            {
                Assert.AreEqual(ta[i].transform.localPosition, tb[i].transform.localPosition, "位置が一致しない");
                Assert.AreEqual(ta[i].transform.localScale,    tb[i].transform.localScale,    "大きさが一致しない");
                Assert.AreSame(ta[i].sharedMaterial, tb[i].sharedMaterial, "選ばれた絵柄が一致しない");
            }

            Object.DestroyImmediate(a);
            Object.DestroyImmediate(b);
        }

        [UnityTest]
        public IEnumerator Preview_DifferentCoords_ProduceDifferentLayout()
        {
            var type = MakeForestTile(24);
            var a = new GameObject("A"); var b = new GameObject("B");
            TilePropVisualBuilder.SpawnProps(type, a.transform, new HexCoord(0, 0, 0));
            TilePropVisualBuilder.SpawnProps(type, b.transform, new HexCoord(1, 0, -1));
            yield return null;

            var ta = a.GetComponentsInChildren<MeshRenderer>();
            var tb = b.GetComponentsInChildren<MeshRenderer>();

            bool anyDifferent = false;
            for (int i = 0; i < Mathf.Min(ta.Length, tb.Length); i++)
                if (ta[i].transform.localPosition != tb[i].transform.localPosition) { anyDifferent = true; break; }

            Assert.IsTrue(anyDifferent, "座標が違っても同じ並びになっている（森が反復して見える）");

            Object.DestroyImmediate(a);
            Object.DestroyImmediate(b);
        }

        [UnityTest]
        public IEnumerator Trees_UseSeveralDifferentVariants()
        {
            var type = MakeForestTile(24);
            TilePropVisualBuilder.SpawnProps(type, _root.transform, new HexCoord(3, -2, -1));
            yield return null;

            var seen = new System.Collections.Generic.HashSet<Material>();
            foreach (var mr in _root.GetComponentsInChildren<MeshRenderer>())
                if (mr.gameObject.name == "TreeBillboard") seen.Add(mr.sharedMaterial);

            Assert.Greater(seen.Count, 4, "1タイル内の木の絵柄が偏りすぎている");
        }

        // ══ Collider / Raycast ═══════════════════════════════════════════

        [UnityTest]
        public IEnumerator Trees_HaveNoColliders()
        {
            var type = MakeForestTile(24);
            TilePropVisualBuilder.SpawnProps(type, _root.transform, new HexCoord(0, 0, 0));
            yield return null;   // Destroy(collider) はフレーム境界で反映される

            foreach (var t in _root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name != "TreeBillboard") continue;
                Assert.IsNull(t.GetComponent<Collider>(), "木にColliderが残っている");
            }
        }

        [UnityTest]
        public IEnumerator Raycast_PassesThroughTrees()
        {
            var type = MakeForestTile(24);
            TilePropVisualBuilder.SpawnProps(type, _root.transform, new HexCoord(0, 0, 0));
            yield return null;

            var target = GameObject.CreatePrimitive(PrimitiveType.Cube);
            target.name = "FakeTile";
            target.transform.position   = new Vector3(0f, -1f, 0f);
            target.transform.localScale = new Vector3(6f, 0.3f, 6f);
            yield return null;

            bool hit = Physics.Raycast(new Vector3(0.3f, 8f, 0.2f), Vector3.down, out var info, 50f);
            Assert.IsTrue(hit, "レイが何にも当たらなかった");
            Assert.AreEqual("FakeTile", info.collider.gameObject.name,
                "木がレイキャストを遮っている: " + info.collider.gameObject.name);

            Object.DestroyImmediate(target);
        }

        // ══ タイル破棄 ═══════════════════════════════════════════════════

        [UnityTest]
        public IEnumerator DestroyingTile_LeavesNoTrees()
        {
            var type = MakeForestTile(24);
            var temp = new GameObject("Temp");
            TilePropVisualBuilder.SpawnProps(type, temp.transform, new HexCoord(1, 1, -2));
            yield return null;

            Assert.AreEqual(24, CountByName(temp.transform, "TreeBillboard"));

            Object.DestroyImmediate(temp);
            yield return null;

            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
                Assert.AreNotEqual("TreeBillboard", t.name, "タイル破棄後に木が残っている");
        }
    }
}
