// 役割: Fieldタイル（花畑）の見た目を、実際にGameObjectを生成した状態で検証する。
//       ParticleSystemの粒の並び・接地・粒ごとの種は、実際に撒いてみないと確かめられない。

using System.Collections;
using System.Reflection;
using NUnit.Framework;
using ElfVillage.HexGrid;
using ElfVillage.Tiles;
using UnityEngine;
using UnityEngine.TestTools;

namespace ElfVillage.Tests
{
    public class FieldFlowerVisualPlayModeTests
    {
        private GameObject _systemsGO;
        private GameObject _root;
        private FlowerBillboardSystem _flowers;

        private const float TileHeight = 0.30f;

        [SetUp]
        public void SetUp()
        {
            // ★非アクティブで作ってから有効化する。PlayModeではAddComponentの時点でAwakeが走るため、
            //   フィールドを入れる前にアトラスが組まれてしまうのを避ける。
            _systemsGO = new GameObject("TestSystems");
            _systemsGO.SetActive(false);
            _flowers = _systemsGO.AddComponent<FlowerBillboardSystem>();
            _systemsGO.SetActive(true);

            _root = new GameObject("TestTileRoot");
        }

        [TearDown]
        public void TearDown()
        {
            if (_root      != null) Object.DestroyImmediate(_root);
            if (_systemsGO != null) Object.DestroyImmediate(_systemsGO);
        }

        private static TileType MakeFieldTile(int propCount)
        {
            var v = ScriptableObject.CreateInstance<TerrainVariantDefinition>();
            v.category    = TileCategory.Field;
            v.variantName = "TestField";
            v.propType    = TilePropType.Flower;
            v.propCount   = propCount;

            var t = ScriptableObject.CreateInstance<TileType>();
            t.elements = new[] { new TileElement { variant = v, areaWeight = 1f } };
            return t;
        }

        private static ParticleSystem FindFlowers(GameObject root)
        {
            foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
                if (ps.gameObject.name == "FlowerBillboards") return ps;
            return null;
        }

        private static ParticleSystem.Particle[] ReadParticles(ParticleSystem ps)
        {
            var buffer = new ParticleSystem.Particle[Mathf.Max(1, ps.particleCount)];
            int n = ps.GetParticles(buffer);
            var result = new ParticleSystem.Particle[n];
            System.Array.Copy(buffer, result, n);
            return result;
        }

        private static float ExpectedGroundY()
        {
            var lift = typeof(HexTile).GetField("PropLiftY", BindingFlags.NonPublic | BindingFlags.Static);
            return HexMeshBuilder.TopY(TileHeight) + (float)lift.GetValue(null);
        }

        // ══ 生成 ═════════════════════════════════════════════════════════

        [UnityTest]
        public IEnumerator FieldTile_Spawns20Flowers()
        {
            var type = MakeFieldTile(20);
            TilePropVisualBuilder.SpawnProps(type, _root.transform, new HexCoord(2, -1, -1));
            yield return null;

            var ps = FindFlowers(_root);
            Assert.IsNotNull(ps, "FlowerBillboards が生成されていない");
            Assert.AreEqual(20, ps.particleCount, "花が20粒ではない");
        }

        [UnityTest]
        public IEnumerator FieldTile_UsesExactlyOneParticleSystem()
        {
            // 「ParticleSystem 1個で20粒」という構成を維持していること。
            var type = MakeFieldTile(20);
            TilePropVisualBuilder.SpawnProps(type, _root.transform, new HexCoord(0, 0, 0));
            yield return null;

            Assert.AreEqual(1, _root.GetComponentsInChildren<ParticleSystem>(true).Length,
                "花タイルのParticleSystemが1個ではない");
        }

        // ══ 接地 ═════════════════════════════════════════════════════════

        [UnityTest]
        public IEnumerator Flowers_SitJustAboveTheTileTop()
        {
            var type = MakeFieldTile(20);
            TilePropVisualBuilder.SpawnProps(type, _root.transform, new HexCoord(0, 0, 0));
            yield return null;

            var ps = FindFlowers(_root);
            float expected = ExpectedGroundY();
            foreach (var p in ReadParticles(ps))
                Assert.AreEqual(expected, p.position.y, 0.0001f, "花の高さがタイル上面基準になっていない");
        }

        // ══ 広がり ═══════════════════════════════════════════════════════

        [UnityTest]
        public IEnumerator Flowers_ReachTowardTheTileEdge()
        {
            var type = MakeFieldTile(20);
            TilePropVisualBuilder.SpawnProps(type, _root.transform, new HexCoord(1, 0, -1));
            yield return null;

            var ps = FindFlowers(_root);
            float worst = 0f;
            foreach (var p in ReadParticles(ps))
                worst = Mathf.Max(worst, new Vector2(p.position.x, p.position.z).magnitude);

            // 辺の中点は約1.732。1.50のままだと端に花の無い縁が残る。
            Assert.Greater(worst, 1.55f, $"花がタイル端まで届いていない（実測 {worst:F3}）");
            Assert.LessOrEqual(worst, 1.76f, $"花が想定より外へ出ている（実測 {worst:F3}）");
        }

        // ══ 決定論（ゴーストと実配置の一致） ═════════════════════════════

        [UnityTest]
        public IEnumerator SameCoord_ProducesIdenticalFlowerField()
        {
            var type  = MakeFieldTile(20);
            var coord = new HexCoord(3, -2, -1);

            var a = new GameObject("A"); var b = new GameObject("B");
            TilePropVisualBuilder.SpawnProps(type, a.transform, coord);
            TilePropVisualBuilder.SpawnProps(type, b.transform, coord);
            yield return null;

            var pa = ReadParticles(FindFlowers(a));
            var pb = ReadParticles(FindFlowers(b));
            Assert.AreEqual(pa.Length, pb.Length, "生成数が一致しない");

            for (int i = 0; i < pa.Length; i++)
            {
                Assert.AreEqual(pa[i].position,   pb[i].position,   "位置が一致しない");
                Assert.AreEqual(pa[i].startSize,  pb[i].startSize,  0.0001f, "大きさが一致しない");
                Assert.AreEqual(pa[i].rotation,   pb[i].rotation,   0.0001f, "向きが一致しない");
                Assert.AreEqual(pa[i].randomSeed, pb[i].randomSeed, "絵柄を決める種が一致しない");
            }

            Object.DestroyImmediate(a);
            Object.DestroyImmediate(b);
        }

        [UnityTest]
        public IEnumerator DifferentCoords_ProduceDifferentFlowerFields()
        {
            var type = MakeFieldTile(20);
            var a = new GameObject("A"); var b = new GameObject("B");
            TilePropVisualBuilder.SpawnProps(type, a.transform, new HexCoord(0, 0, 0));
            TilePropVisualBuilder.SpawnProps(type, b.transform, new HexCoord(1, 0, -1));
            yield return null;

            var pa = ReadParticles(FindFlowers(a));
            var pb = ReadParticles(FindFlowers(b));

            bool anyDifferent = false;
            for (int i = 0; i < Mathf.Min(pa.Length, pb.Length); i++)
                if (pa[i].position != pb[i].position) { anyDifferent = true; break; }

            Assert.IsTrue(anyDifferent, "座標が違っても同じ並びになっている（花畑が反復して見える）");

            Object.DestroyImmediate(a);
            Object.DestroyImmediate(b);
        }

        // ══ 絵柄（アトラス） ═════════════════════════════════════════════

        [UnityTest]
        public IEnumerator Atlas_IsBuiltAndSharedAcrossTiles()
        {
            var type = MakeFieldTile(20);
            var a = new GameObject("A"); var b = new GameObject("B");
            TilePropVisualBuilder.SpawnProps(type, a.transform, new HexCoord(0, 0, 0));
            TilePropVisualBuilder.SpawnProps(type, b.transform, new HexCoord(5, -3, -2));
            yield return null;

            Assert.IsTrue(_flowers.IsReady, "アトラスが組まれていない");
            Assert.Greater(_flowers.ShapeCount, 1, "絵柄が1種類しかない（複数絵柄の構造になっていない）");

            var ma = FindFlowers(a).GetComponent<ParticleSystemRenderer>().sharedMaterial;
            var mb = FindFlowers(b).GetComponent<ParticleSystemRenderer>().sharedMaterial;
            Assert.AreSame(ma, mb, "タイルごとにMaterialが複製されている");
            Assert.AreSame(ma, _flowers.SharedMaterial, "共有Materialが使われていない");

            Object.DestroyImmediate(a);
            Object.DestroyImmediate(b);
        }

        [UnityTest]
        public IEnumerator TextureSheet_IsGridMatchingTheAtlas()
        {
            var type = MakeFieldTile(20);
            TilePropVisualBuilder.SpawnProps(type, _root.transform, new HexCoord(0, 0, 0));
            yield return null;

            var tsa = FindFlowers(_root).textureSheetAnimation;
            Assert.IsTrue(tsa.enabled, "TextureSheetAnimationが無効（絵柄が切り替わらない）");
            Assert.AreEqual(_flowers.ShapeCount, tsa.numTilesX, "格子の列数がアトラスの絵柄数と合っていない");
            Assert.AreEqual(1, tsa.numTilesY, "アトラスは横一列の想定");
        }

        [UnityTest]
        public IEnumerator EachFlower_GetsItsOwnDeterministicSeed()
        {
            var type = MakeFieldTile(20);
            TilePropVisualBuilder.SpawnProps(type, _root.transform, new HexCoord(4, -2, -2));
            yield return null;

            var particles = ReadParticles(FindFlowers(_root));
            var distinct  = new System.Collections.Generic.HashSet<uint>();
            foreach (var p in particles)
            {
                Assert.AreNotEqual(0u, p.randomSeed, "種が0の粒がある");
                distinct.Add(p.randomSeed);
            }
            Assert.AreEqual(particles.Length, distinct.Count, "同じ種の粒がある（絵柄が揃ってしまう）");
        }

        [UnityTest]
        public IEnumerator SystemAbsent_FallsBackWithoutError()
        {
            // Sceneにコンポーネントが無くても従来どおり単一スプライトで生成できること。
            Object.DestroyImmediate(_systemsGO);
            _systemsGO = null;
            yield return null;

            var type = MakeFieldTile(20);
            TilePropVisualBuilder.SpawnProps(type, _root.transform, new HexCoord(0, 0, 0));
            yield return null;

            var ps = FindFlowers(_root);
            Assert.IsNotNull(ps, "システム未設置で花が生成されない");
            Assert.AreEqual(20, ps.particleCount);
            Assert.IsFalse(ps.textureSheetAnimation.enabled, "絵柄が1つならTSAは無効のはず");
        }

        // ══ Collider ═════════════════════════════════════════════════════

        [UnityTest]
        public IEnumerator Flowers_HaveNoColliders()
        {
            var type = MakeFieldTile(20);
            TilePropVisualBuilder.SpawnProps(type, _root.transform, new HexCoord(0, 0, 0));
            yield return null;

            Assert.AreEqual(0, _root.GetComponentsInChildren<Collider>(true).Length,
                "花にColliderが付いている（タイル選択のレイキャストを妨げる）");
        }
    }
}
