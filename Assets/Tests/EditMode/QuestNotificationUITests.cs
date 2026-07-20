// 役割: QuestNotificationUI（トースト通知）の単体テスト。
//       StartCoroutine自体はPlay Mode外では進行しないため、コンテンツ更新（同期部分）は
//       イベント購読経由で、時間経過に応じた表示状態（アルファ・位置）は純粋関数
//       QuestNotificationUI.ComputeFrame()を直接呼び出して検証する（既存のCalc系テストと同じ方針）。

using System.Reflection;
using NUnit.Framework;
using ElfVillage.Core;
using ElfVillage.Quest;
using ElfVillage.UI;
using UnityEngine;
using TMPro;

namespace ElfVillage.Tests
{
    public class QuestNotificationUITests
    {
        // ── ライフサイクル呼び出しヘルパー ────────────────────────────

        private static void InvokeLifecycle(Component c, string methodName)
        {
            var method = c.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(method, $"{c.GetType().Name}に{methodName}メソッドが見つかりません（リフレクション対象名の変更を確認してください）");
            method.Invoke(c, null);
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            var field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, $"{target.GetType().Name}に{fieldName}フィールドが見つかりません");
            field.SetValue(target, value);
        }

        // ── テスト用オブジェクト生成ヘルパー ──────────────────────────

        private static TMP_Text MakeText(string name)
        {
            var go = new GameObject(name);
            return go.AddComponent<TextMeshProUGUI>();
        }

        private readonly struct NotifParts
        {
            public readonly QuestNotificationUI Notif;
            public readonly GameObject           Root;
            public readonly CanvasGroup          CanvasGroup;
            public readonly TMP_Text             Header;
            public readonly TMP_Text             Title;
            public readonly TMP_Text             Progress;

            public NotifParts(QuestNotificationUI notif, GameObject root, CanvasGroup canvasGroup, TMP_Text header, TMP_Text title, TMP_Text progress)
            {
                Notif = notif;
                Root = root;
                CanvasGroup = canvasGroup;
                Header = header;
                Title = title;
                Progress = progress;
            }
        }

        private static NotifParts MakeNotification()
        {
            var go = new GameObject("TestQuestNotificationUI", typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            rt.anchoredPosition = new Vector2(0f, -30f);

            var canvasGroup = go.AddComponent<CanvasGroup>();
            var notif = go.AddComponent<QuestNotificationUI>();

            var header   = MakeText("HeaderText");
            var title    = MakeText("TitleText");
            var progress = MakeText("ProgressText");

            SetPrivateField(notif, "_root", rt);
            SetPrivateField(notif, "_canvasGroup", canvasGroup);
            SetPrivateField(notif, "_headerText", header);
            SetPrivateField(notif, "_titleText", title);
            SetPrivateField(notif, "_progressText", progress);

            InvokeLifecycle(notif, "OnEnable");
            return new NotifParts(notif, go, canvasGroup, header, title, progress);
        }

        private static void Teardown(NotifParts parts)
        {
            InvokeLifecycle(parts.Notif, "OnDisable");
            Object.DestroyImmediate(parts.Root);
            Object.DestroyImmediate(parts.Header.gameObject);
            Object.DestroyImmediate(parts.Title.gameObject);
            Object.DestroyImmediate(parts.Progress.gameObject);
        }

        private static QuestDefinition MakeQuest(string title, int targetCount)
        {
            var q = ScriptableObject.CreateInstance<QuestDefinition>();
            q.title          = title;
            q.targetCategory = TerrainClusterCategory.Forest;
            q.targetCount    = targetCount;
            return q;
        }

        // ── 1. 開始通知が表示される（内容が正しく設定される） ───────────────

        [Test]
        public void OnQuestStarted_SetsHeaderTitleAndProgress()
        {
            var parts = MakeNotification();
            try
            {
                var quest = MakeQuest("森を育てよう", 5);
                EventBus.Publish(new QuestStartedEvent(quest));

                Assert.AreEqual("🌲 新しいクエスト", parts.Header.text);
                Assert.AreEqual("森を育てよう", parts.Title.text);
                Assert.AreEqual("0 / 5", parts.Progress.text);
                Assert.IsTrue(parts.Progress.gameObject.activeSelf, "開始通知では進捗行が表示されるはず");
            }
            finally
            {
                Teardown(parts);
            }
        }

        // ── 2. 達成通知が表示される（内容が正しく設定される、進捗行は非表示） ──

        [Test]
        public void OnQuestCompleted_SetsHeaderAndTitle_HidesProgress()
        {
            var parts = MakeNotification();
            try
            {
                var quest = MakeQuest("森を育てよう", 5);
                EventBus.Publish(new QuestCompletedEvent(quest));

                Assert.AreEqual("✨ Quest Complete!", parts.Header.text);
                Assert.AreEqual("森を育てよう", parts.Title.text);
                Assert.IsFalse(parts.Progress.gameObject.activeSelf, "達成通知では進捗行は非表示のはず");
            }
            finally
            {
                Teardown(parts);
            }
        }

        // ── 3. 一定時間後に非表示になる（ComputeFrameの時間経過を直接検証） ──

        [Test]
        public void ComputeFrame_BeforeSlideIn_IsHiddenAboveScreen()
        {
            var frame = QuestNotificationUI.ComputeFrame(0f, 3f, 0.4f, 0.6f, 80f);
            Assert.AreEqual(0f, frame.Alpha);
            Assert.AreEqual(80f, frame.PositionOffsetY);
            Assert.IsFalse(frame.Finished);
        }

        [Test]
        public void ComputeFrame_DuringHold_IsFullyVisibleAtRestPosition()
        {
            var frame = QuestNotificationUI.ComputeFrame(1.5f, 3f, 0.4f, 0.6f, 80f);
            Assert.AreEqual(1f, frame.Alpha);
            Assert.AreEqual(0f, frame.PositionOffsetY);
            Assert.IsFalse(frame.Finished);
        }

        [Test]
        public void ComputeFrame_AfterTotalDuration_IsHiddenAgain()
        {
            var frame = QuestNotificationUI.ComputeFrame(3f, 3f, 0.4f, 0.6f, 80f);
            Assert.AreEqual(0f, frame.Alpha);
            Assert.AreEqual(80f, frame.PositionOffsetY);
            Assert.IsTrue(frame.Finished, "総表示時間が経過したら非表示状態で終了するはず");
        }

        // ── 4. 通知表示中に別通知が来た場合、内容が上書きされる ───────────────

        [Test]
        public void SecondNotification_WhileFirstStillActive_OverwritesContent()
        {
            var parts = MakeNotification();
            try
            {
                var questA = MakeQuest("森を育てよう", 5);
                EventBus.Publish(new QuestStartedEvent(questA));
                Assert.AreEqual("🌲 新しいクエスト", parts.Header.text);

                // 1つ目の表示が終わらないうちに2つ目（達成）を発行する。
                var questB = MakeQuest("花畑を広げよう", 8);
                EventBus.Publish(new QuestCompletedEvent(questB));

                Assert.AreEqual("✨ Quest Complete!", parts.Header.text, "後から来た通知の内容へ上書きされるはず");
                Assert.AreEqual("花畑を広げよう", parts.Title.text);
                Assert.IsFalse(parts.Progress.gameObject.activeSelf);
            }
            finally
            {
                Teardown(parts);
            }
        }
    }
}
