// 役割: Settingsパネルの表示・非表示とスライドアニメーションを担当するコントローラー。
//       見た目は生成せず、シーン上に配置済みのUIを操作するだけ。
//       ESCキーの監視は行わない（PauseMenuControllerが一元管理し、このクラスのCloseを呼ぶ）。

using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using ElfVillage.Core;

namespace ElfVillage.UI
{
    public class SettingsPanelController : MonoBehaviour
    {
        [Header("参照")]
        [SerializeField] private PauseMenuController pauseMenuController;

        [Header("Settings UI（シーン上に配置済みのものを割り当てる）")]
        [Tooltip("FullScreenBlocker + SettingsPanel の親。表示/非表示の切り替え対象")]
        [SerializeField] private GameObject    settingsRoot;
        [SerializeField] private RectTransform settingsPanelRect;
        [SerializeField] private Button        backButton;
        [SerializeField] private Dropdown      bgmDropdown;
        [SerializeField] private Slider        masterVolumeSlider;
        [SerializeField] private Slider        bgmVolumeSlider;
        [SerializeField] private Slider        seVolumeSlider;
        [SerializeField] private Slider        ambientVolumeSlider;

        [Header("アニメーション")]
        [SerializeField] private float slideDuration = 0.25f;
        [Tooltip("非表示時にパネルをどれだけ右へ隠すか（表示位置からのオフセット）")]
        [SerializeField] private float hiddenOffsetX = 420f;

        private Vector2   _shownPos;
        private Coroutine _slideCoroutine;

        private void Awake()
        {
            if (settingsPanelRect != null) _shownPos = settingsPanelRect.anchoredPosition;
            if (backButton != null) backButton.onClick.AddListener(Close);
            if (bgmDropdown != null) bgmDropdown.onValueChanged.AddListener(OnBGMTrackChanged);

            if (masterVolumeSlider  != null) masterVolumeSlider.onValueChanged.AddListener(v => AudioManager.Instance?.SetMasterVolume(v));
            if (bgmVolumeSlider     != null) bgmVolumeSlider.onValueChanged.AddListener(v => AudioManager.Instance?.SetBGMVolume(v));
            if (seVolumeSlider      != null) seVolumeSlider.onValueChanged.AddListener(v => AudioManager.Instance?.SetSEVolume(v));
            if (ambientVolumeSlider != null) ambientVolumeSlider.onValueChanged.AddListener(v => AudioManager.Instance?.SetAmbientVolume(v));
        }

        private void OnBGMTrackChanged(int index)
        {
            AudioManager.Instance?.PlayBGMTrack(index);
        }

        // パネルを開くたびに、AudioManagerが持つ実際の現在音量をスライダーへ反映する
        // （SetValueWithoutNotifyでSet*Volumeへの再送信ループを防ぐ）。
        private void SyncVolumeSliders()
        {
            if (AudioManager.Instance == null) return;
            masterVolumeSlider?.SetValueWithoutNotify(AudioManager.Instance.GetVolume(AudioChannel.Master));
            bgmVolumeSlider?.SetValueWithoutNotify(AudioManager.Instance.GetVolume(AudioChannel.BGM));
            seVolumeSlider?.SetValueWithoutNotify(AudioManager.Instance.GetVolume(AudioChannel.SE));
            ambientVolumeSlider?.SetValueWithoutNotify(AudioManager.Instance.GetVolume(AudioChannel.Ambient));
        }

        private void Start()
        {
            if (settingsPanelRect != null)
                settingsPanelRect.anchoredPosition = _shownPos + new Vector2(hiddenOffsetX, 0f);
            if (settingsRoot != null) settingsRoot.SetActive(false);
        }

        public void Open()
        {
            GameInteractionStateController.SetState(GameInteractionState.Settings);
            if (settingsRoot != null) settingsRoot.SetActive(true);
            SyncVolumeSliders();
            StartSlide(_shownPos, deactivateOnEnd: false);
        }

        public void Close()
        {
            pauseMenuController?.ReturnFromSettings();
            StartSlide(_shownPos + new Vector2(hiddenOffsetX, 0f), deactivateOnEnd: true);
        }

        private void StartSlide(Vector2 target, bool deactivateOnEnd)
        {
            if (settingsPanelRect == null) return;
            if (_slideCoroutine != null) StopCoroutine(_slideCoroutine);
            _slideCoroutine = StartCoroutine(SlideRoutine(settingsPanelRect, target, slideDuration, deactivateOnEnd ? settingsRoot : null));
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
    }
}
