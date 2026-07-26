// 役割: タイル1枚ぶんの木陰が、実際にGameObjectとして正しく敷かれることを検証する。
//       Materialが共有されていること・Colliderが無いこと・タイル破棄で残らないことを見る。

using System.Collections;
using System.Reflection;
using NUnit.Framework;
using ElfVillage.HexGrid;
using ElfVillage.Tiles;
using UnityEngine;
using UnityEngine.TestTools;

namespace ElfVillage.Tests
{
    public class ForestTileShadePlayModeTests
    {
        private GameObject _systemsGO;
        private GameObject _cameraGO;
        private GameObject _root;

        private TileShadeSystem _shades;

        [SetUp]
        public void SetUp()
        {
            _cameraGO = new GameObject("TestCamera");
            _cameraGO.AddComponent<Camera>();
            _cameraGO.tag = "MainCamera";
            _cameraGO.transform.position = new Vector3(0f, 6f, -8f);
            _cameraGO.transform.rotation = Quaternion.Euler(40f, 0f, 0f);

            // 非アクティブで作ってからフィールドを入れ、最後に有効化する
            // （PlayModeではAddComponentの時点でAwakeが走るため）。
            _systemsGO = new GameObject("TestSystems");
            _systemsGO.SetActive(false);
            var billboards = _systemsGO.AddComponent<TreeBillboardSystem>();
            _shades = _systemsGO.AddComponent<TileShadeSystem>();
            SetPrivate(billboards, "_treeTextures", MakeTestTextures());
            _systemsGO.SetActive(true);

            _root = new GameObject("TestTileRoot");
        }

        [TearDown]
        public void TearDown()
        {
            // OnDestroyでランタイム生成のMaterial/Textureが解放されることを利用する。
            if (_root      != null) Object.DestroyImmediate(_root);
            if (_systemsGO != null) Object.DestroyImmediate(_systemsGO);
            if (_cameraGO  != null) Object.DestroyImmediate(_cameraGO);
        }

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

        private static TileType MakeFlowerTile(int propCount)
        {
            var v = ScriptableObject.CreateInstance<TerrainVariantDefinition>();
            v.category  = TileCategory.Field;
            v.propType  = TilePropType.Flower;
            v.propCount = propCount;

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

        // ══ 生成条件 ═════════════════════════════════════════════════════

        [UnityTest]
        public IEnumerator ForestTile_SpawnsExactlyOneShade()
        {
            var type = MakeForestTile(24);
            TilePropVisualBuilder.SpawnProps(type, _root.transform, new HexCoord(0, 0, 0));
            yield return null;

            Assert.AreEqual(1, CountByName(_root.transform, "TileShade"), "木陰はタイル1枚につき1つ");
        }

        [UnityTest]
        public IEnumerator NonTreeTile_DoesNotSpawnShade()
        {
            TilePropVisualBuilder.SpawnProps(MakeFlowerTile(8), _root.transform, new HexCoord(0, 0, 0));
            yield return null;

            Assert.AreEqual(0, CountByName(_root.transform, "TileShade"), "木の無いタイルに木陰が出ている");
        }

        // ══ Material の共有 ══════════════════════════════════════════════

        [UnityTest]
        public IEnumerator ShadeMaterial_IsSharedAcrossTiles()
        {
            var type = MakeForestTile(24);
            var a = new GameObject("A"); var b = new GameObject("B");
            TilePropVisualBuilder.SpawnProps(type, a.transform, new HexCoord(0, 0, 0));
            TilePropVisualBuilder.SpawnProps(type, b.transform, new HexCoord(5, -3, -2));
            yield return null;

            var ma = a.transform.Find("PreviewElementProps/TileShade").GetComponent<MeshRenderer>().sharedMaterial;
            var mb = b.transform.Find("PreviewElementProps/TileShade").GetComponent<MeshRenderer>().sharedMaterial;

            Assert.IsNotNull(ma);
            Assert.AreSame(ma, mb, "タイルごとにMaterialが複製されている");
            Assert.AreSame(ma, _shades.SharedMaterial, "共有Materialが使われていない");

            Object.DestroyImmediate(a);
            Object.DestroyImmediate(b);
        }

        // ══ 見た目の設定 ═════════════════════════════════════════════════

        [UnityTest]
        public IEnumerator Shade_DoesNotCastOrReceiveShadows()
        {
            var type = MakeForestTile(24);
            TilePropVisualBuilder.SpawnProps(type, _root.transform, new HexCoord(0, 0, 0));
            yield return null;

            var mr = _root.transform.Find("PreviewElementProps/TileShade").GetComponent<MeshRenderer>();
            Assert.AreEqual(UnityEngine.Rendering.ShadowCastingMode.Off, mr.shadowCastingMode);
            Assert.IsFalse(mr.receiveShadows);
        }

        [UnityTest]
        public IEnumerator Shade_IsFlatAndDeterministicPerCoord()
        {
            var type = MakeForestTile(24);
            var a = new GameObject("A");
            TilePropVisualBuilder.SpawnProps(type, a.transform, new HexCoord(4, -2, -2));
            yield return null;

            var shade = a.transform.Find("PreviewElementProps/TileShade");
            var euler = shade.localEulerAngles;

            // X = -90（=270）で水平。Yだけが座標由来の面内回転。
            Assert.AreEqual(270f, euler.x, 0.01f, "木陰が水平になっていない");
            Assert.AreEqual(TileShadeLayout.RotationDeg(4, -2), euler.y, 0.01f);

            float expectedSide = 4f * TileShadeLayout.SizeMultiplier(4, -2);
            float expectedX    = TileShadeLayout.IsMirrored(4, -2) ? -expectedSide : expectedSide;
            Assert.AreEqual(expectedX,    shade.localScale.x, 0.001f, "反転が反映されていない");
            Assert.AreEqual(expectedSide, shade.localScale.y, 0.001f);

            Object.DestroyImmediate(a);
        }

        [UnityTest]
        public IEnumerator Shade_IsDrawnBeforeOtherTransparents()
        {
            // 葉・花びら・精霊（RenderQueue 3000）より先に描かれること。
            var type = MakeForestTile(24);
            TilePropVisualBuilder.SpawnProps(type, _root.transform, new HexCoord(0, 0, 0));
            yield return null;

            var mat = _root.transform.Find("PreviewElementProps/TileShade").GetComponent<MeshRenderer>().sharedMaterial;
            Assert.Greater(mat.renderQueue, (int)UnityEngine.Rendering.RenderQueue.Geometry,
                "不透明の地面より先に描かれるとアルファが乗らない");
            Assert.Less(mat.renderQueue, (int)UnityEngine.Rendering.RenderQueue.Transparent,
                "他の半透明表現より後だと、それらが木陰に埋もれる");
        }

        // ══ Collider / Raycast ═══════════════════════════════════════════

        [UnityTest]
        public IEnumerator Shade_HasNoCollider()
        {
            var type = MakeForestTile(24);
            TilePropVisualBuilder.SpawnProps(type, _root.transform, new HexCoord(0, 0, 0));
            yield return null;   // Destroy(collider) はフレーム境界で反映される

            foreach (var t in _root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name != "TileShade") continue;
                Assert.IsNull(t.GetComponent<Collider>(), "木陰にColliderが残っている");
            }
        }

        [UnityTest]
        public IEnumerator Raycast_PassesThroughShade()
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
                "木か木陰がレイキャストを遮っている: " + info.collider.gameObject.name);

            Object.DestroyImmediate(target);
        }

        // ══ 破棄 ═════════════════════════════════════════════════════════

        [UnityTest]
        public IEnumerator DestroyingTile_LeavesNoShade()
        {
            var type = MakeForestTile(24);
            var temp = new GameObject("Temp");
            TilePropVisualBuilder.SpawnProps(type, temp.transform, new HexCoord(1, 1, -2));
            yield return null;

            Assert.AreEqual(1, CountByName(temp.transform, "TileShade"));

            Object.DestroyImmediate(temp);
            yield return null;

            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
                Assert.AreNotEqual("TileShade", t.name, "タイル破棄後に木陰が残っている");
        }

        [UnityTest]
        public IEnumerator DestroyingSystem_ReleasesSharedResources()
        {
            Assert.IsTrue(_shades.IsReady);
            var mat = _shades.SharedMaterial;
            Assert.IsNotNull(mat);

            Object.DestroyImmediate(_systemsGO);
            _systemsGO = null;
            yield return null;

            Assert.IsNull(TileShadeSystem.Instance, "破棄後もInstanceが残っている");
            Assert.IsTrue(mat == null, "共有Materialが解放されていない");
        }
    }
}
