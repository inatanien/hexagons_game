// 役割: 「配置ゴーストを回転すると木の板が横を向いたまま残る」不具合の回帰テスト。
//
//       ★不具合の構造
//         TreeBillboardSystem は「カメラが動いたか」だけで全体更新するかを決める。
//         配置ゴーストは カメラ静止のまま親だけが60度ずつ回る ため更新が丸ごと省かれ、
//         板が親の回転を継承したまま残る（実測: 誤差 最大173.7度＝ほぼ裏返し）。
//         RealignUnder(root) が、その部分木だけを組み直す。
//
//       ★このテストが守りたいこと
//         1. 回した後に再整列すれば必ず正対する（本体）
//         2. 指定した root の外は触らない（部分更新であること）
//         3. 部分更新の後でも、カメラが動けば従来どおり全体が直る（既存挙動の維持）
//         4. カメラ静止時に毎フレーム更新へ退行していない（性能特性の維持）

using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ElfVillage.Tiles;

namespace ElfVillage.Tests
{
    public class PreviewBillboardRotationTests
    {
        /// <summary>再整列後に許す正対誤差。実測では0.000度になる。</summary>
        private const float FacingTolerance = 0.01f;

        private readonly List<Object> _spawned = new();

        private Camera              _camera;
        private TreeBillboardSystem _system;

        [SetUp]
        public void SetUp()
        {
            var camGo = Track(new GameObject("TestCamera"));
            _camera = camGo.AddComponent<Camera>();
            camGo.tag = "MainCamera";                       // ResolveCamera が Camera.main を見るため
            camGo.transform.position = new Vector3(0f, 9f, -12f);
            camGo.transform.rotation = Quaternion.Euler(40f, 0f, 0f);

            _system = BuildSystem();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _spawned) if (o != null) Object.DestroyImmediate(o);
            _spawned.Clear();
        }

        private T Track<T>(T o) where T : Object { _spawned.Add(o); return o; }

        /// <summary>
        /// 画像を設定した TreeBillboardSystem を用意する。
        /// ★PlayModeでは AddComponent した瞬間に Awake が走るため、
        ///   非アクティブで生成 → SerializeField を設定 → アクティブ化 の順にする。
        /// </summary>
        private TreeBillboardSystem BuildSystem()
        {
            var go = Track(new GameObject("TreeBillboards"));
            go.SetActive(false);

            var system = go.AddComponent<TreeBillboardSystem>();

            var tex = Track(new Texture2D(4, 4));
            var f = typeof(TreeBillboardSystem).GetField("_treeTextures",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(f, "_treeTextures が見つからない");
            f.SetValue(system, new[] { tex });

            go.SetActive(true);   // ここで Awake が走り、Material と Instance が揃う
            Assert.IsTrue(system.HasTextures, "画像が設定できていない");
            return system;
        }

        /// <summary>タイル1枚ぶんの親を作り、その配下に木の板を count 本生やす。</summary>
        private Transform BuildTile(Vector3 position, int count)
        {
            var root = Track(new GameObject("PreviewRoot"));
            root.transform.position = position;

            for (int i = 0; i < count; i++)
            {
                // 実際の配置と同じくタイル内へ散らす（1点に固まると誤差が出ない）
                float angle  = i * 137.50776f * Mathf.Deg2Rad;
                float radius = Mathf.Sqrt((i + 0.5f) / count) * 1.70f;
                var   offset = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);

                Assert.IsTrue(_system.TrySpawnTree(root.transform, offset, 0.16f, i * 40361 + 7919),
                    $"{i}本目の板を生成できなかった");
            }
            return root.transform;
        }

        private static List<Transform> Billboards(Transform root)
        {
            var list = new List<Transform>();
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == "TreeBillboard") list.Add(t);
            return list;
        }

        /// <summary>その板からカメラ位置への水平正対からのズレ（度）。0なら正しく正対している。</summary>
        private float FacingError(Transform billboard)
        {
            Vector3 want = billboard.position - _camera.transform.position; want.y = 0f;
            Vector3 have = billboard.forward;                               have.y = 0f;
            if (want.sqrMagnitude < 1e-8f || have.sqrMagnitude < 1e-8f) return 0f;
            return Vector3.Angle(have.normalized, want.normalized);
        }

        private float MaxFacingError(Transform root)
        {
            float max = 0f;
            foreach (var b in Billboards(root)) max = Mathf.Max(max, FacingError(b));
            return max;
        }

        // ══ 1. 前提確認: 再整列しなければ本当に壊れる ═══════════════════

        [UnityTest]
        public IEnumerator Rotating_WithoutRealign_BreaksFacing()
        {
            // ★このテストが落ちるようになったら、下の回帰テストが
            //   「そもそも壊れない状況」を確認しているだけになっている。
            var root = BuildTile(Vector3.zero, 24);
            yield return null;

            Assert.Less(MaxFacingError(root), FacingTolerance, "生成直後は正対しているはず");

            root.rotation = Quaternion.Euler(0f, 120f, 0f);
            yield return null;   // カメラは動かさない → 全体更新は省かれる

            Assert.Greater(MaxFacingError(root), 30f,
                "親を120度回しても板がズレない＝不具合が再現できていない");
        }

        // ══ 2. 回帰の本体: 再整列すれば必ず正対する ═════════════════════

        [UnityTest]
        public IEnumerator RealignUnder_RestoresFacing_AtEveryRotationStep()
        {
            var root = BuildTile(Vector3.zero, 24);
            yield return null;

            // rotation 0〜5（0〜300度）の全段を確認する
            for (int step = 0; step < 6; step++)
            {
                root.rotation = Quaternion.Euler(0f, step * 60f, 0f);
                _system.RealignUnder(root);

                Assert.Less(MaxFacingError(root), FacingTolerance,
                    $"rotation={step}（{step * 60}度）で正対していない（最大 {MaxFacingError(root):F3}度）");
            }
        }

        [UnityTest]
        public IEnumerator RealignUnder_SurvivesRepeatedRotation_WithoutCameraMovement()
        {
            // 実際の操作（Rキー連打）と同じく、カメラを一切動かさずに何周も回す
            var root = BuildTile(Vector3.zero, 24);
            yield return null;

            Vector3 cameraAtStart = _camera.transform.position;
            for (int i = 0; i < 13; i++)
            {
                root.rotation = Quaternion.Euler(0f, (i % 6) * 60f, 0f);
                _system.RealignUnder(root);
                yield return null;

                Assert.Less(MaxFacingError(root), FacingTolerance, $"{i + 1}回目の回転で正対していない");
            }
            Assert.AreEqual(cameraAtStart, _camera.transform.position, "テスト中にカメラが動いている");
        }

        // ══ 3. 指定した root の外は触らない ═════════════════════════════

        [UnityTest]
        public IEnumerator RealignUnder_DoesNotTouchOtherSubtrees()
        {
            var target = BuildTile(new Vector3(-4f, 0f, 0f), 12);
            var other  = BuildTile(new Vector3( 4f, 0f, 0f), 12);
            yield return null;

            // 両方を同じだけ回して、両方ともズレた状態を作る
            target.rotation = Quaternion.Euler(0f, 120f, 0f);
            other.rotation  = Quaternion.Euler(0f, 120f, 0f);

            var otherBefore = new List<Quaternion>();
            foreach (var b in Billboards(other)) otherBefore.Add(b.rotation);

            _system.RealignUnder(target);

            Assert.Less(MaxFacingError(target), FacingTolerance, "指定したrootが直っていない");

            var otherAfter = Billboards(other);
            for (int i = 0; i < otherAfter.Count; i++)
                Assert.AreEqual(0f, Quaternion.Angle(otherBefore[i], otherAfter[i].rotation), 0.0001f,
                    $"root外の板[{i}]が書き換えられている");
            Assert.Greater(MaxFacingError(other), 30f, "root外はズレたままであるべき");
        }

        // ══ 4. 部分更新の後でも、カメラ移動時は従来どおり全体が直る ══════

        [UnityTest]
        public IEnumerator RealignUnder_KeepsTheCameraMoveBehaviour()
        {
            // ★_appliedCameraPosition 等を RealignUnder が書き換えてしまうと、
            //   「カメラが動いたら全部直す」判定が部分更新だけで満たされ、
            //   root の外の木が永久に直らなくなる。それを行動として固定する。
            var target = BuildTile(new Vector3(-4f, 0f, 0f), 12);
            var other  = BuildTile(new Vector3( 4f, 0f, 0f), 12);
            yield return null;

            target.rotation = Quaternion.Euler(0f, 180f, 0f);
            other.rotation  = Quaternion.Euler(0f, 180f, 0f);

            _system.RealignUnder(target);
            Assert.Greater(MaxFacingError(other), 30f, "前提: root外はまだズレている");

            // カメラを動かす（しきい値 0.005 より十分大きく）
            _camera.transform.position += new Vector3(1.5f, 0f, 0f);
            yield return null;

            Assert.Less(MaxFacingError(other), FacingTolerance,
                "カメラを動かしてもroot外の木が直らない＝全体更新のゲートを部分更新が消費している");
            Assert.Less(MaxFacingError(target), FacingTolerance, "カメラ移動後にroot内が崩れている");
        }

        // ══ 5. カメラ静止時に毎フレーム更新へ退行していない ═════════════

        [UnityTest]
        public IEnumerator StillCamera_DoesNotRealignEveryFrame()
        {
            var root = BuildTile(Vector3.zero, 24);
            yield return null;

            // 一度ズレさせてから、カメラを動かさずに何フレームも待つ。
            // 毎フレーム更新へ退行していたら、ここで勝手に直ってしまう。
            root.rotation = Quaternion.Euler(0f, 120f, 0f);

            var afterRotation = new List<Quaternion>();
            foreach (var b in Billboards(root)) afterRotation.Add(b.rotation);

            for (int i = 0; i < 10; i++) yield return null;

            var now = Billboards(root);
            for (int i = 0; i < now.Count; i++)
                Assert.AreEqual(0f, Quaternion.Angle(afterRotation[i], now[i].rotation), 0.0001f,
                    $"カメラ静止中に板[{i}]の向きが変わった＝毎フレーム更新へ退行している");
        }
    }
}
