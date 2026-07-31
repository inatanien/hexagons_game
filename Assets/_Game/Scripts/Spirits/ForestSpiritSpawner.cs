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
        /// <summary>生成する精霊の性格をどう決めるか。</summary>
        public enum PersonalitySelectionMode
        {
            /// <summary>home森の代表座標から決定的に決める（本番の既定）。</summary>
            DeterministicFromHome = 0,
            /// <summary>指定した性格を必ず使う（検証Sceneで2種類を並べて比較するため）。</summary>
            Fixed = 1,
        }

        [Header("行動範囲")]
        [Tooltip("森クラスターが小さい場合でも確保する最低限の行動半幅")]
        [SerializeField] private float _minExtent    = 0.8f;
        [Tooltip("クラスターの外周からどれだけ内側に留めるか（森の外へ出ないようにする余白）")]
        [SerializeField] private float _extentInset  = 0.6f;

        [Header("生成条件（Stage 15）")]
        [Tooltip("精霊が住み着くのに必要な森クラスタの最小枚数。" +
                  "本編では森タイル1枚ごとに成長イベントが飛ぶため、これが無いと" +
                  "最初の1枚に住人が現れてしまう。プロトタイプの旧挙動は1で再現できる")]
        [SerializeField] private int _minClusterSizeToSpawn = 4;

        [Header("性格（Stage 13）")]
        [Tooltip("DeterministicFromHome: home森の代表座標から決定的に決める（本番の既定）。" +
                  "Fixed: _fixedPersonalityを必ず使う（検証用）")]
        [SerializeField] private PersonalitySelectionMode _personalityMode = PersonalitySelectionMode.DeterministicFromHome;
        [Tooltip("_personalityMode が Fixed のときだけ使われる")]
        [SerializeField] private SpiritPersonalityKind _fixedPersonality = SpiritPersonalityKind.Calm;

        private ForestSpirit _spirit;

        // 重複排除用の使い回しバッファ（イベントごとに確保しないため）。
        private readonly HashSet<HexTile> _uniqueTileBuffer = new();

        private void Awake()
        {
            // ★誕生はタイル配置と同じフレームに起きる。
            //   そのフレームで初めて Shader.Find が走るとフリーズすることが
            //   WorldBreathSystem で確認されているため、Scene開始時に一度引いて
            //   Unity側のシェーダーキャッシュを温めておく。
            ForestSpiritPresentation.PrewarmShader();
        }

        private void OnEnable()  => EventBus.Subscribe<TerrainGrowthEvent<ForestGrowthMetrics>>(OnForestGrow);
        private void OnDisable() => EventBus.Unsubscribe<TerrainGrowthEvent<ForestGrowthMetrics>>(OnForestGrow);

        private void OnForestGrow(TerrainGrowthEvent<ForestGrowthMetrics> evt)
        {
            if (evt.AffectedTiles == null || evt.AffectedTiles.Count == 0) return;

            ComputeBounds(evt.AffectedTiles, out Vector3 center, out float extentX, out float extentZ);

            if (_spirit == null)
            {
                // ★判定には「これからhomeになるタイル集合そのもの」の枚数を使う。
                //   evt.Metrics.LargestClusterSize は名前に反して対象クラスタの枚数だが、
                //   名前だけを見て世界最大値と誤解される余地があるため、
                //   homeとして実際に採用する集合を直接数えて意味のずれを無くしている。
                int clusterSize = CountUniqueTiles(evt.AffectedTiles);

                if (!SpiritSpawnPolicy.ShouldSpawn(false, clusterSize, _minClusterSizeToSpawn)) return;

                // 最初に条件を満たした森クラスターにだけ1体生成する。
                SpawnSpirit(evt.AffectedTiles, center, extentX, extentZ);

                // 生まれた森の成長を、最初の体験として明示的に渡す。
                // EventBus経由（Relay）だけに任せると、SpawnerとRelayの購読順によって
                // 体験するかどうかが変わってしまうため、ここで決定的に確定させる。
                // Relay経由で同じ刺激が続けて届いても、React中の同優先度として弾かれる。
                _spirit.ReceiveInitialStimulus(
                    new SpiritStimulus(SpiritStimulusKind.ForestGrew, center, evt.AffectedTiles));
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

            // 性格は生成時にここで一度だけ決まる。以後の森の成長では再決定しない。
            SpiritPersonalityKind personality = DecidePersonality(tiles);

            _spirit = go.AddComponent<ForestSpirit>();
            _spirit.Initialize(tiles, center, extentX, extentZ, Random.value, personality);

            // Hierarchy上でどの子がどの性格か一目で分かるようにする
            // （精霊自身が確定させた値を読む。Initializeが未知enumをCalmへ倒した場合もそれが出る）。
            go.name = "ForestSpirit_" + _spirit.Personality;

            // 誕生演出も通知と同じく「今生まれた」ときだけの処理なので、ここで1回だけ始める。
            // ★地面の座標はここで確定させてPresentationへ渡す。
            //   精霊は空中に浮いているため、精霊のYをそのまま使うと目印が宙に浮く。
            //   Presentation側でタイルを探したりRaycastを撃ったりしないよう、
            //   home森を知っているこの場所で計算し切る。
            _spirit.BeginBirthPresentation(ComputeBirthGroundPosition(tiles, _spirit.transform.position));

            // ★誕生の通知はここ1回だけ（Stage 16）。
            //   生成は _spirit == null のときにしか通らず、以後は TryFollowForestGrowth へ分岐するため、
            //   森が何枚育っても誕生イベントが再発行されることはない。
            //   Initializeが済んだ後に発行するので、購読側は確定済みの性格と段階を受け取れる。
            EventBus.Publish(new ForestSpiritSpawnedEvent(
                _spirit.transform.position, _spirit.Personality, _spirit.GrowthStage));
        }

        /// <summary>
        /// 誕生の目印を置く地面のワールド座標。
        /// XZは精霊が生まれた真下、Yはhome森のタイル上面に合わせる。
        ///
        /// ★高さは HexTile.GroundWorldPosition から取る。
        ///   これは木・花と同じ `HexMeshBuilder.TopY(tileHeight) + PropLiftY` の式で、
        ///   0.16のような絶対値をここで新しく作らないためにタイル側が配っている値。
        ///
        /// ★home森で最も高い上面を採用する（最大値）。
        ///   タイルの列挙順に依存せず、かつ将来タイルごとに高さが変わっても
        ///   輪が地面へ埋まらない側へ倒れる。
        ///   有効なタイルが1枚も無い場合は精霊の足元をそのまま使う（安全な既定）。
        /// </summary>
        private static Vector3 ComputeBirthGroundPosition(IReadOnlyList<HexTile> tiles, Vector3 spiritPosition)
        {
            bool  found    = false;
            float highestY = 0f;

            if (tiles != null)
            {
                foreach (var tile in tiles)
                {
                    if (tile == null) continue;

                    float y = tile.GroundWorldPosition.y;
                    if (!float.IsFinite(y)) continue;

                    if (!found || y > highestY) { highestY = y; found = true; }
                }
            }

            return new Vector3(spiritPosition.x,
                                found ? highestY : spiritPosition.y,
                                spiritPosition.z);
        }

        /// <summary>
        /// 生成する精霊の性格を決める。
        /// ★Fixedは2種類を並べて比較するための正規の設定であり、Initializeを回避する抜け道ではない。
        ///   どちらのモードでも生成経路（SpawnSpirit → Initialize → 生成時刺激）は完全に同じ。
        /// ★DeterministicFromHomeの結果は永続IDではなく「セーブ導入前の決定的な既定値」。
        ///   将来セーブを導入したら、保存されたSpiritPersonalityKindを正とすること。
        /// </summary>
        private SpiritPersonalityKind DecidePersonality(IReadOnlyList<HexTile> tiles)
        {
            if (_personalityMode == PersonalitySelectionMode.Fixed) return _fixedPersonality;

            if (!TryGetRepresentativePosition(tiles, out Vector3 representative))
                return SpiritPersonalityKind.Calm; // 代表座標が取れない場合の安全な既定

            return SpiritBehaviorMath.PickPersonality(representative.x, representative.z);
        }

        /// <summary>
        /// home森を代表する1タイルの座標を、タイルの列挙順に依存せずに選ぶ。
        /// ★「最小X、同値なら最小Z」という全順序で選ぶため、同じ森であればリストの並びが
        ///   どう変わっても必ず同じタイルが選ばれる。
        ///   （EventBusの購読順やクラスター走査順が変わっても性格が変わらないことを保証する）
        /// </summary>
        private static bool TryGetRepresentativePosition(IReadOnlyList<HexTile> tiles, out Vector3 representative)
        {
            representative = Vector3.zero;
            if (tiles == null || tiles.Count == 0) return false;

            bool found = false;
            foreach (var tile in tiles)
            {
                if (tile == null) continue;
                var p = tile.transform.position;
                if (!float.IsFinite(p.x) || !float.IsFinite(p.z)) continue;

                if (!found || p.x < representative.x ||
                    (Mathf.Approximately(p.x, representative.x) && p.z < representative.z))
                {
                    representative = p;
                    found = true;
                }
            }

            return found;
        }

        /// <summary>
        /// 有効（非null）なタイルのユニーク件数を数える。
        /// 現在のForestGrowthEvaluatorはBFSのvisited集合により重複を出さないが、
        /// 生成条件が将来の評価器の実装詳細に依存しないよう、ここで明示的に重複を排除する。
        /// バッファは使い回すため、イベントごとの確保は発生しない。
        /// </summary>
        private int CountUniqueTiles(IReadOnlyList<HexTile> tiles)
        {
            if (tiles == null) return 0;

            _uniqueTileBuffer.Clear();
            foreach (var tile in tiles)
                if (tile != null) _uniqueTileBuffer.Add(tile);

            return _uniqueTileBuffer.Count;
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
