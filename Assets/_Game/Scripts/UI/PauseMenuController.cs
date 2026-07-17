// 役割: ESCキー監視とPlaying/PauseMenu/Settingsの状態遷移を一元管理するコントローラー。
//       見た目は一切生成せず、あらかじめシーン上に配置されたUI（RectTransform・Button等）を
//       表示/非表示・スライドアニメーションさせるだけに責務を絞る。

using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using ElfVillage.Core;

namespace ElfVillage.UI
{
    public class PauseMenuController : MonoBehaviour
    {
        [Header("参照")]
        [SerializeField] private SettingsPanelController settingsPanel;

        [Header("Pause Menu UI（シーン上に配置済みのものを割り当てる）")]
        [SerializeField] private GameObject    pauseMenuRoot;
        [SerializeField] private RectTransform leftPanelRect;
        [SerializeField] private Button        resumeButton;
        [SerializeField] private Button        settingsButton;
        [SerializeField] private Button        saveButton;
        [SerializeField] private Button        quitButton;

        [Header("アニメーション")]
        [SerializeField] private float slideDuration = 0.25f;
        [Tooltip("非表示時にパネルをどれだけ左へ隠すか（表示位置からのオフセット）")]
        [SerializeField] private float hiddenOffsetX = -420f;

        private Vector2   _shownPos;
        private Coroutine _slideCoroutine;

        private void Awake()
        {
            if (leftPanelRect != null) _shownPos = leftPanelRect.anchoredPosition;

            if (resumeButton   != null) resumeButton.onClick.AddListener(OnResumeClicked);
            if (settingsButton != null) settingsButton.onClick.AddListener(OnSettingsClicked);
            if (saveButton     != null) saveButton.onClick.AddListener(OnSaveClicked);
            if (quitButton     != null) quitButton.onClick.AddListener(OnQuitClicked);
        }

        private void Start()
        {
            // 初期状態は非表示（パネルを隠し位置へ置いてからルートを非アクティブにする）
            if (leftPanelRect != null)
                leftPanelRect.anchoredPosition = _shownPos + new Vector2(hiddenOffsetX, 0f);
            if (pauseMenuRoot != null) pauseMenuRoot.SetActive(false);
        }

        private void Update()
        {
            if (Keyboard.current == null) return;
            if (!Keyboard.current.escapeKey.wasPressedThisFrame) return;

            // ESCの状態遷移はここで一元管理する（PauseMenu/Settingsそれぞれで
            // 個別にESCを監視すると、同一フレームで二重に遷移する恐れがあるため）。
            switch (GameInteractionStateController.Current)
            {
                case GameInteractionState.Playing:
                    Open();
                    break;
                case GameInteractionState.PauseMenu:
                    Close();
                    break;
                case GameInteractionState.Settings:
                    settingsPanel?.Close();
                    break;
            }
        }

        private void Open()
        {
            GameInteractionStateController.SetState(GameInteractionState.PauseMenu);
            if (pauseMenuRoot != null) pauseMenuRoot.SetActive(true);
            StartSlide(_shownPos, deactivateOnEnd: false);
        }

        private void Close()
        {
            GameInteractionStateController.SetState(GameInteractionState.Playing);
            StartSlide(_shownPos + new Vector2(hiddenOffsetX, 0f), deactivateOnEnd: true);
        }

        /// <summary>SettingsPanelController から「戻る」で呼ばれる。パネル自体は表示したまま状態だけ戻す。</summary>
        public void ReturnFromSettings()
        {
            GameInteractionStateController.SetState(GameInteractionState.PauseMenu);
        }

        private void StartSlide(Vector2 target, bool deactivateOnEnd)
        {
            if (leftPanelRect == null) return;
            if (_slideCoroutine != null) StopCoroutine(_slideCoroutine);
            _slideCoroutine = StartCoroutine(SlideRoutine(leftPanelRect, target, slideDuration, deactivateOnEnd ? pauseMenuRoot : null));
        }

        private static IEnumerator SlideRoutine(RectTransform rect, Vector2 target, float duration, GameObject deactivateOnEnd)
        {
            Vector2 start = rect.anchoredPosition;
            float   t     = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                float u = Mathf.Clamp01(t / duration);
                u = 1f - Mathf.Pow(1f - u, 3f); // ease-out
                rect.anchoredPosition = Vector2.Lerp(start, target, u);
                yield return null;
            }
            rect.anchoredPosition = target;
            if (deactivateOnEnd != null) deactivateOnEnd.SetActive(false);
        }

        // ── ボタン ─────────────────────────────────────────────────

        private void OnResumeClicked() => Close();

        private void OnSettingsClicked() => settingsPanel?.Open();

        private void OnSaveClicked()
        {
            Debug.Log("Save feature is not implemented yet.");
        }

        private void OnQuitClicked()
        {
#if UNITY_EDITOR
            Debug.Log("Quit requested (Editor: not exiting).");
#else
            Application.Quit();
#endif
        }
    }
}
