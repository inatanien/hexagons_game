// 役割: QuestPanelUI（表示専用のクエストUI）の単体テスト。
//       TMP_Textはレンダリングに依存しない範囲でEditModeでも安全に扱えるため、
//       QuestManagerTests.cs等と同じくEditModeで検証する。
//       EditModeではAddComponent直後にOnEnable/OnDisable/Startが自動発火しないため、
//       リフレクションで明示的に呼び出す（既存テストと同じ手法）。

using System.Collections;
using System.Reflection;
using NUnit.Framework;
using ElfVillage.Core;
using ElfVillage.Quest;
using ElfVillage.UI;
using UnityEngine;
using TMPro;

namespace ElfVillage.Tests
{
    public class QuestPanelUITests
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

        private readonly struct PanelParts
        {
            public readonly QuestPanelUI PanelUI;
            public readonly GameObject   PanelRoot;
            public readonly TMP_Text     TitleText;
            public readonly TMP_Text     ProgressText;
            public readonly TMP_Text     CompletedText;

            public PanelParts(QuestPanelUI panelUI, GameObject panelRoot, TMP_Text titleText, TMP_Text progressText, TMP_Text completedText)
            {
                PanelUI       = panelUI;
                PanelRoot     = panelRoot;
                TitleText     = titleText;
                ProgressText  = progressText;
                CompletedText = completedText;
            }
        }

        private static PanelParts MakePanel()
        {
            var go      = new GameObject("TestQuestPanelUI");
            var panelUI = go.AddComponent<QuestPanelUI>();

            var panelRoot     = new GameObject("PanelRoot");
            var titleText     = MakeText("TitleText");
            var progressText  = MakeText("ProgressText");
            var completedText = MakeText("CompletedText");
            panelRoot.SetActive(false); // 初期状態は非表示（QuestStartedEventで表示される想定）

            SetPrivateField(panelUI, "_panelRoot", panelRoot);
            SetPrivateField(panelUI, "_titleText", titleText);
            SetPrivateField(panelUI, "_progressText", progressText);
            SetPrivateField(panelUI, "_completedText", completedText);

            InvokeLifecycle(panelUI, "OnEnable");
            return new PanelParts(panelUI, panelRoot, titleText, progressText, completedText);
        }

        private static void Teardown(PanelParts parts)
        {
            InvokeLifecycle(parts.PanelUI, "OnDisable");
            Object.DestroyImmediate(parts.PanelUI.gameObject);
            Object.DestroyImmediate(parts.PanelRoot);
            Object.DestroyImmediate(parts.TitleText.gameObject);
            Object.DestroyImmediate(parts.ProgressText.gameObject);
            Object.DestroyImmediate(parts.CompletedText.gameObject);
        }

        private static QuestDefinition MakeQuest(string title, int targetCount)
        {
            var q = ScriptableObject.CreateInstance<QuestDefinition>();
            q.title          = title;
            q.targetCategory = TerrainClusterCategory.Forest;
            q.targetCount    = targetCount;
            return q;
        }

        // ── 1. QuestStartedEventでタイトルと0 / 5が表示される ───────────────

        [Test]
        public void OnQuestStarted_ShowsTitleAndZeroProgress()
        {
            var parts = MakePanel();
            try
            {
                var quest = MakeQuest("森を育てよう", 5);
                EventBus.Publish(new QuestStartedEvent(quest));

                Assert.IsTrue(parts.PanelRoot.activeSelf, "QuestStartedEventでパネルが表示されるはず");
                Assert.AreEqual("森を育てよう", parts.TitleText.text);
                Assert.AreEqual("0 / 5", parts.ProgressText.text);
                Assert.IsFalse(parts.CompletedText.gameObject.activeSelf, "開始時点では達成表示は非表示のはず");
            }
            finally
            {
                Teardown(parts);
            }
        }

        // ── 2. QuestProgressChangedEventで3 / 5へ更新される ─────────────────

        [Test]
        public void OnQuestProgressChanged_UpdatesProgressText()
        {
            var parts = MakePanel();
            try
            {
                var quest = MakeQuest("森を育てよう", 5);
                EventBus.Publish(new QuestProgressChangedEvent(quest, 3));

                Assert.AreEqual("3 / 5", parts.ProgressText.text);
            }
            finally
            {
                Teardown(parts);
            }
        }

        // ── 3. 異なる進捗を連続で受けても最新値が表示される ───────────────

        [Test]
        public void OnQuestProgressChanged_MultipleUpdates_ShowsLatestValue()
        {
            var parts = MakePanel();
            try
            {
                var quest = MakeQuest("森を育てよう", 5);
                EventBus.Publish(new QuestProgressChangedEvent(quest, 1));
                EventBus.Publish(new QuestProgressChangedEvent(quest, 3));
                EventBus.Publish(new QuestProgressChangedEvent(quest, 4));

                Assert.AreEqual("4 / 5", parts.ProgressText.text);
            }
            finally
            {
                Teardown(parts);
            }
        }

        // ── 4. QuestCompletedEventで5 / 5と達成表示になる ───────────────────

        [Test]
        public void OnQuestCompleted_ShowsFullProgressAndCompletedText()
        {
            var parts = MakePanel();
            try
            {
                var quest = MakeQuest("森を育てよう", 5);
                EventBus.Publish(new QuestCompletedEvent(quest));

                Assert.AreEqual("5 / 5", parts.ProgressText.text);
                Assert.IsTrue(parts.CompletedText.gameObject.activeSelf, "達成時は達成表示がアクティブになるはず");
            }
            finally
            {
                Teardown(parts);
            }
        }

        // ── 5. OnDisable後はイベントを受けても表示が更新されない ─────────────

        [Test]
        public void AfterOnDisable_DoesNotUpdateOnFurtherEvents()
        {
            var parts = MakePanel();
            var quest = MakeQuest("森を育てよう", 5);
            EventBus.Publish(new QuestProgressChangedEvent(quest, 2));
            Assert.AreEqual("2 / 5", parts.ProgressText.text); // 前提確認

            InvokeLifecycle(parts.PanelUI, "OnDisable");
            try
            {
                EventBus.Publish(new QuestProgressChangedEvent(quest, 4));

                Assert.AreEqual("2 / 5", parts.ProgressText.text, "OnDisable後はイベントを受けても表示が更新されないはず");
            }
            finally
            {
                Object.DestroyImmediate(parts.PanelUI.gameObject);
                Object.DestroyImmediate(parts.PanelRoot);
                Object.DestroyImmediate(parts.TitleText.gameObject);
                Object.DestroyImmediate(parts.ProgressText.gameObject);
                Object.DestroyImmediate(parts.CompletedText.gameObject);
            }
        }

        // ── 6. OnEnableし直した際に重複購読されない ─────────────────────────

        [Test]
        public void ReEnabling_DoesNotDoubleSubscribe()
        {
            var parts = MakePanel();
            try
            {
                InvokeLifecycle(parts.PanelUI, "OnDisable");
                InvokeLifecycle(parts.PanelUI, "OnEnable");

                var handlersField = typeof(EventBus).GetField("_handlers", BindingFlags.NonPublic | BindingFlags.Static);
                var handlers = (System.Collections.IDictionary)handlersField.GetValue(null);
                var del = (System.Delegate)handlers[typeof(QuestProgressChangedEvent)];

                int countForThisPanel = 0;
                foreach (var d in del.GetInvocationList())
                    if (System.Object.ReferenceEquals(d.Target, parts.PanelUI))
                        countForThisPanel++;

                Assert.AreEqual(1, countForThisPanel,
                    "OnDisable→OnEnableを経てもQuestProgressChangedEventの購読はこのパネルにつき1つだけのはず");
            }
            finally
            {
                Teardown(parts);
            }
        }

        // ── 7. Play開始時にQuestStartedEventを取り逃がしても正しい初期表示になる ──
        // QuestManager.OnEnable/QuestPanelUI.OnEnableの相対順序に関わらず、
        // QuestManager.Startの時点でQuestPanelUIが確実に購読済みであることを確認する。
        // 順序を両方試すことで、特定の順序に依存していないことを検証する。

        private static QuestManager MakeManagerWithoutStart(QuestDefinition quest)
        {
            var go      = new GameObject("TestQuestManager");
            var manager = go.AddComponent<QuestManager>();
            SetPrivateField(manager, "_activeQuest", quest);
            return manager;
        }

        [Test]
        public void QuestStarted_NotMissed_WhenManagerEnabledBeforePanel()
        {
            var quest   = MakeQuest("森を育てよう", 5);
            var manager = MakeManagerWithoutStart(quest);
            InvokeLifecycle(manager, "OnEnable"); // Managerが先にOnEnable

            var parts = MakePanel(); // Panelが後にOnEnable
            try
            {
                InvokeLifecycle(manager, "Start"); // Startはこの後

                Assert.AreEqual("森を育てよう", parts.TitleText.text);
                Assert.AreEqual("0 / 5", parts.ProgressText.text);
            }
            finally
            {
                InvokeLifecycle(manager, "OnDisable");
                Object.DestroyImmediate(manager.gameObject);
                Teardown(parts);
            }
        }

        [Test]
        public void QuestStarted_NotMissed_WhenPanelEnabledBeforeManager()
        {
            var parts = MakePanel(); // Panelが先にOnEnable

            var quest   = MakeQuest("森を育てよう", 5);
            var manager = MakeManagerWithoutStart(quest);
            try
            {
                InvokeLifecycle(manager, "OnEnable"); // Managerが後にOnEnable
                InvokeLifecycle(manager, "Start");     // Startはこの後

                Assert.AreEqual("森を育てよう", parts.TitleText.text);
                Assert.AreEqual("0 / 5", parts.ProgressText.text);
            }
            finally
            {
                InvokeLifecycle(manager, "OnDisable");
                Object.DestroyImmediate(manager.gameObject);
                Teardown(parts);
            }
        }
    }
}
