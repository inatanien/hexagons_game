// 役割: Fieldタイル（花畑）の「決められた値」と決定論を固定する。
//       接地・広がり・粒ごとの種は、あとから何気なく変えると花畑の見え方が壊れるので、
//       テストで留めておく。

using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using ElfVillage.Tiles;

namespace ElfVillage.Tests
{
    public class FieldFlowerVisualTests
    {
        private const string VariantPath  = "Assets/_Game/ScriptableObjects/TerrainVariants/TerrainVariant_Field_Flower.asset";
        private const string TileTypePath = "Assets/_Game/ScriptableObjects/TileDefinitions/TileType_Field.asset";

        private static T GetInternalConst<T>(System.Type type, string name)
        {
            var field = type.GetField(name, BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(field, $"{type.Name}.{name} が見つからない");
            return (T)field.GetValue(null);
        }

        // ══ 本数 ═════════════════════════════════════════════════════════

        [Test]
        public void FieldVariant_PropCountIs20()
        {
            var v = AssetDatabase.LoadAssetAtPath<TerrainVariantDefinition>(VariantPath);
            Assert.IsNotNull(v, VariantPath + " が見つからない");
            Assert.AreEqual(20, v.propCount, "花は1タイル20本固定（今回のStageでは増やさない）");
            Assert.AreEqual(TilePropType.Flower, v.propType);
        }

        [Test]
        public void FieldTileType_LegacyPropCountMatchesVariant()
        {
            var t = AssetDatabase.LoadAssetAtPath<TileType>(TileTypePath);
            var v = AssetDatabase.LoadAssetAtPath<TerrainVariantDefinition>(VariantPath);
            Assert.IsNotNull(t, TileTypePath + " が見つからない");
            Assert.IsTrue(t.HasVisualElements, "Fieldは複合要素タイルとして生成される想定");
            Assert.AreEqual(v.propCount, t.propCount, "legacy propCount と variant propCount がずれている");
        }

        // ※接地そのものの検証は FieldFlowerGroundingTests.cs にある。

        // ══ 広がり ═══════════════════════════════════════════════════════

        [Test]
        public void FlowerMaxRadius_ReachesTileEdge()
        {
            float radius = GetInternalConst<float>(typeof(HexTile), "FlowerMaxRadius");

            // 六角形（outerRadius=2.0）の辺の中点までは約1.732、角までは2.0。
            Assert.AreEqual(1.70f, radius, 0.0001f, "タイル端まで花を咲かせる値（1.70）から変わっている");
            Assert.Less(radius, 2.0f, "角の距離を超えると花が完全に隣タイルへ出てしまう");
        }

        [Test]
        public void FlowerSpiral_StaysWithinRadiusPlusJitter()
        {
            float maxRadius = GetInternalConst<float>(typeof(HexTile), "FlowerMaxRadius");
            float golden    = GetInternalConst<float>(typeof(HexTile), "FlowerGoldenAngleDeg");

            var method = typeof(HexTile).GetMethod("ComputeSpiralOffset", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(method, "ComputeSpiralOffset が見つからない");

            // productionの半径ジッターは ±0.05。+0.01 は浮動小数点の許容のみで仕様値ではない。
            const float productionJitter = 0.05f;

            float worst = 0f;
            for (int q = -3; q <= 3; q++)
                for (int r = -3; r <= 3; r++)
                    for (int i = 0; i < 20; i++)
                    {
                        int seed = q * 31 + r * 17 + i * 7;
                        var v = (Vector3)method.Invoke(null,
                            new object[] { i, 20, seed, golden, maxRadius, q * 23f + r * 37f });
                        worst = Mathf.Max(worst, new Vector2(v.x, v.z).magnitude);
                    }

            Assert.LessOrEqual(worst, maxRadius + productionJitter + 0.01f,
                $"花の中心が想定より外へ出ている（実測 {worst:F4}）");
        }

        // ══ 粒ごとの種（絵柄の決定論） ═══════════════════════════════════

        [Test]
        public void ParticleSeed_IsDeterministic()
        {
            for (int seed = -5000; seed < 5000; seed += 13)
                Assert.AreEqual(FlowerBillboardSystem.ParticleSeed(seed),
                                FlowerBillboardSystem.ParticleSeed(seed),
                                $"seed={seed} で結果が揺れた");
        }

        [Test]
        public void ParticleSeed_IsNeverZero()
        {
            // 0はUnity側で「種の指定なし」と解釈されうるため、必ず1以上へ寄せている。
            for (int seed = -20000; seed < 20000; seed += 7)
                Assert.AreNotEqual(0u, FlowerBillboardSystem.ParticleSeed(seed), $"seed={seed} で0になった");
        }

        [Test]
        public void ParticleSeed_ScattersConsecutiveFlowerSeeds()
        {
            // ★花のseedは `q*31 + r*17 + i*7` という歩幅7の等差数列。
            //   ハッシュを通さないと絵柄が規則的に並んでしまう。
            //   1タイル20粒ぶんの種が、上位ビットで見て十分ばらけていることを確認する。
            var buckets = new System.Collections.Generic.HashSet<uint>();
            for (int i = 0; i < 20; i++)
            {
                int seed = 3 * 31 + (-2) * 17 + i * 7;
                buckets.Add(FlowerBillboardSystem.ParticleSeed(seed) % 5u);   // 絵柄5種を想定
            }
            Assert.GreaterOrEqual(buckets.Count, 4, "1タイル内の花の絵柄が偏りすぎている");
        }

        [Test]
        public void ParticleSeed_DiffersBetweenNeighbourTiles()
        {
            // 隣り合うタイルで同じ並びにならないこと（花畑に格子模様が出ないため）。
            int same = 0, total = 0;
            for (int q = -8; q <= 8; q++)
                for (int r = -8; r <= 8; r++)
                {
                    total++;
                    uint a = FlowerBillboardSystem.ParticleSeed(q * 31 + r * 17);
                    uint b = FlowerBillboardSystem.ParticleSeed((q + 1) * 31 + r * 17);
                    if (a % 5u == b % 5u) same++;
                }
            Assert.Less(same * 100f / total, 35f, "隣接タイルの先頭の花が同じ絵柄になりすぎている");
        }
    }
}
