// 役割: 世界の出来事（精霊の誕生・成長など）を画面へ短く表示するトースト通知。
//       WorldNoticeEvent だけを購読する表示専用コンポーネント。
//
//       ★QuestNotificationUIとは別枠にしている理由
//         既存のクエスト通知はキューを持たず、表示中に新しい通知が来ると上書きする。
//         同じ枠を共有すると、クエスト達成と精霊の誕生が重なったときに
//         どちらかが一瞬で消えてしまう。別のRectTransformへ分けて共存させる。
//
//       ★依存方向
//         発行元（Spirits等）を一切知らない。判定に使うのはCoreのInteractionTimePolicyだけで、
//         UIからSpiritsへの参照は作らない（asmdefの依存方向を維持するため）。
//
//       ★時間の進め方
//         コルーチンではなくUpdateで進める。Settings中に「通知だけ進んで消える」不整合を
//         避けるため、毎フレーム操作状態を見て進行可否を判断する必要があるため。
//         スライド・フェードの計算は QuestNotificationUI.ComputeFrame を再利用する
//         （同一アセンブリの純粋関数。EditModeで検証済みのものを重複実装しない）。

using System.Collections.Generic;
using UnityEngine;
using TMPro;
using ElfVillage.Core;

namespace ElfVillage.UI
{
    public class WorldNoticeUI : MonoBehaviour
    {
        [SerializeField] private RectTransform _root;
        [SerializeField] private CanvasGroup   _canvasGroup;
        [SerializeField] private TMP_Text      _headerText;
        [SerializeField] private TMP_Text      _bodyText;

        [Header("表示")]
        [Tooltip("イベント側が不正な秒数を渡してきたときに使う既定の表示時間")]
        [SerializeField] private float _defaultDisplayDuration = 3f;
        [SerializeField] private float _slideInDuration = 0.35f;
        [SerializeField] private float _fadeOutDuration = 0.6f;
        [SerializeField] private float _hiddenOffsetY   = 80f;

        [Header("キュー")]
        [Tooltip("待機できる通知の最大数。超えた場合は最も古い待機通知を捨てる")]
        [SerializeField] private int _maxQueued = 3;

        /// <summary>待機中の通知1件ぶん。Unity Object参照を持たない値だけの構造。</summary>
        private readonly struct Notice
        {
            public readonly string Header;
            public readonly string Body;
            public readonly float  Duration;

            public Notice(string header, string body, float duration)
            {
                Header   = header;
                Body     = body;
                Duration = duration;
            }
        }

        private readonly Queue<Notice> _queue = new();

        private bool  _showing;
        private float _elapsed;
        private float _currentDuration;

        private float _restY;
        private bool  _restYCaptured;

        private System.Action<WorldNoticeEvent> _handler;

        /// <summary>
        /// 表示秒数の健全化。0以下・NaN・Infinityは既定値へ倒す。
        /// 純粋関数なのでEditModeから直接検証できる。
        /// </summary>
        public static float SafeDuration(float requested, float fallback)
        {
            float safeFallback = (float.IsFinite(fallback) && fallback > 0f) ? fallback : 3f;
            return (float.IsFinite(requested) && requested > 0f) ? requested : safeFallback;
        }

        /// <summary>
        /// キューへ積むべきか（上限超過時は最も古い待機通知を捨てる）。
        /// 「今から表示するもの」より「これから来るもの」を優先するのは、
        /// 最新の出来事の方がプレイヤーにとって意味があるため。
        /// </summary>
        public static int SafeMaxQueued(int requested)
            => Mathf.Clamp(requested, 1, 16);

        private void OnEnable()
        {
            CaptureRestY();
            ApplyHidden();

            // Subscribe/Unsubscribeで同じデリゲート実体を使うため変数に保持する。
            _handler = OnWorldNotice;
            EventBus.Subscribe(_handler);
        }

        private void OnDisable()
        {
            if (_handler != null)
            {
                EventBus.Unsubscribe(_handler);
                _handler = null;
            }

            // 破棄時に待機中の通知を残さない（次に有効化されたとき古い通知が出ないように）。
            _queue.Clear();
            _showing = false;
            _elapsed = 0f;
            ApplyHidden();
        }

        private void OnWorldNotice(WorldNoticeEvent evt)
        {
            if (evt == null) return;

            int max = SafeMaxQueued(_maxQueued);
            while (_queue.Count >= max) _queue.Dequeue();   // 最も古い待機通知を捨てる

            _queue.Enqueue(new Notice(
                evt.Header, evt.Body, SafeDuration(evt.DisplayDuration, _defaultDisplayDuration)));
        }

        private void Update()
        {
            // ★Settings中は表示時間もキュー処理も止める。
            //   精霊の演出が止まっているのに通知だけ消えていく不整合を避ける。
            //   PauseMenu中は既存Critterと同じく進める。
            if (!InteractionTimePolicy.ShouldAdvanceNow()) return;

            if (!_showing)
            {
                if (_queue.Count == 0) return;
                BeginShowing(_queue.Dequeue());
            }

            _elapsed += Time.deltaTime;

            var frame = QuestNotificationUI.ComputeFrame(
                _elapsed, _currentDuration, _slideInDuration, _fadeOutDuration, _hiddenOffsetY);
            ApplyFrame(frame);

            if (frame.Finished)
            {
                _showing = false;
                _elapsed = 0f;
            }
        }

        private void BeginShowing(Notice notice)
        {
            if (_headerText != null) _headerText.text = notice.Header;
            if (_bodyText   != null) _bodyText.text   = notice.Body;

            _currentDuration = notice.Duration;
            _elapsed = 0f;
            _showing = true;
        }

        // ── 表示 ──────────────────────────────────────────────────────

        private void CaptureRestY()
        {
            if (_restYCaptured || _root == null) return;
            _restY = _root.anchoredPosition.y;
            _restYCaptured = true;
        }

        private void ApplyHidden()
            => ApplyFrame(new QuestNotificationUI.Frame(_hiddenOffsetY, 0f, true));

        private void ApplyFrame(QuestNotificationUI.Frame frame)
        {
            if (_root != null)
            {
                var pos = _root.anchoredPosition;
                pos.y = _restY + frame.PositionOffsetY;
                _root.anchoredPosition = pos;
            }
            if (_canvasGroup != null) _canvasGroup.alpha = frame.Alpha;
        }
    }
}
