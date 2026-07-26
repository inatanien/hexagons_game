// 役割: 精霊のマイルストーン通知の文言を1箇所へ集める（Stage 16）。
//
//       ★大規模なLocalizationシステムは入れない
//         ただし文言をForestSpiritSpawnerやPresenterへ直書きすると、
//         将来言語対応するときに探し回ることになる。定数として集約しておくだけで、
//         差し替え地点がこの1ファイルに閉じる。コストはほぼゼロ。
//
//       文言の方針
//         ・主語を「森」側に置き、精霊を名指ししない
//           （将来複数体になっても「〜が生まれました」が破綻しないため）
//         ・短く、操作の手を止めさせない

namespace ElfVillage.Spirits
{
    public static class SpiritNoticeText
    {
        public const string BirthHeader = "🌱 森の精霊";
        public const string BirthBody   = "森に小さな住人が現れました";

        public const string BloomHeader = "🌿 森の精霊";
        public const string BloomBody   = "森の精霊がすっかり育ちました";

        /// <summary>通知の表示秒数。長く出しすぎるとクエスト通知と重なりやすい。</summary>
        public const float NoticeDuration = 3f;
    }
}
