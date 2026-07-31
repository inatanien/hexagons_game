// 役割: 木のビルボードが「カメラへ水平正対する」向きの計算を固定する。
//
//       ★この計算が world rotation を絶対値で返すことが、今回の不具合修正の土台になっている。
//         親（配置ゴースト）が何度回っていても同じ向きを返すからこそ、
//         回転後に適用し直すだけで板が直る。
//         ここが「親からの相対」になった瞬間、RealignUnder は意味を失う。
//
//       Euler角の直接比較は避け、forward / up のベクトルで確認する
//       （同じ向きでもEuler表現は一意でないため）。

using NUnit.Framework;
using UnityEngine;
using ElfVillage.Tiles;

namespace ElfVillage.Tests
{
    public class BillboardFacingTests
    {
        private const float Tolerance = 0.0005f;

        /// <summary>板からカメラへ向かう水平方向（＝板のforwardが向くべき向き）。</summary>
        private static Vector3 ExpectedForward(Vector3 billboardPos, Vector3 cameraPos)
        {
            Vector3 d = billboardPos - cameraPos;
            d.y = 0f;
            return d.normalized;
        }

        // ══ カメラ方向を向くこと ═════════════════════════════════════════

        [Test]
        public void Facing_ForwardMatchesTheHorizontalDirectionToCamera()
        {
            // 4方位 + 斜め。カメラは板より高い位置に置く（実際の見下ろし配置に近い）。
            var billboard = new Vector3(0f, 0.16f, 0f);
            var cameras = new[]
            {
                new Vector3( 0f, 8f, -10f),
                new Vector3( 0f, 8f,  10f),
                new Vector3(-10f, 8f,  0f),
                new Vector3( 10f, 8f,  0f),
                new Vector3( 7f, 8f, -7f),
                new Vector3(-3f, 20f, 4f),
            };

            foreach (var cam in cameras)
            {
                Assert.IsTrue(TreeBillboardSystem.TryComputeFacing(billboard, cam, out Quaternion rot),
                    $"cam={cam} で向きを算出できなかった");

                Vector3 forward  = rot * Vector3.forward;
                Vector3 expected = ExpectedForward(billboard, cam);

                Assert.AreEqual(0f, forward.y, Tolerance, $"cam={cam} でforwardが水平でない");
                Assert.AreEqual(1f, Vector3.Dot(forward.normalized, expected), Tolerance,
                    $"cam={cam} でカメラ方向を向いていない（forward={forward}, expected={expected}）");
            }
        }

        [Test]
        public void Facing_KeepsTheBillboardUpright()
        {
            // ★上下に傾けない。傾けると見下ろしたときに木が地面へ寝てしまう。
            var cameras = new[]
            {
                new Vector3(0f,  0.16f, -10f),   // ほぼ真横から
                new Vector3(0f, 30f,    -2f),    // かなり上から
                new Vector3(0f, -12f,    6f),    // 下から
            };

            foreach (var cam in cameras)
            {
                Assert.IsTrue(TreeBillboardSystem.TryComputeFacing(new Vector3(1f, 0.16f, 2f), cam, out Quaternion rot));

                Vector3 up = rot * Vector3.up;
                Assert.AreEqual(1f, Vector3.Dot(up, Vector3.up), Tolerance,
                    $"cam={cam} で板が傾いている（up={up}）");
            }
        }

        // ══ 向きを決められない場合 ═══════════════════════════════════════

        [Test]
        public void Facing_WhenCameraIsDirectlyAboveOrBelow_ReturnsFalse()
        {
            var billboard = new Vector3(4f, 0.16f, -3f);

            // 水平距離ゼロ（真上・真下・同一点）では前方向が決まらない
            foreach (var cam in new[]
            {
                new Vector3(4f,  50f, -3f),
                new Vector3(4f, -50f, -3f),
                billboard,
            })
            {
                Assert.IsFalse(TreeBillboardSystem.TryComputeFacing(billboard, cam, out Quaternion rot),
                    $"cam={cam} で向きを決められてしまっている");
                Assert.AreEqual(Quaternion.identity, rot, "false時はidentityを返す約束");
            }
        }

        [Test]
        public void Facing_JustOutsideTheDegenerateRange_IsStillComputed()
        {
            // しきい値（水平距離の二乗 < 0.000001）のすぐ外側では、通常どおり算出できること。
            var billboard = new Vector3(0f, 0.16f, 0f);
            var cam       = new Vector3(0.01f, 9f, 0f);

            Assert.IsTrue(TreeBillboardSystem.TryComputeFacing(billboard, cam, out Quaternion rot));
            Vector3 forward = rot * Vector3.forward;
            Assert.AreEqual(1f, Vector3.Dot(forward, ExpectedForward(billboard, cam)), Tolerance);
        }

        // ══ 親の回転から独立であること（今回の中核） ═════════════════════

        [Test]
        public void Facing_IsIndependentOfParentRotation()
        {
            // ★配置ゴーストは親だけが60度ずつ回る。板のworld位置が同じなら、
            //   親が何度回っていても同じworld rotationを適用すべき
            //   ＝この計算は親を引数に取らず、world座標だけで決まる。
            var cameraPos   = new Vector3(0f, 9f, -12f);
            var worldPos    = new Vector3(1.2f, 0.16f, -0.4f);

            Assert.IsTrue(TreeBillboardSystem.TryComputeFacing(worldPos, cameraPos, out Quaternion expected));

            var parent = new GameObject("Parent");
            var child  = new GameObject("TreeBillboard");
            try
            {
                child.transform.SetParent(parent.transform, true);

                foreach (int step in new[] { 0, 1, 2, 3, 4, 5 })
                {
                    parent.transform.rotation = Quaternion.Euler(0f, step * 60f, 0f);
                    // 親を回した後で、板のworld位置は毎回同じ場所に置き直す
                    child.transform.position = worldPos;

                    Assert.IsTrue(TreeBillboardSystem.TryComputeFacing(
                        child.transform.position, cameraPos, out Quaternion actual));

                    Assert.AreEqual(0f, Quaternion.Angle(expected, actual), 0.001f,
                        $"親を{step * 60}度回すと算出結果が変わっている");

                    // 実際に適用したときの世界での向きも一致すること
                    child.transform.rotation = actual;
                    Assert.AreEqual(1f,
                        Vector3.Dot(child.transform.forward, ExpectedForward(worldPos, cameraPos)),
                        Tolerance, $"親を{step * 60}度回した状態で正対していない");
                }
            }
            finally
            {
                Object.DestroyImmediate(child);
                Object.DestroyImmediate(parent);
            }
        }
    }
}
