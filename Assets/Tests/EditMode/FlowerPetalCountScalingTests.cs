// 役割: FlowerPetalSystem.CalcCountMultiplier（クラスターサイズに応じた花びら発生数倍率）の単体テスト。

using NUnit.Framework;
using ElfVillage.Tiles;

namespace ElfVillage.Tests
{
    public class FlowerPetalCountScalingTests
    {
        private const int   BoostStart = 7;
        private const int   BoostMax   = 25;
        private const float MaxMult    = 4f;

        [Test]
        public void CalcCountMultiplier_AtOrBelowBoostStart_ReturnsOne()
        {
            Assert.AreEqual(1f, FlowerPetalSystem.CalcCountMultiplier(3, BoostStart, BoostMax, MaxMult));
            Assert.AreEqual(1f, FlowerPetalSystem.CalcCountMultiplier(7, BoostStart, BoostMax, MaxMult));
        }

        [Test]
        public void CalcCountMultiplier_AtOrAboveBoostMax_ReturnsMaxMultiplier()
        {
            Assert.AreEqual(MaxMult, FlowerPetalSystem.CalcCountMultiplier(25, BoostStart, BoostMax, MaxMult));
            Assert.AreEqual(MaxMult, FlowerPetalSystem.CalcCountMultiplier(100, BoostStart, BoostMax, MaxMult));
        }

        [Test]
        public void CalcCountMultiplier_Between_IncreasesMonotonically()
        {
            float prev = FlowerPetalSystem.CalcCountMultiplier(BoostStart, BoostStart, BoostMax, MaxMult);
            for (int size = BoostStart + 1; size <= BoostMax; size++)
            {
                float cur = FlowerPetalSystem.CalcCountMultiplier(size, BoostStart, BoostMax, MaxMult);
                Assert.GreaterOrEqual(cur, prev, $"size={size}での倍率は単調増加であるべき");
                prev = cur;
            }
        }

        [Test]
        public void CalcCountMultiplier_Midpoint_IsBetweenOneAndMax()
        {
            int mid = (BoostStart + BoostMax) / 2;
            float value = FlowerPetalSystem.CalcCountMultiplier(mid, BoostStart, BoostMax, MaxMult);
            Assert.Greater(value, 1f);
            Assert.Less(value, MaxMult);
        }
    }
}
