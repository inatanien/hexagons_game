// 役割: forest_unlock_birds報酬を実際の鳥の出現へ接続する。
//       QuestManager・QuestDefinition・QuestRewardSystemを直接参照せず、
//       CoreのRewardUnlockedEventだけを購読して鳥を出現させる。
//       出現位置は「最大森林クラスター付近」を狙うため、Tiles内部で完結する
//       TerrainGrowthEvent<ForestGrowthMetrics>（森の成長イベント）も別途購読し、
//       直近の森クラスターの中心座標を記憶しておく。これはQuestとは無関係な
//       Tiles内部の情報なので、Quest側への依存にはならない。該当イベントが
//       一度も来ていない場合はワールド原点へフォールバックする（このコンポーネントを
//       乗せるWorldBreathはアンビエント風演出のため盤面から離れた高所に置かれており、
//       transform.positionをそのまま使うとプレイヤーから見えない位置に鳥が出てしまう。
//       盤面の中心は常にワールド原点であるため、原点の方が安全なフォールバックになる）。
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
        [SerializeField] private float _flightRadius   = 1.5f;
        [SerializeField] private float _flightHeight    = 2.5f;
        [SerializeField] private float _bobAmplitude    = 0.2f;
        [SerializeField] private float _angularSpeedMin = 0.15f;
        [SerializeField] private float _angularSpeedMax = 0.3f;
        [SerializeField] private float _bobFrequencyMin = 0.5f;
        [SerializeField] private float _bobFrequencyMax = 0.9f;

        private readonly HashSet<string> _spawnedRewardIds = new();
        private Vector3? _lastForestCenter;
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
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<RewardUnlockedEvent>(OnRewardUnlocked);
            EventBus.Unsubscribe<TerrainGrowthEvent<ForestGrowthMetrics>>(OnForestGrow);
        }

        // ── 森クラスター中心の追跡（Tiles内部で完結、Questとは無関係） ──────

        private void OnForestGrow(TerrainGrowthEvent<ForestGrowthMetrics> evt)
        {
            if (evt.AffectedTiles == null || evt.AffectedTiles.Count == 0) return;

            var sum = Vector3.zero;
            foreach (var tile in evt.AffectedTiles)
                sum += tile.transform.position;

            _lastForestCenter = sum / evt.AffectedTiles.Count;
        }

        // ── 報酬解放 → 鳥の出現 ──────────────────────────────────────────

        private void OnRewardUnlocked(RewardUnlockedEvent evt)
        {
            if (evt.RewardId != BirdsRewardId) return;
            if (_spawnedRewardIds.Contains(evt.RewardId)) return;
            _spawnedRewardIds.Add(evt.RewardId);

            Vector3 center = _lastForestCenter ?? Vector3.zero;
            int count = Random.Range(_minBirdCount, _maxBirdCount + 1);

            for (int i = 0; i < count; i++)
                SpawnBird(center, i);
        }

        private void SpawnBird(Vector3 center, int index)
        {
            var go = BuildBirdVisual();
            go.transform.SetParent(transform, true);

            Vector3 birdCenter = center + new Vector3(
                Random.Range(-0.3f, 0.3f),
                _flightHeight,
                Random.Range(-0.3f, 0.3f));

            float angularSpeed = Random.Range(_angularSpeedMin, _angularSpeedMax) * (Random.value < 0.5f ? -1f : 1f);
            float bobFrequency = Random.Range(_bobFrequencyMin, _bobFrequencyMax);
            float phase        = index * (Mathf.PI * 2f / 3f) + Random.Range(0f, 0.5f);

            var bird = go.AddComponent<RewardBird>();
            bird.Init(birdCenter, _flightRadius, angularSpeed, _bobAmplitude, bobFrequency, phase);
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
