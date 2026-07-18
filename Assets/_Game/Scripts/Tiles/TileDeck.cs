// 役割: タイル種別の重み付き抽選デッキ。手札（最大3枚）を管理し、配置後に1枚補充する。

using System.Collections.Generic;
using UnityEngine;

namespace ElfVillage.Tiles
{
    [System.Serializable]
    public class TileDeckEntry
    {
        public TileType tileType;
        [Range(1, 20)] public int weight = 1;
    }

    public class TileDeck : MonoBehaviour
    {
        [Header("デッキ定義")]
        [SerializeField] private TileDeckEntry[] entries;

        [Header("手札枚数")]
        [SerializeField] private int handSize = 3;

        [Header("初手固定")]
        [Tooltip("ゲーム開始時の手札1枚目を必ずこのタイルにする。空欄なら通常どおり抽選する")]
        [SerializeField] private TileType firstTileOverride;

        private readonly List<TileType> _hand = new();

        // 直近2回の抽選結果が持つカテゴリ集合（複合タイル対応）を保持し、
        // 同じカテゴリが3連続にならないようにする
        private readonly List<HashSet<TileCategory>> _recentCategorySets = new();

        public IReadOnlyList<TileType> Hand => _hand;
        public TileType Current => _hand.Count > 0 ? _hand[0] : null;

        public event System.Action OnHandChanged;

        private void Start()
        {
            if (firstTileOverride != null)
            {
                _hand.Add(firstTileOverride);
                RecordCategories(firstTileOverride);
            }
            while (_hand.Count < handSize)
                DrawOne();
            OnHandChanged?.Invoke();
        }

        /// <summary>先頭タイルを消費してデッキから1枚補充する。配置後に呼ぶ。</summary>
        public void ConsumeTop()
        {
            if (_hand.Count > 0) _hand.RemoveAt(0);
            DrawOne();
            OnHandChanged?.Invoke();
        }

        /// <summary>デッキに登録されている有効なタイル種別を重複なく返す（デバッグパネル用）。</summary>
        public List<TileType> AllActiveTileTypes()
        {
            var result = new List<TileType>();
            if (entries == null) return result;
            foreach (var e in entries)
                if (e.tileType != null && e.tileType.isActive && !result.Contains(e.tileType))
                    result.Add(e.tileType);
            return result;
        }

        private void DrawOne()
        {
            if (entries == null || entries.Length == 0) return;

            // 直近2回のカテゴリ集合の共通部分を「3連続になり得るカテゴリ」として候補から除外する。
            // 複合タイルは含むすべてのカテゴリを持つものとして扱う（EffectiveCategories経由）。
            HashSet<TileCategory> excludedCategories = GetExcludedCategories();

            int total = SumWeights(excludedCategories);
            // 除外すると候補がゼロになる場合（有効な種類がそのカテゴリしかない等）は、
            // 手札が組めなくなる詰みを避けるため除外せず通常どおり抽選する
            if (total == 0)
            {
                excludedCategories = null;
                total = SumWeights(excludedCategories);
            }
            if (total == 0) return; // アクティブなエントリがない

            int roll = Random.Range(0, total);
            int cumulative = 0;
            foreach (var e in entries)
            {
                if (e.tileType == null || !e.tileType.isActive) continue;
                if (IsExcluded(e.tileType, excludedCategories)) continue;
                cumulative += Mathf.Max(1, e.weight);
                if (roll < cumulative)
                {
                    _hand.Add(e.tileType);
                    RecordCategories(e.tileType);
                    return;
                }
            }
        }

        /// <summary>直近2回の抽選結果のカテゴリ集合の共通部分を返す。履歴が2件未満なら除外なし（null）。</summary>
        private HashSet<TileCategory> GetExcludedCategories()
        {
            if (_recentCategorySets.Count < 2) return null;

            HashSet<TileCategory> last = _recentCategorySets[_recentCategorySets.Count - 1];
            HashSet<TileCategory> prev = _recentCategorySets[_recentCategorySets.Count - 2];

            HashSet<TileCategory> intersection = null;
            foreach (var c in last)
            {
                if (!prev.Contains(c)) continue;
                intersection ??= new HashSet<TileCategory>();
                intersection.Add(c);
            }
            return intersection;
        }

        /// <summary>tileTypeが持つカテゴリのいずれかがexcludedCategoriesに含まれるか判定する。</summary>
        private static bool IsExcluded(TileType tileType, HashSet<TileCategory> excludedCategories)
        {
            if (excludedCategories == null || excludedCategories.Count == 0) return false;
            foreach (var c in tileType.GetEffectiveCategories())
                if (excludedCategories.Contains(c)) return true;
            return false;
        }

        private int SumWeights(HashSet<TileCategory> excludedCategories)
        {
            int total = 0;
            foreach (var e in entries)
                if (e.tileType != null && e.tileType.isActive && !IsExcluded(e.tileType, excludedCategories))
                    total += Mathf.Max(1, e.weight);
            return total;
        }

        private void RecordCategories(TileType tileType)
        {
            _recentCategorySets.Add(new HashSet<TileCategory>(tileType.GetEffectiveCategories()));
            if (_recentCategorySets.Count > 2)
                _recentCategorySets.RemoveAt(0);
        }
    }
}
