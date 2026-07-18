// 役割: 接続判定・デッキ抽選・成長評価が参照する「粗い分類」を表すenum。
//       EdgeTypeとは別enum（Villageのように辺を持たないカテゴリがあるため）。

namespace ElfVillage.Tiles
{
    public enum TileCategory
    {
        Forest = 0,
        Field,
        River,
        Road,
        Village,
    }
}
