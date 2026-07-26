// 役割: 木の絵の「決定論的な重み付き抽選」を検証する。
//       実配置とプレビューは同じseedで同じ関数を通るため、ここが決定論であれば
//       ゴーストと実タイルで木の絵柄が食い違うことはない。

using NUnit.Framework;
using ElfVillage.Tiles;

namespace ElfVillage.Tests
{
    public class TreeVariantWeightsTests
    {
        // Scene（TreeBillboardSystem._treeTextures）へ入っている10枚と同じ並び。
        private static readonly string[] ForestTreeNames =
        {
            "Tree_01_Rounded_Layered", "Tree_02_Columnar",        "Tree_03_Wide_Dome",
            "Tree_04_Compact_Conifer", "Tree_05_Airy_Branches",   "Tree_06_Asymmetric",
            "Tree_07_Slender_Conifer", "Tree_08_Yellow_Clusters", "Tree_09_Deep_Green_Oval",
            "Tree_10_Lime_Egg",
        };

        // ══ 重みの割り当て ═══════════════════════════════════════════════

        [Test]
        public void WeightForName_WideTypes_GetWideWeight()
        {
            foreach (var n in new[] { "Tree_01_Rounded_Layered", "Tree_03_Wide_Dome",
                                       "Tree_09_Deep_Green_Oval", "Tree_10_Lime_Egg" })
                Assert.AreEqual(TreeVariantWeights.WideWeight, TreeVariantWeights.WeightForName(n), n);
        }

        [Test]
        public void WeightForName_SlimTypes_GetSlimWeight()
        {
            foreach (var n in new[] { "Tree_02_Columnar", "Tree_04_Compact_Conifer", "Tree_07_Slender_Conifer" })
                Assert.AreEqual(TreeVariantWeights.SlimWeight, TreeVariantWeights.WeightForName(n), n);
        }

        [Test]
        public void WeightForName_StandardTypes_GetStandardWeight()
        {
            foreach (var n in new[] { "Tree_05_Airy_Branches", "Tree_06_Asymmetric", "Tree_08_Yellow_Clusters" })
                Assert.AreEqual(TreeVariantWeights.StandardWeight, TreeVariantWeights.WeightForName(n), n);
        }

        [Test]
        public void WeightForName_UnknownOrEmpty_FallsBackToStandard()
        {
            // 将来画像が増えたとき、重み表の更新漏れで木が出なくなるのを防ぐ。
            Assert.AreEqual(TreeVariantWeights.StandardWeight, TreeVariantWeights.WeightForName("Tree_11_Unknown"));
            Assert.AreEqual(TreeVariantWeights.StandardWeight, TreeVariantWeights.WeightForName(""));
            Assert.AreEqual(TreeVariantWeights.StandardWeight, TreeVariantWeights.WeightForName(null));
        }

        [Test]
        public void BuildWeights_ForestSet_SumsTo100()
        {
            var w = TreeVariantWeights.BuildWeights(ForestTreeNames);
            int sum = 0;
            foreach (var v in w) sum += v;
            Assert.AreEqual(100, sum, "10種の重みは合計100になる設計");
        }

        [Test]
        public void BuildWeights_NullInput_ReturnsEmpty()
            => Assert.AreEqual(0, TreeVariantWeights.BuildWeights(null).Length);

        // ══ 抽選 ═════════════════════════════════════════════════════════

        [Test]
        public void Select_SameSeed_AlwaysSameResult()
        {
            var w = TreeVariantWeights.BuildWeights(ForestTreeNames);
            for (int seed = -5000; seed < 5000; seed += 37)
                Assert.AreEqual(TreeVariantWeights.Select(w, seed), TreeVariantWeights.Select(w, seed),
                    $"seed={seed} で結果が揺れた");
        }

        [Test]
        public void Select_AlwaysInRange()
        {
            var w = TreeVariantWeights.BuildWeights(ForestTreeNames);
            for (int seed = -20000; seed < 20000; seed += 13)
            {
                int i = TreeVariantWeights.Select(w, seed);
                Assert.IsTrue(i >= 0 && i < w.Length, $"seed={seed} で範囲外のindex {i}");
            }
        }

        [Test]
        public void Select_AllVariantsAreReachable()
        {
            var w   = TreeVariantWeights.BuildWeights(ForestTreeNames);
            var hit = new bool[w.Length];
            for (int seed = 0; seed < 20000; seed++) hit[TreeVariantWeights.Select(w, seed)] = true;

            for (int i = 0; i < hit.Length; i++)
                Assert.IsTrue(hit[i], $"{ForestTreeNames[i]} が一度も選ばれない（到達不能）");
        }

        [Test]
        public void Select_DistributionMatchesWeightsWithinTolerance()
        {
            var w = TreeVariantWeights.BuildWeights(ForestTreeNames);
            const int samples = 200000;

            var counts = new int[w.Length];
            for (int seed = 0; seed < samples; seed++) counts[TreeVariantWeights.Select(w, seed)]++;

            for (int i = 0; i < w.Length; i++)
            {
                float actual = counts[i] * 100f / samples;
                Assert.AreEqual(w[i], actual, 1.0f,
                    $"{ForestTreeNames[i]} の出現率が設計値 {w[i]}% から離れている（実測 {actual:F2}%）");
            }
        }

        [Test]
        public void Select_SlimTypesStillAppearOftenEnough()
        {
            // 「細身が極端に出なくならないこと」を数で担保する。
            var w = TreeVariantWeights.BuildWeights(ForestTreeNames);
            const int samples = 100000;

            var counts = new int[w.Length];
            for (int seed = 0; seed < samples; seed++) counts[TreeVariantWeights.Select(w, seed)]++;

            int slim = counts[1] + counts[3] + counts[6];   // Columnar / Compact_Conifer / Slender_Conifer
            float ratio = slim * 100f / samples;
            Assert.Greater(ratio, 15f, $"細身3種の合計が少なすぎる（実測 {ratio:F2}%・設計18%）");
        }

        [Test]
        public void Select_ConsecutiveSeeds_DoNotRepeatOneVariant()
        {
            // 木のseedは i*40361 の等差数列。ハッシュを通さないとここで同じ絵が並んでしまう。
            var w = TreeVariantWeights.BuildWeights(ForestTreeNames);

            var distinct = new System.Collections.Generic.HashSet<int>();
            for (int i = 0; i < 24; i++)                     // 1タイル24本ぶん
                distinct.Add(TreeVariantWeights.Select(w, 92821 * 3 + 68917 * -2 + i * 40361));

            Assert.Greater(distinct.Count, 4, "1タイル内の木の絵柄が偏りすぎている");
        }

        // ══ 境界・異常値 ═════════════════════════════════════════════════

        [Test]
        public void Select_BoundariesHaveNoGapOrOverlap()
        {
            // 区間 [0,w0) [w0,w0+w1) … が連続していることを、
            // 各バケットの実測幅が重みと一致するかで確認する。
            var w = new[] { 3, 1, 2 };   // 合計6 → 0,1,2 / 3 / 4,5
            var counts = new int[w.Length];
            for (int seed = 0; seed < 60000; seed++) counts[TreeVariantWeights.Select(w, seed)]++;

            int total = 0;
            foreach (var c in counts) total += c;
            Assert.AreEqual(60000, total, "どのバケットにも入らないseedがある（隙間）");

            for (int i = 0; i < w.Length; i++)
                Assert.AreEqual(w[i] * 100f / 6f, counts[i] * 100f / 60000, 1.5f, $"index {i} の幅が重みと一致しない");
        }

        [Test]
        public void Select_EmptyOrNullWeights_ReturnsZeroSafely()
        {
            Assert.AreEqual(0, TreeVariantWeights.Select(null, 123));
            Assert.AreEqual(0, TreeVariantWeights.Select(new int[0], 123));
        }

        [Test]
        public void Select_AllZeroWeights_FallsBackToEvenPick()
        {
            var w   = new[] { 0, 0, 0, 0 };
            var hit = new bool[w.Length];
            for (int seed = 0; seed < 5000; seed++)
            {
                int i = TreeVariantWeights.Select(w, seed);
                Assert.IsTrue(i >= 0 && i < w.Length);
                hit[i] = true;
            }
            foreach (var h in hit) Assert.IsTrue(h, "全重み0のとき均等抽選になっていない");
        }

        [Test]
        public void Select_NegativeWeights_AreTreatedAsZero()
        {
            var w = new[] { -5, 10 };
            for (int seed = 0; seed < 2000; seed++)
                Assert.AreEqual(1, TreeVariantWeights.Select(w, seed), "負の重みが選ばれてはいけない");
        }

        // ══ ハッシュ ═════════════════════════════════════════════════════

        [Test]
        public void TileVisualHash_IsDeterministic()
        {
            for (int v = -1000; v < 1000; v += 7)
                Assert.AreEqual(TileVisualHash.Mix(v), TileVisualHash.Mix(v));
            for (int q = -20; q < 20; q++)
                for (int r = -20; r < 20; r++)
                    Assert.AreEqual(TileVisualHash.Mix(q, r), TileVisualHash.Mix(q, r));
        }

        [Test]
        public void TileVisualHash_Unit_IsInZeroToOne()
        {
            for (int v = -10000; v < 10000; v += 11)
            {
                float u = TileVisualHash.Unit(TileVisualHash.Mix(v));
                Assert.IsTrue(u >= 0f && u < 1f, $"value={v} で {u}");
            }
        }

        [Test]
        public void TileVisualHash_NeighbourCoords_DifferStrongly()
        {
            // 隣接タイルで木陰の向きが揃わないことの担保。
            int same = 0;
            for (int q = -10; q <= 10; q++)
                for (int r = -10; r <= 10; r++)
                    if (TileVisualHash.Mix(q, r) >> 24 == TileVisualHash.Mix(q + 1, r) >> 24) same++;

            Assert.Less(same, 10, "隣接タイルのハッシュ上位が一致しすぎている");
        }
    }
}
