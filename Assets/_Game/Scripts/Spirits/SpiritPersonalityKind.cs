// 役割: 精霊の性格の種類（Stage 13）。
//       性格は見た目（体色）では表現せず、行動の傾向だけで読み取れるようにする
//       （体色は将来の「種族ごとの差別化」用に温存しているため）。
//
//       ★将来セーブ対象になるため、enum値を明示する。
//         既存の値は変更しないこと。新しい性格を追加するときは末尾へ新しい値を足す。

namespace ElfVillage.Spirits
{
    public enum SpiritPersonalityKind
    {
        /// <summary>のんびり屋。あまり動かず、よく眠り、狭い範囲で過ごす。早く慣れる。</summary>
        Calm = 0,

        /// <summary>好奇心旺盛。よく動き回り、木をよく眺め、ほとんど眠らない。慣れにくい。</summary>
        Curious = 1,

        // Playful / Shy などは、必要になったStageで末尾へ追加する（既存値は動かさない）。
    }
}
