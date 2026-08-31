// 役割: OutlineTraceSampler（輪の上を走る光の点列の切り出し）を固定する（Stage 3）。
//
//       ★頂点番号ではなく距離で扱っていることを守る。
//         辺の長さが揃っていない輪でも、進行度に対して見た目の速度が一定になる。
//
//       ★輪の終端をまたぐときに、線が輪の反対側へ飛ばないこと。
//         ここが崩れると、一周する瞬間だけ光が輪を横切る線になる。

using System.Collections.Generic;
using NUnit.Framework;
using ElfVillage.Tiles;
using UnityEngine;

namespace ElfVillage.Tests
{
    public class OutlineTraceSamplerTests
    {
        /// <summary>一辺1の正方形の輪（周長4）。距離の計算を目で追えるようにあえて単純な形にする。</summary>
        private static List<Vector3> Square() => new()
        {
            new Vector3(0f, 0f, 0f),
            new Vector3(1f, 0f, 0f),
            new Vector3(1f, 0f, 1f),
            new Vector3(0f, 0f, 1f),
        };

        /// <summary>辺の長さが揃っていない輪。距離ベースであることを確かめるために使う。</summary>
        private static List<Vector3> UnevenTriangle() => new()
        {
            new Vector3(0f, 0f, 0f),
            new Vector3(4f, 0f, 0f),
            new Vector3(0f, 0f, 3f),
        };

        private static float PathLength(List<Vector3> points)
        {
            float sum = 0f;
            for (int i = 1; i < points.Count; i++) sum += Vector3.Distance(points[i - 1], points[i]);
            return sum;
        }

        // ── 周長 ────────────────────────────────────────────────────────

        [Test]
        public void Perimeter_IncludesTheClosingSegment()
        {
            Assert.AreEqual(4f,  OutlineTraceSampler.Perimeter(Square()), 1e-4f);
            Assert.AreEqual(12f, OutlineTraceSampler.Perimeter(UnevenTriangle()), 1e-4f, "4 + 5 + 3");
            Assert.AreEqual(0f,  OutlineTraceSampler.Perimeter(null));
        }

        // ── 先端の位置 ──────────────────────────────────────────────────

        [Test]
        public void HeadAtZero_StartsAtTheFirstCorner()
        {
            var results = new List<Vector3>();
            OutlineTraceSampler.Sample(Square(), 0f, 0f, results);

            Assert.GreaterOrEqual(results.Count, 1);
            Assert.AreEqual(Vector3.zero, results[results.Count - 1], "t=0の先端は輪の先頭にあるはず");
        }

        [Test]
        public void HeadAtFullPerimeter_ComesBackToTheFirstCorner()
        {
            var loop    = Square();
            var results = new List<Vector3>();
            OutlineTraceSampler.Sample(loop, 4f, 4f, results);

            Assert.AreEqual(0f, Vector3.Distance(Vector3.zero, results[results.Count - 1]), 1e-4f,
                "一周したら先頭へ戻るはず");
        }

        [Test]
        public void SamplingIsDistanceBased_NotVertexBased()
        {
            // 周長12の三角形。距離6の位置は、長さ4の辺を過ぎて長さ5の辺の途中にある。
            // 頂点番号で補間していたら、ここは3辺の真ん中（頂点1と2の中間）になってしまう
            var loop    = UnevenTriangle();
            var results = new List<Vector3>();
            OutlineTraceSampler.Sample(loop, 6f, 6f, results);

            Vector3 head     = results[results.Count - 1];
            Vector3 expected = Vector3.Lerp(loop[1], loop[2], 2f / 5f);   // 1辺目4 + 2辺目の2

            Assert.AreEqual(0f, Vector3.Distance(expected, head), 1e-4f);
        }

        // ── 尾の長さ ────────────────────────────────────────────────────

        [Test]
        public void TailLength_MatchesTheRequestedDistance()
        {
            var results = new List<Vector3>();
            OutlineTraceSampler.Sample(Square(), 1.5f, 2.5f, results);

            Assert.AreEqual(1f, PathLength(results), 1e-4f, "切り出した長さは指定どおりのはず");
            Assert.GreaterOrEqual(results.Count, 2);
        }

        [Test]
        public void TailLongerThanTheLoop_IsClampedToOneLap()
        {
            var results = new List<Vector3>();
            OutlineTraceSampler.Sample(Square(), 0f, 100f, results);

            Assert.AreEqual(4f, PathLength(results), 1e-4f, "一周ぶんで止まり、輪を二重に描かないはず");
        }

        // ── 終端をまたぐ ────────────────────────────────────────────────

        [Test]
        public void CrossingTheEnd_StaysContinuous()
        {
            var loop    = Square();
            var results = new List<Vector3>();
            // 尾が輪の終わり、先端が輪の始まりを少し越えたところ
            OutlineTraceSampler.Sample(loop, 3.5f, 4.5f, results);

            Assert.AreEqual(1f, PathLength(results), 1e-4f);

            // 隣り合う点は必ず同じ辺の上にある＝1辺の長さを超えて飛ばない
            for (int i = 1; i < results.Count; i++)
                Assert.LessOrEqual(Vector3.Distance(results[i - 1], results[i]), 1f + 1e-4f,
                    "輪の反対側へ線が飛んではいけない");

            Assert.AreEqual(0f, Vector3.Distance(new Vector3(0.5f, 0f, 0f), results[results.Count - 1]), 1e-4f,
                "先端は輪の始まりを0.5だけ越えた位置のはず");
        }

        [Test]
        public void CrossingTheEnd_PassesThroughTheFirstCorner()
        {
            var results = new List<Vector3>();
            OutlineTraceSampler.Sample(Square(), 3.5f, 4.5f, results);

            bool passesOrigin = results.Exists(p => Vector3.Distance(p, Vector3.zero) < 1e-4f);
            Assert.IsTrue(passesOrigin, "輪の角はそのまま線の角として残るはず");
        }

        // ── 壊れた入力・決定性 ──────────────────────────────────────────

        [Test]
        public void DegenerateInput_ProducesNoPoints()
        {
            var results = new List<Vector3> { Vector3.one };

            OutlineTraceSampler.Sample(null, 0f, 1f, results);
            Assert.AreEqual(0, results.Count, "nullでは何も返さないはず");

            OutlineTraceSampler.Sample(new List<Vector3> { Vector3.zero }, 0f, 1f, results);
            Assert.AreEqual(0, results.Count, "点が1つでは輪にならない");

            var samePoint = new List<Vector3> { Vector3.zero, Vector3.zero, Vector3.zero };
            OutlineTraceSampler.Sample(samePoint, 0f, 1f, results);
            Assert.AreEqual(0, results.Count, "長さ0の輪でも止まらないはず");
        }

        [Test]
        public void ShortLoop_WithLongTail_DoesNotBreak()
        {
            // 六角形1枚ぶん（6辺）の短い輪に、輪より長い尾を指定した場合
            var loop = new List<Vector3>();
            for (int i = 0; i < 6; i++)
            {
                float a = Mathf.Deg2Rad * 60f * i;
                loop.Add(new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)));
            }

            var results = new List<Vector3>();
            OutlineTraceSampler.Sample(loop, 0f, OutlineTraceSampler.Perimeter(loop) * 3f, results);

            Assert.AreEqual(OutlineTraceSampler.Perimeter(loop), PathLength(results), 1e-3f);
        }

        [Test]
        public void RepeatedCalls_ProduceIdenticalPoints()
        {
            var loop  = UnevenTriangle();
            var first = new List<Vector3>();
            OutlineTraceSampler.Sample(loop, 2.5f, 7.5f, first);

            for (int run = 0; run < 3; run++)
            {
                var again = new List<Vector3>();
                OutlineTraceSampler.Sample(loop, 2.5f, 7.5f, again);

                CollectionAssert.AreEqual(first, again, "同じ入力なら毎回同じ点列のはず");
            }
        }

        [Test]
        public void ResultsAreOrderedFromTailToHead()
        {
            var results = new List<Vector3>();
            OutlineTraceSampler.Sample(Square(), 0.25f, 1.75f, results);

            // 先頭が尾、末尾が先端。LineRendererの太さ・色カーブは0→1で尾→先端に対応する
            Assert.AreEqual(0f, Vector3.Distance(new Vector3(0.25f, 0f, 0f), results[0]), 1e-4f);
            Assert.AreEqual(0f, Vector3.Distance(new Vector3(1f, 0f, 0.75f), results[results.Count - 1]), 1e-4f);
        }
    }
}
