// 役割: 森に住み着く精霊1体。Idle / Wander / ObserveTree / Sleep の4状態を持つ自律的な住人。
//       プレイヤーからの命令・捕獲・操作は受け付けない（Stage 9の設計方針）。
//
//       ★home森の固定（Stage 9要件）
//         生成時に「その時点の森クラスター」をhomeとして自分のフィールドへ保持する。
//         Spawnerの可変フィールドを毎フレーム参照しない。
//         以後の森成長イベントは TryFollowForestGrowth() でhomeと重なる場合だけ範囲を更新し、
//         遠方の別クラスターでは中心も範囲も変更しない。
//         （RewardBirdで同種の問題が起きたため同じ考え方を採るが、コードは共有せず独立実装している。
//           鳥は報酬演出、精霊は世界の住人であり責務が異なるため。）
//
//       ★乱数の扱い
//         行動計算は SpiritBehaviorMath（純粋関数・UnityEngine.Random不使用）に置き、
//         乱数はこのクラスで生成して引数として渡す。
//
//       ★仮モデルについて
//         プリミティブ合成のモフモフ表現はプロトタイプ専用。大量配置する本番方式ではない。
//         ランタイム生成したMaterialはOnDestroyで明示的に破棄し、参照を残さない。

using System.Collections.Generic;
using UnityEngine;
using ElfVillage.Core;
using ElfVillage.Tiles;

namespace ElfVillage.Spirits
{
    public class ForestSpirit : MonoBehaviour
    {
        [Header("見た目（プロトタイプ用の仮モデル）")]
        [Tooltip("森の精霊の体色（淡い緑系）。将来、種族ごとに変える想定。" +
                  "明るくしすぎると光の玉に見えてしまうため、緑がはっきり残る値にしている")]
        [SerializeField] private Color _bodyColor   = new Color(0.50f, 0.74f, 0.42f);
        // 毛玉の数と膨らみはStage 14から成長段階が決めるため、SerializeFieldでは持たない
        // （SpiritGrowthMath.ComputeGrowthVisualが唯一の供給元）。

        [Header("動き")]
        [SerializeField] private float _idleSwayAmplitude = 0.06f;
        [SerializeField] private float _idleSwaySpeed     = 2.2f;
        [Tooltip("ObserveTree時に、対象タイル中心からどれだけ離れて眺めるか")]
        [SerializeField] private float _observeDistance   = 0.9f;
        [Tooltip("接地からの浮き量（綿毛なので少しだけ浮く）")]
        [SerializeField] private float _hoverHeight       = 0.35f;

        [Header("跳ね移動（Wander/ObserveTreeの移動中）")]
        [Tooltip("跳ねの高さの【基準値】。単体では体高(約0.15)の15〜30%が目安。" +
                  "実際の高さは性格のHopHeightScaleを掛けた値になり、" +
                  "弾む性格（Curious=1.3）ではこの目安を意図的に超える。回数も性格のHopCountに従う")]
        [SerializeField] private float _hopHeight = 0.038f;
        [Tooltip("着地時に少し潰れる量（Visualルートのみに適用）")]
        [SerializeField] private float _hopSquash = 0.10f;

        [Header("起床時の伸び / ObserveTreeのリアクション")]
        [Tooltip("Stretchの変形量。大きくするとゴム的になるので控えめにする")]
        [SerializeField] private float _stretchIntensity   = 0.12f;
        [Tooltip("首を傾げる最大角度（度）")]
        [SerializeField] private float _tiltMaxAngleDeg    = 16f;
        [Tooltip("リアクション1回の長さ（秒）")]
        [SerializeField] private float _reactionDuration   = 0.9f;
        [Tooltip("その場で小さく跳ねるリアクションの高さ")]
        [SerializeField] private float _reactionHopHeight  = 0.05f;

        [Header("世界への反応（Stage 11）")]
        [Tooltip("花の開花などを知覚できる水平距離。これより遠い刺激は完全に無視する")]
        [SerializeField] private float _perceptionRadius = 6f;

        [Header("記憶・見慣れ（Stage 12）")]
        [Tooltip("見慣れ度が半分に薄れるまでの秒数（ゲーム時間）")]
        [SerializeField] private float _familiarityHalfLife = 60f;
        [Tooltip("見慣れ度の上限。ここに達すると反応の強さが最小になる")]
        [SerializeField] private float _familiarityMaximum  = 4f;
        [Tooltip("Reactの最短継続時間。見慣れても一瞬で終わって見えなくならないようにする")]
        [SerializeField] private float _reactMinDuration    = 0.75f;

        // 刺激種類ごとの見慣れ度。減衰・加算はSpiritMemoryが一元的に扱う。
        [SerializeField] private SpiritMemory _memory = new();

        [Header("成長（Stage 14）")]
        [Tooltip("Fluff段階へ上がるのに必要な累積体験。Play Modeで短時間に確認したいときは小さくする")]
        [SerializeField] private float _growthThresholdFluff = 8f;
        [Tooltip("Bloom段階へ上がるのに必要な累積体験")]
        [SerializeField] private float _growthThresholdBloom = 20f;
        [Tooltip("成長演出の変形量。通常のStretch(0.12)より大きくして「育った瞬間」を気づけるようにする")]
        [SerializeField] private float _growthFlourishIntensity = 0.22f;
        [Tooltip("成長演出1回の長さ（秒）")]
        [SerializeField] private float _growthFlourishDuration  = 1.2f;

        // 現在の成長段階。累積体験から導出される値であり、これ自体は保存対象にしない。
        private SpiritGrowthStage _growthStage;
        // 到達すべき段階（複数段階を跨いだ場合は最終到達段階を保持する）。
        private SpiritGrowthStage _pendingGrowthStage;

        private bool  _growthFlourishActive;
        private float _growthFlourishElapsed;
        private bool  _growthAppliedThisFlourish;      // 頂点で1回だけ適用するためのガード
        private bool  _growthFlourishConsumedThisIdle; // 1回のIdle滞在につき1段階に制限する

        /// <summary>検証用の読み取り（表示専用。外部から段階を変更する手段は提供しない）。</summary>
        public SpiritGrowthStage GrowthStage => _growthStage;

        // ── 性格（Stage 13。生成時に一度だけ確定し、以後再計算しない） ──
        //    半減期(_familiarityHalfLife)と上限(_familiarityMaximum)は全性格共通のまま。
        //    「慣れる速さ」はgainで、「慣れた後の反応の残り方」はMinReactionScaleで表現する。
        private SpiritPersonalityKind    _personality = SpiritPersonalityKind.Calm;
        private SpiritPersonalityProfile _profile     = SpiritPersonalityProfile.For(SpiritPersonalityKind.Calm);

        /// <summary>検証用の読み取り（表示専用。外部から性格を変更する手段は提供しない）。</summary>
        public SpiritPersonalityKind Personality => _personality;

        // 今回のReactで使う反応の強さ（受理時に、加算前の見慣れ度から算出して保持する）
        private float _reactScale = 1f;

        // ── home森（生成時に確定し、別の森では変更しない） ─────────────
        private readonly List<HexTile> _homeTiles = new();
        private Vector3 _homeCenter;
        private float   _homeExtentX;
        private float   _homeExtentZ;

        // ── 状態機械 ──────────────────────────────────────────────────
        private SpiritState _state = SpiritState.Idle;
        private float       _stateElapsed;
        private float       _stateDuration;

        // Wander/ObserveTreeの移動
        private Vector3 _moveFrom;
        private Vector3 _moveTo;
        private bool    _isMoving;

        private float _swayPhase;

        // ── 停止可能な個体時計（Stage 15） ────────────────────────────
        //    Settings中はUpdateごと止まるため、この値も進まない。
        //    Familiarityの減衰基準とIdleの揺れ位相はこちらを使い、Time.timeは使わない。
        //    ★静的な共有時計にしないこと（誰が進めるのかが曖昧になり、
        //      複数体になったとき時間が体数ぶん倍速で進む）。
        private float _simulationTime;

        // 外部刺激への反応（Stage 11）
        private int                _currentPriority;   // React中の刺激の優先度（通常は0）
        private SpiritReactionKind _reactKind;         // React中に見せるリアクション
        private Vector3            _reactLookTarget;   // 向くべき刺激の発生位置

        // ObserveTree中のリアクション（1回のObserveTreeにつき最大1回だけ実行する）
        private SpiritReactionKind _reaction;
        private float _reactionStartTime;   // このObserveTree内での開始時刻（state経過秒）
        private bool  _reactionScheduled;   // 今回のObserveTreeでリアクションを予定したか
        private bool  _reactionFinished;    // 既に再生し終えたか

        // ランタイム生成物（OnDestroyで破棄する）
        private readonly List<Material> _runtimeMaterials = new();
        private Transform _bodyRoot;

        // 毛玉の参照。BuildVisualで最大数ぶん確保し、以後は作り直さない（Stage 14）。
        private Transform[] _fluffTransforms;

        // ── Visualスケールの合成（Stage 16） ──────────────────────────
        //    ★_bodyRoot.localScaleへ複数の演出が直接書き込むと互いを潰し合う。
        //      状態演出（Sleep/Stretch/Hop/成長flourish）の値はここに保持し、
        //      誕生演出の倍率と掛け合わせた結果だけをTransformへ書く。
        //        最終スケール = _stateVisualScale × 誕生倍率
        //      成長段階の大きさ（毛玉の数・配置・サイズ）は_bodyRootではなく
        //      各Fluffへ適用されているため、この合成の影響を受けず失われない。
        private Vector3 _stateVisualScale = Vector3.one;

        // 誕生・成長の見せ方。論理は持たず、見た目と音だけを担当する。
        private ForestSpiritPresentation _presentation;

        // 毛玉のリング配置パラメータ（段階によらず共通。変わるのは個数とサイズだけ）。
        private const float FluffRingRadius = 0.075f;
        private const float FluffRingHeight = 0.028f;
        private const float FluffBaseSize   = 0.10f;

        /// <summary>検証Scene用の現在状態の読み取り（表示専用。外部から状態を変更する手段は提供しない）。</summary>
        public SpiritState CurrentState => _state;

        // ── 初期化 ────────────────────────────────────────────────────

        /// <summary>
        /// home森と性格を確定させ、初期状態を開始する。生成直後に1回だけ呼ぶ。
        /// 値はすべてコピーされ、以後Spawner側の変化には影響されない。
        /// ★性格はここで一度だけ決まり、home森が成長しても再決定されない
        ///   （TryFollowForestGrowthは範囲だけを更新する）。
        /// </summary>
        public void Initialize(IReadOnlyList<HexTile> homeTiles, Vector3 homeCenter,
                                float homeExtentX, float homeExtentZ, float randomSeed01,
                                SpiritPersonalityKind personality)
        {
            // 未知のenum値でもProfile.ForがCalmへ安全に倒すため、未初期化Profileは発生しない。
            _personality = personality;
            _profile     = SpiritPersonalityProfile.For(personality);

            SetHome(homeTiles, homeCenter, homeExtentX, homeExtentZ);

            _swayPhase = randomSeed01 * Mathf.PI * 2f;

            transform.position = GroundedPosition(_homeCenter);

            // ★成長Visualの初期化順（Stage 14）
            //   1. 累積体験から現在の段階を明示的に計算する（default(enum)任せにしない）
            //   2. 最大段階ぶんの毛玉を一度だけ生成する
            //   3. 最初のUpdateを待たずに現在段階を適用する
            //   この順にしないと、生成直後の1フレームだけ別段階の姿が見えてしまう。
            _growthStage = SpiritGrowthMath.ComputeGrowthStage(
                _memory.GetLifetimeExperience(), _growthThresholdFluff, _growthThresholdBloom);
            _pendingGrowthStage = _growthStage;

            // ★Presentationの存在保証はここ（＝精霊自身）が持つ（Stage 16）。
            //   Spawnerは「精霊を生成すること」だけに集中でき、
            //   テストでForestSpiritを直接Initializeしても演出が欠落しない。
            _presentation = GetComponent<ForestSpiritPresentation>();
            if (_presentation == null) _presentation = gameObject.AddComponent<ForestSpiritPresentation>();

            BuildVisual();
            ApplyGrowthVisual(_growthStage);

            EnterState(SpiritState.Idle);
        }

        /// <summary>
        /// 誕生演出を始める（生成直後にSpawnerから1回だけ呼ぶ）。
        /// ★Initializeの中では始めない。
        ///   Initializeは「精霊を組み立てる」処理であり、将来セーブから復元するときにも通る。
        ///   復元された精霊に誕生演出が走ってしまうと「今生まれた」という嘘になるため、
        ///   誕生は生成経路だけの関心事としてここへ分けている。
        /// </summary>
        /// <summary>
        /// 誕生演出を始める。
        /// </summary>
        /// <param name="birthGroundPosition">
        /// 誕生の目印（地面の光の輪）を残すワールド座標。
        /// ★精霊は空中に浮いているため、自分のtransform.positionは使えない。
        ///   home森を知っているSpawner側で確定した地面の高さを受け取る。
        /// </param>
        internal void BeginBirthPresentation(Vector3 birthGroundPosition)
        {
            if (_presentation == null) return;

            _presentation.BeginBirth(birthGroundPosition);
            ApplyComposedVisualScale();
        }

        private void SetHome(IReadOnlyList<HexTile> homeTiles, Vector3 center, float extentX, float extentZ)
        {
            _homeTiles.Clear();
            if (homeTiles != null)
            {
                foreach (var t in homeTiles)
                    if (t != null) _homeTiles.Add(t);
            }
            _homeCenter  = center;
            _homeExtentX = extentX;
            _homeExtentZ = extentZ;
        }

        /// <summary>
        /// 森成長イベントが自分のhome森と重なる場合だけ、home範囲を更新する。
        /// 別の場所の森なら何もしない（＝住み着いた森から動かない）。
        /// </summary>
        /// <returns>自分の森が育ったとみなして更新した場合はtrue。</returns>
        public bool TryFollowForestGrowth(IReadOnlyList<HexTile> tiles, Vector3 center, float extentX, float extentZ)
        {
            bool overlaps = OverlapsHome(tiles);
            if (!overlaps) return false;

            SetHome(tiles, center, extentX, extentZ);
            return true;
        }

        /// <summary>
        /// 与えられたタイル群が自分のhome森と重なるか（タイルの同一性で判定する）。
        /// home更新（TryFollowForestGrowth）と刺激の受理判定の両方がこの1箇所を使うことで、
        /// 「自分の森かどうか」の意味が2箇所へ散らばらないようにしている。
        /// </summary>
        private bool OverlapsHome(IReadOnlyList<HexTile> tiles)
        {
            if (tiles == null || tiles.Count == 0) return false;
            if (_homeTiles.Count == 0) return false;

            foreach (var t in tiles)
                if (t != null && _homeTiles.Contains(t)) return true;
            return false;
        }

        // ── 世界からの刺激（Stage 11） ────────────────────────────────

        private void OnEnable()  => EventBus.Subscribe<SpiritStimulusEvent>(OnStimulus);
        private void OnDisable() => EventBus.Unsubscribe<SpiritStimulusEvent>(OnStimulus);

        private void OnStimulus(SpiritStimulusEvent evt) => HandleStimulus(evt.Stimulus);

        /// <summary>
        /// 生成直後に、自分を生み出した森の成長を最初の体験として受け取る（Spawnerから呼ぶ）。
        /// ★EventBus経由だけに任せると、SpawnerとRelayのどちらが先に購読しているかで
        ///   「生成イベントを体験するか否か」が変わってしまう（購読順依存）。
        ///   生成した本人が明示的に渡すことで、購読順に関わらず必ず最初の体験になる。
        ///   この直後にRelay経由で同じ刺激が届いても、React中の同優先度として弾かれるため
        ///   二重に記憶されることはない。
        /// </summary>
        internal void ReceiveInitialStimulus(SpiritStimulus stimulus) => HandleStimulus(stimulus);

        private void HandleStimulus(SpiritStimulus stimulus)
        {
            if (!Accepts(stimulus)) return;

            // SleepとStretchは外部刺激で中断しない。
            if (!SpiritBehaviorMath.CanBeInterruptedByStimulus(_state)) return;

            int incoming = SpiritBehaviorMath.GetStimulusPriority(stimulus.Kind);
            if (!SpiritBehaviorMath.ShouldInterrupt(_currentPriority, incoming)) return;

            // ここまでの判定を全て通過した＝実際に知覚して反応する刺激だけが記憶に残る。
            // 受理しなかった刺激（home外の森・知覚距離外の花・Sleep/Stretch中・同優先度など）は
            // 上のreturnで抜けているため、Reinforceへ到達しない。
            //
            // 処理順（Stage 12の仕様）:
            //   1. 現時点まで減衰させた見慣れ度を取得
            //   2. その「加算前」の値から今回の反応の強さを算出
            //   3. Reactを開始
            //   4. 今回の体験分を加算
            // 先に加算すると初回の反応まで弱まってしまうため、この順序を守る。
            // 性格はこの2つの値だけを差し替える（純粋関数のシグネチャは変えない）。
            //   MinReactionScale … 見慣れきった後にどれだけ反応が残るか
            //   FamiliarityGain  … どれだけ早く慣れるか
            // ★時刻はTime.timeではなく個体時計を使う（Stage 15）。
            //   Settings中は_simulationTimeが進まないため、停止中に見慣れ度だけが
            //   薄れていくような不整合が起きない。
            float now      = _simulationTime;
            float familiar = _memory.GetFamiliarity(stimulus.Kind, now, _familiarityHalfLife);
            _reactScale    = SpiritBehaviorMath.ComputeReactionScale(
                                 familiar, _familiarityMaximum, _profile.MinReactionScale);

            BeginReact(stimulus, incoming);

            // 5. Familiarity強化 ＋ 6. 累積体験を1増加（SpiritMemoryが同じ経路でまとめて行う）
            _memory.Reinforce(stimulus.Kind, now, _familiarityHalfLife,
                              _profile.FamiliarityGain, _familiarityMaximum);

            // 7. 段階が上がるなら予約だけする。ここでは中断も見た目変更も行わない。
            QueueGrowthIfNeeded();
        }

        /// <summary>
        /// 累積体験から到達段階を求め、上がっていれば予約する（Stage 14）。
        /// ★予約するだけで、既存の状態も見た目も一切変えない。
        ///   実際の反映は「安全なIdle」に入ったときだけ行う（UpdateGrowthFlourish）。
        /// 複数段階を跨いだ場合は最終到達段階を保持し、1段階ずつ消化していく。
        /// </summary>
        private void QueueGrowthIfNeeded()
        {
            var reached = SpiritGrowthMath.ComputeGrowthStage(
                _memory.GetLifetimeExperience(), _growthThresholdFluff, _growthThresholdBloom);

            if (SpiritGrowthMath.ShouldQueueGrowthVisual(_pendingGrowthStage, reached))
                _pendingGrowthStage = reached;
        }

        /// <summary>この刺激が自分に関係するか（種類ごとの受理条件）。</summary>
        private bool Accepts(SpiritStimulus stimulus)
        {
            switch (stimulus.Kind)
            {
                case SpiritStimulusKind.ForestGrew:
                    // 自分のhome森の成長だけ受理する（遠方の別クラスターは無視）。
                    return OverlapsHome(stimulus.RelatedTiles);

                case SpiritStimulusKind.FlowerBloomed:
                    // 知覚距離内の開花だけ受理する（水平距離で判定）。
                    return SpiritBehaviorMath.IsWithinPerception(
                        transform.position, stimulus.WorldPosition, _perceptionRadius);

                default:
                    return false; // 未知の刺激は安全に無視する
            }
        }

        private void BeginReact(SpiritStimulus stimulus, int priority)
        {
            _currentPriority = priority;
            _reactKind       = SpiritBehaviorMath.PickReactionFor(stimulus.Kind);
            _reactLookTarget = stimulus.WorldPosition;

            // EnterStateがWanderの目的地破棄・移動停止・表示リセットをまとめて行う。
            EnterState(SpiritState.React);
        }

        // ── 状態機械 ──────────────────────────────────────────────────

        private void EnterState(SpiritState next)
        {
            // ★成長演出の後始末をここへ一元化する（Stage 14）。
            //   React・Wander・ObserveTree・Sleep・Stretch など、Idleを離れる経路は
            //   すべてEnterStateを通るため、_growthFlourishActiveがIdle以外で残り続けない。
            EndGrowthFlourish();

            // 前の状態の演出（傾き・変形・跳ねの高さ）を必ずここで打ち消してから次へ進む。
            // これにより、どの状態を途中で抜けても表示が残らない。
            ResetVisualPose();

            // Idle滞在ごとに成長演出は1回だけ。状態が変わるたびに解禁する
            // （＝1回のIdleで1段階、残りは次にIdleへ入ったときに演出される）。
            _growthFlourishConsumedThisIdle = false;

            _state         = next;
            _stateElapsed  = 0f;
            _isMoving      = false;

            // ★性格のIdle倍率はIdleだけへ掛ける。
            //   ここで状態を明示的に判定することで、Sleep/Stretch/React/Wander/ObserveTreeへ
            //   倍率が漏れないことがこの1行から読み取れるようにしている。
            //   Reactの長さはFamiliarity由来の_reactScaleが下で別途調整する（両者を混ぜない）。
            float durationScale = (next == SpiritState.Idle) ? _profile.IdleDurationScale : 1f;
            _stateDuration = SpiritBehaviorMath.ComputeStateDuration(next, Random.value, durationScale);

            _reactionScheduled = false;
            _reactionFinished  = false;
            _reactionStartTime = 0f; // 前回のObserveTreeの開始時刻を持ち越さない

            switch (next)
            {
                case SpiritState.Wander:
                    // ★性格の行動範囲は「目的地を選ぶ範囲」を狭めることで表現する。
                    //   WanderRadiusScaleは1.0を超えないため、Curiousでもhome範囲を出ない。
                    //   最終的な収まりはBeginMoveのClampToBoundsが保証する
                    //   （PickWanderTargetの既存の保証はそのまま維持される）。
                    BeginMove(SpiritBehaviorMath.PickWanderTarget(
                        _homeCenter, RoamExtentX, RoamExtentZ, Random.value, Random.value));
                    break;

                case SpiritState.ObserveTree:
                    BeginMove(PickObserveSpot());
                    // 到着後に1回だけ行うリアクションを、この時点で決めておく
                    // （乱数はここで生成し、純粋関数へ渡す）。
                    _reaction          = SpiritBehaviorMath.PickObserveReaction(Random.value);
                    _reactionScheduled = true;
                    break;

                case SpiritState.React:
                    // 刺激の方向を向く。中断されたWanderの目的地は_isMoving=falseにより破棄され、
                    // React後はIdleから改めて目的地を選び直す（古い目的地へは戻らない）。
                    FaceTowards(_reactLookTarget);

                    // 見慣れているほど短く終わる。ただし最短時間を下回らせず、
                    // 反応が一瞬で消えて視認できなくなるのを防ぐ（Stage 12）。
                    _stateDuration = Mathf.Max(_reactMinDuration, _stateDuration * _reactScale);
                    break;

                case SpiritState.Idle:
                case SpiritState.Sleep:
                case SpiritState.Stretch:
                    // その場に留まる（水平移動しない）。
                    break;
            }

            // React以外の状態へ移ったら、反応中の優先度をクリアして次の刺激を受け付ける。
            if (next != SpiritState.React) _currentPriority = 0;
        }

        /// <summary>指定位置の方向を向く（水平のみ）。同一位置なら回転を変えない（不正な回転を作らない）。</summary>
        private void FaceTowards(Vector3 worldPosition)
        {
            var dir = worldPosition - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) return; // 同じ位置ならLookRotationを呼ばない
            transform.rotation = Quaternion.LookRotation(dir.normalized);
        }

        /// <summary>
        /// Visualルートの回転・スケールを既定へ戻す（演出の残留を防ぐ単一のリセット地点）。
        /// ★戻すのは「状態演出ぶん」だけ。誕生演出の倍率は合成側で維持されるため、
        ///   誕生の途中でReactへ割り込まれても精霊が急に原寸へ弾けることはない。
        /// </summary>
        private void ResetVisualPose()
        {
            if (_bodyRoot == null) return;
            _bodyRoot.localRotation = Quaternion.identity;
            _bodyRoot.localPosition = Vector3.zero;

            _stateVisualScale = Vector3.one;
            ApplyComposedVisualScale();
        }

        /// <summary>
        /// 状態演出のスケールを設定し、その場で合成して反映する。
        /// ★記録だけにして反映をUpdate末尾へ任せると、Update以外から呼ばれた場合に
        ///   1フレーム遅れる。設定と反映を必ず同時に行うことでその隙間をなくす。
        /// </summary>
        private void SetStateVisualScale(Vector3 scale)
        {
            _stateVisualScale = scale;
            ApplyComposedVisualScale();
        }

        /// <summary>
        /// 状態演出と誕生演出を掛け合わせてTransformへ書き込む唯一の地点。
        /// 誕生倍率は演出中以外は必ず1なので、通常時は状態演出の値がそのまま出る。
        /// </summary>
        private void ApplyComposedVisualScale()
        {
            if (_bodyRoot == null) return;

            float birth = _presentation != null ? _presentation.BirthScaleMultiplier : 1f;
            if (!float.IsFinite(birth) || birth <= 0f) birth = 1f;

            _bodyRoot.localScale = _stateVisualScale * birth;
        }

        // ── 実際に歩き回る範囲（Stage 13） ────────────────────────────
        //    ★性格の行動範囲は「Wanderの目的地の選び方」だけでなく、
        //      自発移動の最終的な収まり先そのものへ掛ける。
        //      Wanderにしか掛けないと、あまりWanderしないCalmは結局
        //      ObserveTreeの移動でhome全域まで出てしまい、
        //      「Calmは中央寄りで狭い」がプレイヤーから見えなくなるため。
        //      倍率は1.0を超えないので、元のhome範囲は決して超えない。
        private float RoamExtentX => _homeExtentX * _profile.WanderRadiusScale;
        private float RoamExtentZ => _homeExtentZ * _profile.WanderRadiusScale;

        private void BeginMove(Vector3 target)
        {
            _moveFrom = transform.position;
            _moveTo   = GroundedPosition(
                SpiritBehaviorMath.ClampToBounds(target, _homeCenter, RoamExtentX, RoamExtentZ));
            _isMoving = true;
        }

        /// <summary>home森のタイルを1枚選び、その少し手前を「眺める位置」として返す。</summary>
        private Vector3 PickObserveSpot()
        {
            if (_homeTiles.Count == 0) return transform.position;

            HexTile target = null;
            for (int attempt = 0; attempt < _homeTiles.Count; attempt++)
            {
                var candidate = _homeTiles[Random.Range(0, _homeTiles.Count)];
                if (candidate != null) { target = candidate; break; }
            }
            if (target == null) return transform.position;

            // タイル中心そのものではなく、少し手前に立って眺める。
            Vector3 tileCenter = target.transform.position;
            Vector3 dir = tileCenter - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) dir = Vector3.forward;

            return tileCenter - dir.normalized * _observeDistance;
        }

        private void Update()
        {
            // ★Settings中はここで完全に止まる（Stage 15）。
            //   returnするだけなので、状態の経過・個体時計・React・成長演出・見た目が
            //   すべてその場で凍結し、解除後は中断ではなく停止地点から自然に再開する。
            //   PauseMenu中は既存のCritter群と同じく動き続ける。
            //   ★以降のTime.deltaTime参照は、この早期returnを通過したときにしか到達しない。
            if (!SpiritSimulationPolicy.ShouldSimulate(GameInteractionStateController.Current)) return;

            float dt = Time.deltaTime;

            // 実際にシミュレーションしたぶんだけ進む個体時計。
            // ★静的な共有時計にしないこと。共有にすると「誰が進めるのか」が曖昧になり、
            //   複数体になったとき時間が体数ぶん倍速で進んでしまう。
            _simulationTime += dt;

            _stateElapsed += dt;

            if (_isMoving)
            {
                float p = SpiritBehaviorMath.ComputeMoveProgress(_stateElapsed, _stateDuration);

                // 水平移動は従来どおりLerp＋イージング（home範囲内の保証はここで維持される）。
                // 跳ねはY方向の一時オフセットとして上乗せするだけなので、
                // progress=1で必ず0に戻り、状態をまたいでY座標が蓄積しない。
                var pos = Vector3.Lerp(_moveFrom, _moveTo, p);
                pos.y += SpiritBehaviorMath.ComputeHopOffset(p, MoveHopCount, MoveHopHeight);
                transform.position = pos;

                ApplyHopSquash(p);

                var flat = _moveTo - _moveFrom;
                flat.y = 0f;
                if (flat.sqrMagnitude > 0.0001f)
                    transform.rotation = Quaternion.LookRotation(flat.normalized);

                if (p >= 1f)
                {
                    _isMoving = false;          // 到着後はその場で残り時間を過ごす（ワープしない）
                    transform.position = _moveTo; // 跳ねのオフセットを完全に清算して接地させる
                    ResetVisualPose();
                }
            }
            else
            {
                switch (_state)
                {
                    case SpiritState.Idle:
                        // 成長演出中は通常の揺れを止める（小さな揺れに演出が埋もれないため）。
                        if (UpdateGrowthFlourish()) break;

                        // その場で小さく上下に揺れる。
                        var idlePos = transform.position;
                        idlePos.y = GroundedY() + SpiritBehaviorMath.ComputeIdleSway(
                            _simulationTime * _idleSwaySpeed, _swayPhase, _idleSwayAmplitude);
                        transform.position = idlePos;
                        break;

                    case SpiritState.Sleep:
                        ApplySleepPose();
                        break;

                    case SpiritState.Stretch:
                        ApplyStretchPose();
                        break;

                    case SpiritState.ObserveTree:
                        ApplyObserveReaction();
                        break;

                    case SpiritState.React:
                        ApplyReactPose();
                        break;
                }
            }

            // 誕生演出は「実際にシミュレーションしたぶん」だけ進む。
            // Settings中はUpdate自体がここへ到達しないため、演出も自動的に止まり、
            // 解除後は停止地点から再開する（Stage 15の保証と同じ仕組みに乗せている）。
            if (_presentation != null) _presentation.Advance(dt);

            // 状態演出と誕生演出を掛け合わせてTransformへ書き込む唯一の地点。
            ApplyComposedVisualScale();

            if (_stateElapsed >= _stateDuration)
                EnterState(SpiritBehaviorMath.DecideNextState(
                    _state, Random.value,
                    _profile.WanderWeight, _profile.ObserveWeight, _profile.SleepWeight));
        }

        // ── 移動中の跳ねに使う実効値（Stage 13） ──────────────────────
        //    ★性格を反映するのは「移動中の跳ね」だけ。
        //      ObserveTree到着後のSmallHopとReactのSmallHopは、その場で1回だけ跳ねる別の演出であり、
        //      移動用のHopCountを流用すると意味が変わってしまうため hopCount:1 のまま維持する。
        private int   MoveHopCount  => _profile.HopCount;
        private float MoveHopHeight => _hopHeight * _profile.HopHeightScale;

        // ══ 成長演出（Stage 14。新しいSpiritStateは追加しない）═══════════

        /// <summary>
        /// 成長演出を進める。Idleの中だけで完結する一時演出であり、状態機械は増やさない。
        /// </summary>
        /// <returns>演出中ならtrue（Idleの通常演出を止める）。</returns>
        private bool UpdateGrowthFlourish()
        {
            if (!_growthFlourishActive)
            {
                if (!CanStartGrowthFlourish()) return false;

                _growthFlourishActive           = true;
                _growthFlourishElapsed          = 0f;
                _growthAppliedThisFlourish      = false;
                _growthFlourishConsumedThisIdle = true; // このIdle滞在では以後開始しない
            }

            if (_bodyRoot == null) { EndGrowthFlourish(); return false; }

            _growthFlourishElapsed += Time.deltaTime;
            float p = _growthFlourishDuration > 0f
                ? Mathf.Clamp01(_growthFlourishElapsed / _growthFlourishDuration)
                : 1f;

            // 一時変形はVisualルートのlocalScaleだけ。毛玉そのものには恒久的な倍率を残さない。
            SetStateVisualScale(SpiritBehaviorMath.ComputeStretchScale(p, _growthFlourishIntensity));

            // 伸びの折り返し地点で1段階だけ確定させ、同じ瞬間に綿毛を差し替える。
            // ★段階の確定と見た目の適用を同一フレームの同一地点で行うことで、
            //   ここより前に中断されれば「未確定＋見た目そのまま」、
            //   ここより後に中断されれば「確定済み＋見た目適用済み」となり、
            //   どちらでも半端な状態が残らない。
            if (!_growthAppliedThisFlourish && p >= 0.5f)
            {
                var previousStage = _growthStage;

                _growthStage = SpiritGrowthMath.ResolveGrowthTransition(_growthStage, _pendingGrowthStage);
                ApplyGrowthVisual(_growthStage);
                _growthAppliedThisFlourish = true;

                // ★演出・音・通知への通知はこの1点だけ（Stage 16）。
                //   段階の確定と同じ地点・同じガードの内側なので、
                //   Stage 14の「頂点前は未発火／頂点後は再発火しない／1段階1回」が
                //   そのまま引き継がれ、二重発火の経路が生まれない。
                if (_presentation != null) _presentation.PlayGrowth(_growthStage);

                EventBus.Publish(new ForestSpiritGrowthCommittedEvent(
                    transform.position, previousStage, _growthStage, _personality));
            }

            if (p >= 1f)
            {
                _growthFlourishActive      = false;
                _growthFlourishElapsed     = 0f;
                _growthAppliedThisFlourish = false;
                ResetVisualPose();
                return false;
            }

            return true;
        }

        /// <summary>
        /// 成長演出を開始してよいか。Sleep・Stretch・React・Wander・ObserveTree・移動中は開始しない
        /// （状態がIdleであることを条件にしているため、これらは自動的に除外される）。
        /// </summary>
        private bool CanStartGrowthFlourish()
            => _state == SpiritState.Idle
            && !_isMoving
            && !_growthFlourishConsumedThisIdle
            && (int)_pendingGrowthStage > (int)_growthStage;

        /// <summary>
        /// 成長演出の中断・終了をまとめて処理する唯一の地点。
        /// 頂点前なら段階は未確定のままpendingが残り、次の安全なIdleで最初からやり直す。
        /// 頂点後なら段階も見た目も確定済みで、そのまま保持される（同じ段階は再演出しない）。
        /// </summary>
        private void EndGrowthFlourish()
        {
            if (!_growthFlourishActive) return;

            _growthFlourishActive      = false;
            _growthFlourishElapsed     = 0f;
            _growthAppliedThisFlourish = false;
            ResetVisualPose();
        }

        /// <summary>
        /// 成長段階に応じて綿毛の「有効数・配置・サイズ」を更新する（永続Visual）。
        /// ★GameObject・Mesh・Materialを作り直さず、事前生成した配列を書き換えるだけ。
        ///   毎回のGetComponentsInChildrenやLINQ検索も行わない。
        /// ★_bodyRoot自体には触れないため、ResetVisualPose（＝一時演出の打ち消し）が
        ///   成長後の毛玉の数・配置・サイズを元へ戻すことはない。
        /// </summary>
        private void ApplyGrowthVisual(SpiritGrowthStage stage)
        {
            if (_fluffTransforms == null) return;

            var visual = SpiritGrowthMath.ComputeGrowthVisual(stage);
            int layers = Mathf.Clamp(visual.FluffLayers, 1, _fluffTransforms.Length);

            for (int i = 0; i < _fluffTransforms.Length; i++)
            {
                var fluff = _fluffTransforms[i];
                if (fluff == null) continue;

                bool active = i < layers;
                if (fluff.gameObject.activeSelf != active) fluff.gameObject.SetActive(active);
                if (!active) continue;

                // ★有効な個数でリングを組み直す。
                //   最大数のまま一部を隠すと、残った毛玉が円周上で偏ってしまうため、
                //   段階が変わるたびに有効なぶんだけで均等配置し直す（確保は発生しない）。
                float angle = i * (Mathf.PI * 2f / layers);
                fluff.localPosition = new Vector3(
                    Mathf.Cos(angle) * FluffRingRadius,
                    Mathf.Sin(angle * 2f) * FluffRingHeight,
                    Mathf.Sin(angle) * FluffRingRadius);

                fluff.localScale = Vector3.one * (FluffBaseSize * visual.FluffScale);
            }
        }

        /// <summary>Sleep中は少し縮んで「丸くなっている」ように見せる（移動はしない）。</summary>
        private void ApplySleepPose()
        {
            if (_bodyRoot == null) return;
            SetStateVisualScale(Vector3.Lerp(_stateVisualScale, Vector3.one * 0.82f, Time.deltaTime * 3f));
        }

        /// <summary>起床時の伸び。純粋関数の結果をVisualルートのスケールへそのまま反映する。</summary>
        private void ApplyStretchPose()
        {
            if (_bodyRoot == null) return;
            float p = _stateDuration > 0f ? Mathf.Clamp01(_stateElapsed / _stateDuration) : 1f;
            SetStateVisualScale(SpiritBehaviorMath.ComputeStretchScale(p, _stretchIntensity));
        }

        /// <summary>
        /// React中の演出。刺激の種類に応じたリアクションを1回だけ再生する。
        /// 新しいサブ状態機械は作らず、React状態の経過時間をそのまま進行度として使う。
        /// 水平移動はせず、Visualルートの回転・オフセットだけを動かす。
        /// </summary>
        private void ApplyReactPose()
        {
            if (_bodyRoot == null) return;

            float p = _stateDuration > 0f ? Mathf.Clamp01(_stateElapsed / _stateDuration) : 1f;

            // 見慣れているほど角度・高さが小さくなる（Stage 12）。
            // 反応の「形」は変えず、大きさだけを_reactScaleで縮める。
            if (_reactKind == SpiritReactionKind.TiltHead)
            {
                float angle = SpiritBehaviorMath.ComputeTiltAngle(p, _tiltMaxAngleDeg * _reactScale);
                _bodyRoot.localRotation = Quaternion.Euler(0f, 0f, angle);
            }
            else
            {
                float y = SpiritBehaviorMath.ComputeHopOffset(p, 1, _reactionHopHeight * _reactScale);
                _bodyRoot.localPosition = new Vector3(0f, y, 0f);
            }
        }

        /// <summary>移動中の着地タイミングで軽く潰れる（Visualルートのみ・体は動かさない）。</summary>
        private void ApplyHopSquash(float progress)
        {
            if (_bodyRoot == null || _hopSquash <= 0f) return;

            // 跳ねの高さが低いほど「着地している」とみなして潰す。
            // 上の移動処理と同じ実効値を使い、潰れと跳ねがずれないようにする。
            float hopHeight = MoveHopHeight;
            float h = hopHeight > 0f
                ? SpiritBehaviorMath.ComputeHopOffset(progress, MoveHopCount, hopHeight) / hopHeight
                : 0f;
            float squash = _hopSquash * (1f - h);
            SetStateVisualScale(new Vector3(1f + squash * 0.5f, 1f - squash, 1f + squash * 0.5f));
        }

        /// <summary>
        /// ObserveTree中に1回だけ小さなリアクションを再生する。
        /// 新しいサブ状態機械は作らず、経過時間の窓に入っているかだけで判定する。
        /// </summary>
        private void ApplyObserveReaction()
        {
            if (_bodyRoot == null || !_reactionScheduled || _reactionFinished) return;

            // 到着してから少し間を置いて始める（眺めてから反応する形にする）。
            if (_reactionStartTime <= 0f)
                _reactionStartTime = Mathf.Min(_stateElapsed + 0.4f, Mathf.Max(0f, _stateDuration - _reactionDuration));

            float t = _stateElapsed - _reactionStartTime;
            if (t < 0f) return;

            if (t >= _reactionDuration)
            {
                // 1回で終了。以後このObserveTree中は再生しない（連続実行を防ぐ）。
                _reactionFinished = true;
                ResetVisualPose();
                return;
            }

            float p = _reactionDuration > 0f ? Mathf.Clamp01(t / _reactionDuration) : 1f;

            if (_reaction == SpiritReactionKind.TiltHead)
            {
                float angle = SpiritBehaviorMath.ComputeTiltAngle(p, _tiltMaxAngleDeg);
                _bodyRoot.localRotation = Quaternion.Euler(0f, 0f, angle);
            }
            else
            {
                // その場で小さく1回だけ跳ねる（本体は動かさずVisualルートを上下させるため、
                // home範囲の水平位置には一切影響しない）。
                float y = SpiritBehaviorMath.ComputeHopOffset(p, 1, _reactionHopHeight);
                _bodyRoot.localPosition = new Vector3(0f, y, 0f);
            }
        }

        private float GroundedY() => _homeCenter.y + _hoverHeight;

        private Vector3 GroundedPosition(Vector3 p) => new Vector3(p.x, GroundedY(), p.z);

        // ── 仮モデル（プロトタイプ専用） ───────────────────────────────
        //    小さな綿毛の生き物として読めるよう、Body＋周囲のFluff＋小さな目で構成する。
        //    発光マテリアルは使わず、Lit系の淡い緑にすることで「光の玉」に見えないようにする。

        private void BuildVisual()
        {
            var root = new GameObject("Visual");
            root.transform.SetParent(transform, false);
            _bodyRoot = root.transform;

            var bodyMat  = CreateRuntimeMaterial(_bodyColor);
            // 白へ寄せすぎると「光の玉」に見えるため、ごく僅かに明るくするだけに留める。
            var fluffMat = CreateRuntimeMaterial(Color.Lerp(_bodyColor, Color.white, 0.12f));
            var eyeMat   = CreateRuntimeMaterial(new Color(0.10f, 0.11f, 0.12f));

            // Body: 少し潰した球。木の樹冠（直径0.45前後）よりはっきり小さくして
            // 「小さな綿毛の生き物」として読めるサイズにしている。
            var body = CreatePart(PrimitiveType.Sphere, root.transform, "Body", bodyMat);
            body.localPosition = Vector3.zero;
            body.localScale    = new Vector3(0.17f, 0.15f, 0.17f);

            // Fluff: Bodyの周囲に配置して輪郭をモフモフにする。
            // ★最大段階(Bloom)ぶんをここで一度だけ生成し、参照を固定長配列で保持する。
            //   成長時はこの配列のSetActive・localPosition・localScaleだけを更新するため、
            //   GameObject・Mesh・Materialの生成破棄も、毎回の子オブジェクト検索も起こらない。
            //   実際の数・配置・サイズはこの直後のApplyGrowthVisualが段階に応じて決める。
            _fluffTransforms = new Transform[SpiritGrowthMath.MaxFluffLayers];
            for (int i = 0; i < _fluffTransforms.Length; i++)
                _fluffTransforms[i] = CreatePart(PrimitiveType.Sphere, root.transform, "Fluff" + i, fluffMat);

            // 目: 正面（+Z）に小さく2つ
            CreateEye(root.transform, -1f, eyeMat);
            CreateEye(root.transform,  1f, eyeMat);
        }

        private void CreateEye(Transform parent, float side, Material mat)
        {
            // Fluffより前（+Z）へ出して、毛に埋もれず目が見えるようにする。
            var eye = CreatePart(PrimitiveType.Sphere, parent, side < 0 ? "EyeL" : "EyeR", mat);
            eye.localPosition = new Vector3(side * 0.045f, 0.030f, 0.115f);
            eye.localScale    = Vector3.one * 0.038f;
        }

        private Transform CreatePart(PrimitiveType type, Transform parent, string name, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);

            // 精霊は当たり判定を持たない（タイルへのレイキャストを妨げないため）。
            var col = go.GetComponent<Collider>();
            if (col != null)
            {
                if (Application.isPlaying) Destroy(col);
                else DestroyImmediate(col);
            }

            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null && mat != null) mr.sharedMaterial = mat;

            return go.transform;
        }

        private Material CreateRuntimeMaterial(Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader == null) return null;

            var mat = new Material(shader) { name = "ForestSpirit_Runtime" };
            mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            _runtimeMaterials.Add(mat);
            return mat;
        }

        // ランタイム生成したMaterialは自動では解放されないため明示的に破棄する
        // （Play Mode終了後・テスト後に不要な参照を残さないため）。
        private void OnDestroy()
        {
            foreach (var mat in _runtimeMaterials)
            {
                if (mat == null) continue;
                if (Application.isPlaying) Destroy(mat);
                else DestroyImmediate(mat);
            }
            _runtimeMaterials.Clear();
        }
    }
}
