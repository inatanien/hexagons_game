// 役割: 精霊にとっての「世界からの刺激」1件分。
//       既存の世界イベント（TerrainGrowthEvent / FlowerClusterEvent 等）を
//       SpiritStimulusRelayがこの形へ翻訳することで、精霊本体はQuest・Reward・UIなどの
//       具体型を一切知らずに済む（Stage 1のTerrainClusterProgressRelayと同じ方針）。
//
//       ★RelatedTilesを持つ理由
//         ForestGrewの受理判定は「自分のhome森と同じクラスターか」をタイルの同一性で見る必要がある。
//         距離で判定すると、近接した別クラスターの成長まで受理してしまい、
//         ForestSpirit.TryFollowForestGrowthの既存のhome判定と意味が食い違ってしまう。
//         そのため位置に加えて、その刺激に関係するタイル群も最小限の情報として持たせている。
//         （HexTileはTilesの型だが、SpiritsはもともとTilesを参照しているため新たな依存は増えない）

using System.Collections.Generic;
using UnityEngine;
using ElfVillage.Tiles;

namespace ElfVillage.Spirits
{
    public enum SpiritStimulusKind
    {
        /// <summary>森クラスターが成長した。</summary>
        ForestGrew = 0,

        /// <summary>花畑クラスターが咲いた（閾値以上に連結した）。</summary>
        FlowerBloomed = 1,

        // 鳥・昼夜などは、実際に対応するStageで追加する（先行して増やさない）。
    }

    public readonly struct SpiritStimulus
    {
        public readonly SpiritStimulusKind    Kind;
        public readonly Vector3               WorldPosition;
        /// <summary>この刺激に関係するタイル群（home一致判定に使う。存在しない場合はnull）。</summary>
        public readonly IReadOnlyList<HexTile> RelatedTiles;

        public SpiritStimulus(SpiritStimulusKind kind, Vector3 worldPosition, IReadOnlyList<HexTile> relatedTiles = null)
        {
            Kind          = kind;
            WorldPosition = worldPosition;
            RelatedTiles  = relatedTiles;
        }
    }
}
