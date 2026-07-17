// 役割: BGM・効果音・環境音・UI音を一元管理するオーディオ基盤（Singleton）。
//       AudioMixerの音量制御APIを提供する。UI側は0〜1の線形値のみを扱い、
//       dB変換はこのクラス内部で完結させる。再生API（PlayBGM等）は
//       将来の音源追加に備えた骨組みのみで、今回は未実装。

using UnityEngine;
using UnityEngine.Audio;

namespace ElfVillage.Core
{
    public enum AudioChannel
    {
        Master,
        BGM,
        SE,
        Ambient,
        UI,
    }

    [System.Serializable]
    public class AudioChannelVolume
    {
        public AudioChannel channel;
        [Range(0f, 1f)] public float initialVolume = 1f;
    }

    public class AudioManager : MonoBehaviour
    {
        public static AudioManager Instance { get; private set; }

        [SerializeField] private AudioMixer audioMixer;

        [Header("チャンネルごとの初期音量（0〜1）")]
        [SerializeField] private AudioChannelVolume[] initialVolumes =
        {
            new AudioChannelVolume { channel = AudioChannel.Master,  initialVolume = 1.0f },
            new AudioChannelVolume { channel = AudioChannel.BGM,     initialVolume = 0.7f },
            new AudioChannelVolume { channel = AudioChannel.SE,      initialVolume = 0.8f },
            new AudioChannelVolume { channel = AudioChannel.Ambient, initialVolume = 0.6f },
            new AudioChannelVolume { channel = AudioChannel.UI,      initialVolume = 0.8f },
        };

        private const float MinDecibel = -80f;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        // AudioMixer.SetFloat は Awake() 時点ではオーディオDSPグラフの初期化が
        // 間に合わず反映されないことがあるため、Start() で初期音量を適用する。
        private void Start()
        {
            ApplyInitialVolumes();
        }

        private void ApplyInitialVolumes()
        {
            if (initialVolumes == null) return;
            foreach (var entry in initialVolumes)
                SetVolume(entry.channel, entry.initialVolume);
        }

        // ── 音量制御 ─────────────────────────────────────────────────
        // UI側はSliderの0〜1をそのまま渡すだけでよい。dB変換はここで完結する。

        public void SetVolume(AudioChannel channel, float linear01)
        {
            if (audioMixer == null) return;
            float dB = LinearToDecibel(Mathf.Clamp01(linear01));
            audioMixer.SetFloat(ParamName(channel), dB);
        }

        public float GetVolume(AudioChannel channel)
        {
            if (audioMixer == null) return 1f;
            return audioMixer.GetFloat(ParamName(channel), out float dB) ? DecibelToLinear(dB) : 1f;
        }

        // Slider.onValueChanged は (float) 単体の関数しか直接バインドできないため、
        // Settings画面のスライダーからそのまま呼べるチャンネル別の薄いラッパーも用意する。
        public void SetMasterVolume(float linear01)  => SetVolume(AudioChannel.Master, linear01);
        public void SetBGMVolume(float linear01)     => SetVolume(AudioChannel.BGM, linear01);
        public void SetSEVolume(float linear01)      => SetVolume(AudioChannel.SE, linear01);
        public void SetAmbientVolume(float linear01) => SetVolume(AudioChannel.Ambient, linear01);
        public void SetUIVolume(float linear01)      => SetVolume(AudioChannel.UI, linear01);

        // ── 再生API（今回は未実装。音源追加後に実装する） ───────────────
        // 呼ばれても無音のまま気づけないと不具合の温床になるため、未実装であることを
        // Warningで明示する。実際の再生処理を実装したらこのWarningは削除すること。

        public void PlayBGM(AudioClip clip)
        {
            Debug.LogWarning("PlayBGM is not implemented yet.");
        }

        public void PlaySE(AudioClip clip)
        {
            Debug.LogWarning("PlaySE is not implemented yet.");
        }

        public void PlayAmbient(AudioClip clip)
        {
            Debug.LogWarning("PlayAmbient is not implemented yet.");
        }

        public void PlayUI(AudioClip clip)
        {
            Debug.LogWarning("PlayUI is not implemented yet.");
        }

        // ── 内部ヘルパー ─────────────────────────────────────────────

        private static string ParamName(AudioChannel channel)
        {
            switch (channel)
            {
                case AudioChannel.Master:  return "MasterVolume";
                case AudioChannel.BGM:     return "BGMVolume";
                case AudioChannel.SE:      return "SEVolume";
                case AudioChannel.Ambient: return "AmbientVolume";
                case AudioChannel.UI:      return "UIVolume";
                default:                   return "MasterVolume";
            }
        }

        private static float LinearToDecibel(float linear)
        {
            return linear <= 0.0001f ? MinDecibel : Mathf.Log10(linear) * 20f;
        }

        private static float DecibelToLinear(float dB)
        {
            return Mathf.Pow(10f, dB / 20f);
        }
    }
}
