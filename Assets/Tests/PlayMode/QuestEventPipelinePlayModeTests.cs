// 役割: クエスト進捗の本番経路が実際につながっていることを、実行時のライフサイクルで確認する。
//
//       TilePlacedEvent（Tiles）
//         → WorldEventRelay
//         → TileCategoryPlacedEvent（Core）
//         → QuestManager
//         → QuestCompletedEvent
//
//       EditModeの単体テストはリフレクションでOnEnable/Startを呼んでいるため、
//       「実際にUnityのライフサイクルで購読が張られるか」までは確認できない。
//       ここだけはPlayModeで通しの経路を固定する（Field2クエストが代表例）。
//       橋・シナジーの成立判定そのものは既存テストとEditModeのRelayテストで担保済みなので、
//       ここでは繰り返さない。

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
    public class QuestEventPipelinePlayModeTests
    {
        private readonly List<Object> _created = new();

        [TearDown]
        public void TearDown()
        {
            // GameObjectの破棄でOnDisableが走り、EventBusの購読も解除される
            foreach (var o in _created)
                if (o != null) Object.Destroy(o);
            _created.Clear();
        }

        private TileType MakeFieldTile()
        {
            var variant = ScriptableObject.CreateInstance<TerrainVariantDefinition>();
            variant.category = TileCategory.Field;
            _created.Add(variant);

            var type = ScriptableObject.CreateInstance<TileType>();
            _created.Add(type);
            type.elements = new[]
            {
                new TileElement { variant = variant, areaWeight = 1f, visualOnly = false },
            };
            for (int d = 0; d < 6; d++) type.edges[d] = EdgeType.Field;
            return type;
        }

        private QuestDefinition MakeFieldPlaced2Quest()
        {
            // Quest_FieldPlaced2.asset と同じ条件（アセットの中身はEditModeテストで固定済み）
            var quest = ScriptableObject.CreateInstance<QuestDefinition>();
            _created.Add(quest);
            quest.title     = "畑をひらこう";
            quest.condition = new QuestCondition(QuestConditionKind.TilePlacedCount, TerrainClusterCategory.Field, 2);
            return quest;
        }

        private void PlaceTile(TileType type, HexCoord coord)
        {
            var go = new GameObject("PlayModeTestTile_" + coord);
            _created.Add(go);

            var tile = go.AddComponent<HexTile>();
            tile.Initialize(coord, 1f);
            tile.Place(type, 0);

            // HexGridManagerが実際の配置で発行しているのと同じイベント
            EventBus.Publish(new TilePlacedEvent(tile, type, coord));
        }

        [UnityTest]
        public IEnumerator PlacingTwoFieldTiles_CompletesFieldQuest_ThroughRealPipeline()
        {
            var relayGo = new GameObject("PlayModeTestWorldEventRelay");
            _created.Add(relayGo);
            relayGo.AddComponent<WorldEventRelay>();   // OnEnableは実行時に自動で走る

            // 実行時はAddComponentした瞬間にOnEnableが走る。QuestManagerはOnEnableで
            // クエストを検証して購読するので、先に無効な状態で有効化されないよう
            // GameObjectを止めた状態で組み立ててから有効化する
            // （本編ではシーン読み込み時点でInspectorの割り当てが済んでいるのと同じ状態）
            var managerGo = new GameObject("PlayModeTestQuestManager");
            _created.Add(managerGo);
            managerGo.SetActive(false);

            var manager = managerGo.AddComponent<QuestManager>();
            typeof(QuestManager)
                .GetField("_activeQuest", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(manager, MakeFieldPlaced2Quest());

            managerGo.SetActive(true);

            bool started   = false;
            int  completed = 0;
            var  progress  = new List<int>();
            System.Action<QuestStartedEvent>         onStarted   = _ => started = true;
            System.Action<QuestProgressChangedEvent> onProgress  = e => progress.Add(e.CurrentCount);
            System.Action<QuestCompletedEvent>       onCompleted = _ => completed++;
            EventBus.Subscribe(onStarted);
            EventBus.Subscribe(onProgress);
            EventBus.Subscribe(onCompleted);

            try
            {
                // AddComponent直後はOnEnableまで。Startは次のフレームで走る
                yield return null;
                Assert.IsTrue(started, "QuestStartedEventはStartのタイミングで発行されるはず");

                var field = MakeFieldTile();
                PlaceTile(field, HexCoord.Zero);
                yield return null;

                Assert.AreEqual(0, completed, "1枚目では達成しないはず");

                PlaceTile(field, HexCoord.Zero.Neighbor(0));
                yield return null;

                CollectionAssert.AreEqual(new[] { 1, 2 }, progress,
                    "TilePlacedEvent → WorldEventRelay → TileCategoryPlacedEvent → QuestManager がつながっているはず");
                Assert.AreEqual(1, completed, "畑2枚で達成するはず");
            }
            finally
            {
                EventBus.Unsubscribe(onStarted);
                EventBus.Unsubscribe(onProgress);
                EventBus.Unsubscribe(onCompleted);
            }
        }
    }
}
