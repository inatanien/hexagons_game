// 役割: 手札（TileDeck）の次3枚を画面右下に表示する UI MonoBehaviour。
//       シーン上の UI オブジェクトを直接参照して更新する（ランタイム生成なし）。

using UnityEngine;
using UnityEngine.UI;

namespace ElfVillage.Tiles
{
    public class HandUI : MonoBehaviour
    {
        [SerializeField] private TileDeck tileDeck;

        // カード0 = NEXT（次に置くタイル）
        [SerializeField] private Graphic _card0Patch;
        [SerializeField] private Text    _card0Name;
        [SerializeField] private GameObject _card0Root;

        // カード1 = 2nd
        [SerializeField] private Graphic _card1Patch;
        [SerializeField] private Text    _card1Name;
        [SerializeField] private GameObject _card1Root;

        // カード2 = 3rd
        [SerializeField] private Graphic _card2Patch;
        [SerializeField] private Text    _card2Name;
        [SerializeField] private GameObject _card2Root;

        private void Start()
        {
            if (tileDeck != null)
            {
                tileDeck.OnHandChanged += Refresh;
                Refresh();
            }
        }

        private void OnDestroy()
        {
            if (tileDeck != null)
                tileDeck.OnHandChanged -= Refresh;
        }

        private void Refresh()
        {
            if (tileDeck == null) return;
            var hand = tileDeck.Hand;

            SetCard(0, _card0Root, _card0Patch, _card0Name, hand);
            SetCard(1, _card1Root, _card1Patch, _card1Name, hand);
            SetCard(2, _card2Root, _card2Patch, _card2Name, hand);
        }

        private static void SetCard(int index, GameObject root, Graphic patch, Text label,
            System.Collections.Generic.IReadOnlyList<TileType> hand)
        {
            if (root == null) return;
            bool hasCard = index < hand.Count && hand[index] != null;
            root.SetActive(hasCard);
            if (!hasCard) return;
            if (patch != null) patch.color = hand[index].EffectivePreviewColor;
            if (label != null) label.text  = hand[index].tileName;
        }
    }
}
