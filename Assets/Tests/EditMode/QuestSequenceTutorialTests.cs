// 役割: チュートリアルSequenceの実アセットと、本編シーンの設定を固定する（Stage C）。
//
//       ★並び順そのものが体験の設計なので、アセットの中身をテストで固定する。
//         森を育てる → 畑を置く（一度軽くする）→ 川をつなぐ → その川を育てて橋 →
//         最後に森と川を組み合わせる、という学習の流れになっている。
//
//       ★シーン設定テストは、Phase1_v002が「Sequence運用」になっていることを固定するための
//         軽量な設定回帰テスト。シーンを開くとエディタの状態（開いているシーン・未保存の変更）を
//         壊してしまうため、.unityのテキストから確認している。
//         Unityのシリアライズ形式に依存しているので、将来保存形式が変わったら見直すこと。

using System.IO;
using NUnit.Framework;
using ElfVillage.Core;
using ElfVillage.Quest;
using UnityEngine;

namespace ElfVillage.Tests
{
    public class QuestSequenceTutorialTests
    {
        private const string QuestFolder  = "Assets/_Game/ScriptableObjects/QuestData/";
        private const string SequencePath = QuestFolder + "QuestSequence_Tutorial.asset";
        private const string ScenePath    = "Assets/Scenes/Phase1_v002.unity";

        private static QuestSequenceDefinition LoadSequence()
        {
            var sequence = UnityEditor.AssetDatabase.LoadAssetAtPath<QuestSequenceDefinition>(SequencePath);
            Assert.IsNotNull(sequence, SequencePath + " が見つかりません");
            return sequence;
        }

        private static string ReadScene() =>
            File.ReadAllText(Path.Combine(Application.dataPath, "..", ScenePath));

        private static int CountOccurrences(string text, string needle)
        {
            int count = 0;
            int index = text.IndexOf(needle, System.StringComparison.Ordinal);
            while (index >= 0)
            {
                count++;
                index = text.IndexOf(needle, index + needle.Length, System.StringComparison.Ordinal);
            }
            return count;
        }

        // ── 1. 並び順 ───────────────────────────────────────────────────

        [Test]
        public void TutorialSequence_HasFiveQuestsInDesignedOrder()
        {
            var sequence = LoadSequence();

            Assert.IsNotNull(sequence.quests, "questsが未設定です");
            var expected = new[]
            {
                "Quest_ForestCluster5",
                "Quest_FieldPlaced2",
                "Quest_RiverCluster3",
                "Quest_Bridge1",
                "Quest_ForestRiverSynergy1",
            };

            Assert.AreEqual(expected.Length, sequence.quests.Length, "クエストの本数が違います");
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.IsNotNull(sequence.quests[i], $"{i}番目が空欄です");
                Assert.AreEqual(expected[i], sequence.quests[i].name, $"{i}番目の並びが違います");
            }
        }

        // ── 2. 各クエストが開始できる形になっていること ─────────────────

        [Test]
        public void TutorialSequence_EveryQuestIsUsable()
        {
            foreach (var quest in LoadSequence().quests)
            {
                Assert.IsNotNull(quest.condition, $"{quest.name} のconditionが未設定です");
                Assert.Greater(quest.condition.targetCount, 0, $"{quest.name} のtargetCountが不正です");

                if (quest.condition.kind == QuestConditionKind.EventOccurrence)
                {
                    Assert.IsFalse(string.IsNullOrWhiteSpace(quest.condition.eventKey),
                        $"{quest.name} はEventOccurrenceなのにeventKeyが空です");
                }
            }
        }

        // ── 3. 出来事キーが発行側と一致していること ─────────────────────

        [Test]
        public void TutorialSequence_EventKeysMatchWhatTheRelayPublishes()
        {
            var quests = LoadSequence().quests;

            Assert.AreEqual(WorldEventKeys.Bridge, quests[3].condition.eventKey,
                "橋クエストのキーがWorldEventRelayの発行するキーと一致していません");
            Assert.AreEqual(WorldEventKeys.Synergy("ForestRiver"), quests[4].condition.eventKey,
                "シナジークエストのキーがシーンのSynergyEvaluator（SynergyId=ForestRiver）と一致していません");
        }

        // ── 4〜5. 本編シーンがSequence運用になっていること ──────────────

        [Test]
        public void Scene_Phase1v002_HasExactlyOneQuestSequenceRunner()
        {
            string guid = UnityEditor.AssetDatabase.AssetPathToGUID(
                "Assets/_Game/Scripts/Quest/QuestSequenceRunner.cs");
            Assert.IsFalse(string.IsNullOrEmpty(guid), "QuestSequenceRunner.csが見つかりません");

            Assert.AreEqual(1, CountOccurrences(ReadScene(), guid),
                $"{ScenePath} にQuestSequenceRunnerはちょうど1つ配置されているはず" +
                "（0個ならクエストが始まらず、2個なら同じクエストが二重に進む）");
        }

        [Test]
        public void Scene_Phase1v002_QuestManagerHasNoActiveQuest()
        {
            // Sequence運用ではRunnerが1本目を供給する。ここにアセットが残っていると、
            // Runnerが渡すより先にQuestManagerが別のクエストを開始してしまう
            Assert.IsTrue(ReadScene().Contains("_activeQuest: {fileID: 0}"),
                $"{ScenePath} のQuestManager._activeQuestは未設定であるはず");
        }

        [Test]
        public void Scene_Phase1v002_RunnerReferencesTutorialSequence()
        {
            string guid = UnityEditor.AssetDatabase.AssetPathToGUID(SequencePath);
            Assert.IsFalse(string.IsNullOrEmpty(guid), "QuestSequence_Tutorial.assetが見つかりません");

            Assert.IsTrue(ReadScene().Contains("_sequence: {fileID: 11400000, guid: " + guid),
                $"{ScenePath} のQuestSequenceRunnerがQuestSequence_Tutorialを参照しているはず");
        }
    }
}
