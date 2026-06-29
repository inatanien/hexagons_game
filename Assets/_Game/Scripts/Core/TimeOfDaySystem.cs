// 役割: 朝・昼・夕方・夜の時間サイクルを管理する。
//       各時間帯の滞在後に transitionDuration 秒かけて徐々に次の時間帯へ遷移する。
//       DirectionalLight・環境光・霧の色を Lerp で滑らかに切り替える。
//       将来の HDRI スカイボックス差し替えに備え、時間帯ごとの設定は SerializeField で公開している。

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

        // ── 各時間帯の設定 ──────────────────────────────────────────────

        [System.Serializable]
        public struct TimeSettings
        {
            public Color ambientColor;
            public Color lightColor;
            public float lightIntensity;
            public Color fogColor;
        }

        [Header("🌅 朝 — 朝靄の森")]
        [SerializeField] private TimeSettings _morning = new TimeSettings
        {
            ambientColor   = new Color(0.85f, 0.62f, 0.38f),
            lightColor     = new Color(1.00f, 0.78f, 0.45f),
            lightIntensity = 0.8f,
            fogColor       = new Color(0.90f, 0.75f, 0.60f),
        };

        [Header("☀️ 昼 — 木漏れ日の昼")]
        [SerializeField] private TimeSettings _afternoon = new TimeSettings
        {
            ambientColor   = new Color(0.72f, 0.80f, 0.90f),
            lightColor     = new Color(1.00f, 0.98f, 0.92f),
            lightIntensity = 1.2f,
            fogColor       = new Color(0.75f, 0.85f, 0.95f),
        };

        [Header("🌇 夕方 — 黄金色の夕暮れ")]
        [SerializeField] private TimeSettings _evening = new TimeSettings
        {
            ambientColor   = new Color(0.80f, 0.38f, 0.12f),
            lightColor     = new Color(1.00f, 0.50f, 0.18f),
            lightIntensity = 0.55f,
            fogColor       = new Color(0.88f, 0.48f, 0.20f),
        };

        [Header("🌌 夜 — 精霊が舞う星空")]
        [SerializeField] private TimeSettings _night = new TimeSettings
        {
            ambientColor   = new Color(0.04f, 0.04f, 0.12f),
            lightColor     = new Color(0.28f, 0.30f, 0.55f),
            lightIntensity = 0.08f,
            fogColor       = new Color(0.03f, 0.04f, 0.10f),
        };

        private TimeOfDayEvent.Phase _currentPhase = TimeOfDayEvent.Phase.Morning;

        private void Start()
        {
            if (_sun == null)
                _sun = FindFirstObjectByType<Light>();

            ApplyImmediate(_morning);
            StartCoroutine(CycleRoutine());
        }

        // ── メインサイクル ────────────────────────────────────────────

        private IEnumerator CycleRoutine()
        {
            while (true)
            {
                yield return new WaitForSeconds(_stayDuration);
                var next = NextPhase(_currentPhase);
                yield return StartCoroutine(TransitionTo(GetSettings(next)));
                _currentPhase = next;
                EventBus.Publish(new TimeOfDayEvent(_currentPhase));
            }
        }

        // ── 遷移コルーチン ────────────────────────────────────────────

        private IEnumerator TransitionTo(TimeSettings to)
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
            if (_sun != null)
            {
                _sun.color     = Color.Lerp(from.lightColor, to.lightColor, t);
                _sun.intensity = Mathf.Lerp(from.lightIntensity, to.lightIntensity, t);
            }
        }

        private TimeSettings GetCurrentLiveSettings()
        {
            return new TimeSettings
            {
                ambientColor   = RenderSettings.ambientLight,
                lightColor     = _sun != null ? _sun.color     : Color.white,
                lightIntensity = _sun != null ? _sun.intensity : 1f,
                fogColor       = RenderSettings.fogColor,
            };
        }

        // ── ヘルパー ────────────────────────────────────────────────

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
