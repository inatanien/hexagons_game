// 役割: 1つのクエストの達成条件を表すデータ。QuestDefinition（SO）へ埋め込まれる。
//       「どのイベントを」「何で数えるか」だけを持ち、判定ロジックは持たない。
//       森・川などの地形ルールはTiles側の評価システムが既に持っているため、
//       クエストのためにそれらを再実装しない（既存イベントの購読だけで進捗を作る）。
//
//       1クエスト1条件。複数条件のAND/ORは対象外（条件別進捗・UI・セーブ形式まで
//       設計対象が広がるため、必要になった時点で別Stageとして設計する）。

using System;
using ElfVillage.Core;

namespace ElfVillage.Quest
{
    [Serializable]
    public class QuestCondition
    {
        [UnityEngine.Tooltip("どのイベントを観測して進捗を数えるか")]
        public QuestConditionKind kind = QuestConditionKind.ClusterSize;

        [UnityEngine.Tooltip("進捗判定の対象カテゴリ（ClusterSize / TilePlacedCountで使用）")]
        public TerrainClusterCategory category;

        [UnityEngine.Tooltip("数える出来事のキー（EventOccurrenceで使用）。" +
                             "例: bridge / synergy:ForestRiver / " +
                             "キーの一覧はCoreのWorldEventKeysを参照。空の場合クエストは開始しない")]
        public string eventKey;

        [UnityEngine.Tooltip("達成に必要な数。0以下は不正値として扱い、QuestManagerは" +
                             "このクエストを開始しない（警告ログを出す）")]
        public int targetCount = 5;

        public QuestCondition() { }

        public QuestCondition(QuestConditionKind kind, TerrainClusterCategory category, int targetCount)
        {
            this.kind        = kind;
            this.category    = category;
            this.targetCount = targetCount;
        }

        /// <summary>EventOccurrence用。カテゴリは使わないのでeventKeyだけを指定する。</summary>
        public QuestCondition(string eventKey, int targetCount)
        {
            this.kind        = QuestConditionKind.EventOccurrence;
            this.eventKey    = eventKey;
            this.targetCount = targetCount;
        }
    }
}
