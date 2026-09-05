// 役割: 朝・昼・夕方・夜の時間サイクルを管理する。
//       各時間帯の滞在後に transitionDuration 秒かけて徐々に次の時間帯へ遷移する。
//       DirectionalLight・環境光・霧の色を Lerp で滑らかに切り替える。
//       スカイボックスは Custom/SkyboxBlend シェーダーで 2 テクスチャをクロスフェードする。

using System.Collections;
using UnityEngine;

namespace ElfVillage.Core
{
    public class TimeOfDaySystem : MonoBehaviour
    {
        [Header("サイクル設定（秒）")]
        [SerializeField] private float _stayDuration       = 10f;
        [SerializeField] private float _transitionDuration =  3f;

        [Header("ライト")]
        [SerializeField] private Light _sun;

        [Header("スカイボックス（時間帯ごとのマテリアル）")]
        [SerializeField] private Material _morningSkybox;
        [SerializeField] private Material _afternoonSkybox;
        [SerializeField] private Material _eveningSkybox;
        [SerializeField] private Material _nightSkybox;

        // ── 各時間帯の設定 ──────────────────────────────────────────────

        [System.Serializable]
        public struct TimeSettings
        {
            public Color ambientColor;
            public Color lightColor;
            public float lightIntensity;
            public Color fogColor;

            // ★空はパノラマ画像ではなく3色のグラデーションで作る。
            //   背景に描かれた木や草花が盤面のタイルと視線を取り合わないようにするため。
            //   時間帯の変化はテクスチャの差し替えではなく、この色を補間して表す
            public Color skyZenith;    // 天頂
            public Color skyHorizon;   // 地平
            public Color skyGround;    // 地平より下
        }

        [Header("🌅 朝 — 朝靄の森")]
        [SerializeField] private TimeSettings _morning = new TimeSettings
        {
            ambientColor   = new Color(0.50f, 0.42f, 0.34f),
            lightColor     = new Color(1.00f, 0.78f, 0.45f),
            lightIntensity = 1.15f,
            fogColor       = new Color(0.90f, 0.75f, 0.60f),
            skyZenith      = new Color(0.35f, 0.58f, 0.88f),
            skyHorizon     = new Color(0.72f, 0.86f, 0.96f),
            skyGround      = new Color(0.55f, 0.70f, 0.82f),
        };

        [Header("☀️ 昼 — 木漏れ日の昼")]
        [SerializeField] private TimeSettings _afternoon = new TimeSettings
        {
            ambientColor   = new Color(0.46f, 0.54f, 0.64f),
            lightColor     = new Color(1.00f, 0.98f, 0.92f),
            lightIntensity = 1.5f,
            fogColor       = new Color(0.75f, 0.85f, 0.95f),
            skyZenith      = new Color(0.13f, 0.38f, 0.82f),
            skyHorizon     = new Color(0.45f, 0.70f, 0.94f),
            skyGround      = new Color(0.35f, 0.55f, 0.78f),
        };

        [Header("🌇 夕方 — 黄金色の夕暮れ")]
        [SerializeField] private TimeSettings _evening = new TimeSettings
        {
            ambientColor   = new Color(0.44f, 0.26f, 0.14f),
            lightColor     = new Color(1.00f, 0.50f, 0.18f),
            lightIntensity = 0.9f,
            fogColor       = new Color(0.88f, 0.48f, 0.20f),
            skyZenith      = new Color(0.72f, 0.34f, 0.18f),
            skyHorizon     = new Color(1.00f, 0.60f, 0.26f),
            skyGround      = new Color(0.62f, 0.32f, 0.22f),
        };

        [Header("🌌 夜 — 精霊が舞う星空")]
        [SerializeField] private TimeSettings _night = new TimeSettings
        {
            ambientColor   = new Color(0.05f, 0.06f, 0.16f),
            lightColor     = new Color(0.28f, 0.30f, 0.55f),
            lightIntensity = 0.18f,
            fogColor       = new Color(0.03f, 0.04f, 0.10f),
            skyZenith      = new Color(0.10f, 0.08f, 0.20f),
            skyHorizon     = new Color(0.24f, 0.20f, 0.38f),
            skyGround      = new Color(0.15f, 0.12f, 0.24f),
        };

        private TimeOfDayEvent.Phase _currentPhase = TimeOfDayEvent.Phase.Morning;

        /// <summary>
        /// 現在の時間帯。TimeOfDayEvent は切り替わった瞬間にしか飛ばないため、
        /// 実行中に生成されたオブジェクト（タイル上の家など）が初期状態を
        /// 合わせるために参照する。読み取り専用。
        /// </summary>
        public static TimeOfDayEvent.Phase Current { get; private set; }
            = TimeOfDayEvent.Phase.Morning;

        [Header("空のグラデーション")]
        [Tooltip("地平線からどれだけの高さで天頂の色へ移りきるか。小さいほど空の色が下まで降りてくる")]
        [SerializeField, Range(0.05f, 1.5f)] private float _skyHorizonWidth = 0.30f;

        [Tooltip("地平線から下へのぼかし幅")]
        [SerializeField, Range(0.05f, 1.5f)] private float _skyGroundWidth = 0.25f;

        // ランタイムで生成する空のマテリアル（シーンには保存しない）
        private Material _skyMat;

        private void Start()
        {
            Current = _currentPhase;
            if (_sun == null)
                _sun = FindFirstObjectByType<Light>();

            InitGradientSkybox();
            ApplyImmediate(_morning);
            StartCoroutine(CycleRoutine());
        }

        private void OnDestroy()
        {
            if (_skyMat != null) Destroy(_skyMat);
        }

        // ── スカイボックス初期化 ──────────────────────────────────────

        private void InitGradientSkybox()
        {
            var shader = Shader.Find("Custom/SkyboxGradient");
            if (shader == null)
            {
                // シェーダーが見つからない場合は、従来のパノラマへフォールバックする
                Debug.LogWarning("[TimeOfDaySystem] Custom/SkyboxGradient が見つかりません。パノラマの空へ戻します。", this);
                ApplySkyboxDirect(TimeOfDayEvent.Phase.Morning);
                return;
            }

            _skyMat = new Material(shader) { name = "SkyGradient_Runtime" };
            _skyMat.SetFloat("_HorizonWidth", _skyHorizonWidth);
            _skyMat.SetFloat("_GroundWidth",  _skyGroundWidth);
            RenderSettings.skybox = _skyMat;

            // ★環境光は空から拾わず、時間帯ごとのambientColorを使う。
            //   グラデーションの空は画面の大半が明るい一色なので、そこから環境光を作ると
            //   全体が持ち上がってタイルの地面が白く飛ぶ（実機で確認）。
            //   ambientColorは元から時間帯ごとに用意されていたが、
            //   Skyboxモードでは無視されていた。ここで実際に効くようにする
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        }

        // ── メインサイクル ────────────────────────────────────────────

        private IEnumerator CycleRoutine()
        {
            while (true)
            {
                yield return new WaitForSeconds(_stayDuration);
                var next = NextPhase(_currentPhase);
                yield return StartCoroutine(TransitionTo(GetSettings(next), next));
                _currentPhase = next;
                Current = _currentPhase;
                EventBus.Publish(new TimeOfDayEvent(_currentPhase));
            }
        }

        // ── 遷移コルーチン ────────────────────────────────────────────

        private IEnumerator TransitionTo(TimeSettings to, TimeOfDayEvent.Phase targetPhase)
        {
            var   from    = GetCurrentLiveSettings();
            float elapsed = 0f;

            while (elapsed < _transitionDuration)
            {
                elapsed += Time.deltaTime;
                float t  = Mathf.SmoothStep(0f, 1f, elapsed / _transitionDuration);
                ApplyLerp(from, to, t);
                yield return null;
            }

            ApplyImmediate(to);
        }

        // ── 設定適用 ─────────────────────────────────────────────────

        private void ApplyImmediate(TimeSettings s)
        {
            RenderSettings.ambientLight = s.ambientColor;
            RenderSettings.fogColor     = s.fogColor;
            ApplySky(s.skyZenith, s.skyHorizon, s.skyGround);
            if (_sun != null)
            {
                _sun.color     = s.lightColor;
                _sun.intensity = s.lightIntensity;
            }
        }

        private void ApplyLerp(TimeSettings from, TimeSettings to, float t)
        {
            RenderSettings.ambientLight = Color.Lerp(from.ambientColor, to.ambientColor, t);
            RenderSettings.fogColor     = Color.Lerp(from.fogColor,     to.fogColor,     t);
            ApplySky(Color.Lerp(from.skyZenith,  to.skyZenith,  t),
                     Color.Lerp(from.skyHorizon, to.skyHorizon, t),
                     Color.Lerp(from.skyGround,  to.skyGround,  t));
            if (_sun != null)
            {
                _sun.color     = Color.Lerp(from.lightColor, to.lightColor, t);
                _sun.intensity = Mathf.Lerp(from.lightIntensity, to.lightIntensity, t);
            }
        }

        /// <summary>
        /// 空の3色を差し替える。空は背景を描くだけで、明るさには関与しない
        /// （環境光はambientColor、陰影は太陽が受け持つ）。
        /// </summary>
        private void ApplySky(Color zenith, Color horizon, Color ground)
        {
            if (_skyMat == null) return;

            _skyMat.SetColor("_ZenithColor",  zenith);
            _skyMat.SetColor("_HorizonColor", horizon);
            _skyMat.SetColor("_GroundColor",  ground);
        }

        // 空のシェーダーが使えない場合の直接スワップ（フォールバック）
        // ★通常は使わない。グラデーションの空へ移行したため、
        //   パノラマ画像（_Game/Art/HDRI）は非常時の受け皿としてだけ残してある
        private void ApplySkyboxDirect(TimeOfDayEvent.Phase phase)
        {
            var mat = GetSkyboxMaterial(phase);
            if (mat != null) RenderSettings.skybox = mat;
        }

        // ── ヘルパー ────────────────────────────────────────────────

        private Material GetSkyboxMaterial(TimeOfDayEvent.Phase phase)
        {
            switch (phase)
            {
                case TimeOfDayEvent.Phase.Morning:   return _morningSkybox;
                case TimeOfDayEvent.Phase.Afternoon: return _afternoonSkybox;
                case TimeOfDayEvent.Phase.Evening:   return _eveningSkybox;
                default:                             return _nightSkybox;
            }
        }

        /// <summary>今の空の色を1つ取り出す。空がまだ無い場合は黒を返さない（遷移が沈むため）。</summary>
        private Color GetSkyColor(string property)
            => _skyMat != null ? _skyMat.GetColor(property) : Color.white;

        private TimeSettings GetCurrentLiveSettings()
        {
            return new TimeSettings
            {
                ambientColor   = RenderSettings.ambientLight,
                lightColor     = _sun != null ? _sun.color     : Color.white,
                lightIntensity = _sun != null ? _sun.intensity : 1f,
                fogColor       = RenderSettings.fogColor,

                // ★空の色も「今の値」を拾う。
                //   ここを埋めないと構造体の既定値（黒）が遷移の起点になり、
                //   時間帯が切り替わる瞬間だけ背景が黒く沈んでから新しい色へ戻る
                skyZenith      = GetSkyColor("_ZenithColor"),
                skyHorizon     = GetSkyColor("_HorizonColor"),
                skyGround      = GetSkyColor("_GroundColor"),
            };
        }

        private TimeSettings GetSettings(TimeOfDayEvent.Phase phase)
        {
            switch (phase)
            {
                case TimeOfDayEvent.Phase.Morning:   return _morning;
                case TimeOfDayEvent.Phase.Afternoon: return _afternoon;
                case TimeOfDayEvent.Phase.Evening:   return _evening;
                default:                             return _night;
            }
        }

        private static TimeOfDayEvent.Phase NextPhase(TimeOfDayEvent.Phase phase)
        {
            switch (phase)
            {
                case TimeOfDayEvent.Phase.Morning:   return TimeOfDayEvent.Phase.Afternoon;
                case TimeOfDayEvent.Phase.Afternoon: return TimeOfDayEvent.Phase.Evening;
                case TimeOfDayEvent.Phase.Evening:   return TimeOfDayEvent.Phase.Night;
                default:                             return TimeOfDayEvent.Phase.Morning;
            }
        }
    }
}
