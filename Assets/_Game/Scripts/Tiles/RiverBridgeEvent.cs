// 役割: 川クラスターが5枚単位の節目に達し、橋を架けるべきことを通知するイベント。
//       RiverBridgeEvaluator が発行し、BridgeSystem が購読する。
//
//       ★クラスター全体（Tiles）も一緒に運ぶ。
//         橋を祝う演出は「橋が架かった川のまとまり」を対象にしたいが、
//         同じ川クラスターを計算しているRiverGrowthEvaluatorとは別コンポーネントで、
//         どちらが先に走るかは購読順に左右される。
//         受け取り側が他システムの発行順を当てにしなくて済むよう、
//         このイベント自身が必要な情報を持つ形にしてある。

using System.Collections.Generic;

namespace ElfVillage.Tiles
{
    public sealed class RiverBridgeEvent
    {
        /// <summary>橋を架ける対象タイル（節目に到達した時点で配置されたタイル）。</summary>
        public HexTile BridgeTile { get; }

        /// <summary>節目に到達した川クラスター全体。BridgeTileもこの中に含まれる。</summary>
        public IReadOnlyList<HexTile> Tiles { get; }

        /// <summary>この時点でのクラスター全体の枚数。</summary>
        public int ClusterSize { get; }

        public RiverBridgeEvent(HexTile bridgeTile, IReadOnlyList<HexTile> tiles, int clusterSize)
        {
            BridgeTile  = bridgeTile;
            Tiles       = tiles ?? new List<HexTile>();
            ClusterSize = clusterSize;
        }
    }
}
