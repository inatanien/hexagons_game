// 役割: 「祝う対象タイル」が実行時の本番経路で正しく決まることを確認するE2E（Stage 1）。
//
//       TilePlacedEvent（Tiles）
//         → WorldEventRelay → TileCategoryPlacedEvent（Core）
//         → QuestManager 達成 → QuestCelebrationEvent（Core）
//         → QuestTileFocusTracker
//         → QuestTileSelectionResolvedEvent（Tiles）
//
//       ★EditModeのテストはLateUpdateをリフレクションで呼んでいるため、
//         「実行時にちゃんと次のLateUpdateで解決されるか」まではここで確かめる。
//       ★購読順に依存しないことが要点なので、Relayを先に、Trackerを後に付ける。
//         この順番でも達成を決めた最後の1枚が対象に含まれる。

using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ElfVillage.Core;
using ElfVillage.HexGrid;
using ElfVillage.Quest;
using ElfVillage.Tiles;

namespace ElfVillage.Tests
{
    public class QuestCelebrationPipelinePlayModeTests
    {
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

        private TileType MakeFieldType()
        {
            var variant = ScriptableObject.CreateInstance<TerrainVariantDefinition>();
            variant.category = TileCategory.Field;
            _created.Add(variant);

            var type = ScriptableObject.CreateInstance<TileType>();
            _created.Add(type);
            type.elements = new[] { new TileElement { variant = variant, areaWeight = 1f, visualOnly = false } };
            for (int d = 0; d < 6; d++) type.edges[d] = EdgeType.Field;
            return type;
        }

        private QuestDefinition MakeFieldQuest()
        {
            var quest = ScriptableObject.CreateInstance<QuestDefinition>();
            _created.Add(quest);
            quest.title     = "花畑をひらこう";
            quest.condition = new QuestCondition(QuestConditionKind.TilePlacedCount, TerrainClusterCategory.Field, 2);
            return quest;
        }

        private HexTile PlaceTile(TileType type, HexCoord coord)
        {
            var go = new GameObject("PlayModeTestTile_" + coord);
            _created.Add(go);

            var tile = go.AddComponent<HexTile>();
            tile.Initialize(coord, 1f);
            tile.Place(type, 0);

            EventBus.Publish(new TilePlacedEvent(tile, type, coord));
            return tile;
        }

        private QuestDefinition MakeFieldQuest(string title, int targetCount)
        {
            var quest = ScriptableObject.CreateInstance<QuestDefinition>();
            _created.Add(quest);
            quest.title     = title;
            quest.condition = new QuestCondition(QuestConditionKind.TilePlacedCount, TerrainClusterCategory.Field, targetCount);
            return quest;
        }

        /// <summary>
        /// Sequenceで次のクエストへ切り替わるとき、前のクエストの祝う対象が正しいことを確認する。
        /// ★切り替え待ちが0秒だと、達成から次クエスト開始までが元のTilePlacedEventと
        ///   同じ同期チェーンで進むため、いちばん壊れやすい条件になる。
        ///   2秒待つ通常の設定でも同じ結果になることを、同じテストで両方確かめる。
        /// </summary>
        private IEnumerator RunSwitchScenario(float nextQuestDelay)
        {
            var questA = MakeFieldQuest("花畑をひらこう", 2);
            var questB = MakeFieldQuest("もう一度ひらこう", 1);

            var sequence = ScriptableObject.CreateInstance<QuestSequenceDefinition>();
            _created.Add(sequence);
            sequence.name   = "SwitchScenarioSequence";
            sequence.quests = new[] { questA, questB };

            var go = new GameObject("PlayModeSwitchRig");
            _created.Add(go);
            go.SetActive(false);

            // ★Relayを先、Trackerを後に付ける（購読順の不利な側）
            go.AddComponent<WorldEventRelay>();
            var manager = go.AddComponent<QuestManager>();
            go.AddComponent<QuestTileFocusTracker>();
            var runner = go.AddComponent<QuestSequenceRunner>();
            SetPrivateField(runner, "_sequence", sequence);
            SetPrivateField(runner, "_questManager", manager);
            SetPrivateField(runner, "_nextQuestDelay", nextQuestDelay);

            go.SetActive(true);

            var resolved = new List<IReadOnlyList<HexTile>>();
            System.Action<QuestTileSelectionResolvedEvent> onResolved = e => resolved.Add(e.Tiles);
            EventBus.Subscribe(onResolved);

            try
            {
                yield return null;   // 1本目が始まる

                var fieldType = MakeFieldType();
                var first     = PlaceTile(fieldType, HexCoord.Zero);
                var second    = PlaceTile(fieldType, HexCoord.Zero.Neighbor(0));   // ここで達成 → 次へ切り替え

                yield return new WaitForSeconds(nextQuestDelay + 0.2f);

                Assert.AreEqual(1, resolved.Count, "1本目の祝いは1回だけのはず");
                CollectionAssert.AreEquivalent(new[] { first, second }, resolved[0],
                    $"切り替え待ち{nextQuestDelay}秒でも、達成を決めた2枚目まで対象に入るはず");

                // 2本目の祝いに1本目のタイルが混ざらない
                var third = PlaceTile(fieldType, HexCoord.Zero.Neighbor(1));
                yield return new WaitForSeconds(nextQuestDelay + 0.2f);

                Assert.AreEqual(2, resolved.Count);
                CollectionAssert.AreEquivalent(new[] { third }, resolved[1],
                    "2本目は自分の1枚だけを祝うはず（前のクエストのタイルは持ち越さない）");
            }
            finally
            {
                EventBus.Unsubscribe(onResolved);
            }
        }

        [UnityTest]
        public IEnumerator InstantQuestSwitch_ResolvesTheFinishedQuestTiles()
            => RunSwitchScenario(nextQuestDelay: 0f);

        [UnityTest]
        public IEnumerator DelayedQuestSwitch_ResolvesTheFinishedQuestTiles()
            => RunSwitchScenario(nextQuestDelay: 0.3f);

        [UnityTest]
        public IEnumerator PlacingTwoFieldTiles_ResolvesBothTilesForCelebration()
        {
            var go = new GameObject("PlayModeCelebrationRig");
            _created.Add(go);
            go.SetActive(false);

            // ★Relayを先、Trackerを後に付ける（購読順の不利な側）
            go.AddComponent<WorldEventRelay>();
            var manager = go.AddComponent<QuestManager>();
            go.AddComponent<QuestTileFocusTracker>();
            SetPrivateField(manager, "_activeQuest", MakeFieldQuest());

            go.SetActive(true);

            var resolved = new List<IReadOnlyList<HexTile>>();
            int completed = 0;
            System.Action<QuestTileSelectionResolvedEvent> onResolved  = e => resolved.Add(e.Tiles);
            System.Action<QuestCompletedEvent>             onCompleted = _ => completed++;
            EventBus.Subscribe(onResolved);
            EventBus.Subscribe(onCompleted);

            try
            {
                yield return null;   // Startでクエストが始まる

                var fieldType = MakeFieldType();
                var first     = PlaceTile(fieldType, HexCoord.Zero);
                yield return null;

                Assert.AreEqual(0, completed, "1枚目では達成しないはず");
                Assert.AreEqual(0, resolved.Count, "達成していないので祝いも起きないはず");

                var second = PlaceTile(fieldType, HexCoord.Zero.Neighbor(0));

                // 達成そのものは同じフレームで即座に届く（祝いの解決だけが後回し）
                Assert.AreEqual(1, completed, "2枚目で達成するはず");

                yield return null;   // LateUpdateで解決される

                Assert.AreEqual(1, resolved.Count, "祝う対象は1回だけ解決されるはず");
                CollectionAssert.AreEquivalent(new[] { first, second }, resolved[0],
                    "Relayが先に処理されても、達成を決めた2枚目まで対象に入るはず");

                // 以降のフレームで二重解決しない
                yield return null;
                Assert.AreEqual(1, resolved.Count);
            }
            finally
            {
                EventBus.Unsubscribe(onResolved);
                EventBus.Unsubscribe(onCompleted);
            }
        }
    }
}
