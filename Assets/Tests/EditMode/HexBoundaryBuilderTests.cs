// 役割: HexBoundaryBuilder（タイル群 → 外周の輪）を固定する（Stage 2）。
//
//       ★角の同一性は整数（3タイルの組）で決まるので、テストも厳密に書ける。
//         world座標の丸めに頼った判定はここにも入れない。
//
//       ★特に守りたいのは次の2つ。
//         ・辺を共有せず角だけで接する2領域が、1本の自己交差した輪へ繋がらないこと
//         ・外周は反時計回り、穴は時計回りで、離れ小島も外周として扱われること

using System.Collections.Generic;
using NUnit.Framework;
using ElfVillage.HexGrid;
using UnityEngine;

namespace ElfVillage.Tests
{
    public class HexBoundaryBuilderTests
    {
        // ── ヘルパー ────────────────────────────────────────────────

        private static List<List<HexCorner>> Build(params HexCoord[] tiles)
            => HexBoundaryBuilder.BuildLoops(tiles);

        private static HexCoord Hex(int q, int r) => new HexCoord(q, r);

        /// <summary>中心が空いた6枚のリング（穴があく形）。</summary>
        private static HexCoord[] Ring(HexCoord center)
        {
            var ring = new HexCoord[6];
            for (int d = 0; d < 6; d++) ring[d] = center.Neighbor(d);
            return ring;
        }

        /// <summary>入力から外周辺の本数を直接数える（実装とは別経路の期待値）。</summary>
        private static int CountBoundaryEdges(IEnumerable<HexCoord> tiles)
        {
            var set   = new HashSet<HexCoord>(tiles);
            int count = 0;
            foreach (var tile in set)
                for (int d = 0; d < 6; d++)
                    if (!set.Contains(tile.Neighbor(d))) count++;
            return count;
        }

        private static float SignedArea(List<HexCorner> loop)
        {
            float sum = 0f;
            for (int i = 0; i < loop.Count; i++)
            {
                Vector3 a = loop[i].ToWorldPosition();
                Vector3 b = loop[(i + 1) % loop.Count].ToWorldPosition();
                sum += a.x * b.z - b.x * a.z;
            }
            return sum * 0.5f;
        }

        private static bool IsCounterClockwise(List<HexCorner> loop) => SignedArea(loop) > 0f;

        /// <summary>輪の隣り合う角が、必ず1辺分だけ離れていること（＝2枚のタイルを共有する）。</summary>
        private static void AssertContinuous(List<HexCorner> loop)
        {
            for (int i = 0; i < loop.Count; i++)
            {
                HexCorner a = loop[i];
                HexCorner b = loop[(i + 1) % loop.Count];

                int shared = 0;
                foreach (var x in new[] { a.A, a.B, a.C })
                    foreach (var y in new[] { b.A, b.B, b.C })
                        if (x == y) shared++;

                Assert.AreEqual(2, shared,
                    $"隣り合う角は1辺分だけ離れているはず: {a} → {b}");
            }
        }

        // ── 1〜4. 基本の形 ──────────────────────────────────────────────

        [Test]
        public void Empty_ProducesNoLoops()
        {
            Assert.AreEqual(0, Build().Count);
            Assert.AreEqual(0, HexBoundaryBuilder.BuildLoops(null).Count, "nullでも空の結果を返すはず");
        }

        [Test]
        public void SingleTile_ProducesOneLoopOfSixCorners()
        {
            var loops = Build(HexCoord.Zero);

            Assert.AreEqual(1, loops.Count);
            Assert.AreEqual(6, loops[0].Count);
            AssertContinuous(loops[0]);
        }

        [Test]
        public void TwoAdjacentTiles_ShareAnEdge_ProducingTenCorners()
        {
            var loops = Build(HexCoord.Zero, HexCoord.Zero.Neighbor(0));

            Assert.AreEqual(1, loops.Count);
            Assert.AreEqual(10, loops[0].Count, "共有辺の分だけ外周が減るはず（6+6-2）");
            AssertContinuous(loops[0]);
        }

        [Test]
        public void ThreeTilesInALine_ProduceFourteenCorners()
        {
            var a = HexCoord.Zero;
            var b = a.Neighbor(0);
            var c = b.Neighbor(0);

            var loops = Build(a, b, c);

            Assert.AreEqual(1, loops.Count);
            Assert.AreEqual(14, loops[0].Count);
            AssertContinuous(loops[0]);
        }

        // ── 5. 離れた2枚 ────────────────────────────────────────────────

        [Test]
        public void SeparateTiles_ProduceSeparateLoops()
        {
            var loops = Build(HexCoord.Zero, Hex(5, 0));

            Assert.AreEqual(2, loops.Count);
            foreach (var loop in loops)
            {
                Assert.AreEqual(6, loop.Count);
                Assert.IsTrue(IsCounterClockwise(loop), "離れ小島はどちらも外周なので反時計回りのはず");
            }
        }

        // ── 6〜7. 穴 ────────────────────────────────────────────────────

        [Test]
        public void RingWithHole_ProducesOuterAndInnerLoops()
        {
            var loops = Build(Ring(HexCoord.Zero));

            Assert.AreEqual(2, loops.Count, "外周と穴の2本になるはず");

            var inner = loops.Find(l => l.Count == 6);
            var outer = loops.Find(l => l.Count != 6);

            Assert.IsNotNull(inner, "穴は中心タイル1枚分（6辺）のはず");
            Assert.IsNotNull(outer);
            Assert.AreEqual(18, outer.Count, "外周は18辺のはず");
            AssertContinuous(inner);
            AssertContinuous(outer);
        }

        [Test]
        public void RingWithHole_OuterIsCounterClockwise_InnerIsClockwise()
        {
            var loops = Build(Ring(HexCoord.Zero));
            var inner = loops.Find(l => l.Count == 6);
            var outer = loops.Find(l => l.Count != 6);

            Assert.IsTrue(IsCounterClockwise(outer),  "外周は反時計回りのはず");
            Assert.IsFalse(IsCounterClockwise(inner), "穴は時計回りのはず");
        }

        // ── 8〜9. 入力の揺れに強いこと ──────────────────────────────────

        [Test]
        public void DuplicatedInput_DoesNotChangeResult()
        {
            var once  = Build(HexCoord.Zero, HexCoord.Zero.Neighbor(0));
            var twice = Build(HexCoord.Zero, HexCoord.Zero.Neighbor(0), HexCoord.Zero, HexCoord.Zero.Neighbor(0));

            Assert.AreEqual(once.Count, twice.Count);
            CollectionAssert.AreEqual(once[0], twice[0], "重複は落として同じ結果になるはず");
        }

        [Test]
        public void InputOrder_DoesNotChangeResult()
        {
            var tiles = new List<HexCoord>(Ring(HexCoord.Zero));
            var forward  = HexBoundaryBuilder.BuildLoops(tiles);

            tiles.Reverse();
            var backward = HexBoundaryBuilder.BuildLoops(tiles);

            Assert.AreEqual(forward.Count, backward.Count);
            for (int i = 0; i < forward.Count; i++)
                CollectionAssert.AreEqual(forward[i], backward[i], $"{i}番目の輪が入力順で変わってはいけない");
        }

        // ── 10〜11. 幾何 ────────────────────────────────────────────────

        [Test]
        public void AdjacentCornersAreExactlyOneEdgeApart()
        {
            var loops = Build(HexCoord.Zero, HexCoord.Zero.Neighbor(0), HexCoord.Zero.Neighbor(2), Hex(4, 0));
            foreach (var loop in loops) AssertContinuous(loop);
        }

        [Test]
        public void CornerWorldPosition_MatchesTheHexMeshCorners()
        {
            // HexMeshBuilderが作る六角形の角（60°×i、外接半径outerRadius）と一致すること。
            // ここがずれると、輪郭は正しくても描画だけ半個ずれる
            const float size = 2.0f;
            var tile = Hex(1, -2);
            Vector3 center = tile.ToWorldPosition(size);

            for (int i = 0; i < 6; i++)
            {
                float angle = Mathf.Deg2Rad * (60f * i);
                var expected = new Vector3(center.x + size * Mathf.Cos(angle),
                                           0f,
                                           center.z + size * Mathf.Sin(angle));

                Vector3 actual = HexCorner.Of(tile, i).ToWorldPosition(size);

                Assert.AreEqual(expected.x, actual.x, 1e-4f, $"角{i}のX");
                Assert.AreEqual(expected.z, actual.z, 1e-4f, $"角{i}のZ");
            }
        }

        [Test]
        public void SameCorner_FromDifferentTiles_HasTheSameIdentity()
        {
            var tile     = HexCoord.Zero;
            var neighbor = tile.Neighbor(0);

            // 角0（0°）は tile と neighbor(0) と neighbor(1) が共有する。
            // neighbor側から見ると同じ角が別のindexになるが、IDは一致しなければならない
            HexCorner fromTile = HexCorner.Of(tile, 0);

            HexCorner match = default;
            bool found = false;
            for (int i = 0; i < 6; i++)
                if (HexCorner.Of(neighbor, i) == fromTile) { match = HexCorner.Of(neighbor, i); found = true; }

            Assert.IsTrue(found, "隣のタイルから見ても同じ角IDになるはず");
            Assert.AreEqual(fromTile.GetHashCode(), match.GetHashCode(), "ハッシュも一致するはず");
        }

        // ── 12. 角だけで接する2領域 ─────────────────────────────────────

        [Test]
        public void RegionsTouchingOnlyAtACorner_DoNotMergeIntoOneLoop()
        {
            // 斜め隣どうしは辺を共有せず、角を1つだけ共有する
            var a = HexCoord.Zero;
            var b = a.Neighbor(0).Neighbor(1);
            Assert.AreEqual(2, a.DistanceTo(b), "この2枚は辺を共有しない配置のはず");

            var loops = Build(a, b);

            Assert.AreEqual(2, loops.Count, "角だけで接する領域は別々の輪になるはず");
            foreach (var loop in loops)
            {
                Assert.AreEqual(6, loop.Count, "自己交差した1本の輪へ繋がってはいけない");
                AssertContinuous(loop);
                Assert.IsTrue(IsCounterClockwise(loop));
            }
        }

        // ── 13. 穴あきリング + 離れた島 ─────────────────────────────────

        [Test]
        public void RingWithHole_PlusSeparateIsland_ProducesTwoOutersAndOneHole()
        {
            var tiles = new List<HexCoord>(Ring(HexCoord.Zero)) { Hex(8, 0) };
            var loops = HexBoundaryBuilder.BuildLoops(tiles);

            Assert.AreEqual(3, loops.Count);

            int outers = 0, holes = 0;
            foreach (var loop in loops)
            {
                if (IsCounterClockwise(loop)) outers++;
                else                          holes++;
            }

            Assert.AreEqual(2, outers, "リングの外周と離れ島の2つが外周のはず");
            Assert.AreEqual(1, holes,  "穴は1つのはず");
        }

        // ── 14〜15. 輪の健全性 ──────────────────────────────────────────

        [Test]
        public void EveryBoundaryEdge_IsUsedExactlyOnce()
        {
            var tiles = new List<HexCoord>(Ring(HexCoord.Zero)) { Hex(8, 0), Hex(9, 0) };
            var loops = HexBoundaryBuilder.BuildLoops(tiles);

            int total = 0;
            foreach (var loop in loops) total += loop.Count;   // 閉じた輪では 角数 == 辺数

            Assert.AreEqual(CountBoundaryEdges(tiles), total, "外周辺はちょうど1回ずつ使われるはず");
        }

        [Test]
        public void EveryLoop_IsClosed()
        {
            var tiles = new List<HexCoord>(Ring(HexCoord.Zero)) { Hex(8, 0) };
            foreach (var loop in HexBoundaryBuilder.BuildLoops(tiles))
            {
                Assert.GreaterOrEqual(loop.Count, 3);
                AssertContinuous(loop);   // 最後の角と最初の角の連続性も含めて確認している

                var unique = new HashSet<HexCorner>(loop);
                Assert.AreEqual(loop.Count, unique.Count, "同じ角を二度通ってはいけない");
            }
        }

        // ── 16. 決定性 ──────────────────────────────────────────────────

        [Test]
        public void RepeatedRuns_ProduceIdenticalLoops()
        {
            var tiles = new List<HexCoord>(Ring(HexCoord.Zero)) { Hex(8, 0), Hex(8, 1) };

            var first = HexBoundaryBuilder.BuildLoops(tiles);
            for (int run = 0; run < 3; run++)
            {
                var again = HexBoundaryBuilder.BuildLoops(tiles);

                Assert.AreEqual(first.Count, again.Count, "輪の数が毎回同じはず");
                for (int i = 0; i < first.Count; i++)
                {
                    CollectionAssert.AreEqual(first[i], again[i], $"{i}番目の輪の角の並びが毎回同じはず");
                    Assert.AreEqual(IsCounterClockwise(first[i]), IsCounterClockwise(again[i]), "向きも毎回同じはず");
                }
            }
        }

        [Test]
        public void LoopsStartAtTheirSmallestCorner()
        {
            var tiles = new List<HexCoord>(Ring(HexCoord.Zero)) { Hex(8, 0) };
            foreach (var loop in HexBoundaryBuilder.BuildLoops(tiles))
            {
                foreach (var corner in loop)
                    Assert.LessOrEqual(loop[0].CompareTo(corner), 0,
                        "輪の先頭はいちばん小さい角のはず（Stage 3の光の開始位置を毎回同じにするため）");
            }
        }
    }
}
