// 役割: クエスト達成で選ばれたタイル群の外周を、光の先端が一周する演出。
//       QuestTileSelectionResolvedEvent（対象タイルの解決結果）だけを購読する。
//       どのタイルを祝うかは QuestTileFocusTracker が決めるので、こちらは描くだけ。
//
//       ★輪の向きは HexBoundaryBuilder が決めたものをそのまま使う。
//         外周は反時計回り、穴は時計回りに揃っているので、
//         どちらも「材質を左手に見ながら走る」形になる。ここで向きを再判定しない。
//
//       ★縁の座標は TileOutlineGeometry から取る。
//         祝福の光柱も同じ縁を基準にするので、求め方が2か所に割れないようまとめてある。
//
//       ★なぞり終わったら QuestOutlineTraceCompletedEvent を出す。
//         渡すのは「終わった」という事実だけで、輪の座標や内部の持ち方は渡さない。
//         受け取る側が自分で外周を求めるので、こちらの作りを変えても道連れにならない。

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ElfVillage.Core;

namespace ElfVillage.Tiles
{
    public class QuestCelebrationOutlineSystem : MonoBehaviour
    {
        [Header("走る時間")]
        [Tooltip("光が外周を一周するまでの秒数")]
        [SerializeField] private float _traceDuration = 1.4f;

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

            var loops = TileOutlineGeometry.BuildWorldLoops(evt.Tiles, _lift);
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

            // ★なぞり終わったこの瞬間に知らせる。尾が消えるのは待たない。
            //   祝福の光柱は「なぞり終わった → 少し間を置いて立ち上がる」ので、
            //   ここで知らせると尾の消えぎわと重なって演出が途切れない。
            //   渡すのは事実だけで、輪の座標は渡さない（あちらは自分で求める）
            EventBus.Publish(new QuestOutlineTraceCompletedEvent());

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
            // ★_Blendは 0=Alpha 1=Premultiply 2=Additive 3=Multiply。
            //   ここを1にしていたせいで、加算のつもりが不透明に近い合成になり、
            //   光ではなく「すりガラスの板」に見えていた。
            //   SrcBlend/DstBlendだけ指定しても、この値からシェーダ側が上書きしてくる
            if (mat.HasProperty("_Blend"))   mat.SetFloat("_Blend", 2f);
            if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            if (mat.HasProperty("_ZWrite"))   mat.SetFloat("_ZWrite", 0f);

            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return mat;
        }
    }
}
