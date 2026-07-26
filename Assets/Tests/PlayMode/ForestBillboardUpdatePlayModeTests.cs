// 役割: 木のビルボードが「カメラが動いたフレームだけ」向きを更新することを検証する。
//       フレームをまたぐ挙動なのでPlayModeでしか確かめられない。
//
//       ★向きは木ごとにカメラ位置へ水平正対する。カメラのYawと比べてはいけない
//         （Perspectiveカメラでは画面端の木ほどカメラYawからずれるのが正しい）。

using System.Collections;
using System.Reflection;
using NUnit.Framework;
using ElfVillage.HexGrid;
using ElfVillage.Tiles;
using UnityEngine;
using UnityEngine.TestTools;

namespace ElfVillage.Tests
{
    public class ForestBillboardUpdatePlayModeTests
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

            // 非アクティブで作ってからフィールドを入れ、最後に有効化する
            // （PlayModeではAddComponentの時点でAwakeが走るため）。
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

        private static object GetPrivate(object target, string field)
        {
            var f = target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(f, field + " が見つからない");
            return f.GetValue(target);
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

        /// <summary>その板が「自分の位置からカメラ位置へ水平正対」しているか。</summary>
        private void AssertFacesCamera(Transform billboard, string message)
        {
            Vector3 dir = billboard.position - _cameraGO.transform.position;
            dir.y = 0f;
            float expectedYaw = Quaternion.LookRotation(dir.normalized, Vector3.up).eulerAngles.y;
            Assert.AreEqual(0f, Mathf.DeltaAngle(expectedYaw, billboard.rotation.eulerAngles.y), 0.01f, message);
        }

        private Transform FirstBillboard(GameObject root)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == "TreeBillboard") return t;
            Assert.Fail("TreeBillboard が見つからない");
            return null;
        }

        // ══ 生成時の即時適用 ═════════════════════════════════════════════

        [UnityTest]
        public IEnumerator NewlyRegisteredTree_FacesCameraImmediately()
        {
            var type = MakeForestTile(24);

            // 1枚目で向きを確定させる。
            TilePropVisualBuilder.SpawnProps(type, _root.transform, new HexCoord(0, 0, 0));
            yield return null;

            // カメラを止めたまま2枚目を置く。LateUpdateは省かれるので、
            // 登録時に向けていなければ横を向いたまま取り残される。
            var later = new GameObject("Later");
            TilePropVisualBuilder.SpawnProps(type, later.transform, new HexCoord(7, -4, -3));
            yield return null;

            foreach (var t in later.GetComponentsInChildren<Transform>(true))
            {
                if (t.name != "TreeBillboard") continue;
                AssertFacesCamera(t, "後から生えた木がカメラを向いていない");
            }

            Object.DestroyImmediate(later);
        }

        [UnityTest]
        public IEnumerator FirstTileAfterSceneStart_GetsOriented()
        {
            var type = MakeForestTile(24);
            TilePropVisualBuilder.SpawnProps(type, _root.transform, new HexCoord(0, 0, 0));
            yield return null;
            yield return null;

            foreach (var t in _root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name != "TreeBillboard") continue;
                AssertFacesCamera(t, "最初のタイルの木が向けられていない");
            }
        }

        // ══ カメラ静止中はスキップ ═══════════════════════════════════════

        [UnityTest]
        public IEnumerator CameraStill_SkipsBillboardUpdate()
        {
            var type = MakeForestTile(24);
            TilePropVisualBuilder.SpawnProps(type, _root.transform, new HexCoord(0, 0, 0));
            yield return null;
            yield return null;

            // わざと向きを壊す。カメラが動いていなければ直されない＝更新を省いている証拠。
            var sample = FirstBillboard(_root);
            sample.rotation = Quaternion.Euler(0f, 123.456f, 0f);
            yield return null;
            yield return null;

            Assert.AreEqual(123.456f, sample.rotation.eulerAngles.y, 0.01f,
                "カメラが静止しているのにビルボードが更新されている");
        }

        [UnityTest]
        public IEnumerator TinyCameraMove_BelowThreshold_SkipsUpdate()
        {
            var type = MakeForestTile(24);
            TilePropVisualBuilder.SpawnProps(type, _root.transform, new HexCoord(0, 0, 0));
            yield return null;
            yield return null;

            var sample = FirstBillboard(_root);
            sample.rotation = Quaternion.Euler(0f, 55f, 0f);
            _cameraGO.transform.position += new Vector3(0.002f, 0f, 0f);   // < 0.005
            yield return null;
            yield return null;

            Assert.AreEqual(55f, sample.rotation.eulerAngles.y, 0.01f,
                "しきい値未満の移動で更新が走っている");
        }

        [UnityTest]
        public IEnumerator TinyYawChange_BelowThreshold_SkipsUpdate()
        {
            var type = MakeForestTile(24);
            TilePropVisualBuilder.SpawnProps(type, _root.transform, new HexCoord(0, 0, 0));
            yield return null;
            yield return null;

            var sample = FirstBillboard(_root);
            sample.rotation = Quaternion.Euler(0f, 55f, 0f);
            _cameraGO.transform.rotation = Quaternion.Euler(40f, 0.02f, 0f);   // < 0.05度
            yield return null;
            yield return null;

            Assert.AreEqual(55f, sample.rotation.eulerAngles.y, 0.01f,
                "しきい値未満のYaw変化で更新が走っている");
        }

        // ══ カメラが動いたら全て更新 ═════════════════════════════════════

        [UnityTest]
        public IEnumerator CameraYawChange_UpdatesAllBillboards()
        {
            var type = MakeForestTile(24);
            TilePropVisualBuilder.SpawnProps(type, _root.transform, new HexCoord(0, 0, 0));
            yield return null;
            yield return null;

            _cameraGO.transform.rotation = Quaternion.Euler(40f, 63f, 0f);
            yield return null;
            yield return null;

            int checkedCount = 0;
            foreach (var t in _root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name != "TreeBillboard") continue;
                checkedCount++;
                AssertFacesCamera(t, "Yaw変更後に向きが更新されていない木がある");
            }
            Assert.AreEqual(24, checkedCount);
        }

        [UnityTest]
        public IEnumerator CameraPan_UpdatesAllBillboards()
        {
            // ★正対方式では、カメラが平行移動しただけでも各木の正しい向きが変わる。
            //   ここを省くと画面端の木が横を向いたまま取り残される。
            var type = MakeForestTile(24);
            TilePropVisualBuilder.SpawnProps(type, _root.transform, new HexCoord(0, 0, 0));
            yield return null;
            yield return null;

            _cameraGO.transform.position += new Vector3(5f, 0f, 3f);   // 平行移動のみ（Yawは不変）
            yield return null;
            yield return null;

            int checkedCount = 0;
            foreach (var t in _root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name != "TreeBillboard") continue;
                checkedCount++;
                AssertFacesCamera(t, "平行移動後に向きが更新されていない木がある");
            }
            Assert.AreEqual(24, checkedCount);
        }

        [UnityTest]
        public IEnumerator SlowRotation_KeepsFacingWithinThreshold()
        {
            // ゆっくり回したときに向きが階段状に取り残されないこと。
            var type = MakeForestTile(24);
            TilePropVisualBuilder.SpawnProps(type, _root.transform, new HexCoord(0, 0, 0));
            yield return null;

            for (float yaw = 0f; yaw <= 2f; yaw += 0.4f)
            {
                _cameraGO.transform.rotation = Quaternion.Euler(40f, yaw, 0f);
                yield return null;
                yield return null;
                AssertFacesCamera(FirstBillboard(_root), $"yaw={yaw} で追従できていない");
            }
        }

        // ══ 向きの規則 ═══════════════════════════════════════════════════

        [UnityTest]
        public IEnumerator EachBillboard_FacesItsOwnDirection_NotASharedYaw()
        {
            // 「全ての板をカメラYawで揃える」方式へ退行していないことの回帰テスト。
            // Perspectiveカメラでそれをやると画面端の木が最大約67度ずれ、見かけ幅が39%まで潰れる。
            var type = MakeForestTile(24);
            _cameraGO.transform.position = new Vector3(0f, 3f, -3f);   // 近づけて角度差を出す
            TilePropVisualBuilder.SpawnProps(type, _root.transform, new HexCoord(0, 0, 0));
            yield return null;
            yield return null;

            float min = float.MaxValue, max = float.MinValue;
            foreach (var t in _root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name != "TreeBillboard") continue;
                float y = t.rotation.eulerAngles.y;
                min = Mathf.Min(min, y);
                max = Mathf.Max(max, y);
            }
            Assert.Greater(max - min, 5f,
                "全ての木が同じ向きになっている（カメラ平面ビルボードへ退行している）");
        }

        [UnityTest]
        public IEnumerator Billboards_NeverTiltVertically()
        {
            var type = MakeForestTile(24);
            TilePropVisualBuilder.SpawnProps(type, _root.transform, new HexCoord(0, 0, 0));

            foreach (var pitch in new[] { 10f, 40f, 89f })
            {
                _cameraGO.transform.rotation = Quaternion.Euler(pitch, pitch * 2f, 0f);
                yield return null;
                yield return null;

                foreach (var t in _root.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name != "TreeBillboard") continue;
                    var e = t.rotation.eulerAngles;
                    Assert.AreEqual(0f, Mathf.DeltaAngle(0f, e.x), 0.01f, $"pitch={pitch} で木が前後に倒れている");
                    Assert.AreEqual(0f, Mathf.DeltaAngle(0f, e.z), 0.01f, $"pitch={pitch} で木が左右に倒れている");
                }
            }
        }

        // ══ 破棄・カメラ不在 ═════════════════════════════════════════════

        [UnityTest]
        public IEnumerator DestroyedBillboards_AreRemovedSafely()
        {
            var type = MakeForestTile(24);
            var temp = new GameObject("Temp");
            TilePropVisualBuilder.SpawnProps(type, temp.transform, new HexCoord(0, 0, 0));
            yield return null;

            Object.DestroyImmediate(temp);                       // 登録済みの参照が全てnullになる
            yield return null;

            // 例外なく更新が回りきり、リストから消えていること。
            _cameraGO.transform.rotation = Quaternion.Euler(40f, 90f, 0f);
            yield return null;
            yield return null;

            var list = (System.Collections.Generic.List<Transform>)GetPrivate(_billboards, "_billboards");
            Assert.AreEqual(0, list.Count, "破棄済みの登録が残っている");
        }

        [UnityTest]
        public IEnumerator NoMainCamera_DoesNotThrowAndRecovers()
        {
            var type = MakeForestTile(24);
            TilePropVisualBuilder.SpawnProps(type, _root.transform, new HexCoord(0, 0, 0));
            yield return null;

            _cameraGO.SetActive(false);                          // Camera.main が取れなくなる
            SetPrivate(_billboards, "_camera", null);
            yield return null;
            yield return null;                                   // ここで例外が出れば失敗する

            _cameraGO.SetActive(true);
            _cameraGO.transform.rotation = Quaternion.Euler(40f, 210f, 0f);
            yield return null;
            yield return null;

            foreach (var t in _root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name != "TreeBillboard") continue;
                AssertFacesCamera(t, "カメラ復帰後に向きが直っていない");
            }
        }
    }
}
