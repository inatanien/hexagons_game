// 役割: Stage 16「汎用通知」の純粋判定を検証する。
//       表示の実挙動（キュー処理・Settings停止）はPlayMode側で検証する。

using NUnit.Framework;
using ElfVillage.Core;
using ElfVillage.UI;

namespace ElfVillage.Tests
{
    public class WorldNoticeTests
    {
        // ══ InteractionTimePolicy ═══════════════════════════════════════

        [Test]
        public void ShouldAdvanceTime_Playing_IsTrue()
            => Assert.IsTrue(InteractionTimePolicy.ShouldAdvanceTime(GameInteractionState.Playing));

        [Test]
        public void ShouldAdvanceTime_PauseMenu_IsTrue()
            => Assert.IsTrue(InteractionTimePolicy.ShouldAdvanceTime(GameInteractionState.PauseMenu),
                   "PauseMenu中は通知も演出も進めてよい");

        [Test]
        public void ShouldAdvanceTime_Settings_IsFalse()
            => Assert.IsFalse(InteractionTimePolicy.ShouldAdvanceTime(GameInteractionState.Settings),
                   "Settings中は通知の表示時間も止める");

        [Test]
        public void ShouldAdvanceTime_UnknownState_IsFalse()
        {
            foreach (var unknown in new[] { (GameInteractionState)99, (GameInteractionState)(-1) })
                Assert.IsFalse(InteractionTimePolicy.ShouldAdvanceTime(unknown),
                    $"未知の状態({unknown})は安全側（停止）へ倒すべき");
        }

        [Test]
        public void SpiritSimulationPolicy_MatchesInteractionTimePolicy()
        {
            // 精霊と通知が同じ条件で止まることを保証する（片方だけ進む不整合を防ぐ）。
            foreach (GameInteractionState s in System.Enum.GetValues(typeof(GameInteractionState)))
                Assert.AreEqual(InteractionTimePolicy.ShouldAdvanceTime(s),
                                ElfVillage.Spirits.SpiritSimulationPolicy.ShouldSimulate(s),
                                $"{s} で精霊と通知の判定がずれている");
        }

        // ══ WorldNoticeEvent ════════════════════════════════════════════

        [Test]
        public void WorldNoticeEvent_NullStrings_BecomeEmpty()
        {
            var e = new WorldNoticeEvent(null, null, 3f);
            Assert.AreEqual(string.Empty, e.Header);
            Assert.AreEqual(string.Empty, e.Body);
        }

        [Test]
        public void WorldNoticeEvent_KeepsGivenValues()
        {
            var e = new WorldNoticeEvent("H", "B", 2.5f, WorldNoticeKind.Spirit);
            Assert.AreEqual("H", e.Header);
            Assert.AreEqual("B", e.Body);
            Assert.AreEqual(2.5f, e.DisplayDuration, 0.0001f);
            Assert.AreEqual(WorldNoticeKind.Spirit, e.Kind);
        }

        // ══ WorldNoticeUI の純粋関数 ════════════════════════════════════

        [Test]
        public void SafeDuration_ValidValue_IsKept()
            => Assert.AreEqual(2.5f, WorldNoticeUI.SafeDuration(2.5f, 3f), 0.0001f);

        [Test]
        public void SafeDuration_InvalidValue_FallsBack()
        {
            foreach (var bad in new[] { 0f, -1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
                Assert.AreEqual(3f, WorldNoticeUI.SafeDuration(bad, 3f), 0.0001f, $"{bad} で既定へ倒れない");
        }

        [Test]
        public void SafeDuration_InvalidFallback_StillReturnsPositive()
        {
            foreach (var badFallback in new[] { 0f, -5f, float.NaN })
            {
                float v = WorldNoticeUI.SafeDuration(float.NaN, badFallback);
                Assert.IsTrue(float.IsFinite(v) && v > 0f, $"fallback={badFallback} で不正な秒数が返った");
            }
        }

        [Test]
        public void SafeMaxQueued_ClampsIntoRange()
        {
            Assert.AreEqual(1,  WorldNoticeUI.SafeMaxQueued(0));
            Assert.AreEqual(1,  WorldNoticeUI.SafeMaxQueued(-5));
            Assert.AreEqual(3,  WorldNoticeUI.SafeMaxQueued(3));
            Assert.AreEqual(16, WorldNoticeUI.SafeMaxQueued(999));
        }

    }
}
