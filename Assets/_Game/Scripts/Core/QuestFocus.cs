// 役割: 「今のクエストが盤面の何を見ているか」だけを表す中立な記述。
//       Quest層はタイルを知らず、Tiles層はクエストを知らないため、
//       達成を祝う演出のために「どのタイル集合を選べばよいか」を伝える共通語彙が要る。
//       ここではタイルへの参照を一切持たず、条件の種類だけを運ぶ。
//
//       ★QuestConditionKind（Quest層）と1対1で対応するが、意図的に別の型にしてある。
//         CoreがQuest層の都合を知ると、条件種別を増やすたびにCoreが変わってしまう。
//         「祝う対象の選び方」はTiles側の関心なので、その語彙をCoreへ置く。

namespace ElfVillage.Core
{
    public enum QuestFocusSource
    {
        /// <summary>連結クラスターの規模で達成する（森・川）。対象はそのクラスター全体。</summary>
        Cluster = 0,

        /// <summary>対象カテゴリのタイルを置いた枚数で達成する。対象は開始後に数えたタイル。</summary>
        TilePlacement = 1,

        /// <summary>出来事の回数で達成する（橋・シナジー）。対象はその出来事に関わったタイル。</summary>
        WorldEvent = 2,
    }

    public sealed class QuestFocus
    {
        public QuestFocusSource Source { get; }

        /// <summary>Cluster / TilePlacement で使う対象カテゴリ。</summary>
        public TerrainClusterCategory Category { get; }

        /// <summary>WorldEvent で使う出来事キー（WorldEventKeysの値）。</summary>
        public string EventKey { get; }

        public QuestFocus(QuestFocusSource source, TerrainClusterCategory category, string eventKey = null)
        {
            Source   = source;
            Category = category;
            EventKey = eventKey;
        }
    }
}
