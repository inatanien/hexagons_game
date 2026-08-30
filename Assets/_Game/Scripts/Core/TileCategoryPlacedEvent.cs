// 役割: タイルが1枚置かれたことを、そのタイルが持つゲームカテゴリごとに通知するイベント。
//       Tiles側のTilePlacedEventをWorldEventRelayが翻訳して発行する。
//       Quest等、Coreのみに依存したいシステムが「森タイルを置いた」「畑タイルを置いた」を
//       Tiles固有の型（TileType/TileCategory）を知らずに数えられるようにする。
//
//       ★カテゴリは文字列にせず型で持つ。数えるのはカテゴリという決まった軸であり、
//         WorldEventOccurredEventのような自由な出来事とは性質が違うため。
//       ★1枚のタイルが複数カテゴリを持つ場合はカテゴリごとに1回ずつ発行される
//         （同じカテゴリで2回発行されることはない）。

namespace ElfVillage.Core
{
    public sealed class TileCategoryPlacedEvent
    {
        /// <summary>置かれたタイルが持つカテゴリの1つ。</summary>
        public TerrainClusterCategory Category { get; }

        public TileCategoryPlacedEvent(TerrainClusterCategory category) => Category = category;
    }
}
