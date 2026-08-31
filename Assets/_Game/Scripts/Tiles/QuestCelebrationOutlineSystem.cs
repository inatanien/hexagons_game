// 役割: クエスト達成で選ばれたタイル群の外周を、光の先端が一周する演出。
//       QuestTileSelectionResolvedEvent（対象タイルの解決結果）だけを購読する。
//       どのタイルを祝うかは QuestTileFocusTracker が決めるので、こちらは描くだけ。
//
//       ★輪の向きは HexBoundaryBuilder が決めたものをそのまま使う。
//         外周は反時計回り、穴は時計回りに揃っているので、
//         どちらも「材質を左手に見ながら走る」形になる。ここで向きを再判定しない。
//
//       ★角のワールド座標は、タイルの実際の transform と outerRadius から求める。
//         HexGridManager.tileSize（グリッドの間隔）と HexTile.outerRadius（見た目の大きさ）は
//         別々に設定できるため、座標だけから角を計算すると、
//         2つがずれている設定では輪郭だけが実際の六角形から浮く。
//         なぞりたいのは「見えている六角形の縁」なので、見えているものを基準にする。

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ElfVillage.Core;
using ElfVillage.HexGrid;

namespace ElfVillage.Tiles
{
    public class QuestCelebrationOutlineSystem : MonoBehaviour
    {
        [Header("走る時間")]
        [Tooltip("光が外周を一周するまでの秒数")]
        [SerializeField] private float _traceDuration = 1.1f;

        [Tooltip("一周したあと、尾が消えきるまでの秒数")]
        [SerializeField] private float _fadeOutDuration = 0.35f;

        [Header("見た目")]
        [Tooltip("尾の長さ。輪一周の長さに対する割合")]
        [SerializeField, Range(0.05f, 1f)] private float _tailRatio = 0.25f;

        [SerializeField] private Color _lightColor = new Color(1f, 0.94f, 0.72f);
        [SerializeField] private float _headWidth  = 0.22f;

        [Tooltip("タイル上面からどれだけ浮かせるか。0だと面と重なってちらつく")]
        [SerializeField] private float _lift = 0.03f;

        // ★祝いのたびに作らない。1つ作って全部の輪で共有し、自分が消えるときだけ捨てる
        private Material   _sharedMaterial;
        private GameObject _current;
        private Coroutine  _routine;

        private void Awake()
        {
            _sharedMaterial = BuildMaterial();
        }

        private void OnEnable()  => EventBus.Subscribe<QuestTileSelectionResolvedEvent>(OnTilesResolved);

        private void OnDisable()
        {
            EventBus.Unsubscribe<QuestTileSelectionResolvedEvent>(OnTilesResolved);
            StopCurrent();
        }

        private void OnDestroy()
        {
            StopCurrent();
            if (_sharedMaterial != null) Destroy(_sharedMaterial);
        }

        // ── 演出の開始 ────────────────────────────────────────────────

        private void OnTilesResolved(QuestTileSelectionResolvedEvent evt)
        {
            // 走っている途中で次の祝いが来たら、前のものは畳んでから差し替える
            StopCurrent();

            var loops = BuildWorldLoops(evt.Tiles);
            if (loops.Count == 0) return;

            _current = new GameObject("CelebrationOutline");
            _current.transform.SetParent(transform, worldPositionStays: false);

            var lines = new List<LineRenderer>(loops.Count);
            for (int i = 0; i < loops.Count; i++)
                lines.Add(CreateLine(_current.transform, i));

            _routine = StartCoroutine(Trace(_current, loops, lines));
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

        // ── 対象タイル → ワールド座標の輪 ─────────────────────────────

        private List<List<Vector3>> BuildWorldLoops(IReadOnlyList<HexTile> tiles)
        {
            var result = new List<List<Vector3>>();
            if (tiles == null || tiles.Count == 0) return result;

            // 重複や欠けたタイルが混じっても、輪が二重になったり落ちたりしないようにする
            var byCoord = new Dictionary<HexCoord, HexTile>();
            foreach (var tile in tiles)
            {
                if (tile == null) continue;
                byCoord[tile.Data.coord] = tile;
            }
            if (byCoord.Count == 0) return result;

            foreach (var loop in HexBoundaryBuilder.BuildLoops(byCoord.Keys))
            {
                var points = new List<Vector3>(loop.Count);
                foreach (var corner in loop)
                    if (TryCornerWorldPosition(corner, byCoord, out var point)) points.Add(point);

                if (points.Count >= 3) result.Add(points);
            }

            return result;
        }

        /// <summary>
        /// 角のワールド座標を、その角に集まるタイルのうち実在するものから求める。
        /// 実際のメッシュと同じ「中心 + 外接半径 × 60°×index」で置くので、
        /// グリッド間隔と見た目の大きさがずれている設定でも縁から浮かない。
        /// </summary>
        private bool TryCornerWorldPosition(HexCorner corner, Dictionary<HexCoord, HexTile> tiles,
                                             out Vector3 position)
        {
            foreach (var coord in new[] { corner.A, corner.B, corner.C })
            {
                if (!tiles.TryGetValue(coord, out var tile) || tile == null) continue;

                for (int i = 0; i < 6; i++)
                {
                    if (HexCorner.Of(coord, i) != corner) continue;

                    float angle = Mathf.Deg2Rad * (60f * i);
                    float y     = HexMeshBuilder.TopY(tile.TileHeight) + _lift;

                    position = tile.transform.position + new Vector3(
                        tile.OuterRadius * Mathf.Cos(angle), y, tile.OuterRadius * Mathf.Sin(angle));
                    return true;
                }
            }

            position = default;
            return false;
        }

        // ── 描画 ──────────────────────────────────────────────────────

        private LineRenderer CreateLine(Transform parent, int index)
        {
            var go = new GameObject($"Loop_{index}");
            go.transform.SetParent(parent, worldPositionStays: false);

            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace     = true;
            line.numCapVertices    = 4;
            line.numCornerVertices = 2;
            line.sharedMaterial    = _sharedMaterial;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows    = false;
            line.positionCount     = 0;

            // 点列は「尾 → 先端」の順に入れるので、0が尾・1が先端になる
            line.widthCurve = new AnimationCurve(
                new Keyframe(0f, _headWidth * 0.15f),
                new Keyframe(1f, _headWidth));

            // ★先端を白にしない。加算合成＋Bloomで芯が白く飛ぶため、
            //   色キーまで白にすると「ただの白い線」に見えて、灯りの温かさが残らない。
            //   明るさはアルファ側で作り、色は淡い金色のまま通す
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(_lightColor, 0f), new GradientColorKey(_lightColor, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.35f, 0.5f), new GradientAlphaKey(1f, 1f) });
            line.colorGradient = gradient;

            return line;
        }

        private IEnumerator Trace(GameObject owner, List<List<Vector3>> loops, List<LineRenderer> lines)
        {
            var perimeters = new List<float>(loops.Count);
            foreach (var loop in loops) perimeters.Add(OutlineTraceSampler.Perimeter(loop));

            var buffer = new List<Vector3>();

            // 一周する。長い輪ほど速く走るが、輪の数に関わらず同時に走り終える
            float elapsed = 0f;
            while (elapsed < _traceDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / Mathf.Max(_traceDuration, 0.0001f));

                for (int i = 0; i < loops.Count; i++)
                {
                    float perimeter = perimeters[i];
                    float head      = t * perimeter;
                    // 走り始めは尾を伸ばしながら出てくる。いきなり全長で現れると唐突に見える
                    float tail      = Mathf.Max(0f, head - _tailRatio * perimeter);
                    Apply(lines[i], loops[i], tail, head, buffer);
                }
                yield return null;
            }

            // 先端は始点に戻ったまま、尾が追いついて消える
            float fade = 0f;
            while (fade < _fadeOutDuration)
            {
                fade += Time.deltaTime;
                float t = Mathf.Clamp01(fade / Mathf.Max(_fadeOutDuration, 0.0001f));

                for (int i = 0; i < loops.Count; i++)
                {
                    float perimeter = perimeters[i];
                    float tail      = Mathf.Lerp(perimeter - _tailRatio * perimeter, perimeter, t);
                    Apply(lines[i], loops[i], tail, perimeter, buffer);
                }
                yield return null;
            }

            _routine = null;
            // ★自分が作った親だけを消す。差し替え後の新しい親には触れない
            if (owner != null) Destroy(owner);
            if (ReferenceEquals(_current, owner)) _current = null;
        }

        private static void Apply(LineRenderer line, List<Vector3> loop, float from, float to, List<Vector3> buffer)
        {
            OutlineTraceSampler.Sample(loop, from, to, buffer);

            line.positionCount = buffer.Count;
            for (int i = 0; i < buffer.Count; i++) line.SetPosition(i, buffer[i]);
        }

        private static Material BuildMaterial()
        {
            // 頂点カラー（LineRendererのグラデーション）が乗るシェーダを使う。
            // 既存の蛍・川の演出と同じ選び方に揃えてある
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                      ?? Shader.Find("Sprites/Default");

            var mat = new Material(shader) { name = "QuestCelebrationOutline_Runtime" };

            // 加算合成。暗い地面でも光って見え、Bloomが拾ってくれる
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
            if (mat.HasProperty("_Blend"))   mat.SetFloat("_Blend", 1f);
            if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            if (mat.HasProperty("_ZWrite"))   mat.SetFloat("_ZWrite", 0f);

            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return mat;
        }
    }
}
