// 役割: 性格ごとの行動調整値（Stage 13）。
//       新しい状態機械は作らず、Stage 9〜12で既に存在する調整点へ値を流し込むだけにする。
//
//       ★ScriptableObjectではなくreadonly structにしている理由
//         これはコンテンツ（タイル・クエスト等）ではなく「挙動の定数」であり、
//         本プロジェクトではその種の値は純粋関数側に置く方針が定着している
//         （SpiritBehaviorMath / TerrainEffectWeight）。値型なら参照を持たず、
//         アセット管理も不要で、EditModeテストからそのまま検証できる。
//         デザイナーが実時間で調整したくなった段階でSO化する余地は残る。
//
//       ★安全性
//         各値はプロパティ読み出し時に必ず健全化される。そのため default(struct) が
//         誤って使われても、中立的で安全な値（等倍・既定回数）として振る舞い、
//         NaN・Infinity・負値が下流の計算へ流れ込むことはない。
//         参照型（GameObject・Component・HexTile）は一切持たず、生成時にアロケーションも起きない。

namespace ElfVillage.Spirits
{
    public readonly struct SpiritPersonalityProfile
    {
        // 生の値。読み出しは必ず下のプロパティ経由で健全化する。
        private readonly float _wanderWeight;
        private readonly float _observeWeight;
        private readonly float _sleepWeight;
        private readonly float _idleDurationScale;
        private readonly float _wanderRadiusScale;
        private readonly float _hopHeightScale;
        private readonly int   _hopCount;
        private readonly float _familiarityGain;
        private readonly float _minReactionScale;

        public SpiritPersonalityProfile(
            float wanderWeight, float observeWeight, float sleepWeight,
            float idleDurationScale, float wanderRadiusScale,
            float hopHeightScale, int hopCount,
            float familiarityGain, float minReactionScale)
        {
            _wanderWeight      = wanderWeight;
            _observeWeight     = observeWeight;
            _sleepWeight       = sleepWeight;
            _idleDurationScale = idleDurationScale;
            _wanderRadiusScale = wanderRadiusScale;
            _hopHeightScale    = hopHeightScale;
            _hopCount          = hopCount;
            _familiarityGain   = familiarityGain;
            _minReactionScale  = minReactionScale;
        }

        // ── 健全化された読み出し ────────────────────────────────────

        /// <summary>Idleから各状態を選ぶ相対的な重み（確率ではない。合計1.0である必要はない）。</summary>
        public float WanderWeight  => SafeWeight(_wanderWeight);
        public float ObserveWeight => SafeWeight(_observeWeight);
        public float SleepWeight   => SafeWeight(_sleepWeight);

        /// <summary>Idle継続時間の倍率。1.0で既存挙動。</summary>
        public float IdleDurationScale => SafeScale(_idleDurationScale);

        /// <summary>
        /// 自発移動の行動圏の倍率。1.0でhome範囲全体。1.0を超えない。
        /// ★名前はWanderだが、Wander専用ではない。Wander・ObserveTreeを含む
        ///   「自分から移動する」すべての目的地がこの倍率で狭めたhome範囲に収まる。
        ///   Wanderにだけ掛けると、あまりWanderしないCalmがObserveTreeの移動で
        ///   home全域まで出てしまい「中央寄りで狭い」がプレイヤーから見えなくなるため。
        /// </summary>
        public float WanderRadiusScale => SafeRadiusScale(_wanderRadiusScale);

        /// <summary>移動中（Wander/ObserveTree）の跳ねの高さ倍率。</summary>
        public float HopHeightScale => SafeScale(_hopHeightScale);

        /// <summary>移動中（Wander/ObserveTree）の跳ね回数。その場の小さな跳ねには使わない。</summary>
        public int HopCount => (_hopCount >= 1 && _hopCount <= 20) ? _hopCount : 2;

        /// <summary>刺激1回あたりに増える見慣れ度。高いほど早く慣れる。</summary>
        public float FamiliarityGain => SafePositive(_familiarityGain, 1f);

        /// <summary>見慣れきったときの反応の強さの下限（0にはしない）。</summary>
        public float MinReactionScale => SafeMinScale(_minReactionScale);

        // ── 性格ごとの定義 ──────────────────────────────────────────

        /// <summary>
        /// 性格に対応するProfileを返す純粋関数。
        /// 未知のenum値は安全にCalmへフォールバックする（不正なProfileを下流へ流さない）。
        /// </summary>
        public static SpiritPersonalityProfile For(SpiritPersonalityKind kind)
        {
            switch (kind)
            {
                case SpiritPersonalityKind.Curious:
                    // よく動き、木をよく眺め、ほとんど眠らない。慣れにくく、慣れても反応が大きめに残る。
                    return new SpiritPersonalityProfile(
                        wanderWeight: 0.50f, observeWeight: 0.40f, sleepWeight: 0.10f,
                        idleDurationScale: 0.7f, wanderRadiusScale: 1.0f,
                        hopHeightScale: 1.3f, hopCount: 3,
                        familiarityGain: 0.6f, minReactionScale: 0.42f);

                case SpiritPersonalityKind.Calm:
                default:
                    // あまり動かず、よく眠り、中央寄りの狭い範囲で過ごす。早く慣れるが無反応にはならない。
                    return new SpiritPersonalityProfile(
                        wanderWeight: 0.30f, observeWeight: 0.30f, sleepWeight: 0.40f,
                        idleDurationScale: 1.4f, wanderRadiusScale: 0.6f,
                        hopHeightScale: 0.6f, hopCount: 2,
                        familiarityGain: 1.5f, minReactionScale: 0.32f);
            }
        }

        // ── 値の健全化 ──────────────────────────────────────────────

        private static float SafeWeight(float v)
            => (float.IsFinite(v) && v > 0f) ? v : 0f;

        private static float SafeScale(float v)
            => (float.IsFinite(v) && v > 0f) ? v : 1f;

        /// <summary>範囲倍率は0より大きく1以下に丸める（home範囲を超えさせないため）。</summary>
        private static float SafeRadiusScale(float v)
        {
            if (!float.IsFinite(v) || v <= 0f) return 1f;
            return v > 1f ? 1f : v;
        }

        private static float SafePositive(float v, float fallback)
            => (float.IsFinite(v) && v > 0f) ? v : fallback;

        private static float SafeMinScale(float v)
        {
            if (!float.IsFinite(v) || v <= 0f) return 0.25f;
            return v > 1f ? 1f : v;
        }
    }
}
