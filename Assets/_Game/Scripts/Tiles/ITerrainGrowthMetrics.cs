// 役割: 地形成長メトリクスの共通マーカーインターフェース。
//       ForestGrowthMetrics・RiverGrowthMetrics など地形ごとのデータクラスが実装する。
//       TerrainGrowthEvent<TMetrics> の型制約として使用し、
//       コンパイル時に「地形メトリクスでない型」を弾く。

namespace ElfVillage.Tiles
{
    public interface ITerrainGrowthMetrics { }
}
