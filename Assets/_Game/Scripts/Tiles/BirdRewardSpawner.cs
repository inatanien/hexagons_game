// 役割: forest_unlock_birds報酬を実際の鳥の出現へ接続する。
//       QuestManager・QuestDefinition・QuestRewardSystemを直接参照せず、
//       CoreのRewardUnlockedEventだけを購読して鳥を出現させる。
//       出現範囲は「森クラスター全体」を覆うよう、Tiles内部で完結する
//       TerrainGrowthEvent<ForestGrowthMetrics>（森の成長イベント）も別途購読し、
//       直近の森クラスターのバウンディングボックス（中心＋X/Z半幅）を記憶しておく。
//       これはQuestとは無関係なTiles内部の情報なので、Quest側への依存にはならない。
//       該当イベントが一度も来ていない場合はワールド原点へフォールバックする
//       （このコンポーネントを乗せるWorldBreathはアンビエント風演出のため盤面から
//       離れた高所に置かれており、transform.positionをそのまま使うとプレイヤーから
//       見えない位置に鳥が出てしまう。盤面の中心は常にワールド原点であるため、
//       原点の方が安全なフォールバックになる）。
//       報酬解放後も森が成長し続けた場合、既に出現済みの鳥の飛行範囲もその都度
//       広げる（RewardBird.UpdateBoundsで中心・範囲だけを差し替え、周波数・位相・
//       経過時間は据え置くため不自然な飛躍は起きない）。
//       CoreのTimeOfDayEventも購読し、夜（Night）になったら各鳥をそれぞれの現在地から
//       一番近い森タイルへ飛ばして隠し、朝（Morning）になったら同じ地点から通常の
//       飛行位置へ戻す（RewardBird.Hide/Showの単純な逆再生）。そのため森タイルの
//       バウンディングボックスだけでなく、個々のタイル座標のリストも保持しておく。
//       群れAI・巣・餌・成長・セーブ・複数種類の鳥・プレイヤー操作との連動は
//       Stage 5では扱わない。
//       同じrewardIdでRewardUnlockedEventが複数回発行されても、鳥が重複生成
//       されないよう解放済みrewardIdを記憶する（QuestRewardSystemと同じ方針）。

using System.Collections.Generic;
using UnityEngine;
using ElfVillage.Core;

namespace ElfVillage.Tiles
{
    public class BirdRewardSpawner : MonoBehaviour
    {
        private const string BirdsRewardId = "forest_unlock_birds";

        [Header("生成数")]
        [SerializeField] private int _minBirdCount = 1;
        [SerializeField] private int _maxBirdCount = 3;

        [Header("飛行範囲")]
        [Tooltip("森クラスターが小さい場合でも確保する最低限の飛行半幅")]
        [SerializeField] private float _minExtent      = 1.2f;
        [Tooltip("クラスターの外周からどれだけ余裕を持って飛ぶか")]
        [SerializeField] private float _extentMargin   = 1.0f;
        [SerializeField] private float _flightHeight    = 2.5f;
        [SerializeField] private float _bobAmplitude    = 0.2f;
        [Tooltip("X方向の周波数（Z方向はこれに_freqRatioMin〜Maxを掛けた値になる。" +
                  "X/Zで周波数を変えることでリサージュ曲線になり、単純な円軌道にならない）")]
        [SerializeField] private float _freqXMin       = 0.15f;
        [SerializeField] private float _freqXMax       = 0.3f;
        [SerializeField] private float _freqRatioMin   = 1.3f;
        [SerializeField] private float _freqRatioMax   = 1.8f;
        [SerializeField] private float _bobFrequencyMin = 0.5f;
        [SerializeField] private float _bobFrequencyMax = 0.9f;

        [Header("夜間の隠れ処理")]
        [Tooltip("隠れる際、一番近い森タイルの位置からどれだけ上（樹冠あたり）へ潜り込ませるか")]
        [SerializeField] private float _hideHeightOffset = 1.0f;

        private readonly HashSet<string> _spawnedRewardIds = new();
        private readonly List<Vector3>   _forestTilePositions = new();
        private Vector3 _lastForestCenter;
        private float   _lastForestExtentX;
        private float   _lastForestExtentZ;
        private bool    _hasForestBounds;
        private Material _bodyMaterial;
        private Material _wingMaterial;

        private void Awake()
        {
            _bodyMaterial = BuildMaterial(new Color(0.42f, 0.28f, 0.16f));
            _wingMaterial = BuildMaterial(new Color(0.30f, 0.20f, 0.11f));
        }

        private void OnEnable()
        {
            EventBus.Subscribe<RewardUnlockedEvent>(OnRewardUnlocked);
            EventBus.Subscribe<TerrainGrowthEvent<ForestGrowthMetrics>>(OnForestGrow);
            EventBus.Subscribe<TimeOfDayEvent>(OnTimeOfDay);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<RewardUnlockedEvent>(OnRewardUnlocked);
            EventBus.Unsubscribe<TerrainGrowthEvent<ForestGrowthMetrics>>(OnForestGrow);
            EventBus.Unsubscribe<TimeOfDayEvent>(OnTimeOfDay);
        }

        // ── 森クラスターのバウンディングボックス・タイル座標の追跡（Tiles内部で完結、Questとは無関係） ──

        private void OnForestGrow(TerrainGrowthEvent<ForestGrowthMetrics> evt)
        {
            if (evt.AffectedTiles == null || evt.AffectedTiles.Count == 0) return;

            var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            _forestTilePositions.Clear();
            foreach (var tile in evt.AffectedTiles)
            {
                var p = tile.transform.position;
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
                _forestTilePositions.Add(p);
            }

            var center = (min + max) * 0.5f;
            var extent = max - min;

            _lastForestCenter  = center;
            _lastForestExtentX = Mathf.Max(extent.x * 0.5f + _extentMargin, _minExtent);
            _lastForestExtentZ = Mathf.Max(extent.z * 0.5f + _extentMargin, _minExtent);
            _hasForestBounds   = true;

            // 既に出現済みの鳥がいれば、成長した範囲へ追従させる（周波数・位相は据え置き）。
            var existingBirds = GetComponentsInChildren<RewardBird>(true);
            foreach (var bird in existingBirds)
                bird.UpdateBounds(FlightCenter(center), _lastForestExtentX, _lastForestExtentZ);
        }

        private Vector3 FlightCenter(Vector3 forestCenter) =>
            new Vector3(forestCenter.x, forestCenter.y + _flightHeight, forestCenter.z);

        // ── 昼夜サイクル → 鳥の出現・消失 ──────────────────────────────────

        private void OnTimeOfDay(TimeOfDayEvent evt)
        {
            if (evt.Current == TimeOfDayEvent.Phase.Night)
                HideAllBirds();
            else if (evt.Current == TimeOfDayEvent.Phase.Morning)
                ShowAllBirds();
        }

        private void HideAllBirds()
        {
            var birds = GetComponentsInChildren<RewardBird>(true);
            foreach (var bird in birds)
            {
                Vector3 hidePoint = FindNearestForestTile(bird.transform.position);
                hidePoint.y += _hideHeightOffset;
                bird.Hide(hidePoint);
            }
        }

        private void ShowAllBirds()
        {
            var birds = GetComponentsInChildren<RewardBird>(true);
            foreach (var bird in birds)
                bird.Show();
        }

        // 指定位置から一番近い森タイルの座標を返す（追跡できていない場合は既知の森クラスター
        // 中心、それも無ければワールド原点にフォールバックする）。
        private Vector3 FindNearestForestTile(Vector3 fromPosition)
        {
            if (_forestTilePositions.Count == 0)
                return _hasForestBounds ? _lastForestCenter : Vector3.zero;

            Vector3 nearest = _forestTilePositions[0];
            float bestDistSq = (nearest - fromPosition).sqrMagnitude;
            for (int i = 1; i < _forestTilePositions.Count; i++)
            {
                float d = (_forestTilePositions[i] - fromPosition).sqrMagnitude;
                if (d < bestDistSq)
                {
                    bestDistSq = d;
                    nearest = _forestTilePositions[i];
                }
            }
            return nearest;
        }

        // ── 報酬解放 → 鳥の出現 ──────────────────────────────────────────

        private void OnRewardUnlocked(RewardUnlockedEvent evt)
        {
            if (evt.RewardId != BirdsRewardId) return;
            if (_spawnedRewardIds.Contains(evt.RewardId)) return;
            _spawnedRewardIds.Add(evt.RewardId);

            Vector3 baseCenter = _hasForestBounds ? FlightCenter(_lastForestCenter) : new Vector3(0f, _flightHeight, 0f);
            float   extentX    = _hasForestBounds ? _lastForestExtentX : _minExtent;
            float   extentZ    = _hasForestBounds ? _lastForestExtentZ : _minExtent;

            int count = Random.Range(_minBirdCount, _maxBirdCount + 1);
            for (int i = 0; i < count; i++)
                SpawnBird(baseCenter, extentX, extentZ, i);
        }

        private void SpawnBird(Vector3 baseCenter, float extentX, float extentZ, int index)
        {
            var go = BuildBirdVisual();
            go.transform.SetParent(transform, true);

            // 個体差用のオフセット（Init時に1回だけ決定し、以後は据え置き）。
            var centerOffset = new Vector3(Random.Range(-0.3f, 0.3f), 0f, Random.Range(-0.3f, 0.3f));

            float freqX = Random.Range(_freqXMin, _freqXMax) * (Random.value < 0.5f ? -1f : 1f);
            float freqZ = freqX * Random.Range(_freqRatioMin, _freqRatioMax);
            float bobFrequency = Random.Range(_bobFrequencyMin, _bobFrequencyMax);
            float phaseX = index * (Mathf.PI * 2f / 3f) + Random.Range(0f, 0.5f);
            float phaseZ = phaseX + Random.Range(0f, Mathf.PI);

            var bird = go.AddComponent<RewardBird>();
            bird.Init(baseCenter, centerOffset, extentX, extentZ, freqX, freqZ, _bobAmplitude, bobFrequency, phaseX, phaseZ);
        }

        // ── 見た目（既存アセットに鳥モデルがないため、簡易メッシュで代用） ────
        //    小さな胴体（つぶした球）+ 左右の翼（薄い直方体）。癒し系の世界観に
        //    合わせ、目立ちすぎない小さめのサイズと落ち着いた茶色にしている。

        private GameObject BuildBirdVisual()
        {
            var root = new GameObject("RewardBird");

            var body = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            body.name = "Body";
            body.transform.SetParent(root.transform, false);
            body.transform.localScale = new Vector3(0.16f, 0.12f, 0.26f);
            RemoveCollider(body);
            ApplyMaterial(body, _bodyMaterial);

            CreateWing(root.transform, "WingLeft",  -1f);
            CreateWing(root.transform, "WingRight",  1f);

            return root;
        }

        private void CreateWing(Transform parent, string name, float side)
        {
            var wing = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wing.name = name;
            wing.transform.SetParent(parent, false);
            wing.transform.localScale = new Vector3(0.24f, 0.02f, 0.12f);
            wing.transform.localPosition = new Vector3(side * 0.14f, 0.02f, -0.02f);
            wing.transform.localRotation = Quaternion.Euler(0f, 0f, side * 20f);
            RemoveCollider(wing);
            ApplyMaterial(wing, _wingMaterial);
        }

        private static void RemoveCollider(GameObject go)
        {
            var collider = go.GetComponent<Collider>();
            if (collider == null) return;

            // Edit ModeのテストからCreatePrimitiveを呼ぶ都合上、Play Mode外ではDestroyImmediateが必要。
            if (Application.isPlaying) Object.Destroy(collider);
            else Object.DestroyImmediate(collider);
        }

        private static void ApplyMaterial(GameObject go, Material mat)
        {
            if (mat == null) return;
            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null) renderer.sharedMaterial = mat;
        }

        private static Material BuildMaterial(Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader == null) return null;

            var mat = new Material(shader) { name = "RewardBird_Runtime" };
            mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            return mat;
        }
    }
}
