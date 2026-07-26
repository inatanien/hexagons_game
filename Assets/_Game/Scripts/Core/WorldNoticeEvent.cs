// 役割: 世界の出来事をプレイヤーへ短く伝えるための汎用通知イベント（Stage 16）。
//
//       ★文字列と数値だけを運ぶ
//         MonoBehaviour参照（ForestSpirit等）をUIへ渡すと、破棄済みオブジェクトへの
//         アクセスや購読側での寿命管理が必要になる。通知に必要なのは表示内容だけなので、
//         値だけを運ぶことでUIは発行元のシステムを一切知らずに済む。
//
//       ★これによりasmdefが変わらない
//         発行側（Spirits等）も購読側（UI）も既にCoreを参照しているため、
//         UIからSpiritsへの新しい依存を作らずにマイルストーン通知を実現できる。
//         将来Reward・River・Bridgeなど他システムも同じ通知へ相乗りできる。

namespace ElfVillage.Core
{
    /// <summary>
    /// 通知の種類。Stage 16では「どこから来た通知か」を表すだけで、
    /// 複雑な優先度制御は行わない（必要になってから足す）。
    /// </summary>
    public enum WorldNoticeKind
    {
        Spirit = 0,
    }

    public sealed class WorldNoticeEvent
    {
        /// <summary>小見出し（例: 🌱 森の精霊）。</summary>
        public string Header { get; }

        /// <summary>本文（例: 森に小さな住人が現れました）。</summary>
        public string Body { get; }

        /// <summary>表示秒数。0以下や非有限値は受け取り側が既定値へ丸める。</summary>
        public float DisplayDuration { get; }

        public WorldNoticeKind Kind { get; }

        public WorldNoticeEvent(string header, string body, float displayDuration,
                                 WorldNoticeKind kind = WorldNoticeKind.Spirit)
        {
            Header          = header ?? string.Empty;
            Body            = body   ?? string.Empty;
            DisplayDuration = displayDuration;
            Kind            = kind;
        }
    }
}
