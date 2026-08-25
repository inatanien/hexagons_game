// 役割: TimeOfDayEvent を購読し、家の窓の灯りを時間帯に応じて明滅させる。
//       家は全軒が1枚のマテリアルを共有しているため、共有マテリアルの _EmissionColor を
//       1回書き換えるだけで盤面上のすべての家に反映される（ドローコールは増えない）。

using System.Collections;
using UnityEngine;
using ElfVillage.Core;

namespace ElfVillage.Tiles
{
    public class HouseWindowLight : MonoBehaviour
    {
        [Header("対象")]
        [Tooltip("家が共有しているマテリアル（HouseFlat）")]
        [SerializeField] private Material _houseMaterial;

        [Header("灯りの色と強さ")]
        [SerializeField] private Color _lightColor = new Color(1.00f, 0.82f, 0.45f);
        [SerializeField] private float _nightIntensity   = 2.2f;
        [SerializeField] private float _eveningIntensity = 1.1f;
        [SerializeField] private float _dayIntensity     = 0f;

        [Header("切り替え")]
        [Tooltip("時間帯が変わってから灯りが変化しきるまでの秒数")]
        [SerializeField] private float _fadeDuration = 2.5f;

        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        private float _current;
        private Coroutine _fade;
        // 共有マテリアルを書き換えるため、終了時に元へ戻せるよう控えておく
        private Color _originalEmission;
        private bool _hasOriginal;

        private void Awake()
        {
            if (_houseMaterial == null)
            {
                Debug.LogWarning("[HouseWindowLight] マテリアルが未設定のため灯りは動作しません。", this);
                return;
            }
            _originalEmission = _houseMaterial.GetColor(EmissionColorId);
            _hasOriginal = true;
            _current = _dayIntensity;
            Apply(_current);
        }

        private void OnEnable()
        {
            EventBus.Subscribe<TimeOfDayEvent>(OnTimeOfDay);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<TimeOfDayEvent>(OnTimeOfDay);
            if (_fade != null) { StopCoroutine(_fade); _fade = null; }
            // アセットを書き換えたままにしないよう、必ず元の値へ戻す
            if (_hasOriginal && _houseMaterial != null)
                _houseMaterial.SetColor(EmissionColorId, _originalEmission);
        }

        private void OnTimeOfDay(TimeOfDayEvent evt)
        {
            if (_houseMaterial == null) return;

            float target = TargetFor(evt.Current);
            if (Mathf.Approximately(target, _current)) return;

            if (_fade != null) StopCoroutine(_fade);
            _fade = StartCoroutine(FadeTo(target));
        }

        private float TargetFor(TimeOfDayEvent.Phase phase)
        {
            switch (phase)
            {
                case TimeOfDayEvent.Phase.Night:   return _nightIntensity;
                case TimeOfDayEvent.Phase.Evening: return _eveningIntensity;
                default:                           return _dayIntensity;
            }
        }

        private IEnumerator FadeTo(float target)
        {
            float from = _current;
            float elapsed = 0f;
            while (elapsed < _fadeDuration)
            {
                elapsed += Time.deltaTime;
                // 急に点かず、じわりと灯るほうが落ち着いて見える
                float t = Mathf.SmoothStep(0f, 1f, elapsed / _fadeDuration);
                Apply(Mathf.Lerp(from, target, t));
                yield return null;
            }
            Apply(target);
            _fade = null;
        }

        private void Apply(float intensity)
        {
            _current = intensity;
            _houseMaterial.SetColor(EmissionColorId, _lightColor * intensity);
        }
    }
}
