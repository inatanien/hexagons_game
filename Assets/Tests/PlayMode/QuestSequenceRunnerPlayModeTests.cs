// 役割: QuestSequenceRunnerの「待ち時間」まわりを実行時の挙動として固定する（Stage B）。
//       コルーチンが必要なためEditModeでは確認できない3点をここで見る。
//         ・達成してすぐには切り替わらず、待ち時間の後に次が始まる
//         ・待っている間に同じ達成通知が何度来ても、切り替えを二重に予約しない
//         ・待っている間にOnDisableされたら、保留中の切り替えを取り消す
//
//       待ち時間の目的は「✨ Quest Complete!」のトーストを読む時間を作ることなので、
//       達成そのもの（QuestCompletedEvent）や報酬の発行は遅らせない。それもここで確認する。

using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ElfVillage.Core;
using ElfVillage.Quest;

namespace ElfVillage.Tests
{
    public class QuestSequenceRunnerPlayModeTests
    {
        private const float Delay = 0.4f;   // テストを待たせすぎない範囲で「即時ではない」ことが分かる長さ

        private readonly List<Object> _created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created)
                if (o != null) Object.Destroy(o);
            _created.Clear();
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            var field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, $"{target.GetType().Name}に{fieldName}フィールドが見つかりません");
            field.SetValue(target, value);
        }

        private QuestDefinition MakeClusterQuest(string title, TerrainClusterCategory category, int targetCount)
        {
            var q = ScriptableObject.CreateInstance<QuestDefinition>();
            _created.Add(q);
            q.title     = title;
            q.condition = new QuestCondition(QuestConditionKind.ClusterSize, category, targetCount);
            return q;
        }

        private QuestSequenceDefinition MakeSequence(params QuestDefinition[] quests)
        {
            var s = ScriptableObject.CreateInstance<QuestSequenceDefinition>();
            _created.Add(s);
            s.name   = "PlayModeTestSequence";
            s.quests = quests;
            return s;
        }

        /// <summary>実行時はAddComponentの瞬間にOnEnableが走るので、止めた状態で組み立ててから有効化する。</summary>
        private (QuestManager manager, QuestSequenceRunner runner, GameObject go) MakeRig(QuestSequenceDefinition sequence)
        {
            var go = new GameObject("PlayModeQuestRig");
            _created.Add(go);
            go.SetActive(false);

            var manager = go.AddComponent<QuestManager>();
            var runner  = go.AddComponent<QuestSequenceRunner>();
            SetPrivateField(runner, "_sequence", sequence);
            SetPrivateField(runner, "_questManager", manager);
            SetPrivateField(runner, "_nextQuestDelay", Delay);

            go.SetActive(true);
            return (manager, runner, go);
        }

        private sealed class Recorder : System.IDisposable
        {
            public readonly List<QuestDefinition> Started   = new();
            public readonly List<QuestDefinition> Completed = new();

            private readonly System.Action<QuestStartedEvent>   _onStarted;
            private readonly System.Action<QuestCompletedEvent> _onCompleted;

            public Recorder()
            {
                _onStarted   = e => Started.Add(e.Quest);
                _onCompleted = e => Completed.Add(e.Quest);
                EventBus.Subscribe(_onStarted);
                EventBus.Subscribe(_onCompleted);
            }

            public void Dispose()
            {
                EventBus.Unsubscribe(_onStarted);
                EventBus.Unsubscribe(_onCompleted);
            }
        }

        // ── 1. 達成の通知は即時、次のクエストは待ってから ───────────────

        [UnityTest]
        public IEnumerator NextQuest_StartsAfterDelay_NotImmediately()
        {
            var first  = MakeClusterQuest("1本目", TerrainClusterCategory.Forest, 5);
            var second = MakeClusterQuest("2本目", TerrainClusterCategory.River, 3);
            var rig    = MakeRig(MakeSequence(first, second));

            using (var r = new Recorder())
            {
                yield return null;   // Startが走り1本目が開始される
                CollectionAssert.AreEqual(new[] { first }, r.Started, "Startで1本目が開始されているはず");
                // ここから先は「切り替えで増えた分」だけを見る
                r.Started.Clear();

                EventBus.Publish(new TerrainClusterProgressEvent(TerrainClusterCategory.Forest, 5));

                Assert.AreEqual(1, r.Completed.Count, "達成の通知自体は遅らせないはず");
                Assert.AreEqual(0, r.Started.Count,   "達成した直後に次のクエストを始めてはいけない（トーストが読めなくなる）");

                yield return new WaitForSeconds(Delay + 0.2f);

                CollectionAssert.AreEqual(new[] { second }, r.Started, "待ち時間の後に次のクエストが始まるはず");
            }

            Object.Destroy(rig.go);
        }

        // ── 2. 待っている間に達成通知が重なっても二重に予約しない ───────

        [UnityTest]
        public IEnumerator DuplicateCompletions_DuringDelay_DoNotScheduleTwice()
        {
            var first  = MakeClusterQuest("1本目", TerrainClusterCategory.Forest, 5);
            var second = MakeClusterQuest("2本目", TerrainClusterCategory.River, 3);
            var third  = MakeClusterQuest("3本目", TerrainClusterCategory.Field, 2);
            var rig    = MakeRig(MakeSequence(first, second, third));

            using (var r = new Recorder())
            {
                yield return null;
                r.Started.Clear();   // Startで開始された1本目を除き、増えた分だけを見る

                EventBus.Publish(new TerrainClusterProgressEvent(TerrainClusterCategory.Forest, 5));
                // 待っている間に同じクエストの達成通知が何度も届いたケース
                EventBus.Publish(new QuestCompletedEvent(first));
                EventBus.Publish(new QuestCompletedEvent(first));

                yield return new WaitForSeconds(Delay + 0.2f);

                CollectionAssert.AreEqual(new[] { second }, r.Started,
                    "予約は1つだけ。3本目まで飛び越して進んではいけない");
            }

            Object.Destroy(rig.go);
        }

        // ── 3. 待っている間にOnDisableされたら切り替えを取り消す ────────

        [UnityTest]
        public IEnumerator DisablingDuringDelay_CancelsPendingAdvance()
        {
            var first  = MakeClusterQuest("1本目", TerrainClusterCategory.Forest, 5);
            var second = MakeClusterQuest("2本目", TerrainClusterCategory.River, 3);
            var rig    = MakeRig(MakeSequence(first, second));

            using (var r = new Recorder())
            {
                yield return null;
                r.Started.Clear();   // Startで開始された1本目を除き、増えた分だけを見る

                EventBus.Publish(new TerrainClusterProgressEvent(TerrainClusterCategory.Forest, 5));
                rig.runner.enabled = false;   // 保留中に無効化

                yield return new WaitForSeconds(Delay + 0.2f);

                Assert.AreEqual(0, r.Started.Count,
                    "無効化したら保留中の切り替えは取り消されるはず");
            }

            Object.Destroy(rig.go);
        }
    }
}
