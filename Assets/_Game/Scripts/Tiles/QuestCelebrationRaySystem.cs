// 役割: 輪郭をなぞり終えたあと、その一帯の外周から淡い光柱を一斉に立ち上げる祝福の演出。
//
//       ★なぞる側からは「終わった」という事実だけを受け取り、立てる場所は自分で決める。
//         祝う対象タイル（QuestTileSelectionResolvedEvent）を自分で持っておき、
//         外周は TileOutlineGeometry で求め直す。
//         演出どうしが座標列をやり取りしないので、片方を作り替えても他方が壊れない。
//
//       ★穴の輪には立てない。
//         穴は「まだ埋まっていない場所」なので、そこから光が出ると
//         達成した領域と読み違えてしまう。
//
//       ★地面を光らせる別のエフェクトは作らない。
//         光柱の足元をいちばん明るくすれば、Bloomが滲ませて地面が光って見える。
//         タイルのマテリアルは共有されているので、そちらを光らせると
//         1枚のつもりが全部光る（家の窓の灯りで踏んだのと同じ落とし穴）。
//
//       URPに体積光は無いので、縦長の板をカメラの方へ向けて立てる。
//       板は1枚のメッシュへまとめるので描画は1回で済む。

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ElfVillage.Core;

namespace ElfVillage.Tiles
{
    public class QuestCelebrationRaySystem : MonoBehaviour
    {
        [Header("間と時間")]
        [Tooltip("なぞり終わってから光柱が立ち上がるまでの間")]
        [SerializeField] private float _startDelay = 0.15f;

        [Tooltip("光柱が伸びきるまでの秒数")]
        [SerializeField] private float _riseDuration = 0.45f;

        [Tooltip("伸びきってから消えきるまでの秒数")]
        [SerializeField] private float _fadeDuration = 1.6f;

        [Header("本数")]
        [Tooltip("外周1辺につき1本を基準にした割合。大きな領域で光の壁になるようなら下げる")]
        [SerializeField, Range(0.1f, 1f)] private float _density = 1f;

        [SerializeField] private int _minRayCount = 3;
        [SerializeField] private int _maxRayCount = 20;

        [Header("光柱の形")]
        [SerializeField] private float _height    = 4.5f;
        [SerializeField] private float _baseWidth = 0.8f;
        [SerializeField] private float _topWidth  = 1.3f;

        [Tooltip("1本ごとの高さ・幅の揺らぎ。0だと全部同じ寸法で人工的に見える")]
        [SerializeField, Range(0f, 0.5f)] private float _sizeJitter = 0.15f;

        [SerializeField] private Color _lightColor = new Color(1f, 0.94f, 0.72f);

        [Tooltip("足元の明るさ。上げるほど後ろの景色が透けなくなる")]
        [SerializeField, Range(0.1f, 1f)] private float _intensity = 0.6f;

        [Tooltip("タイル上面からどれだけ浮かせて足元を置くか")]
        [SerializeField] private float _baseLift = 0.01f;

        /// <summary>
        /// 光柱1本あたりの頂点数（足元・中腹・上端の3段 × 左端・中心・右端の3列）。
        /// ★左右の端を必ず透明にするために、中心の列を持たせている。
        ///   左右2列だけだと板の側面が硬い直線で切れ、光ではなく「すりガラスの板」に見える。
        /// </summary>
        private const int VerticesPerRay = 9;

        // ★祝いのたびに作らない。1つ作って使い回し、自分が消えるときだけ捨てる
        private Material   _sharedMaterial;
        private GameObject _current;
        private Coroutine  _routine;

        // 祝う対象。なぞりが終わるまで持っておく
        private readonly List<HexTile> _targetTiles = new();

        private void Awake()
        {
            _sharedMaterial = BuildMaterial();
        }

        private void OnEnable()
        {
            EventBus.Subscribe<QuestTileSelectionResolvedEvent>(OnTilesResolved);
            EventBus.Subscribe<QuestOutlineTraceCompletedEvent>(OnTraceCompleted);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<QuestTileSelectionResolvedEvent>(OnTilesResolved);
            EventBus.Unsubscribe<QuestOutlineTraceCompletedEvent>(OnTraceCompleted);
            StopCurrent();
            _targetTiles.Clear();
        }

        private void OnDestroy()
        {
            StopCurrent();
            if (_sharedMaterial != null) Destroy(_sharedMaterial);
        }

        // ── 受け取り ──────────────────────────────────────────────────

        private void OnTilesResolved(QuestTileSelectionResolvedEvent evt)
        {
            // 新しい祝いが始まったので、前の光柱は畳む
            StopCurrent();

            _targetTiles.Clear();
            if (evt.Tiles == null) return;
            foreach (var tile in evt.Tiles)
                if (tile != null) _targetTiles.Add(tile);
        }

        private void OnTraceCompleted(QuestOutlineTraceCompletedEvent evt)
        {
            if (_targetTiles.Count == 0) return;

            var positions = BuildRayPositions();
            if (positions.Count == 0) return;

            StopCurrent();
            _routine = StartCoroutine(Rise(positions));
        }

        private void StopCurrent()
        {
            // ★先にコルーチンを止める。止めずに親だけ捨てると、
            //   古いコルーチンが次の演出の親を消しにいく
            if (_routine != null)
            {
                StopCoroutine(_routine);
                _routine = null;
            }

            if (_current != null)
            {
                Destroy(_current);
                _current = null;
            }
        }

        // ── どこに立てるか ────────────────────────────────────────────

        private List<Vector3> BuildRayPositions()
        {
            var positions = new List<Vector3>();
            var buffer    = new List<Vector3>();

            foreach (var loop in TileOutlineGeometry.BuildWorldLoops(_targetTiles, _baseLift))
            {
                if (!TileOutlineGeometry.IsOuterLoop(loop)) continue;   // 穴には立てない

                CelebrationRayLayout.SelectPositions(loop, _density, _minRayCount, _maxRayCount, buffer);
                positions.AddRange(buffer);
            }

            return positions;
        }

        // ── 立ち上がり ────────────────────────────────────────────────

        private IEnumerator Rise(List<Vector3> positions)
        {
            // 間を置いてから一斉に立ち上げる。「なぞる → 完成 → 祝福」の3段に見せるため
            if (_startDelay > 0f) yield return new WaitForSeconds(_startDelay);

            _current = new GameObject("CelebrationRays");
            _current.transform.SetParent(transform, worldPositionStays: false);

            var filter   = _current.AddComponent<MeshFilter>();
            var renderer = _current.AddComponent<MeshRenderer>();
            renderer.sharedMaterial    = _sharedMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows    = false;

            var mesh = new Mesh { name = "CelebrationRays" };
            mesh.MarkDynamic();
            filter.mesh = mesh;

            var vertices = new Vector3[positions.Count * VerticesPerRay];
            var colors   = new Color[positions.Count * VerticesPerRay];

            // ★先に頂点を入れてから三角形を渡す。
            //   頂点が空のままインデックスを渡すと、範囲外としてUnityに捨てられ、
            //   以後どれだけ頂点を更新しても何も描かれない
            UpdateMesh(mesh, positions, vertices, colors, reach: 0f, alpha: 1f);
            mesh.triangles = BuildTriangles(positions.Count);

            float elapsed = 0f;
            float total   = _riseDuration + _fadeDuration;

            while (elapsed < total)
            {
                float grow  = _riseDuration > 0f ? Mathf.Clamp01(elapsed / _riseDuration) : 1f;
                // 伸びは終わりを緩める。まっすぐ伸びると機械的に見える
                float reach = 1f - (1f - grow) * (1f - grow);
                float alpha = elapsed <= _riseDuration
                    ? 1f
                    : 1f - Mathf.Clamp01((elapsed - _riseDuration) / Mathf.Max(_fadeDuration, 0.0001f));

                UpdateMesh(mesh, positions, vertices, colors, reach, alpha);

                elapsed += Time.deltaTime;
                yield return null;
            }

            _routine = null;
            // ★自分が作った親だけを消す。差し替え後の新しい親には触れない
            if (_current != null && ReferenceEquals(_current.GetComponent<MeshFilter>(), filter))
            {
                Destroy(_current);
                _current = null;
            }
            Destroy(mesh);
        }

        /// <summary>
        /// 光柱の板をカメラの方へ向け直す。縦は常に垂直のまま、横向きだけ追従させる
        /// （既存の木・花のビルボードと同じ考え方）。
        /// </summary>
        private void UpdateMesh(Mesh mesh, List<Vector3> positions, Vector3[] vertices, Color[] colors,
                                 float reach, float alpha)
        {
            Camera camera = Camera.main;

            for (int i = 0; i < positions.Count; i++)
            {
                Vector3 basePoint = positions[i];

                // 1本ごとの揺らぎ。位置から決めるので、同じ場所なら毎回同じ形になる
                float jitter = Hash01(basePoint);
                float height = _height    * (1f + (jitter - 0.5f) * 2f * _sizeJitter) * reach;
                float bottom = _baseWidth * (1f + (jitter - 0.5f) * _sizeJitter);
                float top    = _topWidth  * (1f + (jitter - 0.5f) * _sizeJitter);

                Vector3 right = Vector3.right;
                if (camera != null)
                {
                    Vector3 toCamera = camera.transform.position - basePoint;
                    toCamera.y = 0f;
                    if (toCamera.sqrMagnitude > 1e-6f)
                        right = Vector3.Cross(Vector3.up, toCamera.normalized);
                }

                Vector3 up  = Vector3.up * height;
                Vector3 mid = basePoint + up * 0.35f;
                Vector3 tip = basePoint + up;

                float midWidth = Mathf.Lerp(bottom, top, 0.35f);

                int v = i * VerticesPerRay;
                WriteRow(vertices, colors, v + 0, basePoint, right, bottom,   _lightColor, alpha * _intensity);
                WriteRow(vertices, colors, v + 3, mid,       right, midWidth, _lightColor, alpha * _intensity * 0.35f);
                WriteRow(vertices, colors, v + 6, tip,       right, top,      _lightColor, 0f);
            }

            mesh.vertices = vertices;
            mesh.colors   = colors;
            mesh.RecalculateBounds();
        }

        /// <summary>
        /// 光柱の1段（左端・中心・右端）を書き込む。
        /// 中心だけを明るくし、左右の端は透明にすることで、輪郭の無い光に見せる。
        /// </summary>
        private static void WriteRow(Vector3[] vertices, Color[] colors, int v,
                                      Vector3 center, Vector3 right, float width, Color color, float alpha)
        {
            vertices[v + 0] = center - right * (width * 0.5f);
            vertices[v + 1] = center;
            vertices[v + 2] = center + right * (width * 0.5f);

            var edge = new Color(color.r, color.g, color.b, 0f);
            var core = new Color(color.r, color.g, color.b, alpha);

            colors[v + 0] = edge;
            colors[v + 1] = core;
            colors[v + 2] = edge;
        }

        private static int[] BuildTriangles(int rayCount)
        {
            // 1本につき 2段 × 2列 × 三角形2枚 = 8枚
            var triangles = new int[rayCount * 24];
            for (int i = 0; i < rayCount; i++)
            {
                int v = i * VerticesPerRay;
                int t = i * 24;

                for (int row = 0; row < 2; row++)
                {
                    int lower = v + row * 3;
                    int upper = lower + 3;

                    for (int col = 0; col < 2; col++)
                    {
                        int o = t + (row * 2 + col) * 6;
                        triangles[o + 0] = lower + col;     triangles[o + 1] = upper + col;     triangles[o + 2] = lower + col + 1;
                        triangles[o + 3] = lower + col + 1; triangles[o + 4] = upper + col;     triangles[o + 5] = upper + col + 1;
                    }
                }
            }
            return triangles;
        }

        /// <summary>位置から0〜1の値を作る。同じ場所なら毎回同じ揺らぎになる。</summary>
        private static float Hash01(Vector3 position)
        {
            float value = Mathf.Sin(position.x * 12.9898f + position.z * 78.233f) * 43758.5453f;
            return value - Mathf.Floor(value);
        }

        private static Material BuildMaterial()
        {
            // 頂点カラーが乗るシェーダを使う。既存の蛍・川・輪郭と同じ選び方
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                      ?? Shader.Find("Sprites/Default");

            var mat = new Material(shader) { name = "QuestCelebrationRay_Runtime" };

            // 加算合成。暗い地面でも光って見え、Bloomが柔らかく滲ませてくれる
            if (mat.HasProperty("_Surface"))  mat.SetFloat("_Surface", 1f);
            // ★_Blendは 0=Alpha 1=Premultiply 2=Additive 3=Multiply。
            //   ここを1にしていたせいで、加算のつもりが不透明に近い合成になり、
            //   光ではなく「すりガラスの板」に見えていた。
            //   SrcBlend/DstBlendだけ指定しても、この値からシェーダ側が上書きしてくる
            if (mat.HasProperty("_Blend"))    mat.SetFloat("_Blend", 2f);
            if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            if (mat.HasProperty("_ZWrite"))   mat.SetFloat("_ZWrite", 0f);
            if (mat.HasProperty("_Cull"))     mat.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);

            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return mat;
        }
    }
}
