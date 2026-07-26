// 役割: Forestタイルの「密度に関わる決められた値」を固定する。
//       本数・広がり・個体サイズ幅は、あとから何気なく変えると森の見え方が壊れる値なので、
//       テストで留めておく。

using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using ElfVillage.Tiles;

namespace ElfVillage.Tests
{
    public class ForestTreeDensityTests
    {
        private const string VariantPath  = "Assets/_Game/ScriptableObjects/TerrainVariants/TerrainVariant_Forest_Forest.asset";
        private const string TileTypePath = "Assets/_Game/ScriptableObjects/TileDefinitions/TileType_Forest.asset";

        private static T GetInternalConst<T>(System.Type type, string name)
        {
            var field = type.GetField(name, BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(field, $"{type.Name}.{name} が見つからない");
            return (T)field.GetValue(null);
        }

        // ══ 本数 ═════════════════════════════════════════════════════════

        [Test]
        public void ForestVariant_PropCountIs24()
        {
            var v = AssetDatabase.LoadAssetAtPath<TerrainVariantDefinition>(VariantPath);
            Assert.IsNotNull(v, VariantPath + " が見つからない");
            Assert.AreEqual(24, v.propCount, "Forestの木は1タイル24本固定");
        }

        [Test]
        public void ForestTileType_LegacyPropCountMatchesVariant()
        {
            // legacy値は elements があるため実際には参照されないが、
            // 片方だけ古い数字が残っていると読む人が混乱するので揃える。
            var t = AssetDatabase.LoadAssetAtPath<TileType>(TileTypePath);
            var v = AssetDatabase.LoadAssetAtPath<TerrainVariantDefinition>(VariantPath);
            Assert.IsNotNull(t, TileTypePath + " が見つからない");
            Assert.IsTrue(t.HasVisualElements, "Forestは複合要素タイルとして生成される想定");
            Assert.AreEqual(v.propCount, t.propCount, "legacy propCount と variant propCount がずれている");
        }

        [Test]
        public void ForestVariant_IsTreeType()
        {
            var v = AssetDatabase.LoadAssetAtPath<TerrainVariantDefinition>(VariantPath);
            Assert.AreEqual(TilePropType.Tree, v.propType);
        }

        // ══ 広がり ═══════════════════════════════════════════════════════

        [Test]
        public void TreeMaxRadius_ReachesTileEdge()
        {
            float radius = GetInternalConst<float>(typeof(HexTile), "TreeMaxRadius");

            // 六角形（outerRadius=2.0）の辺の中点までは約1.732、角までは2.0。
            Assert.AreEqual(1.70f, radius, 0.0001f, "タイル端まで木を茂らせる値（1.70）から変わっている");
            Assert.Greater(radius, 1.6f, "小さすぎるとタイル境界に木のない緑の帯が再発する");
            Assert.Less(radius, 2.0f, "角の距離を超えると木が完全に隣タイルへ出てしまう");
        }

        [Test]
        public void SpiralOffset_StaysWithinRadiusPlusJitter()
        {
            float maxRadius = GetInternalConst<float>(typeof(HexTile), "TreeMaxRadius");
            float golden    = GetInternalConst<float>(typeof(HexTile), "TreeGoldenAngleDeg");

            var method = typeof(HexTile).GetMethod("ComputeSpiralOffset", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(method, "ComputeSpiralOffset が見つからない");

            // productionの半径ジッターは ((seed/21)%21 - 10)/200 ＝ ちょうど ±0.05。
            const float productionJitter = 0.05f;

            float worst = 0f;
            for (int q = -3; q <= 3; q++)
                for (int r = -3; r <= 3; r++)
                    for (int i = 0; i < 24; i++)
                    {
                        int seed = Mathf.Abs(q * 92821 + r * 68917 + i * 40361);
                        var v = (Vector3)method.Invoke(null,
                            new object[] { i, 24, seed, golden, maxRadius, q * 23f + r * 37f });
                        worst = Mathf.Max(worst, new Vector2(v.x, v.z).magnitude);
                    }

            // ジッター0.05ぶんは超えうる設計。+0.01 は浮動小数点の許容のみで、仕様値ではない。
            Assert.LessOrEqual(worst, maxRadius + productionJitter + 0.01f,
                $"木の中心が想定より外へ出ている（実測 {worst:F4}）");
        }

        [Test]
        public void SpiralOffset_JitterIsExactlyPlusMinus005()
        {
            // ジッター幅そのものを固定する。ここが広がると木が隣タイルへ深く入る。
            float golden = GetInternalConst<float>(typeof(HexTile), "TreeGoldenAngleDeg");
            var method = typeof(HexTile).GetMethod("ComputeSpiralOffset", BindingFlags.NonPublic | BindingFlags.Static);

            // index=0, count=1 のとき rNorm=0 なので、半径はジッターそのものになる（負は0へクランプ）。
            float maxSeen = 0f;
            for (int seed = 0; seed < 20000; seed++)
            {
                var v = (Vector3)method.Invoke(null, new object[] { 0, 1, seed, golden, 0f, 0f });
                maxSeen = Mathf.Max(maxSeen, new Vector2(v.x, v.z).magnitude);
            }
            Assert.AreEqual(0.05f, maxSeen, 0.0001f, $"半径ジッターが ±0.05 から変わっている（実測 {maxSeen:F4}）");
        }

        // ══ 個体サイズ ═══════════════════════════════════════════════════

        [Test]
        public void TreeSizeMultiplier_StaysWithin085To115()
        {
            var method = typeof(TreeBillboardSystem).GetMethod("SizeMultiplier", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(method, "SizeMultiplier が見つからない");

            float min = float.MaxValue, max = float.MinValue;
            for (int seed = -50000; seed < 50000; seed += 7)
            {
                float m = (float)method.Invoke(null, new object[] { seed });
                min = Mathf.Min(min, m);
                max = Mathf.Max(max, m);
            }

            Assert.GreaterOrEqual(min, 0.85f - 0.0001f, $"下限が広がっている（実測 {min:F3}）");
            Assert.LessOrEqual(max,   1.15f + 0.0001f, $"上限が広がっている（実測 {max:F3}）");
        }

        [Test]
        public void TreeSizeMultiplier_IsDeterministic()
        {
            var method = typeof(TreeBillboardSystem).GetMethod("SizeMultiplier", BindingFlags.NonPublic | BindingFlags.Static);

            for (int seed = -1000; seed < 1000; seed += 3)
                Assert.AreEqual((float)method.Invoke(null, new object[] { seed }),
                                (float)method.Invoke(null, new object[] { seed }), 0f);
        }
    }
}
