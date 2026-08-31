// 役割: 祝福の光柱をどこに立てるか（CelebrationRayLayout）を固定する（Stage 4）。
//
//       ★立てるのは外周の辺の中点。
//         距離で等間隔に割ると辺の途中の中途半端な位置に落ちて、六角形のリズムから外れる。
//
//       ★間引きは端を切り捨てず、輪全体から均等に取る。
//         大きなクラスターで光の壁にならないよう上限を持たせてあるので、
//         そこで偏ると「片側だけ光っている」ように見えてしまう。

using System.Collections.Generic;
using NUnit.Framework;
using ElfVillage.Tiles;
using UnityEngine;

namespace ElfVillage.Tests
{
    public class CelebrationRayLayoutTests
    {
        /// <summary>一辺1の正方形（4辺）。中点が目で追える形にしてある。</summary>
        private static List<Vector3> Square() => new()
        {
            new Vector3(0f, 0f, 0f),
            new Vector3(1f, 0f, 0f),
            new Vector3(1f, 0f, 1f),
            new Vector3(0f, 0f, 1f),
        };

        /// <summary>指定した辺数の輪（円周上に等間隔）。間引きの確認用。</summary>
        private static List<Vector3> Ring(int edges)
        {
            var loop = new List<Vector3>(edges);
            for (int i = 0; i < edges; i++)
            {
                float a = Mathf.PI * 2f * i / edges;
                loop.Add(new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)));
            }
            return loop;
        }

        private static List<Vector3> Select(List<Vector3> loop, float density = 1f,
                                             int min = 3, int max = 20)
        {
            var results = new List<Vector3>();
            CelebrationRayLayout.SelectPositions(loop, density, min, max, results);
            return results;
        }

        // ── 基本 ────────────────────────────────────────────────────────

        [Test]
        public void FullDensity_PutsOneRayOnEveryEdge()
        {
            var loop = Square();
            var rays = Select(loop);

            Assert.AreEqual(4, rays.Count, "1辺1本のはず");
        }

        [Test]
        public void RaysSitAtEdgeMidpoints()
        {
            var rays = Select(Square());

            // 正方形の4辺の中点
            var expected = new[]
            {
                new Vector3(0.5f, 0f, 0f),
                new Vector3(1f,   0f, 0.5f),
                new Vector3(0.5f, 0f, 1f),
                new Vector3(0f,   0f, 0.5f),
            };

            foreach (var e in expected)
                Assert.IsTrue(rays.Exists(r => Vector3.Distance(r, e) < 1e-4f), $"{e} に光柱が立つはず");
        }

        // ── 間引き ──────────────────────────────────────────────────────

        [Test]
        public void HalfDensity_ThinsEvenlyAroundTheLoop()
        {
            var loop = Ring(8);
            var rays = Select(loop, density: 0.5f);

            Assert.AreEqual(4, rays.Count, "8辺の半分になるはず");

            // 1つおきに拾えているか（隣り合う光柱の間隔がすべて同じ）
            float first = Vector3.Distance(rays[0], rays[1]);
            for (int i = 1; i < rays.Count; i++)
                Assert.AreEqual(first, Vector3.Distance(rays[i - 1], rays[i]), 1e-3f,
                    "間引いても偏らず、均等に並ぶはず");
        }

        [Test]
        public void MaxCount_CapsLargeLoops()
        {
            var rays = Select(Ring(60), density: 1f, min: 3, max: 20);

            Assert.AreEqual(20, rays.Count, "大きな輪でも上限で頭打ちになるはず");
        }

        [Test]
        public void MinCount_KeepsSmallLoopsFromLookingEmpty()
        {
            // 密度を下げても最低本数は割らない
            var rays = Select(Ring(12), density: 0.1f, min: 5, max: 20);

            Assert.AreEqual(5, rays.Count);
        }

        [Test]
        public void CountNeverExceedsTheNumberOfEdges()
        {
            var rays = Select(Square(), density: 1f, min: 10, max: 40);

            Assert.AreEqual(4, rays.Count, "辺の数より多く立ててはいけない");
        }

        // ── 壊れた入力・決定性 ──────────────────────────────────────────

        [Test]
        public void DegenerateInput_ProducesNoRays()
        {
            Assert.AreEqual(0, Select(null).Count, "nullでは何も返さないはず");
            Assert.AreEqual(0, Select(new List<Vector3>()).Count);
            Assert.AreEqual(0, Select(new List<Vector3> { Vector3.zero, Vector3.right }).Count,
                "点が2つでは輪にならない");
        }

        [Test]
        public void RepeatedCalls_ProduceIdenticalPositions()
        {
            var loop  = Ring(17);
            var first = Select(loop, density: 0.6f);

            for (int run = 0; run < 3; run++)
                CollectionAssert.AreEqual(first, Select(loop, density: 0.6f),
                    "同じ入力なら毎回同じ位置に立つはず");
        }
    }
}
