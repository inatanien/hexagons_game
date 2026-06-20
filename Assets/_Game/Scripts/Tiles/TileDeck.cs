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

        private readonly List<TileType> _hand = new();

        public IReadOnlyList<TileType> Hand => _hand;
        public TileType Current => _hand.Count > 0 ? _hand[0] : null;

        public event System.Action OnHandChanged;

        private void Start()
        {
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

        private void DrawOne()
        {
            if (entries == null || entries.Length == 0) return;

            int total = 0;
            foreach (var e in entries)
                total += Mathf.Max(1, e.weight);

            int roll = Random.Range(0, total);
            int cumulative = 0;
            foreach (var e in entries)
            {
                cumulative += Mathf.Max(1, e.weight);
                if (roll < cumulative)
                {
                    if (e.tileType != null) _hand.Add(e.tileType);
                    return;
                }
            }

            // フォールバック: 最後のエントリを使用
            if (entries[entries.Length - 1].tileType != null)
                _hand.Add(entries[entries.Length - 1].tileType);
        }
    }
}
