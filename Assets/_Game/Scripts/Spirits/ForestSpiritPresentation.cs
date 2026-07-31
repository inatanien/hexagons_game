// 役割: 森の精霊の「誕生」と「成長」を目と耳へ伝える演出（Stage 16）。
//       論理（状態・成長・記憶）は一切持たず、見た目と音だけを担当する。
//
//       ★新しいSpiritStateを追加しない
//         誕生演出はVisual専用のトラックとして動く。行動状態は通常どおり進み、
//         刺激も通常どおり受理される（Stage 11〜12の保証を変えない）。
//
//       ★誕生の目印について分かっている制約（将来の強化候補・未着手）
//         地面の光の輪は、誕生位置の周りに木が密集していると billboard に隠れて
//         弧の一部しか見えないことがある。誕生位置は定義上「森4枚以上のクラスタ中心」なので、
//         木が周囲にあるのは避けられない。
//         現状は通知UI（WorldNoticeEvent）との組み合わせで目的を満たしているが、
//         誕生演出を強化する段階では「上向きに舞う少数の光粒」のように、
//         樹木に隠れても視認できる要素を輪へ足すことを検討すること。
//
//       ★時間の扱い（不整合を作らない設計）
//         演出タイマーは自分では進めず、ForestSpirit.Update から Advance(dt) を
//         呼んでもらう。ForestSpirit.Update は Settings 中に早期 return するため、
//         演出時間も自動的に停止し、解除後は停止地点から再開する。
//         一方 ParticleSystem は Unity 自身が進めてしまうので、
//         停止判定だけは Update で毎フレーム評価する（早期 return しない）。
//         これにより「精霊は止まっているのにパーティクルだけ進む」状態が構造的に起きない。

using UnityEngine;
using ElfVillage.Core;

namespace ElfVillage.Spirits
{
    [DisallowMultipleComponent]
    public class ForestSpiritPresentation : MonoBehaviour
    {
        [Header("誕生演出")]
        [Tooltip("誕生演出の長さ（秒）。成長演出と揃えている")]
        [SerializeField] private float _birthDuration = 1.2f;
        [Tooltip("生まれた瞬間の大きさ。0にすると法線が壊れて描画が乱れるため、必ず正の値にする")]
        [SerializeField] private float _birthStartScale = 0.15f;

        [Header("誕生の目印（地面に広がって消える光の輪）")]
        [Tooltip("輪が消えるまでの秒数。誕生演出より短いと、精霊が現れ切る前に目印が消える")]
        [SerializeField] private float _markerDuration  = 1.8f;
        [Tooltip("輪の初期サイズ")]
        [SerializeField] private float _markerStartSize = 0.6f;
        [Tooltip("輪の最大サイズ。タイルの辺中点（約1.73）より少し大きくすると" +
                  "「このタイルのあたり」として読める")]
        [SerializeField] private float _markerEndSize   = 2.4f;

        [Header("VFX")]
        [SerializeField] private Color _birthLightColor  = new Color(0.85f, 1f, 0.75f, 1f);
        [SerializeField] private Color _growthLightColor = new Color(0.80f, 1f, 0.70f, 1f);
        [Tooltip("Bloom到達時だけ暖色寄りにして少し華やかに見せる")]
        [SerializeField] private Color _bloomLightColor  = new Color(1f, 0.95f, 0.70f, 1f);

        [Header("SE（未設定なら何も鳴らさない）")]
        [Tooltip("AudioManager.PlaySEは未実装のため、実装され次第そのまま鳴り出す")]
        [SerializeField] private AudioClip _birthSe;
        [SerializeField] private AudioClip _growthSe;
        [SerializeField] private AudioClip _bloomSe;

        // ── 誕生演出の進行 ────────────────────────────────────────────
        private bool  _birthPlaying;
        private float _birthElapsed;
        // 誕生は一度きり。二重に呼ばれても演出をやり直さず、目印も増やさない。
        private bool  _birthStarted;

        // ── VFX ───────────────────────────────────────────────────────
        private ParticleSystem _vfx;
        private Material       _vfxMaterial;
        private Texture2D      _vfxTexture;
        private bool           _particlesRunning = true;

        // ── 誕生の目印 ────────────────────────────────────────────────
        // ★精霊とは別のParticleSystemにする。
        //   目印はワールド空間へ置き去りにする必要があり、
        //   きらめきのバースト（_vfx）とは寿命も大きさも動きも別物のため。
        private ParticleSystem _marker;
        private Material       _markerMaterial;
        private Texture2D      _markerTexture;

        private const int   MaxBurstParticles = 9;
        private const float VfxLifetimeMin    = 0.8f;
        private const float VfxLifetimeMax    = 1.2f;

        /// <summary>
        /// 誕生演出による大きさの倍率。ForestSpirit がこれを状態演出のスケールへ掛け合わせる。
        /// 演出中でなければ必ず 1（＝何も変えない）。
        /// </summary>
        public float BirthScaleMultiplier =>
            _birthPlaying ? ComputeBirthScale(Progress01(_birthElapsed, _birthDuration), _birthStartScale) : 1f;

        public bool IsPlayingBirth => _birthPlaying;

        // ── 純粋関数（EditModeから直接検証できる） ────────────────────

        /// <summary>経過と長さから0〜1の進行率。長さが不正なら即完了扱い。</summary>
        public static float Progress01(float elapsed, float duration)
        {
            if (!float.IsFinite(elapsed) || elapsed <= 0f) return 0f;
            if (!float.IsFinite(duration) || duration <= 0f) return 1f;
            return Mathf.Clamp01(elapsed / duration);
        }

        /// <summary>
        /// 誕生時の大きさ。startScaleから1へ、ふわっと膨らんでから落ち着く。
        /// progress=1 では必ず 1.0 を返すため、演出後に大きさが残らない。
        /// startScaleが不正でも0以下にはならない（0スケールは描画が壊れるため）。
        /// </summary>
        public static float ComputeBirthScale(float progress, float startScale)
        {
            float p = Mathf.Clamp01(float.IsFinite(progress) ? progress : 1f);
            float s = (float.IsFinite(startScale) && startScale > 0f) ? Mathf.Min(startScale, 1f) : 0.15f;

            if (p >= 1f) return 1f;

            // 前半で一気に膨らみ、後半で軽く行き過ぎてから1へ収まる。
            float eased = Mathf.SmoothStep(0f, 1f, p);
            float overshoot = Mathf.Sin(p * Mathf.PI) * 0.08f * (1f - p);

            return Mathf.Lerp(s, 1f, eased) + overshoot;
        }

        /// <summary>
        /// 目印の輪の大きさ。進行0で開始サイズ、1で最大サイズ。
        /// 「勢いよく広がってから緩やかに止まる」ため、水面の波紋のように見える。
        /// 不正な値でも必ず正の大きさを返す（0スケールは描画が壊れるため）。
        /// </summary>
        public static float ComputeMarkerSize(float progress, float startSize, float endSize)
        {
            float p = Mathf.Clamp01(float.IsFinite(progress) ? progress : 1f);
            float s = (float.IsFinite(startSize) && startSize > 0f) ? startSize : 0.1f;
            float e = (float.IsFinite(endSize)   && endSize   > 0f) ? endSize   : s;

            if (e < s) e = s;   // 逆転していても縮まないようにする

            // 1 - (1-p)^2 で、序盤に大きく広がって終盤で緩む。
            float eased = 1f - (1f - p) * (1f - p);
            return Mathf.Lerp(s, e, eased);
        }

        /// <summary>
        /// 目印の表示時間。
        /// ★誕生演出より短くしない。短いと、精霊が現れ切る前に目印が消えてしまい、
        ///   「どこで起きたのか」を確かめる時間が無くなる。
        /// </summary>
        public static float SafeMarkerDuration(float requested, float birthDuration)
        {
            float b = (float.IsFinite(birthDuration) && birthDuration > 0f) ? birthDuration : 0f;
            float r = (float.IsFinite(requested)     && requested     > 0f) ? requested     : b;

            return Mathf.Clamp(Mathf.Max(r, b), 0.1f, 10f);
        }

        /// <summary>成長段階に応じた光の色。未知の段階は成長色へ安全に倒れる。</summary>
        public Color LightColorFor(SpiritGrowthStage stage)
            => SpiritGrowthMath.ClampStage(stage) == SpiritGrowthStage.Bloom ? _bloomLightColor : _growthLightColor;

        /// <summary>成長段階に応じたSE。未設定ならnullのまま（呼び出し側が何もしない）。</summary>
        public AudioClip SeFor(SpiritGrowthStage stage)
            => SpiritGrowthMath.ClampStage(stage) == SpiritGrowthStage.Bloom ? _bloomSe : _growthSe;

        // ── 演出の開始（ForestSpiritから呼ばれる） ────────────────────

        /// <summary>
        /// 誕生演出を始める。一度きりで、やり直しはしない。
        /// ★二度目以降の呼び出しは完全に無視する（演出のやり直しも、目印の重複も起こさない）。
        /// </summary>
        /// <param name="groundPosition">
        /// 目印の光の輪を残す地面のワールド座標。
        /// 精霊は空中に浮いているため、Spawner側で確定した地面の高さを受け取る。
        /// </param>
        internal void BeginBirth(Vector3 groundPosition)
        {
            if (_birthStarted) return;
            _birthStarted = true;

            _birthPlaying = true;
            _birthElapsed = 0f;

            PlayBurst(_birthLightColor, MaxBurstParticles);
            PlayGroundMarker(groundPosition);
            PlaySe(_birthSe);
        }

        /// <summary>成長が確定した瞬間に呼ばれる（頂点commitと同じ1点）。</summary>
        internal void PlayGrowth(SpiritGrowthStage newStage)
        {
            bool isBloom = SpiritGrowthMath.ClampStage(newStage) == SpiritGrowthStage.Bloom;

            PlayBurst(LightColorFor(newStage), isBloom ? MaxBurstParticles : 6);
            PlaySe(SeFor(newStage));
        }

        /// <summary>
        /// 演出時間を進める。ForestSpirit.Update から、実際にシミュレーションした
        /// deltaだけが渡される（Settings中は呼ばれないので自動的に止まる）。
        /// </summary>
        internal void Advance(float deltaSeconds)
        {
            if (!_birthPlaying) return;
            if (!float.IsFinite(deltaSeconds) || deltaSeconds <= 0f) return;

            _birthElapsed += deltaSeconds;

            // 終了時にフラグを下ろすだけ。ForestSpirit側のスケールは
            // BirthScaleMultiplierが1へ戻ることで自然に元へ収まる
            // （状態演出のスケールを1へ潰さない）。
            if (Progress01(_birthElapsed, _birthDuration) >= 1f)
            {
                _birthPlaying = false;
                _birthElapsed = 0f;
            }
        }

        // ── ParticleSystemの停止制御 ──────────────────────────────────

        private void Update()
        {
            // ★ここは早期returnしない。
            //   ParticleSystemはUnity自身が進めるため、精霊の停止とは独立に止める必要がある。
            //   Settings中に「精霊は止まっているのにパーティクルだけ舞う」を防ぐ唯一の地点。
            bool shouldRun = InteractionTimePolicy.ShouldAdvanceNow();
            if (shouldRun == _particlesRunning) return;

            _particlesRunning = shouldRun;

            // ★きらめきと目印の両方を止める。片方だけ動くと不整合が見える。
            SetParticlesRunning(_vfx,    shouldRun);
            SetParticlesRunning(_marker, shouldRun);
        }

        private static void SetParticlesRunning(ParticleSystem ps, bool run)
        {
            if (ps == null) return;
            if (run) ps.Play(true);
            else     ps.Pause(true);
        }

        // ── VFX本体 ───────────────────────────────────────────────────

        /// <summary>
        /// シェーダーを先に温めておく（Spawnerの Awake から呼ぶ）。
        /// ★タイル配置と同じフレームで Shader.Find が初めて走るとフリーズすることが
        ///   WorldBreathSystemで確認されている。誕生はまさにその同フレームなので、
        ///   Scene開始時に一度だけ引いてUnity側のキャッシュを温めておく。
        ///
        /// ★事前に済ませているのは Shader.Find だけであることに注意。
        ///   Material・Texture2D・ParticleSystem用GameObjectは、いずれも
        ///   EnsureVfx() の初回呼び出し（＝誕生と同じフレーム）で生成される。
        ///   「Shaderを温めたので生成負荷も解消済み」ではない。
        ///
        ///   Phase1_v002での実測（森タイル配置1回あたりの所要時間）:
        ///     1枚目 13.58ms / 2枚目 3.68ms / 3枚目 6.59ms
        ///     4枚目（誕生＋VFX初回生成）10.24ms / 5枚目（生成済み）6.49ms
        ///   誕生フレームは1枚目より軽く、目に見える停止はない。
        ///   1体・低頻度の現状では個体所有のままで問題ないと判断している。
        ///   将来複数体を同時に生む段階になったら、MaterialとTextureの共有化を検討すること。
        /// </summary>
        public static void PrewarmShader() => FindVfxShader();

        private static Shader FindVfxShader()
            => Shader.Find("Universal Render Pipeline/Particles/Unlit")
            ?? Shader.Find("Particles/Standard Unlit")
            ?? Shader.Find("Sprites/Default");

        private void PlayBurst(Color color, int count)
        {
            EnsureVfx();
            if (_vfx == null) return;

            var main = _vfx.main;
            main.startColor = new ParticleSystem.MinMaxGradient(color);

            // Emitは一度きりの短いバースト。Poolも常駐パーティクルも持たない。
            _vfx.Emit(Mathf.Clamp(count, 1, MaxBurstParticles));
        }

        // ── 誕生の目印（地面の光の輪） ────────────────────────────────

        /// <summary>
        /// 誕生位置へ光の輪を1つ置く。
        /// ★simulationSpace = World なので、放出した瞬間からこの粒はワールド座標へ固定される。
        ///   精霊がこのあと漂い始めても、輪は生まれた場所に残る（本Stageの核心）。
        /// </summary>
        private void PlayGroundMarker(Vector3 groundPosition)
        {
            if (!IsFinite(groundPosition)) return;

            EnsureMarker();
            if (_marker == null) return;

            var main = _marker.main;
            main.startColor = new ParticleSystem.MinMaxGradient(_birthLightColor);

            _marker.Emit(new ParticleSystem.EmitParams
            {
                position      = groundPosition,   // World空間なのでワールド座標をそのまま渡す
                velocity      = Vector3.zero,
                startLifetime = SafeMarkerDuration(_markerDuration, _birthDuration),
                // sizeOverLifetime はこの値へ掛かるので、ここには最大サイズを入れる
                startSize     = ComputeMarkerSize(1f, _markerStartSize, _markerEndSize),
                rotation      = 0f,
            }, 1);
        }

        private static bool IsFinite(Vector3 v)
            => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);

        private void EnsureMarker()
        {
            // ★増殖防止。BeginBirthが二重に呼ばれてもParticleSystemは1つのまま。
            if (_marker != null) return;

            var go = new GameObject("SpiritBirthMarker");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.zero;

            _marker = go.AddComponent<ParticleSystem>();

            var main = _marker.main;
            main.playOnAwake     = false;
            main.loop            = false;
            main.startSpeed      = new ParticleSystem.MinMaxCurve(0f);
            main.gravityModifier = new ParticleSystem.MinMaxCurve(0f);
            // ★これがあるから、精霊が動いても輪が誕生位置に残る。
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles    = 1;

            // 自動発生はしない。誕生の瞬間に1粒だけEmitする。
            var emission = _marker.emission;
            emission.enabled = false;

            var shape = _marker.shape;
            shape.enabled = false;   // 位置はEmitParamsで直接指定する

            // 広がる：小さく現れて最大まで開く
            var sizeOverLifetime = _marker.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size    = new ParticleSystem.MinMaxCurve(1f, MarkerSizeCurve());

            // 消える：さっと現れてゆっくり薄くなる
            var colorOverLifetime = _marker.colorOverLifetime;
            colorOverLifetime.enabled = true;
            colorOverLifetime.color   = MarkerFadeGradient();

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            if (renderer != null)
            {
                // ★地面に寝かせる。見下ろし視点で輪として読ませるため、
                //   カメラへ正対するBillboardではなくHorizontalBillboardを使う
                //   （水・花の板と同じ方式）。
                renderer.renderMode = ParticleSystemRenderMode.HorizontalBillboard;

                _markerMaterial = BuildMarkerMaterial();
                if (_markerMaterial != null) renderer.material = _markerMaterial;
            }

            _marker.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            _marker.Play(true);
        }

        /// <summary>広がり方のカーブ。ComputeMarkerSize と同じ形を最大値1.0で正規化したもの。</summary>
        private AnimationCurve MarkerSizeCurve()
        {
            float max = ComputeMarkerSize(1f, _markerStartSize, _markerEndSize);
            if (!float.IsFinite(max) || max <= 0f) max = 1f;

            var curve = new AnimationCurve();
            const int samples = 8;
            for (int i = 0; i <= samples; i++)
            {
                float p = i / (float)samples;
                curve.AddKey(p, ComputeMarkerSize(p, _markerStartSize, _markerEndSize) / max);
            }
            return curve;
        }

        private static ParticleSystem.MinMaxGradient MarkerFadeGradient()
        {
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(0f,    0f),
                    new GradientAlphaKey(0.75f, 0.15f),   // さっと現れる
                    new GradientAlphaKey(0f,    1f),      // ゆっくり消える
                });
            return new ParticleSystem.MinMaxGradient(g);
        }

        private Material BuildMarkerMaterial()
        {
            var shader = FindVfxShader();
            if (shader == null) return null;

            var mat = new Material(shader) { name = "SpiritBirthMarker_Runtime" };

            _markerTexture = BuildRingTexture();
            if (_markerTexture != null)
            {
                if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", _markerTexture);
                if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", _markerTexture);
                mat.mainTexture = _markerTexture;
            }

            ConfigureTransparency(mat);
            return mat;
        }

        /// <summary>
        /// 中空の輪。中心は透明で、縁だけが柔らかく光る。
        /// ★塗り潰した円にすると地面の花や木を隠してしまう。輪郭だけを光らせて
        ///   「ここで何かが起きた」とだけ伝える。
        /// </summary>
        private static Texture2D BuildRingTexture()
        {
            const int   size        = 64;
            const float ringRadius  = 0.74f;   // 中心からの距離（0〜1）
            const float ringWidth   = 0.20f;   // 輪の太さ（片側）

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name       = "SpiritBirthMarker_Ring",
                wrapMode   = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

            var pixels = new Color[size * size];
            float center = (size - 1) * 0.5f;
            float radius = center;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - center) / radius;
                    float dy = (y - center) / radius;
                    float d  = Mathf.Sqrt(dx * dx + dy * dy);

                    // 輪の中心線からの距離が近いほど明るい
                    float t     = Mathf.Clamp01(Mathf.Abs(d - ringRadius) / ringWidth);
                    float alpha = 1f - t;
                    alpha = alpha * alpha;                 // 縁を柔らかく

                    if (d >= 1f) alpha = 0f;               // 四角い輪郭を出さない

                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            tex.SetPixels(pixels);
            tex.Apply(false, false);
            return tex;
        }

        private void EnsureVfx()
        {
            if (_vfx != null) return;

            var go = new GameObject("SpiritVfx");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.zero;

            _vfx = go.AddComponent<ParticleSystem>();

            var main = _vfx.main;
            main.playOnAwake      = false;
            main.loop             = false;
            main.startLifetime    = new ParticleSystem.MinMaxCurve(VfxLifetimeMin, VfxLifetimeMax);
            main.startSpeed       = new ParticleSystem.MinMaxCurve(0.15f, 0.4f);
            main.startSize        = new ParticleSystem.MinMaxCurve(0.03f, 0.09f);
            main.startRotation    = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier  = new ParticleSystem.MinMaxCurve(-0.05f); // ふわっと上へ
            main.simulationSpace  = ParticleSystemSimulationSpace.World;
            main.maxParticles     = MaxBurstParticles;

            // 自動発生はしない。Emit()で必要なときだけ出す。
            var emission = _vfx.emission;
            emission.enabled = false;

            var shape = _vfx.shape;
            shape.enabled    = true;
            shape.shapeType  = ParticleSystemShapeType.Sphere;
            shape.radius     = 0.18f;

            // 出て消えるだけ。Collisionモジュールは有効化しないのでColliderは一切増えない。
            var colorOverLifetime = _vfx.colorOverLifetime;
            colorOverLifetime.enabled = true;
            colorOverLifetime.color   = FadeInOutGradient();

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            if (renderer != null)
            {
                _vfxMaterial = BuildMaterial();
                if (_vfxMaterial != null) renderer.material = _vfxMaterial;
            }

            _vfx.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            _vfx.Play(true);
        }

        private Material BuildMaterial()
        {
            var shader = FindVfxShader();
            if (shader == null) return null;

            var mat = new Material(shader) { name = "SpiritVfx_Runtime" };

            // ★テクスチャを与えないと、パーティクルが硬い四角として描画されてしまう
            //   （本編で実際にそう見えることを確認した）。
            //   専用のVFX素材を増やさずに柔らかい光点にするため、
            //   中心が明るく外側へ向かって透明になる小さなテクスチャを実行時に作る。
            _vfxTexture = BuildSoftDotTexture();
            if (_vfxTexture != null)
            {
                // ★URPのUnlit系は _MainTex ではなく _BaseMap を見る。
                //   mainTexture（＝_MainTex）だけを設定するとテクスチャが効かず、
                //   本編で実際に四角いまま描画された。両方へ設定して取りこぼさない。
                if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", _vfxTexture);
                if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", _vfxTexture);
                mat.mainTexture = _vfxTexture;
            }

            ConfigureTransparency(mat);
            return mat;
        }

        /// <summary>
        /// マテリアルを半透明として設定する。
        /// ★URPのParticles/Unlitは既定がOpaqueで、テクスチャのアルファが完全に無視される。
        ///   本編で「テクスチャを設定したのに四角いまま」という状態を実際に確認したため、
        ///   Surface/Blend/ZWrite/RenderQueueを明示的に半透明へ切り替える。
        /// </summary>
        private static void ConfigureTransparency(Material mat)
        {
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f); // 1 = Transparent
            if (mat.HasProperty("_Blend"))   mat.SetFloat("_Blend",   0f); // 0 = Alpha

            if (mat.HasProperty("_SrcBlend"))
                mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (mat.HasProperty("_DstBlend"))
                mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (mat.HasProperty("_ZWrite"))
                mat.SetFloat("_ZWrite", 0f);

            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");

            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }

        /// <summary>中心から外側へ滑らかに消える小さな光点。32pxで十分（拡大されても粗が見えない）。</summary>
        private static Texture2D BuildSoftDotTexture()
        {
            const int size = 32;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name       = "SpiritVfx_SoftDot",
                wrapMode   = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

            var pixels = new Color[size * size];
            float center = (size - 1) * 0.5f;
            float radius = center;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - center) / radius;
                    float dy = (y - center) / radius;
                    float d  = Mathf.Sqrt(dx * dx + dy * dy);

                    // 縁を完全に透明にして四角い輪郭を消す
                    float alpha = Mathf.Clamp01(1f - d);
                    alpha = alpha * alpha;   // 中心へ寄せて柔らかく

                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            tex.SetPixels(pixels);
            tex.Apply(false, false);
            return tex;
        }

        private static ParticleSystem.MinMaxGradient FadeInOutGradient()
        {
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.25f),
                    new GradientAlphaKey(0f, 1f),
                });
            return new ParticleSystem.MinMaxGradient(g);
        }

        // ── SE ────────────────────────────────────────────────────────

        /// <summary>
        /// SEを鳴らす。clipが未設定なら何もしない（Warningも出さない）。
        /// ★AudioManager.PlaySEはまだ未実装。実装された時点でそのまま鳴り出す。
        /// </summary>
        private static void PlaySe(AudioClip clip)
        {
            if (clip == null) return;
            AudioManager.Instance?.PlaySE(clip);
        }

        // ── 後始末 ────────────────────────────────────────────────────

        private void OnDestroy()
        {
            // ランタイム生成したMaterialとTextureは自動では解放されないため明示的に破棄する。
            DestroyRuntimeAsset(_vfxMaterial);
            DestroyRuntimeAsset(_vfxTexture);
            DestroyRuntimeAsset(_markerMaterial);
            DestroyRuntimeAsset(_markerTexture);
            _vfxMaterial    = null;
            _vfxTexture     = null;
            _markerMaterial = null;
            _markerTexture  = null;
        }

        private static void DestroyRuntimeAsset(Object asset)
        {
            if (asset == null) return;
            if (Application.isPlaying) Destroy(asset);
            else                       DestroyImmediate(asset);
        }
    }
}
