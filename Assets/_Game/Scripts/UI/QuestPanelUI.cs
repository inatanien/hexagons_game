// 役割: 現在アクティブなクエストのタイトルと進捗を画面表示する、表示専用のUIコンポーネント。
//       QuestManagerを直接参照せず、EventBus経由のQuestStartedEvent/QuestProgressChangedEvent/
//       QuestCompletedEventだけを購読する。入力処理は一切行わない（クリック等のハンドラなし。
//       Raycast Targetは各UI要素側で無効化し、タイル配置・カメラ操作を妨げない）。
//       QuestManager/QuestPanelUIともシーン開始時から常駐し無効化されないことを前提とする
//       （Stage 2時点。動的生成・再有効化への対応は将来のStageで検討する）。

using UnityEngine;
using TMPro;
using ElfVillage.Core;
using ElfVillage.Quest;

namespace ElfVillage.UI
{
    public class QuestPanelUI : MonoBehaviour
    {
        [SerializeField] private GameObject _panelRoot;
        [SerializeField] private TMP_Text   _titleText;
        [SerializeField] private TMP_Text   _progressText;
        [SerializeField] private TMP_Text   _completedText;

        private void OnEnable()
        {
            EventBus.Subscribe<QuestStartedEvent>(OnQuestStarted);
            EventBus.Subscribe<QuestProgressChangedEvent>(OnQuestProgressChanged);
            EventBus.Subscribe<QuestCompletedEvent>(OnQuestCompleted);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<QuestStartedEvent>(OnQuestStarted);
            EventBus.Unsubscribe<QuestProgressChangedEvent>(OnQuestProgressChanged);
            EventBus.Unsubscribe<QuestCompletedEvent>(OnQuestCompleted);
        }

        // ── イベントハンドラ ──────────────────────────────────────────

        private void OnQuestStarted(QuestStartedEvent evt)
        {
            if (_panelRoot != null) _panelRoot.SetActive(true);
            SetTitle(evt.Quest.title);
            SetProgress(0, evt.Quest.targetCount);
            SetCompleted(false);
        }

        private void OnQuestProgressChanged(QuestProgressChangedEvent evt)
        {
            SetTitle(evt.Quest.title);
            SetProgress(evt.CurrentCount, evt.Quest.targetCount);
        }

        private void OnQuestCompleted(QuestCompletedEvent evt)
        {
            SetTitle(evt.Quest.title);
            SetProgress(evt.Quest.targetCount, evt.Quest.targetCount);
            SetCompleted(true);
        }

        // ── 表示更新（Unityオブジェクトへの依存を最小化し、EditModeから直接検証できるようにする） ──

        private void SetTitle(string title)
        {
            if (_titleText != null) _titleText.text = title;
        }

        private void SetProgress(int current, int target)
        {
            if (_progressText != null) _progressText.text = $"{current} / {target}";
        }

        private void SetCompleted(bool completed)
        {
            if (_completedText != null) _completedText.gameObject.SetActive(completed);
        }
    }
}
