// 役割: Stage 15「Settings中に精霊を止める」判定の検証。
//       停止・再開の実挙動はPlayMode（SpiritSimulationPlayModeTests）で検証する。

using NUnit.Framework;
using ElfVillage.Core;
using ElfVillage.Spirits;

namespace ElfVillage.Tests
{
    public class SpiritSimulationTests
    {
        [Test]
        public void ShouldSimulate_Playing_IsTrue()
            => Assert.IsTrue(SpiritSimulationPolicy.ShouldSimulate(GameInteractionState.Playing));

        [Test]
        public void ShouldSimulate_PauseMenu_IsTrue()
            => Assert.IsTrue(SpiritSimulationPolicy.ShouldSimulate(GameInteractionState.PauseMenu),
                   "PauseMenu中は背景で世界が息づいていてよい（既存Critterとも揃える）");

        [Test]
        public void ShouldSimulate_Settings_IsFalse()
            => Assert.IsFalse(SpiritSimulationPolicy.ShouldSimulate(GameInteractionState.Settings),
                   "Settings中はゲーム全体を触っているので世界も止める");

        [Test]
        public void ShouldSimulate_UnknownState_IsFalse()
        {
            foreach (var unknown in new[] { (GameInteractionState)99, (GameInteractionState)(-1) })
                Assert.IsFalse(SpiritSimulationPolicy.ShouldSimulate(unknown),
                    $"未知の状態({unknown})は安全側（停止）へ倒すべき");
        }

        [Test]
        public void ShouldSimulate_IsDeterministic()
        {
            foreach (GameInteractionState s in System.Enum.GetValues(typeof(GameInteractionState)))
            {
                bool first = SpiritSimulationPolicy.ShouldSimulate(s);
                for (int i = 0; i < 5; i++)
                    Assert.AreEqual(first, SpiritSimulationPolicy.ShouldSimulate(s));
            }
        }
    }
}
