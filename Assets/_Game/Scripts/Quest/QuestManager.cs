// 役割: アクティブな1つのクエストの進捗を管理する。
//       QuestDefinition.condition（QuestCondition）が示す種別に従って進捗を更新するだけで、
//       questIdごとの分岐は持たない。クエストを増やしてもこのクラスは変わらない。
//       購読するのはCoreのイベントだけで、Tiles固有の型には一切依存しない
//       （森・川のクラスター判定や橋・シナジーの成立判定はTiles側の評価システムが行い、
//        こちらはRelayが翻訳した結果を観測するだけ）。
//       達成後の次クエストへの切り替え・キュー管理・報酬・演出はまだこのクラスの責務ではない。
//
//       ライフサイクル:
//         OnEnable ... クエストの妥当性を検証し、有効なときだけ条件種別に応じたイベントを購読する
//                      （無効なデータが一瞬でも購読状態になるのを避けるため、検証を遅らせない）
//         Start    ... 有効なときだけQuestStartedEventを発行する
//         OnDisable... 実際に購読した種別（_subscribedKind）だけを解除する
//
//       ★解除時にCondition.kindを読み直さないこと。実行中にQuestDefinitionの値が
//         書き換わると、購読したものと違うイベントを解除しようとして購読が残る。
//
//       QuestStartedEventはOnEnableではなくStartで発行する。Unityは「シーン読み込み時に
//       存在する全オブジェクトのOnEnableが完了してから、初めてどれかのStartが呼ばれる」ことを
//       保証しているため、QuestPanelUI（OnEnableで購読）がQuestManagerより後にOnEnableされても、
//       QuestStartedEventの発行（Start）より必ず前に購読が完了している。Script Execution Order
//       には一切依存しない（Session 14）。
//       この仕組みはQuestManager/QuestPanelUIがシーン開始時から常駐し無効化されないことを前提とする。
//       動的生成・再有効化に対応する再通知機構はまだ導入しない（将来必要になれば別途検討）。

using System;
using UnityEngine;
using ElfVillage.Core;

namespace ElfVillage.Quest
{
    public class QuestManager : MonoBehaviour
    {
        [SerializeField] private QuestDefinition _activeQuest;

        private int  _currentCount;
        private bool _isCompleted;
        private bool _started;

        private bool               _subscribed;
        private QuestConditionKind _subscribedKind;

        /// <summary>有効なクエストの条件。_subscribedがtrueのときだけ意味を持つ。</summary>
        private QuestCondition Condition => _activeQuest.condition;

        private void OnEnable()
        {
            if (!IsQuestValid()) return;
            // 購読できなかった種別で_subscribed = trueにしないため、成功可否を見てから確定する
            if (!SubscribeForKind(Condition.kind)) return;

            _subscribed     = true;
            _subscribedKind = Condition.kind;
            _currentCount   = 0;
            _isCompleted    = false;
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

            UnsubscribeForKind(_subscribedKind);
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

            var condition = _activeQuest.condition;
            if (condition == null)
            {
                Debug.LogWarning(
                    $"[QuestManager] {_activeQuest.name} のconditionが未設定のため開始しません。", this);
                return false;
            }

            if (!Enum.IsDefined(typeof(QuestConditionKind), condition.kind))
            {
                Debug.LogWarning(
                    $"[QuestManager] {_activeQuest.name} のkindが未対応の値（{(int)condition.kind}）のため開始しません。", this);
                return false;
            }

            if (condition.targetCount <= 0)
            {
                Debug.LogWarning(
                    $"[QuestManager] {_activeQuest.name} のtargetCountが{condition.targetCount}のため開始しません。" +
                    "targetCountは1以上を設定してください。", this);
                return false;
            }

            // eventKeyが空のままだと、どの出来事とも一致せず永久に進まないクエストになる
            if (condition.kind == QuestConditionKind.EventOccurrence &&
                string.IsNullOrWhiteSpace(condition.eventKey))
            {
                Debug.LogWarning(
                    $"[QuestManager] {_activeQuest.name} のeventKeyが未設定のため開始しません。" +
                    "EventOccurrenceではWorldEventKeysのキー（例: bridge）を設定してください。", this);
                return false;
            }

            return true;
        }

        // ── 条件種別ごとの購読 ────────────────────────────────────────
        // 種別ごとに必要なイベントだけを購読する。
        // これによりClusterSizeのクエストが配置イベントを受け取ることはなく、
        // 種別を増やしてもquestIdごとの分岐ではなく購読の追加だけで済む。

        private bool SubscribeForKind(QuestConditionKind kind)
        {
            switch (kind)
            {
                case QuestConditionKind.ClusterSize:
                    EventBus.Subscribe<TerrainClusterProgressEvent>(OnClusterProgress);
                    return true;

                case QuestConditionKind.TilePlacedCount:
                    EventBus.Subscribe<TileCategoryPlacedEvent>(OnTileCategoryPlaced);
                    return true;

                case QuestConditionKind.EventOccurrence:
                    EventBus.Subscribe<WorldEventOccurredEvent>(OnWorldEventOccurred);
                    return true;

                default:
                    Debug.LogWarning($"[QuestManager] 未対応の条件種別のため購読しません: {kind}", this);
                    return false;
            }
        }

        private void UnsubscribeForKind(QuestConditionKind kind)
        {
            switch (kind)
            {
                case QuestConditionKind.ClusterSize:
                    EventBus.Unsubscribe<TerrainClusterProgressEvent>(OnClusterProgress);
                    break;

                case QuestConditionKind.TilePlacedCount:
                    EventBus.Unsubscribe<TileCategoryPlacedEvent>(OnTileCategoryPlaced);
                    break;

                case QuestConditionKind.EventOccurrence:
                    EventBus.Unsubscribe<WorldEventOccurredEvent>(OnWorldEventOccurred);
                    break;
            }
        }

        // ── 各種別の観測 ──────────────────────────────────────────────

        private void OnClusterProgress(TerrainClusterProgressEvent evt)
        {
            if (evt.Category != Condition.category) return;

            // クラスター規模は「現在の状態の観測」であって出来事の回数ではない。
            // 届いた値を足し込まず、そのまま進捗値として報告する
            ReportProgress(evt.ClusterSize);
        }

        private void OnTileCategoryPlaced(TileCategoryPlacedEvent evt)
        {
            if (evt.Category != Condition.category) return;
            ReportIncrement();
        }

        private void OnWorldEventOccurred(WorldEventOccurredEvent evt)
        {
            if (!KeyMatches(evt.EventKey, Condition.eventKey)) return;
            ReportIncrement();
        }

        /// <summary>
        /// 出来事キーの一致判定。前後の空白を落とし、大文字小文字は区別しない。
        /// SynergyIdはInspectorへ、eventKeyはSOへそれぞれ手入力される文字列なので、
        /// 綴りの大小差だけでクエストが永久に進まなくなるのを避ける。
        /// </summary>
        private static bool KeyMatches(string published, string wanted)
        {
            if (string.IsNullOrWhiteSpace(published) || string.IsNullOrWhiteSpace(wanted)) return false;
            return string.Equals(published.Trim(), wanted.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        // ── 進捗の確定（クランプ・通知・完了判定を1か所へ集約） ────────

        /// <summary>出来事を1回数える。加算型の条件（TilePlacedCount / EventOccurrence）用。</summary>
        private void ReportIncrement() => ReportProgress(_currentCount + 1);

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
