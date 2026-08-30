// 役割: アクティブな1つのクエストの進捗を管理する。
//       QuestDefinition.condition（QuestCondition）が示す種別に従って進捗を更新するだけで、
//       questIdごとの分岐は持たない。クエストを増やしてもこのクラスは変わらない。
//       購読するのはCoreのイベントだけで、Tiles固有の型には一切依存しない
//       （森・川のクラスター判定はTiles側の評価システムが行い、こちらは結果を観測するだけ）。
//       達成後の次クエストへの切り替え・キュー管理・報酬・演出はまだこのクラスの責務ではない。
//
//       ライフサイクル:
//         OnEnable ... クエストの妥当性を検証し、有効なときだけ進捗イベントを購読する
//                      （無効なデータが一瞬でも購読状態になるのを避けるため、検証を遅らせない）
//         Start    ... 有効なときだけQuestStartedEventを発行する
//         OnDisable... 実際に購読していたときだけ解除する
//
//       QuestStartedEventはOnEnableではなくStartで発行する。Unityは「シーン読み込み時に
//       存在する全オブジェクトのOnEnableが完了してから、初めてどれかのStartが呼ばれる」ことを
//       保証しているため、QuestPanelUI（OnEnableで購読）がQuestManagerより後にOnEnableされても、
//       QuestStartedEventの発行（Start）より必ず前に購読が完了している。Script Execution Order
//       には一切依存しない（Session 14）。
//       この仕組みはQuestManager/QuestPanelUIがシーン開始時から常駐し無効化されないことを前提とする。
//       動的生成・再有効化に対応する再通知機構はまだ導入しない（将来必要になれば別途検討）。

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

        /// <summary>有効なクエストの条件。_subscribedがtrueのときだけ意味を持つ。</summary>
        private QuestCondition Condition => _activeQuest.condition;

        private void OnEnable()
        {
            if (!IsQuestValid()) return;

            EventBus.Subscribe<TerrainClusterProgressEvent>(OnClusterProgress);
            _subscribed   = true;
            _currentCount = 0;
            _isCompleted  = false;
        }

        private void Start()
        {
            // OnEnableで無効判定された場合はここでも何もしない。
            if (!_subscribed) return;
            // 通常のUnityライフサイクルではStartは1回しか呼ばれないが、念のため多重発行を防ぐ。
            if (_started) return;
            _started = true;

            EventBus.Publish(new QuestStartedEvent(_activeQuest));
        }

        private void OnDisable()
        {
            if (!_subscribed) return;
            EventBus.Unsubscribe<TerrainClusterProgressEvent>(OnClusterProgress);
            _subscribed = false;
        }

        // ── 妥当性検証 ────────────────────────────────────────────────
        // 不正データのクエストは開始しない（購読もStarted発行も行わない）。
        // 「設定し忘れたクエストが静かに動かない」より、警告を出して止まるほうが原因を追いやすい。

        private bool IsQuestValid()
        {
            if (_activeQuest == null)
            {
                Debug.LogWarning("[QuestManager] _activeQuestが未設定のため開始しません。", this);
                return false;
            }

            if (_activeQuest.condition == null)
            {
                Debug.LogWarning(
                    $"[QuestManager] {_activeQuest.name} のconditionが未設定のため開始しません。", this);
                return false;
            }

            if (_activeQuest.condition.targetCount <= 0)
            {
                Debug.LogWarning(
                    $"[QuestManager] {_activeQuest.name} のtargetCountが{_activeQuest.condition.targetCount}のため開始しません。" +
                    "targetCountは1以上を設定してください。", this);
                return false;
            }

            return true;
        }

        // ── 条件種別ごとの観測 ────────────────────────────────────────
        // 種別ごとにハンドラを分けることで、条件を増やしても
        // questIdごとの分岐ではなく購読の追加だけで済むようにする。

        private void OnClusterProgress(TerrainClusterProgressEvent evt)
        {
            if (Condition.kind != QuestConditionKind.ClusterSize) return;
            if (evt.Category   != Condition.category)             return;

            // クラスター規模は「現在の状態の観測」であって出来事の回数ではない。
            // 届いた値を足し込まず、そのまま進捗値として報告する
            ReportProgress(evt.ClusterSize);
        }

        // ── 進捗の確定（クランプ・通知・完了判定を1か所へ集約） ────────

        private void ReportProgress(int value)
        {
            if (_isCompleted) return;

            int observed = Mathf.Clamp(value, 0, Condition.targetCount);
            // 進捗は後退させない。プレイヤーが何かを失ったわけではないのに数字だけ減ると、
            // 「急かさない・ストレスを与えない」という本作の方針に反するため。
            // 例: 森を4枚つなげた後、離れた場所へ森を1枚置くと、そのクラスターの規模1が届く
            int next = Mathf.Max(_currentCount, observed);
            if (next == _currentCount) return;

            _currentCount = next;
            EventBus.Publish(new QuestProgressChangedEvent(_activeQuest, _currentCount));

            if (_currentCount >= Condition.targetCount)
            {
                _isCompleted = true;
                Debug.Log($"[QuestManager] クエスト達成: {_activeQuest.title}（{_currentCount}/{Condition.targetCount}）");
                EventBus.Publish(new QuestCompletedEvent(_activeQuest));
            }
        }
    }
}
