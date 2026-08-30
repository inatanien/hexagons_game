// 役割: 「このタイル群を祝う」と決まったことを通知するイベント。
//       QuestTileFocusTracker が QuestCelebrationEvent を受けて対象を解決し、これを発行する。
//       外周を走る光・薄明光線など、見た目の側はこのイベントだけを購読すればよい。
//
//       ★解決した結果はどこにも保持しない。
//         「最後に祝った集合」を誰かが持つと、その寿命管理（破棄済みタイルへの参照）と
//         いつ捨てるかの判断が増える。必要な瞬間に一度だけ流すのがいちばん単純。

using System.Collections.Generic;

namespace ElfVillage.Tiles
{
    public sealed class QuestTileSelectionResolvedEvent
    {
        /// <summary>祝う対象のタイル群。重複はなく、順序に意味はない。</summary>
        public IReadOnlyList<HexTile> Tiles { get; }

        public QuestTileSelectionResolvedEvent(IReadOnlyList<HexTile> tiles)
        {
            Tiles = tiles ?? new List<HexTile>();
        }
    }
}
