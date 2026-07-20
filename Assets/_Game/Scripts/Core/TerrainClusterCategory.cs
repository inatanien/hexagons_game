// 役割: クラスター進捗をシステム間（Tiles→Quest等）へ通知するための軽量なカテゴリenum。
//       Tiles側のTileCategory（タイル構造・接続・ゲームプレイルール上の分類）とは責務が異なり、
//       Coreイベントを介した通知専用。TileCategoryの値が変わってもこちらは独立して保つこと。

namespace ElfVillage.Core
{
    public enum TerrainClusterCategory
    {
        Forest = 0,
        Field  = 1,
        River  = 2,
    }
}
