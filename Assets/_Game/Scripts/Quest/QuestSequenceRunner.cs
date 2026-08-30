// 役割: QuestSequenceDefinitionの順番どおりにクエストを出し続ける進行役。
//       現在のクエストが達成されたら、少し間を置いて次のクエストをQuestManagerへ渡す。
//
//       ★QuestManagerの内部状態（進捗・購読・開始済みフラグ）には触れない。
//         使うのは SetQuest() と QuestCompletedEvent の2つだけ。
//         進捗管理はQuestManager、順番の管理はこちら、と責務を分けておくことで、
//         Sequenceを使わない単体運用（Inspectorへ直接クエストを割り当てる）もそのまま残る。
//
//       ★クエストが有効かどうかをここで判定しない。
//         conditionの中身を読み始めるとQuestManagerと同じ検証が二重になり、
//         片方だけ直したときに食い違う。判断はSetQuestの戻り値に委ねて、
//         falseなら次の要素を試すだけにする。
//
//       ★切り替えに間を置くのはゲームロジックの都合ではなく、
//         「✨ Quest Complete!」のトーストを読む時間を作るため。
//         即座に次を開始すると、QuestNotificationUIが新しい通知で上書きしてしまう
//         （同UIは表示中に次が来たらアニメーションを中断して差し替える仕様）。
//         達成の余韻を消さないための待ち時間なので、報酬の発行やCompletedEvent自体は遅らせない。

using System.Collections;
using UnityEngine;
using ElfVillage.Core;

namespace ElfVillage.Quest
{
    public class QuestSequenceRunner : MonoBehaviour
    {
        [Tooltip("この順番にクエストを出す。未設定なら何もしない（単体運用のまま）")]
        [SerializeField] private QuestSequenceDefinition _sequence;

        [Tooltip("クエストを渡す相手。未設定なら同じGameObjectから探す")]
        [SerializeField] private QuestManager _questManager;

        [Tooltip("達成から次のクエスト開始までの待ち時間（秒）。" +
                  "達成トーストを読む時間を作るためのもので、0にすると即座に切り替わる")]
        [SerializeField] private float _nextQuestDelay = 2.0f;

        private int             _currentIndex = -1;
        private QuestDefinition _currentQuest;

        /// <summary>次のクエストへの切り替えを予約済みか。同じ達成で二重に予約しないためのガード。</summary>
        private bool _isAdvancing;

        /// <summary>1本でも達成したか。1本も開始できなかったSequenceを「完走」と呼ばないための記録。</summary>
        private bool _completedAny;

        /// <summary>Sequenceを終えたか。終了後に完了イベントを再発行しないためのガード。</summary>
        private bool _finished;

        private Coroutine _advanceRoutine;

        private void Awake()
        {
            // 同じGameObjectへ並べて置く運用が基本なので、割り当て忘れでも動くようにしておく
            if (_questManager == null) _questManager = GetComponent<QuestManager>();
        }

        private void OnEnable()
        {
            EventBus.Subscribe<QuestCompletedEvent>(OnQuestCompleted);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<QuestCompletedEvent>(OnQuestCompleted);
            CancelPendingAdvance();
        }

        private void Start()
        {
            if (_sequence == null)
            {
                // Sequenceを使わない構成（QuestManagerへ直接クエストを割り当てる）も正常
                Debug.Log("[QuestSequenceRunner] Sequenceが未設定のため何もしません。", this);
                return;
            }

            if (_questManager == null)
            {
                Debug.LogWarning("[QuestSequenceRunner] QuestManagerが見つからないため開始できません。", this);
                return;
            }

            if (_sequence.quests == null || _sequence.quests.Length == 0)
            {
                Debug.LogWarning($"[QuestSequenceRunner] {_sequence.name} にクエストが1本も入っていません。", this);
                return;
            }

            StartFromIndex(0);
        }

        // ── 進行 ──────────────────────────────────────────────────────

        private void OnQuestCompleted(QuestCompletedEvent evt)
        {
            if (_finished) return;
            // 既に次を予約済みなら何もしない。同じ達成で複数の切り替えを予約しないため
            if (_isAdvancing) return;
            // 現在出題中のクエスト以外の達成では進めない
            // （別のQuestManagerや、切り替え前の古いクエストの通知を拾わないため）
            if (!ReferenceEquals(evt.Quest, _currentQuest)) return;

            _completedAny = true;
            _isAdvancing  = true;

            // 待ち時間が無い設定のとき、およびPlay中でないとき（EditModeテスト等）は
            // コルーチンが進まないのでその場で切り替える
            if (_nextQuestDelay <= 0f || !Application.isPlaying)
            {
                AdvanceToNext();
                return;
            }

            _advanceRoutine = StartCoroutine(AdvanceAfterDelay());
        }

        private IEnumerator AdvanceAfterDelay()
        {
            yield return new WaitForSeconds(_nextQuestDelay);

            _advanceRoutine = null;
            AdvanceToNext();
        }

        private void AdvanceToNext()
        {
            _isAdvancing = false;
            StartFromIndex(_currentIndex + 1);
        }

        /// <summary>indexから順に、実際に開始できたクエストが見つかるまで試す。</summary>
        private void StartFromIndex(int index)
        {
            var quests = _sequence.quests;

            for (int i = index; i < quests.Length; i++)
            {
                var quest = quests[i];
                if (quest == null)
                {
                    Debug.LogWarning($"[QuestSequenceRunner] {_sequence.name} の{i}番目が空欄のため飛ばします。", this);
                    continue;
                }

                // 有効かどうかの判断はQuestManagerに委ねる。falseなら理由はあちらがログに出す
                if (!_questManager.SetQuest(quest)) continue;

                _currentIndex = i;
                _currentQuest = quest;
                return;
            }

            FinishSequence();
        }

        private void FinishSequence()
        {
            _finished     = true;
            _currentQuest = null;
            CancelPendingAdvance();

            // 1本も達成していないSequence（全要素が空欄・不正など）を「完走」とは呼ばない
            if (!_completedAny)
            {
                Debug.LogWarning(
                    $"[QuestSequenceRunner] {_sequence.name} に開始できるクエストが1本もありませんでした。", this);
                return;
            }

            Debug.Log($"[QuestSequenceRunner] Sequence完走: {_sequence.name}", this);
            EventBus.Publish(new QuestSequenceCompletedEvent(_sequence));
        }

        private void CancelPendingAdvance()
        {
            if (_advanceRoutine != null)
            {
                StopCoroutine(_advanceRoutine);
                _advanceRoutine = null;
            }
            _isAdvancing = false;
        }
    }
}
