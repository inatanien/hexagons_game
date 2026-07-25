// 役割: 森クラスターの成長を購読し、森の精霊を1体だけ生成する（Stage 9プロトタイプ）。
//       ★汎用のSpiritSpawnerにはしない。現時点ではTerrainGrowthEvent<ForestGrowthMetrics>
//         だけを購読し森の精霊のみを扱うため、名前と責務を森に限定している。
//         花・川の精霊を追加する段階で、共通化の必要性を確認してから抽象化する。
//
//       生成した精霊のhome森はForestSpirit側が自分でコピー保持する。このSpawnerが持つ
//       「直近の森」は次の生成候補にすぎず、生成済みの精霊へ後から影響を与えない。
//       別クラスターの成長イベントでは2体目を生成せず、既存精霊の範囲も変更しない。

using System.Collections.Generic;
using UnityEngine;
using ElfVillage.Core;
using ElfVillage.Tiles;

namespace ElfVillage.Spirits
{
    public class ForestSpiritSpawner : MonoBehaviour
    {
        [Header("行動範囲")]
        [Tooltip("森クラスターが小さい場合でも確保する最低限の行動半幅")]
        [SerializeField] private float _minExtent    = 0.8f;
        [Tooltip("クラスターの外周からどれだけ内側に留めるか（森の外へ出ないようにする余白）")]
        [SerializeField] private float _extentInset  = 0.6f;

        private ForestSpirit _spirit;

        private void OnEnable()  => EventBus.Subscribe<TerrainGrowthEvent<ForestGrowthMetrics>>(OnForestGrow);
        private void OnDisable() => EventBus.Unsubscribe<TerrainGrowthEvent<ForestGrowthMetrics>>(OnForestGrow);

        private void OnForestGrow(TerrainGrowthEvent<ForestGrowthMetrics> evt)
        {
            if (evt.AffectedTiles == null || evt.AffectedTiles.Count == 0) return;

            ComputeBounds(evt.AffectedTiles, out Vector3 center, out float extentX, out float extentZ);

            if (_spirit == null)
            {
                // 最初に対象となった森クラスターにだけ1体生成する。
                SpawnSpirit(evt.AffectedTiles, center, extentX, extentZ);
                return;
            }

            // 2体目は生成しない。既存精霊は「自分のhome森が育ったか」を自分で判定し、
            // 別クラスターの成長であれば何も変更しない。
            _spirit.TryFollowForestGrowth(evt.AffectedTiles, center, extentX, extentZ);
        }

        private void SpawnSpirit(IReadOnlyList<HexTile> tiles, Vector3 center, float extentX, float extentZ)
        {
            var go = new GameObject("ForestSpirit");
            go.transform.SetParent(transform, true);

            _spirit = go.AddComponent<ForestSpirit>();
            _spirit.Initialize(tiles, center, extentX, extentZ, Random.value);
        }

        /// <summary>クラスターのAABBから中心と行動半幅を求める（森の外へ出ないよう内側へ寄せる）。</summary>
        private void ComputeBounds(IReadOnlyList<HexTile> tiles, out Vector3 center, out float extentX, out float extentZ)
        {
            var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);

            foreach (var tile in tiles)
            {
                if (tile == null) continue;
                var p = tile.transform.position;
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }

            center = (min + max) * 0.5f;
            var extent = max - min;

            // タイル中心のAABBから内側へ寄せることで、精霊が森の縁より外に出にくくする。
            extentX = Mathf.Max(extent.x * 0.5f - _extentInset, _minExtent);
            extentZ = Mathf.Max(extent.z * 0.5f - _extentInset, _minExtent);
        }
    }
}
