// 役割: アクティブな1つのクエストの進捗を管理する（Stage 1〜2）。
//       TerrainClusterProgressEvent（Core）だけを購読し、Tiles固有の型には一切依存しない。
//       達成後の次クエストへの切り替え・キュー管理・報酬・演出はまだこのクラスの責務ではない。
//       QuestStartedEventはOnEnableではなくStartで発行する。Unityは「シーン読み込み時に
//       存在する全オブジェクトのOnEnableが完了してから、初めてどれかのStartが呼ばれる」ことを
//       保証しているため、QuestPanelUI（OnEnableで購読）がQuestManagerより後にOnEnableされても、
//       QuestStartedEventの発行（Start）より必ず前に購読が完了している。Script Execution Order
//       には一切依存しない（Session 14）。
//       この仕組みはQuestManager/QuestPanelUIがシーン開始時から常駐し無効化されないことを前提とする。
//       動的生成・再有効化に対応する再通知機構はStage 2では導入しない（将来必要になれば別途検討）。

using UnityEngine;
using ElfVillage.Core;

namespace ElfVillage.Quest
{
    public class QuestManager : MonoBehaviour
    {
        [SerializeField] private QuestDefinition _activeQuest;

        private int  _currentCount;
        private bool _isCompleted;
        private bool _subscribed;
        private bool _started;

        private void OnEnable()
        {
            if (_activeQuest == null)
            {
                Debug.LogWarning("[QuestManager] _activeQuestが未設定のため開始しません。", this);
                return;
            }

            // targetCountが0以下は不正値として扱い、クエストを開始しない（購読もStarted発行も行わない）。
            if (_activeQuest.targetCount <= 0)
            {
                Debug.LogWarning(
                    $"[QuestManager] {_activeQuest.name} のtargetCountが{_activeQuest.targetCount}のため開始しません。" +
                    "targetCountは1以上を設定してください。", this);
                return;
            }

            EventBus.Subscribe<TerrainClusterProgressEvent>(OnProgress);
            _subscribed   = true;
            _currentCount = 0;
            _isCompleted  = false;
        }

        private void Start()
        {
            // OnEnableで無効判定された場合（_activeQuest未設定・targetCount<=0）はここでも何もしない。
            if (!_subscribed) return;
            // 通常のUnityライフサイクルではStartは1回しか呼ばれないが、念のため多重発行を防ぐ。
            if (_started) return;
            _started = true;

            EventBus.Publish(new QuestStartedEvent(_activeQuest));
        }

        private void OnDisable()
        {
            if (!_subscribed) return;
            EventBus.Unsubscribe<TerrainClusterProgressEvent>(OnProgress);
            _subscribed = false;
        }

        private void OnProgress(TerrainClusterProgressEvent evt)
        {
            if (_isCompleted) return;
            if (evt.Category != _activeQuest.targetCategory) return;

            int clamped = Mathf.Clamp(evt.ClusterSize, 0, _activeQuest.targetCount);
            if (clamped == _currentCount) return;

            _currentCount = clamped;
            EventBus.Publish(new QuestProgressChangedEvent(_activeQuest, _currentCount));

            if (_currentCount >= _activeQuest.targetCount)
            {
                _isCompleted = true;
                Debug.Log($"[QuestManager] クエスト達成: {_activeQuest.title}（{_currentCount}/{_activeQuest.targetCount}）");
                EventBus.Publish(new QuestCompletedEvent(_activeQuest));
            }
        }
    }
}
