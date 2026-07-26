// 役割: Forestタイル1枚につき1枚だけ、地面に薄い木陰を敷く。
//
//       ★なぜ木1本ごとの丸影を作らないのか
//         24本×タイル数ぶんのRendererが増える。まとめて1枚にすれば
//         「タイル1枚 = GameObject 1個 / Renderer 1個」で済む。
//
//       ★なぜ真円ではなく、まだらな塊なのか
//         真円を並べると地面に六角形の格子模様が浮かび上がってしまう。
//         柔らかい塊がいくつか集まった形にして、タイルごとに回転・反転させると、
//         隣り合ったForestタイルの木陰が繋がって一枚の木漏れ日のように見える。
//
//       ★なぜ重なりを max で合成するのか（テクスチャ生成）
//         塊を足し算で重ねると、重なった部分だけ真っ黒になって「汚れ」に見える。
//         濃い方を採用すれば、どれだけ重ねても設定した濃さを超えない。
//
//       ★リソースの所有と破棄
//         テクスチャとMaterialはこのコンポーネントがAwakeで1つだけ作り、OnDestroyで破棄する。
//         タイル側はsharedMaterialを参照するだけで、複製は一切作らない。
//         Instanceは自分がAwakeしたときに上書きし、OnDestroyで自分のときだけnullへ戻すので、
//         Domain Reload無効（静的変数が持ち越される）でも古い参照が残らない。

using UnityEngine;

namespace ElfVillage.Tiles
{
    public class TileShadeSystem : MonoBehaviour
    {
        [Header("見た目")]
        [Tooltip("木陰の色。黒ではなく、地面になじむ暗い緑〜灰色にする")]
        [SerializeField] private Color _shadeColor = new Color(0.17f, 0.23f, 0.18f, 1f);

        [Tooltip("最も濃い部分の不透明度。隣タイルと重なっても暗くなりすぎない値にする")]
        [Range(0f, 0.6f)]
        [SerializeField] private float _maxAlpha = 0.24f;

        [Tooltip("木陰1枚の一辺（ワールド単位）。タイルの角までが2.0なので、" +
                  "4.0でちょうど角に届き、辺の方向へは少しはみ出す")]
        [SerializeField] private float _size = 4.0f;

        [Header("生成テクスチャ")]
        [Tooltip("木陰テクスチャの解像度。輪郭がぼやけた絵なので大きくしても効果が薄い")]
        [SerializeField] private int _textureSize = 256;

        [Tooltip("重ねる柔らかい塊の数。少ないと真円に、多いと均一な円盤に近づく")]
        [Range(3, 16)]
        [SerializeField] private int _blobCount = 9;

        /// <summary>
        /// Sceneに置かれていなければnullのまま＝木陰なしで従来どおり動く。
        /// 「やめたければコンポーネントを外すだけ」で戻せるようにするため。
        /// </summary>
        public static TileShadeSystem Instance { get; private set; }

        private Material  _material;
        private Texture2D _texture;

        /// <summary>Materialが1つだけ共有されていることを検証できるようにする（複製していない証明）。</summary>
        public Material SharedMaterial => _material;

        public bool IsReady => _material != null;

        private void Awake()
        {
            Build();
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;

            DestroyRuntimeAsset(_material);
            DestroyRuntimeAsset(_texture);
            _material = null;
            _texture  = null;
        }

        private static void DestroyRuntimeAsset(Object asset)
        {
            if (asset == null) return;
            if (Application.isPlaying) Destroy(asset);
            else                       DestroyImmediate(asset);
        }

        // ── 生成 ──────────────────────────────────────────────────────

        /// <summary>
        /// タイル1枚ぶんの木陰を敷く。既にこのタイルへ敷かれているかどうかは呼び出し側の責務
        /// （プロップのルートごと作り直される前提のため）。
        /// </summary>
        /// <param name="parent">タイルのプロップルート</param>
        /// <param name="q">タイル座標q（向き・反転・大きさの種）</param>
        /// <param name="r">タイル座標r</param>
        /// <param name="tileHeight">タイルの厚み</param>
        public bool TrySpawnShade(Transform parent, int q, int r, float tileHeight)
        {
            if (!IsReady || parent == null) return false;

            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "TileShade";
            quad.transform.SetParent(parent, false);

            // Quadは既定で+Zを向くので、X軸-90度で上向き（+Y）に寝かせる。
            // Yの回転が木陰の面内回転になる。
            quad.transform.localPosition = new Vector3(0f, TileShadeLayout.LocalY(tileHeight), 0f);
            quad.transform.localRotation = Quaternion.Euler(-90f, TileShadeLayout.RotationDeg(q, r), 0f);

            float side = Mathf.Max(0.01f, _size * TileShadeLayout.SizeMultiplier(q, r));
            float signedX = TileShadeLayout.IsMirrored(q, r) ? -side : side;   // 負のスケールで左右反転
            quad.transform.localScale = new Vector3(signedX, side, 1f);

            RemoveCollider(quad);

            var renderer = quad.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial    = _material;   // 共有。タイルごとに複製しない
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows    = false;
            }

            return true;
        }

        private static void RemoveCollider(GameObject go)
        {
            // タイル選択のレイキャストを妨げないよう当たり判定を外す
            // （HexGridManagerのRaycastはLayerMaskなしで最初に当たったものへ打ち切るため）。
            var col = go.GetComponent<Collider>();
            if (col == null) return;
            if (Application.isPlaying) Destroy(col);
            else                       DestroyImmediate(col);
        }

        // ── ランタイム生成のリソース ──────────────────────────────────

        private void Build()
        {
            _texture = BuildShadeTexture(
                Mathf.Clamp(_textureSize, 32, 1024),
                Mathf.Clamp(_blobCount, 3, 16),
                _shadeColor,
                Mathf.Clamp(_maxAlpha, 0f, 0.6f));

            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Transparent");
            if (shader == null) { DestroyRuntimeAsset(_texture); _texture = null; return; }

            _material = new Material(shader) { name = "TileShade_Shared" };

            if (_material.HasProperty("_BaseMap")) _material.SetTexture("_BaseMap", _texture);
            if (_material.HasProperty("_MainTex")) _material.SetTexture("_MainTex", _texture);
            _material.mainTexture = _texture;
            if (_material.HasProperty("_BaseColor")) _material.SetColor("_BaseColor", Color.white); // 色はテクスチャに焼いてある

            // ★URPのUnlitは既定がOpaqueで、明示的に切り替えないとアルファが無視される。
            if (_material.HasProperty("_Surface")) _material.SetFloat("_Surface", 1f);   // Transparent
            if (_material.HasProperty("_Blend"))   _material.SetFloat("_Blend",   0f);   // Alpha
            if (_material.HasProperty("_ZWrite"))  _material.SetFloat("_ZWrite",  0f);
            if (_material.HasProperty("_SrcBlend")) _material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (_material.HasProperty("_DstBlend")) _material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            // 左右反転に負のスケールを使うため、裏面が消えないよう両面描画にする。
            if (_material.HasProperty("_Cull")) _material.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);

            _material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            _material.DisableKeyword("_ALPHATEST_ON");

            // 不透明の地面より後、葉・花びら・精霊などの他の半透明表現より先に描く。
            _material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent - 50;
        }

        /// <summary>
        /// 柔らかい塊をいくつか重ねた、まだらな木陰テクスチャを作る。
        /// 塊の位置は黄金角スパイラルで散らすので、乱数を使わずに毎回同じ絵になる。
        /// </summary>
        private static Texture2D BuildShadeTexture(int size, int blobCount, Color color, float maxAlpha)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name       = "TileShade_Generated",
                wrapMode   = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

            // 塊の中心と半径を先に決める（正規化座標 -1〜1）。
            var blobX = new float[blobCount];
            var blobY = new float[blobCount];
            var blobR = new float[blobCount];
            const float goldenAngleDeg = 137.50776f;
            for (int b = 0; b < blobCount; b++)
            {
                float t     = (b + 0.5f) / blobCount;
                float dist  = Mathf.Sqrt(t) * 0.42f;                  // 中心寄りに密、外へ向かって疎
                float angle = b * goldenAngleDeg * Mathf.Deg2Rad;
                blobX[b] = Mathf.Cos(angle) * dist;
                blobY[b] = Mathf.Sin(angle) * dist;
                blobR[b] = 0.30f + 0.16f * TileVisualHash.Unit(TileVisualHash.Mix(b * 7919 + 13));
            }

            var pixels = new Color32[size * size];
            var rgb    = new Color(color.r, color.g, color.b, 1f);

            for (int y = 0; y < size; y++)
            {
                float v = (y + 0.5f) / size * 2f - 1f;
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size * 2f - 1f;

                    // ★足し算ではなく max で合成する。重ねても設定した濃さを超えない。
                    float a = 0f;
                    for (int b = 0; b < blobCount; b++)
                    {
                        float dx = (u - blobX[b]) / blobR[b];
                        float dy = (v - blobY[b]) / blobR[b];
                        float d2 = dx * dx + dy * dy;
                        if (d2 >= 1f) continue;

                        float f = 1f - d2;
                        a = Mathf.Max(a, f * f * (3f - 2f * f));      // なめらかな減衰
                    }

                    // 外周は必ず0へ落とす。四角い縁も六角形の輪郭も見えないようにするため。
                    float radius = Mathf.Sqrt(u * u + v * v);
                    a *= 1f - EdgeSmoothStep(0.60f, 1f, radius);

                    var c = rgb;
                    c.a = a * maxAlpha;
                    pixels[y * size + x] = c;
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, false);
            return tex;
        }

        /// <summary>
        /// edge0 で0、edge1 で1になる、なめらかな段差（シェーダのsmoothstepと同じ意味）。
        /// ★Unityの Mathf.SmoothStep(from, to, t) は「fromからtoへ補間する」別物で、
        ///   しきい値として使うと中央でも 1-from の値が残ってしまう（木陰全体が薄くなる）。
        /// </summary>
        private static float EdgeSmoothStep(float edge0, float edge1, float x)
        {
            float t = Mathf.Clamp01((x - edge0) / Mathf.Max(0.0001f, edge1 - edge0));
            return t * t * (3f - 2f * t);
        }
    }
}
