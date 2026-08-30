// 役割: チュートリアルSequenceを最初から最後まで通す実行時のE2E（Stage C）。
//
//       森5枚 → 畑2枚 → 川3枚 → 橋1回 → 森×川シナジー1回 → Sequence完走
//       という5本を、それぞれの条件種別（ClusterSize / TilePlacedCount / EventOccurrence）が
//       混ざった状態で順に達成できることを確認する。
//
//       ★クエストの中身は QuestSequence_Tutorial.asset と同じ条件をコードで組み立てている。
//         PlayModeテストはエディタ専用APIを使わない方針のためアセットを直接読めないが、
//         アセットの中身（並び順・条件・eventKey）はEditModeのQuestSequenceTutorialTestsで
//         固定してあるので、両者を合わせて「本編の並びが通しで成立する」ことを担保する。
//
//       ★流すイベントはすべてCoreの汎用イベント。実際のゲームではWorldEventRelayと
//         TerrainClusterProgressRelayがTiles側の出来事をこれらへ翻訳しており、
//         その翻訳経路はQuestEventPipelinePlayModeTestsとWorldEventRelayTestsで確認済み。

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
    public class QuestSequenceTutorialPlayModeTests
    {
        // 通しで5本進めるので短め。遅延そのものの挙動はQuestSequenceRunnerPlayModeTestsで確認済み
        private const float Delay = 0.1f;

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

        private QuestDefinition MakeQuest(string title, QuestCondition condition)
        {
            var q = ScriptableObject.CreateInstance<QuestDefinition>();
            _created.Add(q);
            q.title     = title;
            q.condition = condition;
            return q;
        }

        [UnityTest]
        public IEnumerator TutorialSequence_RunsThroughAllFiveQuests()
        {
            // QuestSequence_Tutorial と同じ並び・同じ条件
            var forest  = MakeQuest("森を育てよう",
                new QuestCondition(QuestConditionKind.ClusterSize, TerrainClusterCategory.Forest, 5));
            var field   = MakeQuest("畑をひらこう",
                new QuestCondition(QuestConditionKind.TilePlacedCount, TerrainClusterCategory.Field, 2));
            var river   = MakeQuest("川をつなげよう",
                new QuestCondition(QuestConditionKind.ClusterSize, TerrainClusterCategory.River, 3));
            var bridge  = MakeQuest("川を育てて橋を架けよう",
                new QuestCondition(WorldEventKeys.Bridge, 1));
            var synergy = MakeQuest("森と川を大きく育てよう",
                new QuestCondition(WorldEventKeys.Synergy("ForestRiver"), 1));

            var sequence = ScriptableObject.CreateInstance<QuestSequenceDefinition>();
            _created.Add(sequence);
            sequence.name   = "TutorialLikeSequence";
            sequence.quests = new[] { forest, field, river, bridge, synergy };

            var go = new GameObject("TutorialRig");
            _created.Add(go);
            go.SetActive(false);
            var manager = go.AddComponent<QuestManager>();
            var runner  = go.AddComponent<QuestSequenceRunner>();
            SetPrivateField(runner, "_sequence", sequence);
            SetPrivateField(runner, "_questManager", manager);
            SetPrivateField(runner, "_nextQuestDelay", Delay);
            go.SetActive(true);

            var started            = new List<string>();
            var completed          = new List<string>();
            int sequenceCompleted  = 0;

            System.Action<QuestStartedEvent>           onStarted   = e => started.Add(e.Quest.title);
            System.Action<QuestCompletedEvent>         onCompleted = e => completed.Add(e.Quest.title);
            System.Action<QuestSequenceCompletedEvent> onSequence  = _ => sequenceCompleted++;
            EventBus.Subscribe(onStarted);
            EventBus.Subscribe(onCompleted);
            EventBus.Subscribe(onSequence);

            try
            {
                yield return null;   // Startで1本目が始まる
                CollectionAssert.AreEqual(new[] { "森を育てよう" }, started);

                // 1本目: 森のクラスターが5枚に育つ
                EventBus.Publish(new TerrainClusterProgressEvent(TerrainClusterCategory.Forest, 5));
                yield return new WaitForSeconds(Delay + 0.1f);
                Assert.AreEqual("畑をひらこう", started[started.Count - 1], "2本目が始まっているはず");

                // 2本目: 畑タイルを2枚置く（加算型）
                EventBus.Publish(new TileCategoryPlacedEvent(TerrainClusterCategory.Field));
                EventBus.Publish(new TileCategoryPlacedEvent(TerrainClusterCategory.Field));
                yield return new WaitForSeconds(Delay + 0.1f);
                Assert.AreEqual("川をつなげよう", started[started.Count - 1], "3本目が始まっているはず");

                // 3本目: 川のクラスターが3枚になる
                EventBus.Publish(new TerrainClusterProgressEvent(TerrainClusterCategory.River, 3));
                yield return new WaitForSeconds(Delay + 0.1f);
                Assert.AreEqual("川を育てて橋を架けよう", started[started.Count - 1], "4本目が始まっているはず");

                // 4本目: 橋が架かる
                EventBus.Publish(new WorldEventOccurredEvent(WorldEventKeys.Bridge));
                yield return new WaitForSeconds(Delay + 0.1f);
                Assert.AreEqual("森と川を大きく育てよう", started[started.Count - 1], "5本目が始まっているはず");

                // 5本目: 森×川のシナジーが起きる
                EventBus.Publish(new WorldEventOccurredEvent(WorldEventKeys.Synergy("ForestRiver")));
                yield return new WaitForSeconds(Delay + 0.1f);

                CollectionAssert.AreEqual(
                    new[] { "森を育てよう", "畑をひらこう", "川をつなげよう", "川を育てて橋を架けよう", "森と川を大きく育てよう" },
                    started, "5本が設計した順に開始されるはず");
                CollectionAssert.AreEqual(started, completed, "開始した5本すべてが達成されるはず");
                Assert.AreEqual(1, sequenceCompleted, "Sequence完走は1回だけのはず");

                // 完走後に達成イベントが来ても何も起きない
                EventBus.Publish(new WorldEventOccurredEvent(WorldEventKeys.Bridge));
                yield return new WaitForSeconds(Delay + 0.1f);
                Assert.AreEqual(5, started.Count,       "完走後に新しいクエストを始めてはいけない");
                Assert.AreEqual(1, sequenceCompleted,   "Sequence完走を再発行してはいけない");
            }
            finally
            {
                EventBus.Unsubscribe(onStarted);
                EventBus.Unsubscribe(onCompleted);
                EventBus.Unsubscribe(onSequence);
            }
        }
    }
}
