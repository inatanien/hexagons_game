// 役割: 森タイルの木を「常にカメラを向く板（ビルボード）」として描く実験的な差し替え。
//       プリミティブ（円柱＋球）で組んでいた木の代わりに、
//       絵として描かれた木のテクスチャを1枚の板に貼って立てる。
//
//       ★なぜ板1枚なのか
//         絵にはすでに立体感・陰影・枝葉が描き込まれている。3Dで作り込むより、
//         絵をそのまま立てた方が密度が出るうえ、1本あたりの頂点数が激減する。
//
//       ★根元の円錐
//         板だけだと地面との接地が「紙が刺さっている」ように見える。
//         幹の付け根に小さな円錐を置くと、板と地面の継ぎ目が隠れて立体感が出る。
//
//       ★回転の扱い
//         Y軸まわりだけ回してカメラを向く。上下にも向けると木が寝てしまうため。
//         木ごとにUpdateを持たせると本数ぶんの呼び出しが発生するので、
//         このシステムが全ての板をまとめて1回のLateUpdateで回す。

using System.Collections.Generic;
using UnityEngine;

namespace ElfVillage.Tiles
{
    public class TreeBillboardSystem : MonoBehaviour
    {
        [Header("木の絵")]
        [Tooltip("木のビルボードに使う画像。木ごとに重み付きで選ばれる（TreeVariantWeights）。" +
                  "未設定の場合は従来どおりプリミティブ（円柱＋球）の木になる")]
        [SerializeField] private Texture2D[] _treeTextures;

        [Header("板のサイズ")]
        [Tooltip("木の高さ（ワールド単位）。個体ごとにseedで±15%ばらつく")]
        [SerializeField] private float _height = 0.95f;
        [Tooltip("高さに対する横幅の比率。絵が正方形なら1.0が素直")]
        [SerializeField] private float _widthRatio = 0.95f;

        [Tooltip("板を地面へどれだけ沈めるか（高さに対する割合）。\n" +
                  "★木の画像は下端に透明な余白を持っている。板の下端をそのまま地面へ合わせると、" +
                  "その余白のぶんだけ絵が宙に浮いて見える。\n" +
                  "現在の10枚を実測した余白は 3.3〜6.6%（平均5.5%）で、既定値はその平均。")]
        [Range(0f, 0.2f)]
        [SerializeField] private float _groundSinkRatio = 0.055f;

        /// <summary>
        /// 実配置・プレビュー双方の生成経路（HexTileの静的メソッド）から参照できるようにする。
        /// ★Sceneに置かれていなければnullのままで、その場合は従来のプリミティブ木が使われる。
        ///   「差し替えをやめたければコンポーネントを外すだけ」で元に戻せる。
        /// </summary>
        public static TreeBillboardSystem Instance { get; private set; }

        // 木ごとの板。まとめて回すためにここで保持する。
        private readonly List<Transform> _billboards = new();

        // 画像1枚につきMaterial 1つ。同じ絵の木どうしはまとめて描画される。
        private Material[] _materials;
        // _materialsと同じ並びの抽選重み（画像の名前から決まる）。
        private int[] _weights;

        private Camera _camera;

        public bool HasTextures => _materials != null && _materials.Length > 0;

        private void Awake()
        {
            BuildMaterials();
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;

            // ランタイム生成物は自動では解放されないため明示的に破棄する。
            if (_materials != null)
                foreach (var m in _materials) DestroyRuntimeAsset(m);
            _materials = null;
            _weights   = null;

            _billboards.Clear();
        }

        private static void DestroyRuntimeAsset(Object asset)
        {
            if (asset == null) return;
            if (Application.isPlaying) Destroy(asset);
            else                       DestroyImmediate(asset);
        }

        // ── 生成 ──────────────────────────────────────────────────────

        /// <summary>
        /// 木1本を板＋根元の円錐として生成する。
        /// 画像が未設定なら false を返し、呼び出し側は従来のプリミティブ木へ切り替える。
        /// </summary>
        /// <param name="parent">タイルのTransform</param>
        /// <param name="offset">タイル内のローカル位置（地面高さは含まない）</param>
        /// <param name="ground">タイル上面の高さ</param>
        /// <param name="seed">個体差（絵柄・大きさ）を決める種</param>
        public bool TrySpawnTree(Transform parent, Vector3 offset, float ground, int seed)
        {
            if (!HasTextures || parent == null) return false;

            int index = TreeVariantWeights.Select(_weights, seed);
            if (index < 0 || index >= _materials.Length) index = 0;

            float sizeMul = SizeMultiplier(seed);

            float h = Mathf.Max(0.01f, _height * sizeMul);
            float w = Mathf.Max(0.01f, h * Mathf.Max(0.01f, _widthRatio));

            var basePos = offset + new Vector3(0f, ground, 0f);

            // ── 板 ────────────────────────────────────────────────
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "TreeBillboard";
            quad.transform.SetParent(parent, false);

            // Quadは中心原点なので、まず半分だけ持ち上げて下端を地面へ合わせる。
            // そのうえで、絵の下端にある透明な余白のぶんだけ沈めて、
            // 「描かれた木の根元」が地面へ接するようにする（浮いて見えるのを防ぐ）。
            float sink = Mathf.Clamp(_groundSinkRatio, 0f, 0.2f) * h;
            quad.transform.localPosition = basePos + new Vector3(0f, h * 0.5f - sink, 0f);
            quad.transform.localScale    = new Vector3(w, h, 1f);

            RemoveCollider(quad);

            var quadRenderer = quad.GetComponent<MeshRenderer>();
            if (quadRenderer != null)
            {
                quadRenderer.sharedMaterial = _materials[index];
                // 板は薄いので影を落とすと不自然な線が出る。受けるだけにする。
                quadRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            _billboards.Add(quad.transform);
            return true;
        }

        /// <summary>個体ごとの大きさのばらつき（0.85〜1.15倍）。</summary>
        private static float SizeMultiplier(int seed) => 0.85f + (Mathf.Abs(seed) % 31) / 100f;

        // ── ビルボードの向き ──────────────────────────────────────────

        private void LateUpdate()
        {
            if (_billboards.Count == 0) return;

            if (_camera == null) _camera = Camera.main;
            if (_camera == null) return;

            Vector3 camPos = _camera.transform.position;

            // 破棄済みの板を詰めながら、まとめて向きを更新する。
            int write = 0;
            for (int i = 0; i < _billboards.Count; i++)
            {
                var t = _billboards[i];
                if (t == null) continue;          // タイルごと破棄された

                FaceCamera(t, camPos);
                _billboards[write++] = t;
            }
            if (write < _billboards.Count)
                _billboards.RemoveRange(write, _billboards.Count - write);
        }

        /// <summary>
        /// カメラの方を向かせる。★Y軸まわりだけ回す。
        /// 上下にも向けると、カメラを見下ろしたときに木が地面へ寝てしまうため。
        /// </summary>
        private static void FaceCamera(Transform billboard, Vector3 cameraPosition)
        {
            Vector3 dir = billboard.position - cameraPosition;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.000001f) return;   // 真上/真下からは向きを決められない

            billboard.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
        }

        // ── ランタイム生成のリソース ──────────────────────────────────

        private void BuildMaterials()
        {
            if (_treeTextures == null || _treeTextures.Length == 0) return;

            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader == null) return;

            var list  = new List<Material>();
            var names = new List<string>();
            foreach (var tex in _treeTextures)
            {
                if (tex == null) continue;

                var mat = new Material(shader) { name = "TreeBillboard_" + tex.name };

                if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
                if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
                mat.mainTexture = tex;

                // ★半透明ではなくアルファクリップ（切り抜き）にする。
                //   木は互いに重なるため、半透明にすると描画順で葉が消えたり
                //   前後関係が崩れたりする。切り抜きなら深度が正しく書かれる。
                if (mat.HasProperty("_AlphaClip")) mat.SetFloat("_AlphaClip", 1f);
                if (mat.HasProperty("_Cutoff"))    mat.SetFloat("_Cutoff", 0.5f);
                mat.EnableKeyword("_ALPHATEST_ON");
                mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;

                // 絵に陰影が描かれているので、光沢は消してフラットに受ける。
                if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0f);
                if (mat.HasProperty("_Metallic"))   mat.SetFloat("_Metallic", 0f);

                list.Add(mat);
                names.Add(tex.name);
            }

            _materials = list.ToArray();
            _weights   = TreeVariantWeights.BuildWeights(names.ToArray());
        }

        private static void RemoveCollider(GameObject go)
        {
            // 木がタイルへのレイキャストを妨げないよう当たり判定を外す
            // （HexGridManagerのRaycastはLayerMaskなしで最初に当たったものへ打ち切るため）。
            var col = go.GetComponent<Collider>();
            if (col == null) return;
            if (Application.isPlaying) Destroy(col);
            else                       DestroyImmediate(col);
        }
    }
}
