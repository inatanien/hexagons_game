// 役割: アクティブな1つのクエストの進捗を管理する。
//       QuestDefinition.condition（QuestCondition）が示す種別に従って進捗を更新するだけで、
//       questIdごとの分岐は持たない。クエストを増やしてもこのクラスは変わらない。
//       購読するのはCoreのイベントだけで、Tiles固有の型には一切依存しない
//       （森・川のクラスター判定や橋・シナジーの成立判定はTiles側の評価システムが行い、
//        こちらはRelayが翻訳した結果を観測するだけ）。
//
//       ★クエストの「順番」はこのクラスの責務ではない。複数クエストを順に出すのは
//         QuestSequenceRunnerの役目で、こちらはSetQuestで渡された現在の1本だけを見る。
//
//       運用は2通りあり、どちらも同じ経路を通る。
//         単体運用   ... Inspectorへ_activeQuestを割り当てる → OnEnableで購読 → StartでStarted発行
//         Sequence運用... _activeQuestは未設定 → 外部がSetQuest() → その場でStarted発行
//       ★どちらの場合も、1つのクエストにつきQuestStartedEventは1回だけしか発行しない。
//         コンポーネントのStart実行順に依存して二重開始・未開始にならないようにするため。
//
//       ライフサイクル:
//         OnEnable ... _activeQuestがあれば検証して購読する（Startedはまだ発行しない）
//         Start    ... まだStartedを発行していなければ発行する
//         OnDisable... 実際に購読した種別（_subscribedKind）だけを解除する
//
//       ★解除時にCondition.kindを読み直さないこと。実行中にQuestDefinitionの値が
//         書き換わると、購読したものと違うイベントを解除しようとして購読が残る。
//
//       QuestStartedEventをOnEnableではなくStartで発行するのは、Unityが「シーン読み込み時に
//       存在する全オブジェクトのOnEnableが完了してから、初めてどれかのStartが呼ばれる」ことを
//       保証しているため。QuestPanelUI（OnEnableで購読）がQuestManagerより後にOnEnableされても、
//       QuestStartedEventの発行より必ず前に購読が完了している（Session 14）。
//
//       ★既知の制約: SetQuestで開始したクエストは、開始時点の盤面を見ない。
//         例えば川が既に8枚つながっていても「川3枚」クエストは0/3から始まり、
//         次に川を1枚置いた時点で届くClusterSizeで即座に達成する。
//         開始時の再評価には評価システムへの問い合わせか状態スナップショットが必要になるため、
//         「盤面を再走査しない」という原則を優先して今は許容している。

using System;
using UnityEngine;
using ElfVillage.Core;

namespace ElfVillage.Quest
{
    public class QuestManager : MonoBehaviour
    {
        [Tooltip("単体運用で出題するクエスト。QuestSequenceRunnerに任せる場合は未設定でよい")]
        [SerializeField] private QuestDefinition _activeQuest;

        private int  _currentCount;
        private bool _isCompleted;
        private bool _started;

        private bool               _subscribed;
        private QuestConditionKind _subscribedKind;

        /// <summary>現在のクエストの条件。_subscribedがtrueのときだけ意味を持つ。</summary>
        private QuestCondition Condition => _activeQuest.condition;

        // ── 外部API ──────────────────────────────────────────────────

        /// <summary>
        /// クエストを差し替えて開始する。QuestSequenceRunnerなど外部の進行管理から呼ぶ。
        /// ★渡されたクエストを先に検証し、有効なときだけ状態を切り替える。
        ///   無効なクエストで現在のクエストが壊れると、
        ///   「Sequenceに設定ミスが1つあるだけで進行中のクエストまで止まる」ことになるため。
        /// </summary>
        /// <returns>切り替えて開始したらtrue。無効で何もしなかったらfalse。</returns>
        public bool SetQuest(QuestDefinition quest)
        {
            if (quest == null)
            {
                // 呼び出し側（Runner）がスキップ判断できるよう、異常扱いはしない
                Debug.Log("[QuestManager] SetQuestにnullが渡されたため切り替えません。", this);
                return false;
            }

            if (!IsQuestValid(quest)) return false;

            // 同じクエストへの再設定は何もしない。
            // これにより「StartとSetQuestのどちらが先に走ってもStartedは1回だけ」になり、
            // コンポーネントのStart実行順に依存しなくなる（進捗も巻き戻らない）
            if (ReferenceEquals(quest, _activeQuest) && _subscribed && _started) return true;

            Activate(quest, publishStarted: true);
            return true;
        }

        // ── Unityライフサイクル ──────────────────────────────────────

        private void OnEnable()
        {
            if (_activeQuest == null)
            {
                // Sequence運用では起動時に未設定なのが正常な経路。異常ではないので警告にしない
                Debug.Log("[QuestManager] クエストが未設定のため待機します（外部からSetQuestされる想定）。", this);
                return;
            }

            if (!IsQuestValid(_activeQuest)) return;

            // ここではStartedを発行しない。UIの購読が出そろうStartまで待つ
            Activate(_activeQuest, publishStarted: false);
        }

        private void Start()
        {
            // OnEnableで無効判定された場合はここでも何もしない。
            if (!_subscribed) return;
            // SetQuestで既に開始済みの場合も二重に発行しない。
            if (_started) return;
            _started = true;

            PublishStarted(_activeQuest);
        }

        private void OnDisable()
        {
            UnsubscribeCurrent();
        }

        // ── 状態の切り替え ────────────────────────────────────────────
        // 検証を通ったクエストだけがここへ来る。途中で失敗する経路を作らないことで、
        // 「解除したのに購読し直せず、どのクエストも進まない」状態が生まれないようにする。

        private void Activate(QuestDefinition quest, bool publishStarted)
        {
            UnsubscribeCurrent();

            _activeQuest  = quest;
            _currentCount = 0;
            _isCompleted  = false;

            SubscribeForKind(quest.condition.kind);
            _subscribed     = true;
            _subscribedKind = quest.condition.kind;

            if (!publishStarted) return;

            _started = true;
            PublishStarted(quest);
        }

        private void UnsubscribeCurrent()
        {
            if (!_subscribed) return;

            UnsubscribeForKind(_subscribedKind);
            _subscribed = false;
        }

        // ── 妥当性検証 ────────────────────────────────────────────────
        // 不正データのクエストは開始しない（購読もStarted発行も行わない）。
        // 「設定し忘れたクエストが静かに動かない」より、警告を出して止まるほうが原因を追いやすい。
        // ★引数のクエストを検証する。_activeQuestを見ないので、
        //   SetQuestが「切り替える前に候補を確かめる」ために使える。

        private bool IsQuestValid(QuestDefinition quest)
        {
            if (quest == null) return false;   // 未設定は異常ではないので、ここでは何も言わない

            var condition = quest.condition;
            if (condition == null)
            {
                Debug.LogWarning(
                    $"[QuestManager] {quest.name} のconditionが未設定のため開始しません。", this);
                return false;
            }

            if (!Enum.IsDefined(typeof(QuestConditionKind), condition.kind))
            {
                Debug.LogWarning(
                    $"[QuestManager] {quest.name} のkindが未対応の値（{(int)condition.kind}）のため開始しません。", this);
                return false;
            }

            if (condition.targetCount <= 0)
            {
                Debug.LogWarning(
                    $"[QuestManager] {quest.name} のtargetCountが{condition.targetCount}のため開始しません。" +
                    "targetCountは1以上を設定してください。", this);
                return false;
            }

            // eventKeyが空のままだと、どの出来事とも一致せず永久に進まないクエストになる
            if (condition.kind == QuestConditionKind.EventOccurrence &&
                string.IsNullOrWhiteSpace(condition.eventKey))
            {
                Debug.LogWarning(
                    $"[QuestManager] {quest.name} のeventKeyが未設定のため開始しません。" +
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
                    // IsQuestValidがEnum.IsDefinedで弾いているためここへは来ない
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

        // ── 盤面側への合図 ────────────────────────────────────────────
        // Quest層はタイルを知らないので、「盤面の何を見ているか」だけをCoreの語彙で伝える。
        // 受け取ったTiles側が、祝う対象のタイル集合を自分で選ぶ。

        private void PublishStarted(QuestDefinition quest)
        {
            EventBus.Publish(new QuestStartedEvent(quest));
            EventBus.Publish(new QuestFocusStartedEvent(BuildFocus(quest.condition)));
        }

        private static QuestFocus BuildFocus(QuestCondition condition)
        {
            return new QuestFocus(ToFocusSource(condition.kind), condition.category, condition.eventKey);
        }

        private static QuestFocusSource ToFocusSource(QuestConditionKind kind)
        {
            switch (kind)
            {
                case QuestConditionKind.TilePlacedCount: return QuestFocusSource.TilePlacement;
                case QuestConditionKind.EventOccurrence: return QuestFocusSource.WorldEvent;
                default:                                 return QuestFocusSource.Cluster;
            }
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

                // 達成を世界の見た目で祝うための合図。
                // 報酬やUIより後に流すのは、まず達成そのものを届けたいから
                EventBus.Publish(new QuestCelebrationEvent(BuildFocus(Condition), _activeQuest.title));
            }
        }
    }
}
