// 役割: クエストの達成を祝うとき、「どのタイルを光らせるか」を解決する。
//       Coreから届くQuestFocusが「何を見ているか」を教えてくれるので、
//       それに対応するタイル集合を選び、QuestTileSelectionResolvedEventとして流すだけ。
//
//       ★見た目は持たない。演出側はQuestTileSelectionResolvedEventだけを購読する。
//       ★結果も保持しない。「最後に祝った集合」を抱えると破棄済みタイルの寿命管理が要るため、
//         解決したその場で流して忘れる。
//       ★Focus.Sourceによる分岐だけで選ぶ。クエスト別・地形別の分岐は増やさない。
//
//       保持する候補は3種類:
//         クラスター  ... 地形ごとの最新クラスター全体（成長イベントが毎回運んでくる）
//         配置        ... フォーカス開始後に置かれた対象カテゴリのタイル（蓄積する）
//         出来事      ... 橋・シナジーなど、その出来事に関わったタイル
//
//       ★配置の蓄積はフォーカス開始でリセットする。
//         クエストが始まる前に置いたタイルまで祝ってしまうと、
//         「何もしていないのに光った」ことになるため。
//
//       ★祝う対象の解決は「その場」ではなくLateUpdateまで待つ。
//         EventBusは同期発行だが、同じイベントを購読している者どうしの順番は保証されない。
//         例えばタイルを置いた瞬間、WorldEventRelayがこちらより先に処理されると、
//           TilePlacedEvent → Relay → QuestManager → 達成 → Celebration
//         までが走った後にこちらのTilePlacedEventが届く。
//         その場で解決すると、達成を決めた最後の1枚がまだ候補に入っていない。
//         同期チェーンが終わってから解くことで、購読順を当てにせずに済む。
//         （Script Execution Orderや購読順の調整では直さない。順序への依存が残るため）
//
//       ★フォーカスの切り替えも同じ理由でLateUpdateまで待つ。
//         Sequenceの切り替え待ちが0秒だと、達成から次クエスト開始までが同じ同期チェーンで進む。
//           TilePlacedEvent → Relay → QuestManager → 達成 → Runner → SetQuest → FocusStarted
//         ここで蓄積を消してしまうと、こちらにTilePlacedEventが届く前に
//         「達成を決めた最後の1枚」ごと捨ててしまう。
//         LateUpdateで「前の祝いを解決 → 消費した候補を片付け → 新しいフォーカスを適用」の
//         順に処理することで、切り替えが何秒後でも同じ結果になる。

using System;
using System.Collections.Generic;
using UnityEngine;
using ElfVillage.Core;

namespace ElfVillage.Tiles
{
    public class QuestTileFocusTracker : MonoBehaviour
    {
        // 地形ごとの最新クラスター。盤面の現状なのでフォーカスが変わっても捨てない
        private readonly Dictionary<TerrainClusterCategory, List<HexTile>> _clusterTiles = new();

        // フォーカス開始後に置かれた対象タイル。祝ったら空にする
        private readonly Dictionary<TerrainClusterCategory, List<HexTile>> _placedTiles = new();

        // 出来事キー → 関わったタイル。キーの綴りはQuestManagerの判定と揃える
        private readonly Dictionary<string, List<HexTile>> _worldEventTiles =
            new(StringComparer.OrdinalIgnoreCase);

        // 解決待ちの祝い。Celebrationが運んできたFocus自身を持つ（現在のクエストを読み直さない）。
        // 同じフレームに複数届いても取りこぼさないよう並べて持つ
        private readonly List<QuestFocus> _pendingCelebrations = new();

        // フォーカスが切り替わった合図。祝いを解決してから蓄積を切り詰める。
        // 「切り替わった時点で何枚貯まっていたか」を覚えるのは、
        // 同じフレームでそのあとに置かれたタイル（新しいクエストの分）まで捨てないため
        private bool _hasPendingFocusStart;
        private readonly Dictionary<TerrainClusterCategory, int> _placedCountAtFocusStart = new();

        private void OnEnable()
        {
            EventBus.Subscribe<TerrainGrowthEvent<ForestGrowthMetrics>>(OnForestGrow);
            EventBus.Subscribe<TerrainGrowthEvent<RiverGrowthMetrics>>(OnRiverGrow);
            EventBus.Subscribe<TilePlacedEvent>(OnTilePlaced);
            EventBus.Subscribe<RiverBridgeEvent>(OnBridge);
            EventBus.Subscribe<TerrainSynergyEvent>(OnSynergy);

            EventBus.Subscribe<QuestFocusStartedEvent>(OnFocusStarted);
            EventBus.Subscribe<QuestCelebrationEvent>(OnCelebration);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<TerrainGrowthEvent<ForestGrowthMetrics>>(OnForestGrow);
            EventBus.Unsubscribe<TerrainGrowthEvent<RiverGrowthMetrics>>(OnRiverGrow);
            EventBus.Unsubscribe<TilePlacedEvent>(OnTilePlaced);
            EventBus.Unsubscribe<RiverBridgeEvent>(OnBridge);
            EventBus.Unsubscribe<TerrainSynergyEvent>(OnSynergy);

            EventBus.Unsubscribe<QuestFocusStartedEvent>(OnFocusStarted);
            EventBus.Unsubscribe<QuestCelebrationEvent>(OnCelebration);

            // 無効化されたら解決待ちは捨てる。
            // 再有効化したときに、古い盤面の祝いが遅れて走り出さないようにする
            _pendingCelebrations.Clear();
            _hasPendingFocusStart = false;
            _placedCountAtFocusStart.Clear();
        }

        // ── 候補の収集 ────────────────────────────────────────────────

        private void OnForestGrow(TerrainGrowthEvent<ForestGrowthMetrics> evt)
            => StoreCluster(TerrainClusterCategory.Forest, evt.AffectedTiles);

        private void OnRiverGrow(TerrainGrowthEvent<RiverGrowthMetrics> evt)
            => StoreCluster(TerrainClusterCategory.River, evt.AffectedTiles);

        private void StoreCluster(TerrainClusterCategory category, IReadOnlyList<HexTile> tiles)
        {
            var list = GetOrCreate(_clusterTiles, category);
            list.Clear();
            AddRange(list, tiles);
        }

        private void OnTilePlaced(TilePlacedEvent evt)
        {
            if (evt.TileType == null || evt.Tile == null) return;

            // ★今のフォーカスを見て振り分けない。
            //   達成と同時に次のクエストが始まると、こちらへイベントが届く時点では
            //   フォーカスが次のクエストのものへ変わっていることがあり、
            //   「達成を決めた最後の1枚」を取りこぼす。
            //   カテゴリごとに貯めておき、どれを使うかは祝うときに選ぶ。
            // ★判定はWorldEventRelayと同じGetEffectiveCategories（ゲームプレイ用のカテゴリ）。
            //   visualOnly要素やlandDecorationの花は数えない
            foreach (var category in evt.TileType.GetEffectiveCategories())
            {
                if (!TryToClusterCategory(category, out var target)) continue;
                Add(GetOrCreate(_placedTiles, target), evt.Tile);
            }
        }

        private void OnBridge(RiverBridgeEvent evt)
        {
            var list = GetOrCreate(_worldEventTiles, WorldEventKeys.Bridge);
            list.Clear();
            AddRange(list, evt.Tiles);
            // クラスターが空の古い発行元にも耐えられるようにしておく
            Add(list, evt.BridgeTile);
        }

        private void OnSynergy(TerrainSynergyEvent evt)
        {
            string key = WorldEventKeys.Synergy(evt.SynergyId);
            if (string.IsNullOrEmpty(key)) return;

            var list = GetOrCreate(_worldEventTiles, key);
            list.Clear();
            AddRange(list, evt.TilesA);
            AddRange(list, evt.TilesB);
        }

        // ── フォーカスと解決 ──────────────────────────────────────────

        private void OnFocusStarted(QuestFocusStartedEvent evt)
        {
            // ★ここでは消さない。理由はクラス先頭のコメントを参照。
            //   同じ同期チェーンの中で次のクエストが始まることがあり、
            //   その場で消すと前のクエストが祝う対象を失う。
            //   代わりに「今どこまでが前のクエストの分か」を控えておく
            _placedCountAtFocusStart.Clear();
            foreach (var pair in _placedTiles)
                _placedCountAtFocusStart[pair.Key] = pair.Value.Count;

            _hasPendingFocusStart = true;
        }

        private void OnCelebration(QuestCelebrationEvent evt)
        {
            // ここでも解決しない。
            // 遅らせるのは対象タイルの解決だけで、達成の通知や報酬はもう流れている
            _pendingCelebrations.Add(evt.Focus);
        }

        /// <summary>そのフレームの同期イベント処理が終わったあとにまとめて片付ける。</summary>
        private void LateUpdate()
        {
            // ★順番が意味を持つ。
            //   1) 前のクエストの祝いを、そのクエスト自身のFocusで解決する
            //   2) 使い終わった候補を片付ける（Resolveの中で行う）
            //   3) そのあとで配置の蓄積をリセットし、次のクエストを空の状態から数え始める
            ResolvePendingCelebrations();
            ApplyPendingFocusStart();
        }

        private void ResolvePendingCelebrations()
        {
            if (_pendingCelebrations.Count == 0) return;

            // 解決中に増えても取りこぼさないよう、いま溜まっている分だけを取り出す
            var pending = new List<QuestFocus>(_pendingCelebrations);
            _pendingCelebrations.Clear();

            foreach (var focus in pending)
            {
                EventBus.Publish(new QuestTileSelectionResolvedEvent(Resolve(focus)));
                ClearConsumed(focus);
            }
        }

        private void ApplyPendingFocusStart()
        {
            if (!_hasPendingFocusStart) return;
            _hasPendingFocusStart = false;

            // 前のクエストの分だけを捨てる。
            // 全部を消さないのは、フォーカスが切り替わったのと同じフレームで
            // そのあとに置かれたタイルが、新しいクエストの1枚目になり得るため。
            // カテゴリを問わず切り詰めるのは、後でそのカテゴリのクエストが来たときに
            // 「開始前に置いたタイル」が混ざらないようにするため
            foreach (var pair in _placedTiles)
            {
                // 控えが無い＝フォーカス開始時にはまだ1枚も無かったカテゴリなので、
                // 貯まっているものはすべて新しいクエストの分。捨ててはいけない
                _placedCountAtFocusStart.TryGetValue(pair.Key, out int belongedToPrevious);

                int removeCount = Mathf.Min(belongedToPrevious, pair.Value.Count);
                if (removeCount > 0) pair.Value.RemoveRange(0, removeCount);
            }

            _placedCountAtFocusStart.Clear();
        }

        /// <summary>フォーカスに対応する候補を選ぶ。ここ以外に選択の分岐を作らないこと。</summary>
        private List<HexTile> Resolve(QuestFocus focus)
        {
            var result = new List<HexTile>();
            if (focus == null) return result;

            switch (focus.Source)
            {
                case QuestFocusSource.Cluster:
                    if (_clusterTiles.TryGetValue(focus.Category, out var cluster))
                        AddRange(result, cluster);
                    break;

                case QuestFocusSource.TilePlacement:
                    if (_placedTiles.TryGetValue(focus.Category, out var placed))
                        AddRange(result, placed);
                    break;

                case QuestFocusSource.WorldEvent:
                    string key = Normalize(focus.EventKey);
                    if (key != null && _worldEventTiles.TryGetValue(key, out var world))
                        AddRange(result, world);
                    break;
            }

            return result;
        }

        private void ClearConsumed(QuestFocus focus)
        {
            if (focus == null) return;

            switch (focus.Source)
            {
                case QuestFocusSource.TilePlacement:
                    if (_placedTiles.TryGetValue(focus.Category, out var placed)) placed.Clear();
                    break;

                case QuestFocusSource.WorldEvent:
                    string key = Normalize(focus.EventKey);
                    if (key != null && _worldEventTiles.TryGetValue(key, out var world)) world.Clear();
                    break;

                // クラスターは盤面の現状そのものなので消さない。
                // 次に同じ地形が育てば上書きされる
            }
        }

        // ── 小物 ──────────────────────────────────────────────────────

        /// <summary>出来事キーの正規化。QuestManagerの一致判定と同じ規則（Trim・大小無視）。</summary>
        private static string Normalize(string key)
            => string.IsNullOrWhiteSpace(key) ? null : key.Trim();

        /// <summary>TilesのカテゴリをCoreのカテゴリへ。対応が無いRoad/Villageはfalseを返す。</summary>
        private static bool TryToClusterCategory(TileCategory category, out TerrainClusterCategory result)
        {
            switch (category)
            {
                case TileCategory.Forest: result = TerrainClusterCategory.Forest; return true;
                case TileCategory.Field:  result = TerrainClusterCategory.Field;  return true;
                case TileCategory.River:  result = TerrainClusterCategory.River;  return true;
                default:                  result = default;                       return false;
            }
        }

        private static List<HexTile> GetOrCreate<TKey>(Dictionary<TKey, List<HexTile>> map, TKey key)
        {
            if (!map.TryGetValue(key, out var list))
            {
                list = new List<HexTile>();
                map[key] = list;
            }
            return list;
        }

        private static void AddRange(List<HexTile> target, IReadOnlyList<HexTile> source)
        {
            if (source == null) return;
            for (int i = 0; i < source.Count; i++) Add(target, source[i]);
        }

        /// <summary>同じタイルを二度入れない（シナジーのA/Bが重なる場合などに効く）。</summary>
        private static void Add(List<HexTile> target, HexTile tile)
        {
            if (tile == null || target.Contains(tile)) return;
            target.Add(tile);
        }
    }
}
