// 役割: 盤面で起きた「出来事」を種類を問わず通知する汎用イベント。
//       Tiles側の個別イベント（RiverBridgeEvent・TerrainSynergyEvent等）を
//       WorldEventRelayが翻訳して発行する。
//       Quest等、Coreのみに依存したいシステムがTiles固有の型を知らずに
//       出来事の発生を数えられるようにするための入れ物。
//
//       ★出来事の種類は文字列キー（EventKey）で表す。キーの綴りはWorldEventKeysに集約してあり、
//         発行側はそこからしか作らない。データ（QuestDefinition）側は文字列で指定する。

namespace ElfVillage.Core
{
    public sealed class WorldEventOccurredEvent
    {
        /// <summary>出来事の種類（例: "bridge" / "synergy:ForestRiver"）。</summary>
        public string EventKey { get; }

        public WorldEventOccurredEvent(string eventKey) => EventKey = eventKey;
    }
}
