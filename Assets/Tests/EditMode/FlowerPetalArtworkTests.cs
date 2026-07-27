// 役割: 花びらの絵柄まわり（色の乗せ方・色ごとの振り分け）を検証する。
//       絵柄を貼ったことで「粒の色 = ティアの色」ではなくなったため、
//       色の扱いを間違えると絵の陰影が沈んだり、透明度が壊れたりする。

using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using ElfVillage.Tiles;

namespace ElfVillage.Tests
{
    public class FlowerPetalArtworkTests
    {
        private readonly List<Object> _created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
        }

        private Texture2D MakeTexture(string name)
        {
            var t = new Texture2D(2, 2) { name = name };
            _created.Add(t);
            return t;
        }

        // ══ 色の乗せ方（TintForTexturedPetal） ═══════════════════════════

        [Test]
        public void Tint_AlwaysKeepsTheTierAlpha()
        {
            // ★ここが壊れると花びらが濃くなったり消えたりする。
            //   RGBだけ白へ寄せ、アルファはティアの値をそのまま使う契約。
            var tier = new Color(0.72f, 0.40f, 1.00f, 0.85f);
            foreach (var tint in new[] { 0f, 0.15f, 0.5f, 1f })
                Assert.AreEqual(tier.a, FlowerPetalSystem.TintForTexturedPetal(tier, tint).a, 0.0001f,
                    $"tint={tint} でアルファが変わった");
        }

        [Test]
        public void Tint_Zero_LeavesTheArtworkColorUntouched()
        {
            // tint=0 は「絵の色そのまま」。粒の色は白（＝テクスチャに何も掛けない）になる。
            var tier   = new Color(1.00f, 0.25f, 0.25f, 0.85f);
            var result = FlowerPetalSystem.TintForTexturedPetal(tier, 0f);

            Assert.AreEqual(1f, result.r, 0.0001f);
            Assert.AreEqual(1f, result.g, 0.0001f);
            Assert.AreEqual(1f, result.b, 0.0001f);
        }

        [Test]
        public void Tint_One_UsesTheTierColor()
        {
            var tier   = new Color(0.45f, 0.72f, 1.00f, 0.90f);
            var result = FlowerPetalSystem.TintForTexturedPetal(tier, 1f);

            Assert.AreEqual(tier.r, result.r, 0.0001f);
            Assert.AreEqual(tier.g, result.g, 0.0001f);
            Assert.AreEqual(tier.b, result.b, 0.0001f);
        }

        [Test]
        public void Tint_IsMonotonicBetweenWhiteAndTierColor()
        {
            // tintを上げるほどティアの色へ近づくこと（途中で反転しない）。
            var tier = new Color(0.2f, 0.4f, 0.6f, 0.85f);
            float previous = 1f;
            foreach (var tint in new[] { 0f, 0.25f, 0.5f, 0.75f, 1f })
            {
                float r = FlowerPetalSystem.TintForTexturedPetal(tier, tint).r;
                Assert.LessOrEqual(r, previous + 0.0001f, $"tint={tint} で色が戻っている");
                previous = r;
            }
        }

        [Test]
        public void Tint_InvalidValues_AreHandledSafely()
        {
            var tier = new Color(0.72f, 0.40f, 1.00f, 0.85f);
            foreach (var bad in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, -1f, 5f })
            {
                var c = FlowerPetalSystem.TintForTexturedPetal(tier, bad);
                Assert.IsTrue(c.r >= 0f && c.r <= 1f, $"tint={bad} で範囲外の色 {c.r}");
                Assert.IsTrue(c.g >= 0f && c.g <= 1f, $"tint={bad} で範囲外の色 {c.g}");
                Assert.IsTrue(c.b >= 0f && c.b <= 1f, $"tint={bad} で範囲外の色 {c.b}");
                Assert.AreEqual(tier.a, c.a, 0.0001f, $"tint={bad} でアルファが変わった");
            }
        }

        // ══ 色ごとの振り分け（CollectTexturesFor） ═══════════════════════

        private static List<Texture2D> CollectFor(FlowerPetalSystem system, string colorName)
        {
            var m = typeof(FlowerPetalSystem).GetMethod("CollectTexturesFor",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(m, "CollectTexturesFor が見つからない");
            return (List<Texture2D>)m.Invoke(system, new object[] { colorName });
        }

        private FlowerPetalSystem MakeSystemWith(params string[] textureNames)
        {
            var go = new GameObject("PetalTest");
            _created.Add(go);
            var system = go.AddComponent<FlowerPetalSystem>();

            var list = new Texture2D[textureNames.Length];
            for (int i = 0; i < textureNames.Length; i++) list[i] = MakeTexture(textureNames[i]);

            typeof(FlowerPetalSystem)
                .GetField("_petalTextures", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(system, list);
            return system;
        }

        [Test]
        public void Collect_PicksOnlyTheMatchingColorSuffix()
        {
            var system = MakeSystemWith(
                "Petal_Shape_01_Yellow", "Petal_Shape_02_Yellow", "Petal_Shape_03_Yellow",
                "Petal_Shape_01_Blue",   "Petal_Shape_01_Pink");

            Assert.AreEqual(3, CollectFor(system, "Yellow").Count, "Yellowの3形状が拾えていない");
            Assert.AreEqual(1, CollectFor(system, "Blue").Count);
            Assert.AreEqual(1, CollectFor(system, "Pink").Count);
        }

        [Test]
        public void Collect_IgnoresUnusedColors()
        {
            // Green / Orange は現在ティアが無いので、どのティアにも入らない。
            var system = MakeSystemWith("Petal_Shape_01_Green", "Petal_Shape_01_Orange");

            foreach (var tier in new[] { "Yellow", "Blue", "Purple", "Red", "Pink" })
                Assert.AreEqual(0, CollectFor(system, tier).Count, $"{tier} に未使用色が混ざっている");
        }

        [Test]
        public void Collect_IsCaseInsensitive()
        {
            var system = MakeSystemWith("Petal_Shape_01_YELLOW");
            Assert.AreEqual(1, CollectFor(system, "Yellow").Count);
        }

        [Test]
        public void Collect_SkipsNamesWithoutASuffix()
        {
            var system = MakeSystemWith("PetalNoUnderscore", "Petal_");
            Assert.AreEqual(0, CollectFor(system, "Yellow").Count);
        }

        [Test]
        public void Collect_EmptyOrNullInput_ReturnsEmpty()
        {
            var system = MakeSystemWith("Petal_Shape_01_Yellow");
            Assert.AreEqual(0, CollectFor(system, null).Count);
            Assert.AreEqual(0, CollectFor(system, "").Count);
        }

        [Test]
        public void Collect_NoTexturesAssigned_ReturnsEmpty()
        {
            var system = MakeSystemWith();
            Assert.AreEqual(0, CollectFor(system, "Yellow").Count);
        }
    }
}
