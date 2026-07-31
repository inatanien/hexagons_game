// 役割: 誕生の目印（地面に広がって消える光の輪）の純粋関数を検証する。
//       大きさと表示時間は「どこで生まれたか」を伝える演出の要なので、
//       あとから何気なく変えると目印が見えなくなる／消えるのが早すぎる。

using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using ElfVillage.Spirits;

namespace ElfVillage.Tests
{
    public class SpiritBirthMarkerTests
    {
        private GameObject _host;

        [TearDown]
        public void TearDown()
        {
            if (_host != null) { Object.DestroyImmediate(_host); _host = null; }
        }

        /// <summary>
        /// Inspectorの既定値を読む。
        /// ★フィールド初期化子はコンストラクタで走るため、実際にAddComponentする必要がある
        ///   （FormatterServicesで作った素のオブジェクトでは全て0になる）。
        /// </summary>
        private T GetSerializedDefault<T>(string fieldName)
        {
            if (_host == null)
            {
                _host = new GameObject("PresentationDefaults");
                _host.AddComponent<ForestSpiritPresentation>();
            }

            var f = typeof(ForestSpiritPresentation).GetField(fieldName,
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(f, $"{fieldName} が見つからない");

            return (T)f.GetValue(_host.GetComponent<ForestSpiritPresentation>());
        }

        // ══ 輪の大きさ ═══════════════════════════════════════════════════

        [Test]
        public void MarkerSize_StartsAtStartSize_AndEndsAtEndSize()
        {
            Assert.AreEqual(0.6f, ForestSpiritPresentation.ComputeMarkerSize(0f, 0.6f, 2.4f), 0.0001f);
            Assert.AreEqual(2.4f, ForestSpiritPresentation.ComputeMarkerSize(1f, 0.6f, 2.4f), 0.0001f);
        }

        [Test]
        public void MarkerSize_GrowsMonotonically()
        {
            // 途中で縮むと「広がる輪」に見えない。
            float previous = -1f;
            for (int i = 0; i <= 20; i++)
            {
                float size = ForestSpiritPresentation.ComputeMarkerSize(i / 20f, 0.6f, 2.4f);
                Assert.GreaterOrEqual(size, previous, $"progress={i / 20f} で縮んだ");
                previous = size;
            }
        }

        [Test]
        public void MarkerSize_ExpandsFastThenEases()
        {
            // 序盤に大きく広がって終盤で緩む（水面の波紋のような動き）。
            float firstHalf  = ForestSpiritPresentation.ComputeMarkerSize(0.5f, 0.6f, 2.4f) - 0.6f;
            float secondHalf = 2.4f - ForestSpiritPresentation.ComputeMarkerSize(0.5f, 0.6f, 2.4f);
            Assert.Greater(firstHalf, secondHalf, "前半より後半の方が大きく広がっている");
        }

        [Test]
        public void MarkerSize_OutOfRangeProgress_IsClamped()
        {
            Assert.AreEqual(0.6f, ForestSpiritPresentation.ComputeMarkerSize(-5f, 0.6f, 2.4f), 0.0001f);
            Assert.AreEqual(2.4f, ForestSpiritPresentation.ComputeMarkerSize(9f,  0.6f, 2.4f), 0.0001f);
        }

        [Test]
        public void MarkerSize_InvalidValues_StayPositive()
        {
            // 0スケールは法線が壊れて描画が乱れるため、どんな入力でも正の大きさを返す。
            foreach (var bad in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, 0f, -3f })
            {
                Assert.Greater(ForestSpiritPresentation.ComputeMarkerSize(0.5f, bad, 2.4f), 0f, $"startSize={bad}");
                Assert.Greater(ForestSpiritPresentation.ComputeMarkerSize(0.5f, 0.6f, bad), 0f, $"endSize={bad}");
                Assert.Greater(ForestSpiritPresentation.ComputeMarkerSize(bad, 0.6f, 2.4f), 0f, $"progress={bad}");
            }
        }

        [Test]
        public void MarkerSize_ReversedRange_DoesNotShrink()
        {
            // endがstartより小さくても縮まない（設定ミスで輪が消えるのを防ぐ）。
            float start = ForestSpiritPresentation.ComputeMarkerSize(0f, 2.4f, 0.6f);
            float end   = ForestSpiritPresentation.ComputeMarkerSize(1f, 2.4f, 0.6f);
            Assert.GreaterOrEqual(end, start);
        }

        // ══ 表示時間 ═════════════════════════════════════════════════════

        [Test]
        public void MarkerDuration_IsNeverShorterThanTheBirthAnimation()
        {
            // ★短いと、精霊が現れ切る前に目印が消えて「どこで起きたか」を確かめられない。
            foreach (var requested in new[] { 0.1f, 0.5f, 1.0f, 1.19f })
                Assert.GreaterOrEqual(
                    ForestSpiritPresentation.SafeMarkerDuration(requested, 1.2f), 1.2f,
                    $"requested={requested} で誕生演出より短くなった");
        }

        [Test]
        public void MarkerDuration_KeepsLongerRequests()
        {
            Assert.AreEqual(1.8f, ForestSpiritPresentation.SafeMarkerDuration(1.8f, 1.2f), 0.0001f);
            Assert.AreEqual(3.0f, ForestSpiritPresentation.SafeMarkerDuration(3.0f, 1.2f), 0.0001f);
        }

        [Test]
        public void MarkerDuration_InvalidValues_AreHandledSafely()
        {
            foreach (var bad in new[] { float.NaN, float.PositiveInfinity, 0f, -2f })
            {
                float d = ForestSpiritPresentation.SafeMarkerDuration(bad, 1.2f);
                Assert.IsTrue(float.IsFinite(d) && d > 0f, $"requested={bad} で {d}");
                Assert.GreaterOrEqual(d, 1.2f, $"requested={bad} で誕生演出より短い");
            }

            foreach (var badBirth in new[] { float.NaN, 0f, -1f })
            {
                float d = ForestSpiritPresentation.SafeMarkerDuration(1.8f, badBirth);
                Assert.AreEqual(1.8f, d, 0.0001f, $"birthDuration={badBirth} で要求値が壊れた");
            }
        }

        [Test]
        public void MarkerDuration_IsCappedToAReasonableLength()
        {
            // 長すぎると次に置いたタイルの目印と重なって、どれが今のものか分からなくなる。
            Assert.LessOrEqual(ForestSpiritPresentation.SafeMarkerDuration(1000f, 1.2f), 10f);
        }

        // ══ Inspectorの既定値 ════════════════════════════════════════════

        [Test]
        public void SerializedDefaults_LetTheMarkerOutlastTheBirthAnimation()
        {
            // 既定値のまま使ったときに、目印が誕生演出より長く残ること。
            float birth  = GetSerializedDefault<float>("_birthDuration");
            float marker = GetSerializedDefault<float>("_markerDuration");

            Assert.Greater(marker, birth,
                $"既定値で目印({marker}s)が誕生演出({birth}s)より短い／同じ");
        }

        [Test]
        public void SerializedDefaults_MakeTheRingReadAsATileArea()
        {
            // 輪の最大サイズは、六角タイルの辺中点（約1.73）より大きいこと。
            // 小さいと「このタイルのあたり」ではなく単なる光点に見える。
            float end = GetSerializedDefault<float>("_markerEndSize");
            Assert.Greater(end, 1.73f, $"最大サイズ {end} がタイルより小さい");

            float start = GetSerializedDefault<float>("_markerStartSize");
            Assert.Less(start, end, "開始サイズが最大サイズ以上（広がらない）");
            Assert.Greater(start, 0f, "開始サイズが0以下");
        }
    }
}
