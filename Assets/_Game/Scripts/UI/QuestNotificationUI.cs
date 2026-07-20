// 役割: クエスト開始・達成を画面上部に一時的なトースト通知として表示する、表示専用のUIコンポーネント。
//       QuestManagerを直接参照せず、EventBus経由のQuestStartedEvent/QuestCompletedEventだけを購読する。
//       QuestPanelUI（常時表示HUD）とは完全に独立しており、互いに参照しない。
//       QuestProgressChangedEventは通知の対象外（QuestPanelUIの数値更新だけで十分なため、Stage 3の方針）。
//       表示中に別の通知が来た場合は、進行中のアニメーションを中断して新しい内容へ上書きする。
//       DOTween等は使わず、Unity標準のコルーチン + CanvasGroup/RectTransformだけでスライド・フェードを行う。
//       このGameObject自体は常時アクティブのままにし、可視状態はCanvasGroup.alphaと位置で制御する
//       （コルーチンを動かし続けるため、SetActive(false)では表現しない）。
//       経過時間→表示状態の変換はComputeFrame()に純粋関数として切り出してある。コルーチンは
//       StartCoroutineを介するためPlay Mode外では進行しないが、ComputeFrame自体はEditModeからも
//       直接検証できる（FlowerPetalSystem.CalcCountMultiplierと同じ設計方針）。

using System.Collections;
using UnityEngine;
using TMPro;
using ElfVillage.Core;
using ElfVillage.Quest;

namespace ElfVillage.UI
{
    public class QuestNotificationUI : MonoBehaviour
    {
        [SerializeField] private RectTransform _root;
        [SerializeField] private CanvasGroup   _canvasGroup;
        [SerializeField] private TMP_Text      _headerText;
        [SerializeField] private TMP_Text      _titleText;
        [SerializeField] private TMP_Text      _progressText;

        [SerializeField] private float _startedDisplayDuration   = 3f;
        [SerializeField] private float _completedDisplayDuration = 4f;
        [SerializeField] private float _slideInDuration = 0.4f;
        [SerializeField] private float _fadeOutDuration  = 0.6f;
        [SerializeField] private float _hiddenOffsetY    = 80f;

        private float     _restY;
        private bool      _restYCaptured;
        private Coroutine _routine;

        public readonly struct Frame
        {
            public readonly float PositionOffsetY; // 0 = 表示位置, hiddenOffsetY = 完全に隠れた位置
            public readonly float Alpha;
            public readonly bool  Finished;

            public Frame(float positionOffsetY, float alpha, bool finished)
            {
                PositionOffsetY = positionOffsetY;
                Alpha = alpha;
                Finished = finished;
            }
        }

        // 経過時間から見た目の状態を求める純粋関数（副作用なし、EditModeから直接テスト可能）。
        public static Frame ComputeFrame(float elapsed, float totalDuration, float slideInDuration, float fadeOutDuration, float hiddenOffsetY)
        {
            float slideIn = Mathf.Min(slideInDuration, totalDuration * 0.5f);
            float fadeOut = Mathf.Min(fadeOutDuration, totalDuration * 0.5f);

            if (elapsed >= totalDuration)
                return new Frame(hiddenOffsetY, 0f, true);

            if (elapsed < slideIn)
            {
                float p = slideIn > 0f ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / slideIn)) : 1f;
                return new Frame(Mathf.LerpUnclamped(hiddenOffsetY, 0f, p), p, false);
            }

            float fadeStart = totalDuration - fadeOut;
            if (elapsed < fadeStart)
                return new Frame(0f, 1f, false);

            float fp = fadeOut > 0f ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((elapsed - fadeStart) / fadeOut)) : 1f;
            return new Frame(Mathf.LerpUnclamped(0f, hiddenOffsetY, fp), 1f - fp, false);
        }

        private void OnEnable()
        {
            CaptureRestY();
            ApplyFrame(ComputeFrame(0f, 1f, _slideInDuration, _fadeOutDuration, _hiddenOffsetY));

            EventBus.Subscribe<QuestStartedEvent>(OnQuestStarted);
            EventBus.Subscribe<QuestCompletedEvent>(OnQuestCompleted);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<QuestStartedEvent>(OnQuestStarted);
            EventBus.Unsubscribe<QuestCompletedEvent>(OnQuestCompleted);

            if (_routine != null)
            {
                StopCoroutine(_routine);
                _routine = null;
            }
        }

        // ── イベントハンドラ ──────────────────────────────────────────

        private void OnQuestStarted(QuestStartedEvent evt)
        {
            SetHeader("🌲 新しいクエスト");
            SetTitle(evt.Quest.title);
            SetProgress($"0 / {evt.Quest.targetCount}", true);
            Show(_startedDisplayDuration);
        }

        private void OnQuestCompleted(QuestCompletedEvent evt)
        {
            SetHeader("✨ Quest Complete!");
            SetTitle(evt.Quest.title);
            SetProgress(string.Empty, false);
            Show(_completedDisplayDuration);
        }

        // ── 表示更新 ──────────────────────────────────────────────────

        private void SetHeader(string text)
        {
            if (_headerText != null) _headerText.text = text;
        }

        private void SetTitle(string text)
        {
            if (_titleText != null) _titleText.text = text;
        }

        private void SetProgress(string text, bool visible)
        {
            if (_progressText == null) return;
            _progressText.text = text;
            _progressText.gameObject.SetActive(visible);
        }

        // ── アニメーション ────────────────────────────────────────────

        private void CaptureRestY()
        {
            if (_restYCaptured || _root == null) return;
            _restY = _root.anchoredPosition.y;
            _restYCaptured = true;
        }

        private void Show(float totalDuration)
        {
            if (_routine != null) StopCoroutine(_routine);
            _routine = StartCoroutine(PlayRoutine(totalDuration));
        }

        private IEnumerator PlayRoutine(float totalDuration)
        {
            float elapsed = 0f;
            while (true)
            {
                var frame = ComputeFrame(elapsed, totalDuration, _slideInDuration, _fadeOutDuration, _hiddenOffsetY);
                ApplyFrame(frame);
                if (frame.Finished) break;

                yield return null;
                elapsed += Time.unscaledDeltaTime;
            }

            _routine = null;
        }

        private void ApplyFrame(Frame frame)
        {
            SetPositionY(_restY + frame.PositionOffsetY);
            if (_canvasGroup != null) _canvasGroup.alpha = frame.Alpha;
        }

        private void SetPositionY(float y)
        {
            if (_root == null) return;
            var pos = _root.anchoredPosition;
            pos.y = y;
            _root.anchoredPosition = pos;
        }
    }
}
