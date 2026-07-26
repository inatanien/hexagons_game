// 役割: 精霊の誕生・成長を、UIが読める汎用通知（WorldNoticeEvent）へ翻訳する（Stage 16）。
//
//       ★翻訳だけを行い、精霊への参照は持たない
//         SpiritStimulusRelayと同じ考え方。個体の一覧も現在状態も保持しないため、
//         精霊が破棄されてもこのコンポーネントは何も壊れない。
//
//       ★なぜUIへ直接伝えないか
//         UIアセンブリはSpiritsを参照していない（依存方向は Tiles + Quest ← UI）。
//         Coreの汎用通知イベントへ翻訳することで、UIは精霊の存在を知らないまま
//         通知を表示でき、asmdefを一切変更せずに済む。
//
//       ★画面外で生まれた場合
//         演出はその場で実行し、通知だけが画面へ出る（カメラは動かさない）。
//         この翻訳器はカメラを一切参照しないため、画面内外を区別せず必ず通知する。

using UnityEngine;
using ElfVillage.Core;

namespace ElfVillage.Spirits
{
    public class SpiritNoticePresenter : MonoBehaviour
    {
        // Subscribe/Unsubscribeで同じデリゲート実体を使うため変数に保持する。
        private System.Action<ForestSpiritSpawnedEvent>         _onSpawned;
        private System.Action<ForestSpiritGrowthCommittedEvent> _onGrowth;

        private void OnEnable()
        {
            _onSpawned = OnSpiritSpawned;
            _onGrowth  = OnSpiritGrowth;

            EventBus.Subscribe(_onSpawned);
            EventBus.Subscribe(_onGrowth);
        }

        private void OnDisable()
        {
            if (_onSpawned != null) { EventBus.Unsubscribe(_onSpawned); _onSpawned = null; }
            if (_onGrowth  != null) { EventBus.Unsubscribe(_onGrowth);  _onGrowth  = null; }
        }

        private static void OnSpiritSpawned(ForestSpiritSpawnedEvent evt)
        {
            if (evt == null) return;

            EventBus.Publish(new WorldNoticeEvent(
                SpiritNoticeText.BirthHeader,
                SpiritNoticeText.BirthBody,
                SpiritNoticeText.NoticeDuration,
                WorldNoticeKind.Spirit));
        }

        private static void OnSpiritGrowth(ForestSpiritGrowthCommittedEvent evt)
        {
            if (evt == null) return;

            // ★通知はBloom到達時だけ。
            //   毎段階で出すと、長期プレイやクエスト通知と重なって画面がうるさくなる。
            //   Sprout→FluffはVFXと見た目の変化で十分気づけるため、
            //   「育ちきった」という節目だけを言葉で伝える。
            if (!ShouldNotify(evt.NewStage)) return;

            EventBus.Publish(new WorldNoticeEvent(
                SpiritNoticeText.BloomHeader,
                SpiritNoticeText.BloomBody,
                SpiritNoticeText.NoticeDuration,
                WorldNoticeKind.Spirit));
        }

        /// <summary>
        /// この段階へ到達したとき通知を出すか。純粋関数なのでEditModeから直接検証できる。
        /// 未知の段階はClampStageでBloom以下へ丸められるため、勝手に通知が増えることはない。
        /// </summary>
        public static bool ShouldNotify(SpiritGrowthStage newStage)
            => SpiritGrowthMath.ClampStage(newStage) == SpiritGrowthStage.Bloom;
    }
}
