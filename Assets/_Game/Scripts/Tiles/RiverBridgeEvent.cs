// 役割: 川クラスターが5枚単位の節目に達し、橋を架けるべきことを通知するイベント。
//       RiverBridgeEvaluator が発行し、BridgeSystem が購読する。

namespace ElfVillage.Tiles
{
    public sealed class RiverBridgeEvent
    {
        /// <summary>橋を架ける対象タイル（節目に到達した時点で配置されたタイル）。</summary>
        public HexTile Tile { get; }

        /// <summary>この時点でのクラスター全体の枚数。</summary>
        public int ClusterSize { get; }

        public RiverBridgeEvent(HexTile tile, int clusterSize)
        {
            Tile = tile;
            ClusterSize = clusterSize;
        }
    }
}
