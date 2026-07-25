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
        [Tooltip("Body周辺に生やす毛玉の数。増やすほどモフモフになる（将来の成長表現用）")]
        [SerializeField] private int   _fluffLayers = 6;
        [Tooltip("毛玉の膨らみ倍率。上げるほどモフモフになる（将来の成長表現用）")]
        [SerializeField] private float _fluffScale  = 1f;
        [Tooltip("森の精霊の体色（淡い緑系）。将来、種族ごとに変える想定。" +
                  "明るくしすぎると光の玉に見えてしまうため、緑がはっきり残る値にしている")]
        [SerializeField] private Color _bodyColor   = new Color(0.50f, 0.74f, 0.42f);

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
            BuildVisual();
            EnterState(SpiritState.Idle);
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
            float now      = Time.time;
            float familiar = _memory.GetFamiliarity(stimulus.Kind, now, _familiarityHalfLife);
            _reactScale    = SpiritBehaviorMath.ComputeReactionScale(
                                 familiar, _familiarityMaximum, _profile.MinReactionScale);

            BeginReact(stimulus, incoming);

            _memory.Reinforce(stimulus.Kind, now, _familiarityHalfLife,
                              _profile.FamiliarityGain, _familiarityMaximum);
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
            // 前の状態の演出（傾き・変形・跳ねの高さ）を必ずここで打ち消してから次へ進む。
            // これにより、どの状態を途中で抜けても表示が残らない。
            ResetVisualPose();

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

        /// <summary>Visualルートの回転・スケールを既定へ戻す（演出の残留を防ぐ単一のリセット地点）。</summary>
        private void ResetVisualPose()
        {
            if (_bodyRoot == null) return;
            _bodyRoot.localRotation = Quaternion.identity;
            _bodyRoot.localScale    = Vector3.one;
            _bodyRoot.localPosition = Vector3.zero;
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
            float dt = Time.deltaTime;
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
                        // その場で小さく上下に揺れる。
                        var idlePos = transform.position;
                        idlePos.y = GroundedY() + SpiritBehaviorMath.ComputeIdleSway(
                            Time.time * _idleSwaySpeed, _swayPhase, _idleSwayAmplitude);
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

        /// <summary>Sleep中は少し縮んで「丸くなっている」ように見せる（移動はしない）。</summary>
        private void ApplySleepPose()
        {
            if (_bodyRoot == null) return;
            _bodyRoot.localScale = Vector3.Lerp(_bodyRoot.localScale, Vector3.one * 0.82f, Time.deltaTime * 3f);
        }

        /// <summary>起床時の伸び。純粋関数の結果をVisualルートのスケールへそのまま反映する。</summary>
        private void ApplyStretchPose()
        {
            if (_bodyRoot == null) return;
            float p = _stateDuration > 0f ? Mathf.Clamp01(_stateElapsed / _stateDuration) : 1f;
            _bodyRoot.localScale = SpiritBehaviorMath.ComputeStretchScale(p, _stretchIntensity);
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
            _bodyRoot.localScale = new Vector3(1f + squash * 0.5f, 1f - squash, 1f + squash * 0.5f);
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

            // Fluff: Bodyの周囲に配置して輪郭をモフモフにする
            int layers = Mathf.Max(0, _fluffLayers);
            for (int i = 0; i < layers; i++)
            {
                float angle = i * (Mathf.PI * 2f / Mathf.Max(1, layers));
                var fluff = CreatePart(PrimitiveType.Sphere, root.transform, "Fluff" + i, fluffMat);
                fluff.localPosition = new Vector3(
                    Mathf.Cos(angle) * 0.075f,
                    Mathf.Sin(angle * 2f) * 0.028f,
                    Mathf.Sin(angle) * 0.075f);
                fluff.localScale = Vector3.one * (0.10f * Mathf.Max(0.01f, _fluffScale));
            }

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
