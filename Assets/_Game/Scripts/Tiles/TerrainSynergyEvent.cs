// 役割: 異なる地形タイルが隣接し合成条件を満たしたことを通知するイベント。
//       SynergyEvaluator が発行し、FireflySystem など演出システムが購読する。
//       SynergyId で将来的な複数シナジー種別（森×川、森×村 など）を識別する。

using System.Collections.Generic;

namespace ElfVillage.Tiles
{
    public sealed class TerrainSynergyEvent
    {
        /// <summary>シナジー種別の識別子（例："ForestRiver"）。</summary>
        public string SynergyId { get; }

        /// <summary>条件を満たした地形Aのタイル群（例：森クラスター）。</summary>
        public IReadOnlyList<HexTile> TilesA { get; }

        /// <summary>条件を満たした地形Bのタイル群（例：川クラスター）。</summary>
        public IReadOnlyList<HexTile> TilesB { get; }

        public TerrainSynergyEvent(string synergyId,
            IReadOnlyList<HexTile> tilesA, IReadOnlyList<HexTile> tilesB)
        {
            SynergyId = synergyId;
            TilesA    = tilesA;
            TilesB    = tilesB;
        }
    }
}
