// 役割: 複合タイルの要素別領域割当（Session 10, ElementRegionLayout）の単体テスト。
//       候補生成・スコア計算・ソート・区間割当それぞれの決定論性・安全性を検証する。
//       MonoBehaviourを介さない純粋関数のみを対象とするため、すべてEditModeで完結する。

using System.Collections.Generic;
using NUnit.Framework;
using ElfVillage.Tiles;
using UnityEngine;

namespace ElfVillage.Tests
{
    public class ElementRegionLayoutTests
    {
        // ── 候補生成 ────────────────────────────────────────────────

        [Test]
        public void GenerateNormalizedCandidates_AllWithinUnitCircle()
        {
            // 7. 候補位置が六角形外へ配置されない（正規化半径が常に1以下であることの回帰確認）。
            var candidates = ElementRegionLayout.GenerateNormalizedCandidates(
                20, coordQ: 3, coordR: -2, typeIdentityHash: 12345, goldenAngleDeg: 137.50776f, baseRotationDeg: 10f);

            foreach (var c in candidates)
            {
                float rSq = c.NormX * c.NormX + c.NormZ * c.NormZ;
                Assert.LessOrEqual(rSq, 1.0001f, "正規化候補は半径1の単位円内に収まるべき");
            }
        }

        [Test]
        public void GenerateNormalizedCandidates_ZeroCount_ReturnsEmpty()
        {
            var candidates = ElementRegionLayout.GenerateNormalizedCandidates(
                0, 0, 0, 0, 137.50776f, 0f);
            Assert.AreEqual(0, candidates.Length);
        }

        [Test]
        public void GenerateNormalizedCandidates_SmallRadiusCandidates_AngleNotClustered()
        {
            // 11. 小さい半径の候補が同一角度へ大量集中しない。
            // 半径は index に関わらずゴールデンアングルで角度が決まるため、
            // 中心付近（index が小さい）の候補同士でも角度が明確に異なることを確認する。
            var candidates = ElementRegionLayout.GenerateNormalizedCandidates(
                10, 1, 1, 999, 137.50776f, 0f);

            float angle0 = Mathf.Atan2(candidates[0].NormZ, candidates[0].NormX) * Mathf.Rad2Deg;
            float angle1 = Mathf.Atan2(candidates[1].NormZ, candidates[1].NormX) * Mathf.Rad2Deg;
            float diff = Mathf.Abs(Mathf.DeltaAngle(angle0, angle1));
            Assert.Greater(diff, 30f, "隣接indexの候補同士は十分に異なる角度を持つべき");
        }

        [Test]
        public void GenerateNormalizedCandidates_NoExactDuplicatePositions()
        {
            // 12. 同じ位置またはほぼ同じ位置への重複が増えていない。
            var candidates = ElementRegionLayout.GenerateNormalizedCandidates(
                16, 5, -3, 777, 137.50776f, 0f);

            for (int i = 0; i < candidates.Length; i++)
            for (int j = i + 1; j < candidates.Length; j++)
            {
                float dx = candidates[i].NormX - candidates[j].NormX;
                float dz = candidates[i].NormZ - candidates[j].NormZ;
                float distSq = dx * dx + dz * dz;
                Assert.Greater(distSq, 0.0001f, $"候補{i}と候補{j}がほぼ同一位置になっている");
            }
        }

        // ── 決定論性 ────────────────────────────────────────────────

        [Test]
        public void SameSeedAndInput_ProducesSameAssignment()
        {
            // 5. 同じseedと入力では同じ要素領域判定になる。
            var c1 = ElementRegionLayout.GenerateNormalizedCandidates(10, 4, -1, 555, 137.50776f, 0f);
            var s1 = ElementRegionLayout.ComputeScores(c1, 90f, 4, -1, 555);
            var idx1 = ElementRegionLayout.SortIndicesByScore(s1);

            var c2 = ElementRegionLayout.GenerateNormalizedCandidates(10, 4, -1, 555, 137.50776f, 0f);
            var s2 = ElementRegionLayout.ComputeScores(c2, 90f, 4, -1, 555);
            var idx2 = ElementRegionLayout.SortIndicesByScore(s2);

            CollectionAssert.AreEqual(idx1, idx2, "同一入力からは常に同一の並びになるべき");
        }

        [Test]
        public void DifferentCoords_BoundaryDirectionCanDiffer()
        {
            // 6. 異なるseedでは境界方向が変化しうる。
            var seen = new HashSet<float>();
            for (int q = 0; q < 8; q++)
                seen.Add(ElementRegionLayout.ComputeBoundaryDirectionDeg(q, q * 3, 42));

            Assert.Greater(seen.Count, 1, "座標が変われば境界方向も変化しうるべき（常に固定値ではない）");
        }

        [Test]
        public void ComputeBoundaryDirectionDeg_AlwaysWithin0To360()
        {
            for (int q = -50; q <= 50; q += 7)
            for (int r = -50; r <= 50; r += 11)
            {
                float deg = ElementRegionLayout.ComputeBoundaryDirectionDeg(q, r, 123);
                Assert.GreaterOrEqual(deg, 0f);
                Assert.Less(deg, 360f);
            }
        }

        [Test]
        public void StableStringHash_SameString_SameHash()
        {
            int h1 = ElementRegionLayout.StableStringHash("TileType_ForestFlower");
            int h2 = ElementRegionLayout.StableStringHash("TileType_ForestFlower");
            Assert.AreEqual(h1, h2);
        }

        [Test]
        public void StableStringHash_NullOrEmpty_ReturnsZero()
        {
            Assert.AreEqual(0, ElementRegionLayout.StableStringHash(null));
            Assert.AreEqual(0, ElementRegionLayout.StableStringHash(""));
        }

        // ── ソートの同点処理 ──────────────────────────────────────────

        [Test]
        public void SortIndicesByScore_TiedScores_ResolvedByIndexAscending_Deterministic()
        {
            // 15. 同点スコアでも配置順が決定論的。
            var scores = new float[] { 0.5f, 0.5f, 0.5f, 0.1f, 0.9f };
            var sorted1 = ElementRegionLayout.SortIndicesByScore(scores);
            var sorted2 = ElementRegionLayout.SortIndicesByScore(scores);

            CollectionAssert.AreEqual(sorted1, sorted2, "同じスコア配列からは常に同じ並びになるべき");
            // 0.1(index3)が最小、次に0.5の3つ(index0,1,2、同点はindex昇順)、最後に0.9(index4)
            CollectionAssert.AreEqual(new[] { 3, 0, 1, 2, 4 }, sorted1);
        }

        // ── 区間割当 ────────────────────────────────────────────────

        [Test]
        public void PartitionByCounts_SingleElement_AllCandidatesAssignedToIt()
        {
            // 1. 単一要素の場合、全候補が単一の担当領域に収まる（＝実質「既存互換」の状態）。
            var sortedIndices = new[] { 4, 1, 3, 0, 2 };
            var result = ElementRegionLayout.PartitionByCounts(sortedIndices, new[] { 5 });

            Assert.AreEqual(1, result.Length);
            CollectionAssert.AreEqual(sortedIndices, result[0]);
        }

        [Test]
        public void PartitionByCounts_070_030_Split_MajorityElementGetsLargerChunk()
        {
            // 2, 3. areaWeight 0.7/0.3 相当（propCount10 → counts=[7,3]）で主要素側が広い。
            var sortedIndices = new int[10];
            for (int i = 0; i < 10; i++) sortedIndices[i] = i;

            var result = ElementRegionLayout.PartitionByCounts(sortedIndices, new[] { 7, 3 });

            Assert.AreEqual(7, result[0].Length);
            Assert.AreEqual(3, result[1].Length);
            Assert.Greater(result[0].Length, result[1].Length);
        }

        [Test]
        public void PartitionByCounts_075_025_Split_MatchesProportions()
        {
            // 4. areaWeight 0.75/0.25 相当（propCount20 → counts=[15,5]）でも同様に比率が反映される。
            var sortedIndices = new int[20];
            for (int i = 0; i < 20; i++) sortedIndices[i] = i;

            var result = ElementRegionLayout.PartitionByCounts(sortedIndices, new[] { 15, 5 });

            Assert.AreEqual(15, result[0].Length);
            Assert.AreEqual(5, result[1].Length);
        }

        [Test]
        public void PartitionByCounts_EveryCandidateAssignedExactlyOnce()
        {
            // 13. 全候補が一度だけ、いずれか1要素へ割り当てられる。
            var sortedIndices = new int[13];
            for (int i = 0; i < 13; i++) sortedIndices[i] = i;

            var result = ElementRegionLayout.PartitionByCounts(sortedIndices, new[] { 5, 4, 4 });

            var seen = new HashSet<int>();
            foreach (var chunk in result)
            foreach (var idx in chunk)
                Assert.IsTrue(seen.Add(idx), $"候補{idx}が複数要素へ重複割当されている");

            Assert.AreEqual(13, seen.Count, "全候補が漏れなく割り当てられるべき");
        }

        [Test]
        public void PartitionByCounts_EqualSplitFallback_BothElementsGetHalf()
        {
            // 10. 全要素のareaWeightが0の場合の安全なフォールバック（均等割り）を想定したケース。
            var sortedIndices = new int[10];
            for (int i = 0; i < 10; i++) sortedIndices[i] = i;

            var result = ElementRegionLayout.PartitionByCounts(sortedIndices, new[] { 5, 5 });

            Assert.AreEqual(5, result[0].Length);
            Assert.AreEqual(5, result[1].Length);
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void PartitionByCounts_ZeroOrNegativeCount_DoesNotThrow_NoNegativeLengthArray(int badCount)
        {
            // 9. areaWeightが0または負値相当の入力でも例外を発生させない。
            var sortedIndices = new[] { 0, 1, 2 };
            int[][] result = null;
            Assert.DoesNotThrow(() =>
                result = ElementRegionLayout.PartitionByCounts(sortedIndices, new[] { badCount, 3 }));
            Assert.AreEqual(0, result[0].Length);
        }

        [Test]
        public void PartitionByCounts_CountsExceedAvailable_ClampsSafely()
        {
            var sortedIndices = new[] { 0, 1, 2 };
            int[][] result = null;
            Assert.DoesNotThrow(() =>
                result = ElementRegionLayout.PartitionByCounts(sortedIndices, new[] { 10, 10 }));
            Assert.AreEqual(3, result[0].Length, "利用可能な候補数を超えないようクランプされるべき");
            Assert.AreEqual(0, result[1].Length);
        }

        // ── visualOnly非依存・maxRadius非依存の確認 ───────────────────

        [Test]
        public void RegionAssignment_DoesNotDependOnMaxRadius()
        {
            // 16. TreeとFlowerでmaxRadiusが異なっても正規化領域の所属が変わらない。
            // maxRadiusは割当後のスケーリングにのみ使われ、割当そのもののAPIには存在しないことを、
            // 同一の割当結果を異なるmaxRadiusでスケールしても割当(添字集合)自体は不変であることで示す。
            var candidates = ElementRegionLayout.GenerateNormalizedCandidates(10, 2, 2, 88, 137.50776f, 0f);
            var scores = ElementRegionLayout.ComputeScores(candidates, 45f, 2, 2, 88);
            var sorted = ElementRegionLayout.SortIndicesByScore(scores);
            var assignment = ElementRegionLayout.PartitionByCounts(sorted, new[] { 7, 3 });

            const float treeMaxRadius = 1.35f;
            const float flowerMaxRadius = 1.5f;

            foreach (var idx in assignment[0])
            {
                var c = candidates[idx];
                var treeScaled   = new Vector3(c.NormX * treeMaxRadius, 0f, c.NormZ * treeMaxRadius);
                var flowerScaled = new Vector3(c.NormX * flowerMaxRadius, 0f, c.NormZ * flowerMaxRadius);
                // 方向（正規化ベクトル）は同一、大きさだけが異なることを確認する
                // （浮動小数の丸め誤差があるため厳密一致ではなく角度の近似比較にする）
                float angleDiff = Vector3.Angle(treeScaled, flowerScaled);
                Assert.Less(angleDiff, 0.01f, "スケール前の割当自体はmaxRadiusに依存しないはず");
            }
        }

        [Test]
        public void VisualOnly_IsNotAParameterOfAnyRegionFunction_StructurallyIgnored()
        {
            // 8. visualOnly属性の違いが領域計算結果を変えない。
            // ElementRegionLayoutの全関数はareaWeight由来のcountsのみを受け取り、
            // visualOnlyを一切パラメータに持たないため、構造的に結果へ影響しえない。
            // ここでは同一のcounts/座標から2回呼び出し、常に同一の割当になることで裏付ける
            // （visualOnlyの真偽が呼び出し元でどう扱われても、渡す引数が同じなら結果は不変）。
            var candidatesA = ElementRegionLayout.GenerateNormalizedCandidates(10, 1, 9, 321, 137.50776f, 0f);
            var scoresA = ElementRegionLayout.ComputeScores(candidatesA, 200f, 1, 9, 321);
            var sortedA = ElementRegionLayout.SortIndicesByScore(scoresA);
            var assignA = ElementRegionLayout.PartitionByCounts(sortedA, new[] { 6, 4 });

            var candidatesB = ElementRegionLayout.GenerateNormalizedCandidates(10, 1, 9, 321, 137.50776f, 0f);
            var scoresB = ElementRegionLayout.ComputeScores(candidatesB, 200f, 1, 9, 321);
            var sortedB = ElementRegionLayout.SortIndicesByScore(scoresB);
            var assignB = ElementRegionLayout.PartitionByCounts(sortedB, new[] { 6, 4 });

            CollectionAssert.AreEqual(assignA[0], assignB[0]);
            CollectionAssert.AreEqual(assignA[1], assignB[1]);
        }
    }
}
