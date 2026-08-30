// 役割: WorldEventOccurredEventで使う出来事キーの唯一の定義場所。
//       発行側（Tiles側のWorldEventRelay）は必ずここから作り、
//       文字列リテラルを複数箇所へ重複させない。
//       QuestDefinition（データ）側は文字列で指定するため、
//       ここが「SOへ何と書けばよいか」の参照元にもなる。

namespace ElfVillage.Core
{
    public static class WorldEventKeys
    {
        /// <summary>川クラスターが節目に達して橋が架かった。</summary>
        public const string Bridge = "bridge";

        /// <summary>地形シナジーのキーの接頭辞。後ろにSynergyIdが付く。</summary>
        public const string SynergyPrefix = "synergy:";

        /// <summary>
        /// 地形シナジーのキー（例: "synergy:ForestRiver"）。
        /// SynergyIdはSynergyEvaluatorのInspectorへ手入力された文字列なので、
        /// 未入力のまま "synergy:" だけの無意味なキーを作らないよう空を返す。
        /// 呼び出し側は空を「翻訳しない」と解釈すること。
        /// </summary>
        public static string Synergy(string synergyId)
            => string.IsNullOrWhiteSpace(synergyId) ? string.Empty : SynergyPrefix + synergyId.Trim();
    }
}
