// 役割: 森タイルの木を「常にカメラを向く板（ビルボード）」として描く。
//       プリミティブ（円柱＋球）で組んでいた木の代わりに、
//       絵として描かれた木のテクスチャを1枚の板に貼って立てる。
//
//       ★なぜ板1枚なのか
//         絵にはすでに立体感・陰影・枝葉が描き込まれている。3Dで作り込むより、
//         絵をそのまま立てた方が密度が出るうえ、1本あたりの頂点数が激減する。
//
//       ★向きの決め方（木ごとにカメラ位置へ水平正対）
//         板1枚ずつ「その木からカメラ位置へ向かう方向」を向く。上下には傾けない
//         （見下ろしたときに木が地面へ寝てしまうため）。
//
//         ここを「全ての板をカメラのYawで揃える」方式に変えると更新判定は単純になるが、
//         このゲームのカメラはPerspective（FOV60・見下ろし40度）なので、
//         画面端の木ほど正対からずれ、実測で最大約67度・見かけ幅39%まで潰れてしまう。
//         森が画面端で痩せて見えるため、正対方式を維持する。
//
//       ★更新コスト
//         木ごとにUpdateを持たせず、このシステムが全ての板をまとめて回す。
//         さらに「カメラのTransformが動いたフレーム」だけ回す。
//         正対方式では、カメラが平行移動しても各木の正しい向きが変わるため、
//         回転だけでなく位置の変化も見る必要がある。
//         逆に言えば、眺めているだけのとき（このゲームで最も多い状態）は
//         カメラが1ミリも動かないので、毎フレーム処理は完全にゼロになる。
//
//       ★将来の性能課題（未着手・記録のみ）
//         ここで抑えられているのは「回転更新のCPUコスト」だけで、描画側は手つかず。
//         Forest 100枚（木2400本）で drawCalls 3605 / batches 3427 を実測している
//         （2026-07-26 時点。木陰100枚ぶんは drawCalls +72 / setPass +7）。
//         板1枚ごとにRendererを持ち、絵柄ごとにMaterialが分かれているためで、
//         描画が問題になった時点で次の順に検討する:
//           1. 木画像10種のAtlas化    2. Material数の統合
//           3. GPU Instancing / Shader側でのBillboard化
//           4. それでも足りなければLOD
//         「回転更新が軽い＝森の描画が軽い」ではない点に注意。

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

        [Header("更新")]
        [Tooltip("カメラのYawがこの角度以上変わったら全ての板の向きを更新する。\n" +
                  "小さすぎると毎フレーム更新になり、大きすぎるとゆっくり回したときに角度が階段状に見える。")]
        [Range(0.01f, 1f)]
        [SerializeField] private float _yawUpdateThresholdDeg = 0.05f;

        [Tooltip("カメラがこの距離以上動いたら全ての板の向きを更新する。\n" +
                  "★木ごとにカメラへ正対させるため、カメラが平行移動しただけでも正しい向きは変わる。\n" +
                  "  近くの木ほど影響が大きいので、しきい値はごく小さくしておく。")]
        [Range(0.001f, 0.5f)]
        [SerializeField] private float _positionUpdateThreshold = 0.005f;

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

        // 最後に全体へ適用したときのカメラ状態（ここから動いていなければ更新を省く）。
        private Vector3 _appliedCameraPosition;
        private float   _appliedYaw;
        private bool    _hasApplied;

        // カメラが取れないまま木が登録された等、次のフレームで必ず適用し直したい状態。
        private bool _needsFullApply;

        // 破棄済み要素の掃除を「登録が一定数たまったとき」にまとめて行うためのカウンタ。
        // 配置ゴーストは座標が変わるたびに作り直されるため、掃除しないとリストが伸び続ける。
        private int _addedSinceCompact;
        private const int CompactInterval = 128;

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
        /// 木1本を板として生成する。
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

            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "TreeBillboard";
            quad.transform.SetParent(parent, false);

            // Quadは中心原点なので、まず半分だけ持ち上げて下端を地面へ合わせる。
            // そのうえで、絵の下端にある透明な余白のぶんだけ沈めて、
            // 「描かれた木の根元」が地面へ接するようにする（浮いて見えるのを防ぐ）。
            // ★沈み量は個体の高さhに比例させるので、大きい木も小さい木も同じ割合で接地する。
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

            Register(quad.transform);
            return true;
        }

        /// <summary>個体ごとの大きさのばらつき（0.85〜1.15倍）。</summary>
        private static float SizeMultiplier(int seed) => 0.85f + (Mathf.Abs(seed) % 31) / 100f;

        private void Register(Transform billboard)
        {
            _billboards.Add(billboard);

            // ★生まれた瞬間に正しい向きにする。
            //   カメラが止まっていると次のLateUpdateは丸ごと省かれるため、
            //   ここで向けておかないと、置いたばかりの木だけ横を向いたままになる。
            var cam = ResolveCamera();
            if (cam != null) FaceCamera(billboard, cam.transform.position);
            else             _needsFullApply = true;   // カメラが無いので次に取れたフレームで直す

            _addedSinceCompact++;
            if (_addedSinceCompact >= CompactInterval) Compact();
        }

        // ── ビルボードの向き ──────────────────────────────────────────

        private void LateUpdate()
        {
            if (_billboards.Count == 0) return;

            var cam = ResolveCamera();
            if (cam == null)
            {
                // カメラが一時的に取れない（シーン遷移など）。例外は出さず、
                // 戻ってきたフレームで必ず全体へ適用し直す。
                _needsFullApply = true;
                return;
            }

            var   camPos = cam.transform.position;
            float yaw    = cam.transform.eulerAngles.y;

            float posThreshold = Mathf.Max(0.001f, _positionUpdateThreshold);
            bool cameraMoved = !_hasApplied
                            || Mathf.Abs(Mathf.DeltaAngle(_appliedYaw, yaw)) >= Mathf.Max(0.01f, _yawUpdateThresholdDeg)
                            || (camPos - _appliedCameraPosition).sqrMagnitude >= posThreshold * posThreshold;

            // ★カメラが止まっているフレームは、ここで完全に打ち切る（毎フレーム処理ゼロ）。
            if (!cameraMoved && !_needsFullApply) return;

            ApplyAll(camPos, yaw);
        }

        private Camera ResolveCamera()
        {
            if (_camera == null) _camera = Camera.main;
            return _camera;
        }

        /// <summary>登録済みの全ての板へ向きを適用し、ついでに破棄済み要素を詰める。</summary>
        private void ApplyAll(Vector3 cameraPosition, float yawDeg)
        {
            int write = 0;
            for (int i = 0; i < _billboards.Count; i++)
            {
                var t = _billboards[i];
                if (t == null) continue;          // タイルごと破棄された

                FaceCamera(t, cameraPosition);
                _billboards[write++] = t;
            }
            if (write < _billboards.Count)
                _billboards.RemoveRange(write, _billboards.Count - write);

            _addedSinceCompact     = 0;
            _appliedCameraPosition = cameraPosition;
            _appliedYaw            = yawDeg;
            _hasApplied            = true;
            _needsFullApply        = false;
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

        /// <summary>破棄済み要素だけを詰める（向きは変えない）。</summary>
        private void Compact()
        {
            int write = 0;
            for (int i = 0; i < _billboards.Count; i++)
            {
                var t = _billboards[i];
                if (t == null) continue;
                _billboards[write++] = t;
            }
            if (write < _billboards.Count)
                _billboards.RemoveRange(write, _billboards.Count - write);

            _addedSinceCompact = 0;
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
