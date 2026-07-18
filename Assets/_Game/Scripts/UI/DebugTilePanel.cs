// 役割: デバッグ用に、好きなタイル種別を選んで何度でも配置できるUIパネル。
//       F1キーで表示/非表示を切り替える。ボタン一覧は実行時に生成する。

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using ElfVillage.Tiles;

namespace ElfVillage.UI
{
    public class DebugTilePanel : MonoBehaviour
    {
        [SerializeField] private HexGridManager hexGridManager;
        [SerializeField] private TileDeck       tileDeck;

        private GameObject _panelRoot;
        private readonly List<Image> _buttonImages = new();
        private readonly Dictionary<Image, Color> _buttonBaseColors = new();
        private Image _selectedImage;

        private void Start()
        {
            // EventSystem はシーンに1つ配置済みのものを使う（このクラスでは生成しない）。
            BuildPanel();
            _panelRoot.SetActive(false);
        }

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current.f1Key.wasPressedThisFrame)
                _panelRoot.SetActive(!_panelRoot.activeSelf);
        }

        private void BuildPanel()
        {
            var canvasGO = new GameObject("DebugTileCanvas");
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1000;
            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<GraphicRaycaster>();

            _panelRoot = new GameObject("Panel");
            _panelRoot.transform.SetParent(canvasGO.transform, false);
            var panelRect = _panelRoot.AddComponent<RectTransform>();
            panelRect.anchorMin        = new Vector2(0f, 0.5f);
            panelRect.anchorMax        = new Vector2(0f, 0.5f);
            panelRect.pivot            = new Vector2(0f, 0.5f);
            panelRect.anchoredPosition = new Vector2(12f, 0f);

            var bg = _panelRoot.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.55f);

            var layout = _panelRoot.AddComponent<VerticalLayoutGroup>();
            layout.padding             = new RectOffset(8, 8, 8, 8);
            layout.spacing             = 6f;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth  = false;
            layout.childControlHeight     = false;
            layout.childControlWidth      = false;
            layout.childAlignment          = TextAnchor.UpperCenter;

            var fitter = _panelRoot.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;

            var title = CreateLabel(_panelRoot.transform, "デバッグ配置 (F1)", 16, Color.white);
            title.GetComponent<RectTransform>().sizeDelta = new Vector2(150f, 24f);

            if (tileDeck == null) return;
            foreach (var type in tileDeck.AllActiveTileTypes())
                CreateButton(type);
        }

        private void CreateButton(TileType type)
        {
            var go = new GameObject("Btn_" + type.tileName);
            go.transform.SetParent(_panelRoot.transform, false);
            var rect = go.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(150f, 32f);

            var img = go.AddComponent<Image>();
            img.color = type.EffectivePreviewColor;
            _buttonImages.Add(img);
            _buttonBaseColors[img] = type.EffectivePreviewColor;

            var btn = go.AddComponent<Button>();
            btn.onClick.AddListener(() => SelectType(type, img));

            var label = CreateLabel(go.transform, type.tileName, 14, ReadableTextColor(type.EffectivePreviewColor));
            var labelRect = label.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
        }

        // 選択中のボタンだけ白に寄せて明るくし、他は元の色に戻す
        private void SelectType(TileType type, Image buttonImage)
        {
            if (hexGridManager != null) hexGridManager.DebugOverrideType = type;

            if (_selectedImage != null)
                _selectedImage.color = _buttonBaseColors[_selectedImage];

            buttonImage.color = Color.Lerp(_buttonBaseColors[buttonImage], Color.white, 0.5f);
            _selectedImage = buttonImage;
        }

        private static GameObject CreateLabel(Transform parent, string text, int fontSize, Color color)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();
            var t = go.AddComponent<Text>();
            t.text      = text;
            t.alignment = TextAnchor.MiddleCenter;
            t.color     = color;
            t.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.fontSize  = fontSize;
            return go;
        }

        private static Color ReadableTextColor(Color background)
        {
            float luminance = 0.299f * background.r + 0.587f * background.g + 0.114f * background.b;
            return luminance > 0.6f ? Color.black : Color.white;
        }
    }
}
