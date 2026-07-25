// 役割: 精霊の「見慣れ度（Familiarity）」を刺激種類ごとに保持する記憶（Stage 12）。
//       出来事の無制限ログは持たず、種類ごとの集約値だけを持つ。
//
//       ★セーブ適性
//         HexTile・GameObject・Componentなどのシーン参照を一切持たず、floatの配列だけで構成している。
//         Dictionaryはそのままではシリアライズされないため使わず、固定長配列にしている。
//         スロットの対応はTryGetIndexでenum値から独立して固定しているため、
//         同一刺激種類のエントリが重複することも起こり得ない。
//
//       ★_lastUpdatedAt と将来のセーブについて（重要）
//         _lastUpdatedAtが保持するのは Time.time（そのセッション内のゲーム時間）であり、
//         ゲームを再起動すると0へ戻るため、この値そのものは永続化しても復元できない。
//         したがって「SpiritMemoryをそのまま丸ごと保存すれば済む」とは扱わないこと。
//         将来セーブ・ロードを実装する際は次の方針とする（Stage 12では実装しない）:
//           ・保存前に、保存時点までFamiliarityを減衰させる
//           ・保存対象の中心は「減衰済みのFamiliarity値」であり、時刻そのものではない
//           ・ロード時は_lastUpdatedAtを、その時点のゲーム時刻で再初期化する
//           ・Time.timeの絶対値はセーブデータとして復元しない
//         この方針であれば、実時刻（DateTime）やリアルタイム時計へ依存する必要はない。
//
//       ★減衰の一元化
//         「減衰後の値の取得」と「体験時の加算」の両方がDecayTo()を通るため、
//         減衰処理が複数箇所へ散らばらない。
//
//       時刻はゲーム時間（Time.time相当）を呼び出し側から渡す。
//       DateTimeなどの実時刻には依存しない（ポーズ中や無効中に記憶が薄れないようにするため）。

using UnityEngine;

namespace ElfVillage.Spirits
{
    [System.Serializable]
    public class SpiritMemory
    {
        /// <summary>SpiritStimulusKindの種類数。enumへ値を追加したらここも合わせる。</summary>
        public const int KindCount = 2;

        [SerializeField] private float[] _familiarity   = new float[KindCount];
        [SerializeField] private float[] _lastUpdatedAt = new float[KindCount];

        /// <summary>
        /// 指定時刻時点での見慣れ度（減衰後）を返す。副作用はなく、記憶自体は変更しない。
        /// 未知の刺激種類・欠損データでは0を返す。
        /// </summary>
        public float GetFamiliarity(SpiritStimulusKind kind, float now, float halfLifeSeconds)
        {
            if (!TryGetIndex(kind, out int i)) return 0f;

            float elapsed = now - _lastUpdatedAt[i];
            return SpiritBehaviorMath.ComputeDecayedFamiliarity(_familiarity[i], elapsed, halfLifeSeconds);
        }

        /// <summary>
        /// 体験を1回ぶん記憶へ加える。まず現時点まで減衰させてから加算するため、
        /// 「久しぶりの体験」では薄れた状態から積み上がる。
        /// 未知の刺激種類では何もしない。
        /// </summary>
        public void Reinforce(SpiritStimulusKind kind, float now, float halfLifeSeconds, float gain, float maximum)
        {
            if (!TryGetIndex(kind, out int i)) return;

            float decayed = GetFamiliarity(kind, now, halfLifeSeconds);
            _familiarity[i]   = SpiritBehaviorMath.ComputeFamiliarityGain(decayed, gain, maximum);
            _lastUpdatedAt[i] = float.IsFinite(now) ? now : 0f;
        }

        /// <summary>
        /// 刺激種類 → 保存スロットの明示的な対応。
        /// ★enumの数値をそのままインデックスへキャストしない。
        ///   キャストに頼ると、enumの宣言順の入れ替えや明示値の変更、将来の値追加によって
        ///   既存スロットの意味（＝保存済みの見慣れ度が何の記憶か）が静かに変わってしまう。
        ///   ここでswitchで固定しておけば、enumへ値を追加しても既存2種類の対応は動かない。
        ///   新しい刺激種類を追加するときは、KindCountを増やし、ここへ新しいスロットを明示的に足す。
        /// 未知のenum値はfalseを返し、記憶の読み書きを行わない。
        /// </summary>
        private bool TryGetIndex(SpiritStimulusKind kind, out int index)
        {
            switch (kind)
            {
                case SpiritStimulusKind.ForestGrew:    index = 0; break;
                case SpiritStimulusKind.FlowerBloomed: index = 1; break;
                default:                                index = -1; return false;
            }

            // 配列の欠損（null・長さ不足）を安全に補う。
            // 毎フレーム呼ばれても新規確保が起きないよう、必要なときだけ作り直す。
            if (_familiarity == null || _familiarity.Length < KindCount)
                _familiarity = new float[KindCount];
            if (_lastUpdatedAt == null || _lastUpdatedAt.Length < KindCount)
                _lastUpdatedAt = new float[KindCount];

            return true;
        }
    }
}
