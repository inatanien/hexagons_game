// 役割: シーン上の1枚のHexタイルを表す MonoBehaviour。
//       TileData を保持し、見た目（色・回転）の反映と
//       配置済みフラグを管理する。

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ElfVillage.HexGrid;

namespace ElfVillage.Tiles
{
    public class HexTile : MonoBehaviour
    {
        [SerializeField] private MeshFilter   meshFilter;
        [SerializeField] private MeshRenderer meshRenderer;
        [SerializeField] private MeshCollider meshCollider;

        [Header("メッシュ設定")]
        [SerializeField] private float outerRadius = 2.0f;
        [SerializeField] private float tileHeight  = 0.30f;

        [Header("配置アニメーション")]
        [SerializeField] private AnimationCurve placementCurve = new AnimationCurve(
            new Keyframe(0f,    0f,   0f,  6f),
            new Keyframe(0.65f, 1.15f, 0f,  0f),
            new Keyframe(1f,    1f,   0f,  0f));

        private Renderer tileRenderer;
        private Color _defaultColor;
        private bool _isAvailable;

        // 世界生成タイプ（プレイヤーが配置する前に事前割り当てされる）
        private TileType _worldType;
        private GameObject _worldPropsRoot;

        // TileType.elements（複数要素タイル）から生成したプロップのルート。
        // legacy単一要素タイルでは使わない（null のまま）。
        // 注意: Water要素だけはこのルートの下に入らず、直接transform直下へ生成される
        // （SpawnSingleElementProps内のTilePropType.Waterケース参照。WaterPSがtransform直下の
        // 子である前提のGetWaterFlowDir/ReverseWaterFlowと整合させるため）。
        private GameObject _elementPropsRoot;

        // 接続状態（6方向・同種タイルと隣接しているか）
        private readonly bool[] _connectedEdges = new bool[6];

        // 川エッジの開放状態（6方向・その方向の辺自体がEdgeType.River同士で一致しているか）
        // タイル同士の同カテゴリ接続(_connectedEdges)とは別に、辺の種別そのもので判定する
        private readonly bool[] _riverEdgeOpen = new bool[6];

        // 空き枠ワイヤーフレーム
        private GameObject _wireRoot;
        private Material   _wireMat;

        public float OuterRadius => outerRadius;
        public float TileHeight  => tileHeight;

        private void Awake()
        {
            tileRenderer = meshRenderer;
            // sharedMaterial から元色をキャッシュ（material を触る前に取得しないとインスタンスが生まれる）
            _defaultColor = tileRenderer != null ? tileRenderer.sharedMaterial.color : Color.gray;
            Mesh mesh = HexMeshBuilder.Build(outerRadius, tileHeight);
            if (meshFilter   != null) meshFilter.sharedMesh   = mesh;
            if (meshCollider != null) meshCollider.sharedMesh  = mesh;

            BuildAvailableWire();
        }

        private void OnDestroy()
        {
            if (_wireMat != null) Destroy(_wireMat);
        }

        private void BuildAvailableWire()
        {
            _wireMat = new Material(Shader.Find("Universal Render Pipeline/Unlit"))
                { name = "AvailWire" };
            _wireMat.color = Color.white;

            _wireRoot = new GameObject("AvailableWire");
            _wireRoot.transform.SetParent(transform);
            _wireRoot.transform.localPosition = Vector3.zero;
            _wireRoot.transform.localRotation = Quaternion.identity;

            float topY = tileHeight * 0.5f + 0.008f;
            var topVerts = new Vector3[6];
            for (int i = 0; i < 6; i++)
            {
                float a = Mathf.Deg2Rad * (60f * i);
                topVerts[i] = new Vector3(outerRadius * Mathf.Cos(a), topY, outerRadius * Mathf.Sin(a));
            }

            AddWireLine(topVerts, loop: true,  width: 0.06f);
            for (int i = 0; i < 6; i++)
                AddWireLine(new[] { topVerts[i], new Vector3(topVerts[i].x, -tileHeight * 0.5f, topVerts[i].z) },
                            loop: false, width: 0.035f);

            _wireRoot.SetActive(false);
        }

        private void AddWireLine(Vector3[] positions, bool loop, float width)
        {
            var go = new GameObject("WL");
            go.transform.SetParent(_wireRoot.transform);
            go.transform.localPosition = Vector3.zero;
            var lr = go.AddComponent<LineRenderer>();
            lr.material          = _wireMat;
            lr.startWidth        = width;
            lr.endWidth          = width;
            lr.loop              = loop;
            lr.useWorldSpace     = false;
            lr.numCapVertices    = 4;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows    = false;
            lr.positionCount     = positions.Length;
            lr.SetPositions(positions);
        }

        public TileData Data { get; private set; }
        public bool IsPlaced { get; private set; }

        public void Initialize(HexCoord coord, float tileSize)
        {
            Data = new TileData(coord, null, 0);
            transform.position = coord.ToWorldPosition(tileSize);
            IsPlaced = false;
            // 未配置タイルは最初は非表示
            if (tileRenderer  != null) tileRenderer.enabled  = false;
            if (meshCollider  != null) meshCollider.enabled  = false;
        }

        /// <summary>世界生成時にタイルタイプを事前割り当てする。プレイヤー配置より前に呼ぶ。</summary>
        public void SetWorldType(TileType type)
        {
            _worldType = type;
        }

        public void Place(TileType tileType, int rotation)
        {
            // 世界プロップをクリアしてプレイヤーのタイルに置き換える
            DestroyWorldProps();
            Data = new TileData(Data.coord, tileType, rotation);
            IsPlaced = true;
            // 配置済みタイルはクリック対象から除外（コライダー無効 → 左クリックでカメラパン可能になる）
            if (meshCollider != null) meshCollider.enabled = false;
            if (_wireRoot    != null) _wireRoot.SetActive(false);
            ApplyRiverChannelMesh(tileType);
            ApplyVisual();
            SpawnPropsFor(tileType, transform);
            StartCoroutine(PlacementAnim());
        }

        // ── 川タイル専用メッシュ ─────────────────────────────────────────

        /// <summary>
        /// 接続状態が変わった後（同種の川タイルが隣接配置された後）に呼び、
        /// 接続した端が陸地の高さへ戻らず溝底のまま隣タイルへ繋がるようにメッシュを再生成する。
        /// </summary>
        public void RefreshRiverChannelMesh()
        {
            if (Data.tileType != null) ApplyRiverChannelMesh(Data.tileType);
        }

        /// <summary>川タイル（propType==Water）の場合、天面に流路の溝を彫り込んだメッシュへ差し替える。</summary>
        private void ApplyRiverChannelMesh(TileType tileType)
        {
            if (tileType == null || tileType.propType != TilePropType.Water) return;

            ComputeRiverEdgeIndices(tileType, out int edgeAIdx, out int edgeBIdx);
            float inRadius = outerRadius * 0.866f;
            Vector3 posA = EdgeCenter(edgeAIdx, inRadius);
            Vector3 posB = EdgeCenter(edgeBIdx, inRadius);

            bool    isStraight = ((posA + posB) * 0.5f).sqrMagnitude < 0.01f;
            Vector3 ctrl       = isStraight ? (posA + posB) * 0.5f : Vector3.zero;

            // その辺自体がEdgeType.River同士で一致している場合のみ、陸地の高さへ戻さず溝底のまま繋げる
            // （タイル同士のカテゴリ一致ではなく、辺の種別そのものによる判定）
            bool openA = IsRiverEdgeOpen((edgeAIdx + Data.rotation) % 6);
            bool openB = IsRiverEdgeOpen((edgeBIdx + Data.rotation) % 6);

            Mesh channelMesh = RiverChannelMeshBuilder.Build(outerRadius, tileHeight, posA, posB, ctrl,
                                                              openA, openB);
            if (meshFilter   != null) meshFilter.sharedMesh   = channelMesh;
            if (meshCollider != null) meshCollider.sharedMesh = channelMesh;

            // 陰影だけでは溝が視認しにくいため、水路サブメッシュには専用の暗い水色マテリアルを使う。
            // スロット0（陸地）はこの後 ApplyVisual() が tileColor で着色するので、ここでは触らない。
            if (meshRenderer != null && channelMesh.subMeshCount > 1)
            {
                var channelMat = new Material(meshRenderer.sharedMaterial) { name = "RiverChannel_Runtime" };
                // コピー元（スロット0の陸地マテリアル）がgroundTexture設定済みの場合、
                // そのままだと川底にも地面テクスチャが混入してしまう（RefreshRiverChannelMesh経由の
                // 再生成時に顕在化）ため、テクスチャだけ明示的に解除する。
                // このプロジェクトのシェーダー（URP/Lit）ではmainTextureが_BaseMapのエイリアスのため、
                // mainTexture=nullだけで_BaseMapも解除されることを実機で確認済み。
                channelMat.mainTexture = null;
                Color c = tileType.tileColor;
                channelMat.color = new Color(c.r * 0.5f, c.g * 0.55f, c.b * 0.75f, c.a);
                meshRenderer.materials = new[] { meshRenderer.material, channelMat };
            }
        }

        // SpawnWater と同じ辺選択ロジック（辺インデックスのみを取り出す版）
        private void ComputeRiverEdgeIndices(TileType type, out int edgeA, out int edgeB)
        {
            var riverEdges    = new List<int>();
            var fallbackEdges = new List<int>();
            for (int d = 0; d < 6; d++)
            {
                var e = type.GetEdge(d);
                if (e == EdgeType.River) riverEdges.Add(d);
                else if (e != EdgeType.None && e != EdgeType.Field && e != EdgeType.Forest)
                    fallbackEdges.Add(d);
            }

            var src = riverEdges.Count >= 2 ? riverEdges
                    : fallbackEdges.Count >= 2 ? fallbackEdges
                    : null;

            if (src != null)
            {
                edgeA = src[0];
                edgeB = src[1];
            }
            else
            {
                int h = Mathf.Abs(Data.coord.q * 31 + Data.coord.r * 17 + Data.coord.s * 7);
                edgeA = h % 6;
                edgeB = (edgeA + 1 + (h / 6) % 5) % 6;
            }
        }

        /// <summary>
        /// 川タイルの橋を架けるための基準点をタイルローカル座標で取得する。
        /// 流路曲線の中央（t=0.5）の位置・流れの接線方向・川幅を返す。川タイルでなければ false。
        /// </summary>
        public bool TryGetRiverBridgeAnchor(out Vector3 localCenter, out Vector3 localTangent, out float riverWidth)
        {
            localCenter  = Vector3.zero;
            localTangent = Vector3.forward;
            riverWidth   = 0f;
            if (Data.tileType == null || Data.tileType.propType != TilePropType.Water) return false;

            ComputeRiverEdgeIndices(Data.tileType, out int edgeAIdx, out int edgeBIdx);
            float   inRadius   = outerRadius * 0.866f;
            Vector3 posA       = EdgeCenter(edgeAIdx, inRadius);
            Vector3 posB       = EdgeCenter(edgeBIdx, inRadius);
            bool    isStraight = ((posA + posB) * 0.5f).sqrMagnitude < 0.01f;
            Vector3 ctrl       = isStraight ? (posA + posB) * 0.5f : Vector3.zero;

            const float dt = 0.02f;
            localCenter  = QuadBezier(posA, ctrl, posB, 0.5f);
            localTangent = (QuadBezier(posA, ctrl, posB, 0.5f + dt)
                           - QuadBezier(posA, ctrl, posB, 0.5f - dt)).normalized;
            riverWidth   = outerRadius * 0.5f;
            return true;
        }

        // ── プロップ生成（プレイヤー配置・世界生成共通）──────────────────────

        private void SpawnPropsFor(TileType type, Transform parent)
        {
            if (type == null) return;

            // 直前のTileTypeが複数要素タイルだった場合の残存プロップを必ず破棄してから判定する
            // （legacy⇔複合の切り替え時に旧プロップが残らないようにするため）。
            ClearElementProps();

            // legacy/elements分岐はTileType.HasVisualElementsを判定源とする。
            // TilePropVisualBuilder（プレビュー）も同じプロパティで判定するため、
            // 実配置とプレビューの分岐条件は常に完全に同じ意味になる（Session 11）。
            if (type.HasVisualElements)
            {
                var effectiveElements = new List<TileElement>(type.EffectiveElements);
                SpawnPropsForElements(effectiveElements, type, parent);
            }
            else
            {
                // ── legacy生成（既存メソッドをそのまま呼び出す。挙動は完全に維持） ──
                switch (type.propType)
                {
                    case TilePropType.Tree:   SpawnTrees(type, parent);   break;
                    case TilePropType.House:  SpawnHouse(parent);         break;
                    case TilePropType.Water:  SpawnWater(type, parent);  break;
                    case TilePropType.Flower: SpawnFlowers(type, parent); break;
                }
            }
            SpawnDividers(type, parent);
        }

        // ── 複数要素タイルのプロップ生成（Session 4） ────────────────────────

        private void ClearElementProps()
        {
            if (_elementPropsRoot != null)
            {
                Object.Destroy(_elementPropsRoot);
                _elementPropsRoot = null;
            }
        }

        // Session 10: 要素ごとの領域割当結果1件（正規化候補から算出済みのローカルオフセット＋seed）。
        private readonly struct PositionedSeed
        {
            public readonly Vector3 LocalOffset;
            public readonly int     Seed;

            public PositionedSeed(Vector3 localOffset, int seed)
            {
                LocalOffset = localOffset;
                Seed        = seed;
            }
        }

        private void SpawnPropsForElements(List<TileElement> elements, TileType type, Transform parent)
        {
            _elementPropsRoot = new GameObject("ElementProps");
            _elementPropsRoot.transform.SetParent(parent, false);
            _elementPropsRoot.transform.localPosition = Vector3.zero;
            _elementPropsRoot.transform.localRotation = Quaternion.identity;

            // areaWeightの合計を計算する（不正値はここで安全な範囲へクランプする）。
            // NaN等の非数値はSafeWeightで0扱いにし、後段の正規化計算を汚染しないようにする。
            float weightSum = 0f;
            foreach (var e in elements) weightSum += SafeWeight(e.areaWeight);
            bool useEqualSplit = weightSum <= 0f;

            // 生成数を全要素ぶん先に決定する（式はSession 4から不変）。
            // Tree/Flowerのみが「位置を持つ」要素として領域割当の対象になる
            // （House/Waterはpropcount・座標という概念自体を使わないため対象外のまま）。
            var counts       = new int[elements.Count];
            var isPositional = new bool[elements.Count];
            int positionalCount = 0;
            int totalPositional = 0;
            for (int i = 0; i < elements.Count; i++)
            {
                var v = elements[i].variant;
                if (v == null) continue;
                bool positional = v.propType == TilePropType.Tree || v.propType == TilePropType.Flower;
                isPositional[i] = positional;
                if (!positional) continue;

                float normalizedWeight = useEqualSplit
                    ? 1f / elements.Count
                    : SafeWeight(elements[i].areaWeight) / weightSum;
                counts[i] = ComputeElementPropCount(v.propCount, normalizedWeight);
                totalPositional += counts[i];
                positionalCount++;
            }

            // 要素ごとの事前割当（Tree/Flowerが2つ以上ある場合のみ算出、それ以外はnullのまま
            // ＝下流で従来どおりComputeSpiralOffsetベースの生成にフォールバックする）。
            var perElementAssignment = new PositionedSeed[elements.Count][];
            if (positionalCount >= 2 && totalPositional > 0)
            {
                ComputeRegionAssignment(elements, isPositional, counts, positionalCount, totalPositional, type, perElementAssignment);
            }

            for (int i = 0; i < elements.Count; i++)
            {
                var variant = elements[i].variant; // EffectiveElementsで既にnullでないことは保証済み

                // 単一/非対象要素向けのフォールバック角度オフセット（Session 4からの既存式）。
                float angleOffsetDeg = i * (360f / elements.Count);

                try
                {
                    SpawnSingleElementProps(variant, counts[i], angleOffsetDeg, perElementAssignment[i], type, parent);
                }
                catch (System.Exception ex)
                {
                    // 1要素の生成失敗が他の有効要素の生成を止めないようにする
                    Debug.LogWarning($"[HexTile] 要素「{variant.variantName}」のプロップ生成に失敗しました: {ex.Message}", this);
                }
            }
        }

        // Tree/Flower要素が2つ以上ある場合に、正規化候補の生成→スコア算出→ソート→区間割当
        // までを行い、要素ごとのPositionedSeed[]をresultへ書き込む（ElementRegionLayoutは純粋関数のみ）。
        private void ComputeRegionAssignment(
            List<TileElement> elements, bool[] isPositional, int[] counts,
            int positionalCount, int totalPositional, TileType type,
            PositionedSeed[][] result)
        {
            int   typeHash      = ElementRegionLayout.StableStringHash(type.name);
            float boundaryDeg   = ElementRegionLayout.ComputeBoundaryDirectionDeg(Data.coord.q, Data.coord.r, typeHash);
            float baseRotation  = Data.coord.q * 23f + Data.coord.r * 37f;

            var candidates = ElementRegionLayout.GenerateNormalizedCandidates(
                totalPositional, Data.coord.q, Data.coord.r, typeHash, TreeGoldenAngleDeg, baseRotation);
            var scores = ElementRegionLayout.ComputeScores(
                candidates, boundaryDeg, Data.coord.q, Data.coord.r, typeHash);
            var sortedIndices = ElementRegionLayout.SortIndicesByScore(scores);

            var positionalCounts  = new int[positionalCount];
            var positionalIndices = new int[positionalCount];
            int pi = 0;
            for (int i = 0; i < elements.Count; i++)
            {
                if (!isPositional[i]) continue;
                positionalCounts[pi]  = counts[i];
                positionalIndices[pi] = i;
                pi++;
            }

            var partitioned = ElementRegionLayout.PartitionByCounts(sortedIndices, positionalCounts);

            for (int p = 0; p < positionalIndices.Length; p++)
            {
                int elementIndex = positionalIndices[p];
                var variant      = elements[elementIndex].variant;
                float maxRadius  = variant.propType == TilePropType.Tree ? TreeMaxRadius : FlowerMaxRadius;

                var chunk = partitioned[p];
                var seeds = new PositionedSeed[chunk.Length];
                for (int k = 0; k < chunk.Length; k++)
                {
                    var cand   = candidates[chunk[k]];
                    var offset = new Vector3(cand.NormX * maxRadius, 0f, cand.NormZ * maxRadius);
                    seeds[k]   = new PositionedSeed(offset, cand.Seed);
                }
                result[elementIndex] = seeds;
            }
        }

        // internal化（Session 11）: TilePropVisualBuilderからも同一の正規化ロジックを再利用するため。
        internal static float SafeWeight(float areaWeight)
            => float.IsFinite(areaWeight) ? Mathf.Clamp01(areaWeight) : 0f;

        private void SpawnSingleElementProps(TerrainVariantDefinition variant, int elementPropCount,
                                              float angleOffsetDeg, PositionedSeed[] precomputed,
                                              TileType type, Transform originalParent)
        {
            if (variant == null) return; // 念のための安全策（EffectiveElements側で既に除外されているはず）

            switch (variant.propType)
            {
                case TilePropType.Tree:
                    SpawnTreesForVariant(variant, elementPropCount, angleOffsetDeg, precomputed, _elementPropsRoot.transform);
                    break;
                case TilePropType.House:
                    // SpawnHouseはpropCountを使わない単一形状のため、要素ごとに1棟だけ生成する
                    // （既存のHouse生成に「個数」という概念が元々存在しないため、areaWeightは適用しない）。
                    SpawnHouse(_elementPropsRoot.transform);
                    break;
                case TilePropType.Flower:
                    SpawnFlowersForVariant(variant, elementPropCount, angleOffsetDeg, precomputed, _elementPropsRoot.transform);
                    break;
                case TilePropType.Water:
                    // WaterPSはtransform直下の子として存在する必要がある
                    // （GetWaterFlowDir/ReverseWaterFlowが直下の子のみを探索するため）。
                    // ラッパー(_elementPropsRoot)の下ではなく元のparentへ直接生成し、edgesはtype（タイル本体）から取得する。
                    SpawnWater(type, originalParent);
                    break;
            }
        }

        /// <summary>
        /// 要素ごとの生成数を、variantのpropCountと正規化weightから算出する。
        /// 丸めで0になっても、propCountが1以上かつ重みが0より大きい要素は最低1個生成する。
        /// </summary>
        // internal化（Session 11）: TilePropVisualBuilderからも同一の生成数計算を再利用するため。
        internal static int ComputeElementPropCount(int basePropCount, float normalizedWeight)
        {
            int count = Mathf.RoundToInt(Mathf.Max(1, basePropCount) * normalizedWeight);
            if (count <= 0 && basePropCount >= 1 && normalizedWeight > 0f) count = 1;
            return count;
        }

        // ── 分割線 ───────────────────────────────────────────────────

        // HexCoord の方向インデックス(0〜5) → XZ平面での世界角度（度、+X軸基準）
        // ToWorldPosition の式から導出: dir0→30°, dir1→330°, dir2→270°,
        //                               dir3→210°, dir4→150°, dir5→90°
        private static readonly float[] s_DirToWorldAngle = { 30f, 330f, 270f, 210f, 150f, 90f };

        // インスタンスメソッドからは serialized fields の値を渡して static を呼ぶ
        private void SpawnDividers(TileType type, Transform parent)
            => SpawnDividersFor(type, parent, outerRadius, tileHeight);

        /// <summary>
        /// プレビュー用プロップ生成。TilePlacementPreview から呼ぶ。
        /// 座標ハッシュを使わず均等配置するため実際の配置と位置が若干異なる。
        /// Water は川岸ラインのみ表示（パーティクルは省略）。
        /// </summary>
        public static void SpawnPropsPreview(TileType type, Transform parent,
                                              float outerRadius = 2.0f, float tileHeight = 0.30f)
        {
            if (type == null) return;
            switch (type.propType)
            {
                case TilePropType.Tree:   SpawnTreesStatic(type, parent, tileHeight);               break;
                case TilePropType.House:  SpawnHouseStatic(parent, tileHeight);                      break;
                case TilePropType.Flower: SpawnFlowersStatic(type, parent, tileHeight);             break;
                case TilePropType.Water:  SpawnWaterPreview(type, parent, outerRadius, tileHeight); break;
            }
        }

        private static void SpawnTreesStatic(TileType type, Transform parent, float tileHeight)
        {
            int   count  = Mathf.Max(1, type.propCount);
            float ground = tileHeight + 0.01f;
            for (int i = 0; i < count; i++)
            {
                // プレビューには coord が無いため index のみで疑似乱数を作る
                int   seed   = i * 40361 + 7919;
                float rNorm  = count > 1 ? Mathf.Sqrt((i + 0.5f) / count) : 0f;
                float radius = Mathf.Max(0f, rNorm * TreeMaxRadius + ((seed / 21) % 21 - 10) / 200f);
                float angle  = (i * TreeGoldenAngleDeg + (seed % 21) - 10f) * Mathf.Deg2Rad;
                var   offset = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);

                GameObject prefab = PickTreeVariant(type, seed, out int variantIndex);
                if (prefab != null)
                {
                    var go = Object.Instantiate(prefab, parent);
                    go.transform.localPosition = offset + new Vector3(0f, ground, 0f);
                    go.transform.localRotation = Quaternion.Euler(0f, seed % 360, 0f);
                    go.transform.localScale   *= 0.90f + (seed % 21) / 100f;
                    RemoveCollidersRecursive(go);
                    continue;
                }
                SpawnPrimitiveTreeVariant(parent, offset, ground, variantIndex, seed);
            }
        }

        // internal化（Session 11）: TilePropVisualBuilderの複合要素House生成からも再利用するため。
        internal static void SpawnHouseStatic(Transform parent, float tileHeight)
        {
            float ground = tileHeight + 0.01f;

            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.transform.SetParent(parent);
            body.transform.localPosition = new Vector3(0f, ground + 0.22f, 0f);
            body.transform.localScale    = new Vector3(0.52f, 0.44f, 0.52f);
            SetPropMaterial(body, new Color(0.90f, 0.82f, 0.65f));

            var roof = GameObject.CreatePrimitive(PrimitiveType.Cube);
            roof.transform.SetParent(parent);
            roof.transform.localPosition = new Vector3(0f, ground + 0.58f, 0f);
            roof.transform.localScale    = new Vector3(0.62f, 0.16f, 0.62f);
            roof.transform.localRotation = Quaternion.Euler(0f, 45f, 0f);
            SetPropMaterial(roof, new Color(0.68f, 0.22f, 0.12f));
        }

        private static void SpawnFlowersStatic(TileType type, Transform parent, float tileHeight)
        {
            int count = Mathf.Max(1, type.propCount);
            var positions = new Vector3[count];
            var seeds     = new int[count];
            for (int i = 0; i < count; i++)
            {
                // プレビューには coord が無いため index のみで疑似乱数を作る
                int   seed   = i * 7919 + 31;
                float rNorm  = count > 1 ? Mathf.Sqrt((i + 0.5f) / count) : 0f;
                float radius = Mathf.Max(0f, rNorm * FlowerMaxRadius + ((seed % 21) - 10) / 200f);
                float angle  = (i * FlowerGoldenAngleDeg + (seed % 21) - 10f) * Mathf.Deg2Rad;
                positions[i] = new Vector3(Mathf.Cos(angle) * radius, tileHeight + 0.02f, Mathf.Sin(angle) * radius);
                seeds[i]     = seed;
            }
            SpawnFlowerBillboards(parent, type.billboardSprite, positions, seeds);
        }

        // 川の2辺を探してベジェ川岸ラインを生成（パーティクルなし、プレビュー用）
        // internal化（Session 11）: TilePropVisualBuilderの複合要素Water生成からも再利用するため
        // （elements[]経由のWaterもelementsを無視しtypeのedgesから辺を選ぶ点は実配置と同じ）。
        internal static void SpawnWaterPreview(TileType type, Transform parent,
                                               float outerRadius, float tileHeight)
        {
            float inRadius = outerRadius * 0.866f;
            var   riverEdges   = new List<int>();
            var   fallbackEdges = new List<int>();
            for (int d = 0; d < 6; d++)
            {
                var e = type.GetEdge(d);
                if (e == EdgeType.River) riverEdges.Add(d);
                else if (e != EdgeType.None && e != EdgeType.Field && e != EdgeType.Forest)
                    fallbackEdges.Add(d);
            }

            int edgeA, edgeB;
            var src = riverEdges.Count >= 2 ? riverEdges
                    : fallbackEdges.Count >= 2 ? fallbackEdges
                    : null;

            if (src != null) { edgeA = src[0]; edgeB = src[1]; }
            else             { edgeA = 0;       edgeB = 1; }   // 辺情報がない場合の fallback

            CreateWaterFlowPreview(parent,
                EdgeCenter(edgeA, inRadius), EdgeCenter(edgeB, inRadius),
                outerRadius, tileHeight);
        }

        // SpawnWater の川岸ライン部分だけを静的に生成（パーティクルは含まない）
        private static void CreateWaterFlowPreview(Transform parent, Vector3 edgeA, Vector3 edgeB,
                                                    float outerRadius, float tileHeight)
        {
            float bankOffset = outerRadius * 0.25f;
            float y          = tileHeight + 0.01f;

            bool    isStraight = ((edgeA + edgeB) * 0.5f).sqrMagnitude < 0.01f;
            Vector3 ctrl       = isStraight ? (edgeA + edgeB) * 0.5f : Vector3.zero;

            const int N = 8;
            var pts = new Vector3[N + 1];
            for (int i = 0; i <= N; i++)
                pts[i] = QuadBezier(edgeA, ctrl, edgeB, (float)i / N);

            var inwardA  = -edgeA.normalized;
            var outwardB =  edgeB.normalized;
            var perpA    = new Vector3(-inwardA.z,  0f, inwardA.x);
            var perpB    = new Vector3(-outwardB.z, 0f, outwardB.x);
            var bankColor = new Color(0.25f, 0.50f, 0.20f);
            const float overshoot = 0.02f;

            // edgeA 側の端セグメント
            float lenA = (pts[1] - edgeA).magnitude + 0.01f;
            var   rotA = Quaternion.LookRotation(new Vector3(inwardA.x, 0f, inwardA.z), Vector3.up);
            for (int side = -1; side <= 1; side += 2)
            {
                var b = GameObject.CreatePrimitive(PrimitiveType.Cube);
                b.transform.SetParent(parent);
                b.transform.localPosition = new Vector3(
                    edgeA.x + perpA.x * bankOffset * side + inwardA.x * (lenA - overshoot) * 0.5f, y,
                    edgeA.z + perpA.z * bankOffset * side + inwardA.z * (lenA - overshoot) * 0.5f);
                b.transform.localRotation = rotA;
                b.transform.localScale    = new Vector3(0.04f, 0.03f, lenA + overshoot);
                SetPropMaterial(b, bankColor);
            }

            // 中間セグメント
            for (int i = 1; i <= N - 2; i++)
            {
                var seg  = pts[i + 1] - pts[i];
                var mid  = (pts[i] + pts[i + 1]) * 0.5f;
                var len  = seg.magnitude + 0.01f;
                var d    = seg / len;
                var perp = new Vector3(-d.z, 0f, d.x);
                var rot  = Quaternion.LookRotation(new Vector3(d.x, 0f, d.z), Vector3.up);
                for (int side = -1; side <= 1; side += 2)
                {
                    var b = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    b.transform.SetParent(parent);
                    b.transform.localPosition = new Vector3(
                        mid.x + perp.x * bankOffset * side, y,
                        mid.z + perp.z * bankOffset * side);
                    b.transform.localRotation = rot;
                    b.transform.localScale    = new Vector3(0.04f, 0.03f, len);
                    SetPropMaterial(b, bankColor);
                }
            }

            // edgeB 側の端セグメント
            float lenB = (edgeB - pts[N - 1]).magnitude + 0.01f;
            var   rotB = Quaternion.LookRotation(new Vector3(outwardB.x, 0f, outwardB.z), Vector3.up);
            for (int side = -1; side <= 1; side += 2)
            {
                var b = GameObject.CreatePrimitive(PrimitiveType.Cube);
                b.transform.SetParent(parent);
                b.transform.localPosition = new Vector3(
                    edgeB.x + perpB.x * bankOffset * side - outwardB.x * (lenB - overshoot) * 0.5f, y,
                    edgeB.z + perpB.z * bankOffset * side - outwardB.z * (lenB - overshoot) * 0.5f);
                b.transform.localRotation = rotB;
                b.transform.localScale    = new Vector3(0.04f, 0.03f, lenB + overshoot);
                SetPropMaterial(b, bankColor);
            }
        }

        /// <summary>プレビューなど HexTile 外からも呼べる静的版。</summary>
        public static void SpawnDividersFor(TileType type, Transform parent,
                                            float outerRadius = 2.0f, float tileHeight = 0.30f)
        {
            if (type == null) return;
            float y = tileHeight + 0.018f;
            float R = outerRadius;

            switch (type.dividerType)
            {
                case TileDividerType.StraightH:
                    SpawnLine(parent, new Vector3(0f, y, 0f), Quaternion.identity,
                              new Vector3(R * 1.8f, 0.03f, 0.07f),
                              new Color(0.15f, 0.12f, 0.05f));
                    break;

                case TileDividerType.BendE:
                    float bLen = R * 0.866f;
                    var   bBlue = new Color(0.18f, 0.52f, 0.92f);
                    SpawnLine(parent, new Vector3(0.375f*R, y,  0.2165f*R),
                              Quaternion.Euler(0f, -30f, 0f),
                              new Vector3(bLen*0.93f, 0.03f, 0.08f), bBlue);
                    SpawnLine(parent, new Vector3(0.375f*R, y, -0.2165f*R),
                              Quaternion.Euler(0f,  30f, 0f),
                              new Vector3(bLen*0.93f, 0.03f, 0.08f), bBlue);
                    break;

                case TileDividerType.Hex6Spokes:
                    float innerR = R * 0.866f;
                    for (int i = 0; i < 6; i++)
                    {
                        float worldAngle = s_DirToWorldAngle[i];
                        float rad = worldAngle * Mathf.Deg2Rad;
                        var pos = new Vector3(innerR * 0.50f * Mathf.Cos(rad), y,
                                             innerR * 0.50f * Mathf.Sin(rad));
                        var rot = Quaternion.Euler(0f, -worldAngle, 0f);
                        var scl = new Vector3(innerR * 0.90f, 0.04f, 0.09f);
                        SpawnLine(parent, pos, rot, scl, EdgeTypeToColor(type.edges[i]));
                    }
                    break;

                case TileDividerType.VerticalPair:
                    break;

                case TileDividerType.BendPairWide:
                    break;

                case TileDividerType.BendPair:
                    break;
            }
        }

        public static Color EdgeTypeToColor(EdgeType edge)
        {
            switch (edge)
            {
                case EdgeType.Forest: return new Color(0.10f, 0.38f, 0.10f);
                case EdgeType.Field:  return new Color(0.68f, 0.82f, 0.28f);
                case EdgeType.River:  return new Color(0.18f, 0.52f, 0.92f);
                default:              return new Color(0.45f, 0.45f, 0.45f);
            }
        }

        private static void SpawnLine(Transform parent, Vector3 localPos, Quaternion localRot,
                                      Vector3 localScale, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.transform.SetParent(parent);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot;
            go.transform.localScale    = localScale;
            SetPropMaterial(go, color);
        }

        // from→to を結ぶ1本のセグメントを生成（BendPairWide 用）
        private static void SpawnBankSegment(Transform parent, Vector3 from, Vector3 to,
                                             float y, float width, Color color)
        {
            var   center = new Vector3((from.x + to.x) * 0.5f, y, (from.z + to.z) * 0.5f);
            var   dir    = new Vector3(to.x - from.x, 0f, to.z - from.z);
            float len    = dir.magnitude;
            if (len < 0.001f) return;
            SpawnLine(parent, center, Quaternion.LookRotation(dir / len, Vector3.up),
                      new Vector3(width, 0.03f, len), color);
        }

        // 黄金角スパイラル（Vogel法）でタイル内に木をまんべんなく散らす。
        // 半径を sqrt(i/count) で増やすことで、リング状に偏らせず面積あたりの密度を均一にする。
        // internal化（Session 11）: TilePropVisualBuilderからも同じ半径・角度定数を参照するため。
        internal const float TreeGoldenAngleDeg = 137.50776f;
        internal const float TreeMaxRadius      = 1.35f; // タイル境界（inRadius≈1.73）から木の葉半径ぶん内側まで使う

        private void SpawnTrees(TileType type, Transform parent)
        {
            int count = Mathf.Max(1, type.propCount);
            float baseRotation = Data.coord.q * 23f + Data.coord.r * 37f; // タイルごとに向きをずらす
            for (int i = 0; i < count; i++)
            {
                // coord ベースの擬似乱数で木の位置・バリエーションを決定（再現性あり・Random.seed 不要）
                int   seed   = Mathf.Abs(Data.coord.q * 92821 + Data.coord.r * 68917 + i * 40361);
                float rNorm  = count > 1 ? Mathf.Sqrt((i + 0.5f) / count) : 0f;
                float radius = Mathf.Max(0f, rNorm * TreeMaxRadius + ((seed / 21) % 21 - 10) / 200f);
                float angle  = (i * TreeGoldenAngleDeg + baseRotation + (seed % 21) - 10f) * Mathf.Deg2Rad;
                var   offset = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                SpawnSingleTree(type, parent, offset, seed);
            }
        }

        /// <summary>
        /// バリエーションプレハブが設定されていればそれを配置し、空欄なら標準プリミティブの木に
        /// フォールバックする（TileType.treeVariantPrefabs 参照）。
        /// </summary>
        private void SpawnSingleTree(TileType type, Transform parent, Vector3 offset, int seed)
        {
            float ground = tileHeight + 0.01f;
            GameObject prefab = PickTreeVariant(type, seed, out int variantIndex);

            if (prefab != null)
            {
                var go = Instantiate(prefab, parent);
                go.transform.localPosition = offset + new Vector3(0f, ground, 0f);
                go.transform.localRotation = Quaternion.Euler(0f, seed % 360, 0f);
                go.transform.localScale   *= 0.90f + (seed % 21) / 100f; // 0.90〜1.10 のサイズジッター
                RemoveCollidersRecursive(go);
                return;
            }

            SpawnPrimitiveTreeVariant(parent, offset, ground, variantIndex, seed);
        }

        /// <summary>
        /// type.treeVariantPrefabs から seed に基づき決定論的に1枠選ぶ。
        /// 枠が空欄（未設定）の場合は null を返す（呼び出し側でプリミティブにフォールバック）。
        /// </summary>
        private static GameObject PickTreeVariant(TileType type, int seed, out int variantIndex)
            => PickTreeVariantFrom(type.treeVariantPrefabs, seed, out variantIndex);

        /// <summary>TileTypeに依存しない汎用版。TerrainVariantDefinition.propPrefabs から選ぶ。
        /// internal化（Session 11）: TilePropVisualBuilderからも再利用するため。</summary>
        internal static GameObject PickTreeVariantFrom(GameObject[] prefabs, int seed, out int variantIndex)
        {
            int slotCount = (prefabs != null && prefabs.Length > 0) ? prefabs.Length : 10;
            variantIndex  = seed % slotCount;

            if (prefabs != null && variantIndex < prefabs.Length && prefabs[variantIndex] != null)
                return prefabs[variantIndex];
            return null;
        }

        // 黄金角スパイラル（Vogel法）の位置計算をTree/Flower・legacy/複数要素タイルで共有するヘルパー。
        // internal化（Session 11）: TilePropVisualBuilderの単一positional要素フォールバックからも
        // 再利用するため。
        internal static Vector3 ComputeSpiralOffset(int index, int count, int seed,
                                                    float goldenAngleDeg, float maxRadius, float baseRotationDeg)
        {
            float rNorm  = count > 1 ? Mathf.Sqrt((index + 0.5f) / count) : 0f;
            float radius = Mathf.Max(0f, rNorm * maxRadius + ((seed / 21) % 21 - 10) / 200f);
            float angle  = (index * goldenAngleDeg + baseRotationDeg + (seed % 21) - 10f) * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
        }

        /// <summary>
        /// TileType.propType/propCount/treeVariantPrefabsではなく、TerrainVariantDefinition側の
        /// 値を情報源として木を生成する（複数要素タイル用）。位置計算のロジックはSpawnTreesと共通。
        /// precomputed（Session 10の領域割当結果）が渡された場合はそちらの位置をそのまま使う。
        /// nullまたは空の場合は既存のComputeSpiralOffsetベースの生成にフォールバックする
        /// （Tree/Flower要素が1つしかない複合タイル・従来挙動の完全維持のため）。
        /// </summary>
        private void SpawnTreesForVariant(TerrainVariantDefinition variant, int propCount,
                                           float angleOffsetDeg, PositionedSeed[] precomputed, Transform parent)
        {
            if (precomputed != null && precomputed.Length > 0)
            {
                for (int i = 0; i < precomputed.Length; i++)
                    SpawnSingleTreeForVariant(variant, parent, precomputed[i].LocalOffset, precomputed[i].Seed);
                return;
            }

            int count = Mathf.Max(1, propCount);
            float baseRotation = Data.coord.q * 23f + Data.coord.r * 37f + angleOffsetDeg;
            for (int i = 0; i < count; i++)
            {
                int seed = Mathf.Abs(Data.coord.q * 92821 + Data.coord.r * 68917 + i * 40361
                                      + Mathf.RoundToInt(angleOffsetDeg) * 131);
                var offset = ComputeSpiralOffset(i, count, seed, TreeGoldenAngleDeg, TreeMaxRadius, baseRotation);
                SpawnSingleTreeForVariant(variant, parent, offset, seed);
            }
        }

        private void SpawnSingleTreeForVariant(TerrainVariantDefinition variant, Transform parent, Vector3 offset, int seed)
            => SpawnSingleTreeForVariantStatic(variant, parent, offset, seed, tileHeight);

        /// <summary>
        /// SpawnSingleTreeForVariantの本体。tileHeightを引数化してinstance非依存にしたもの
        /// （Session 11）。実配置（上のインスタンスメソッド経由）とTilePropVisualBuilder
        /// （プレビュー）の両方から呼ばれる、ロジック自体は元のまま変更していない。
        /// </summary>
        internal static void SpawnSingleTreeForVariantStatic(TerrainVariantDefinition variant, Transform parent,
                                                               Vector3 offset, int seed, float tileHeight)
        {
            float ground = tileHeight + 0.01f;
            GameObject prefab = PickTreeVariantFrom(variant.propPrefabs, seed, out int variantIndex);

            if (prefab != null)
            {
                var go = Object.Instantiate(prefab, parent);
                go.transform.localPosition = offset + new Vector3(0f, ground, 0f);
                go.transform.localRotation = Quaternion.Euler(0f, seed % 360, 0f);
                go.transform.localScale   *= 0.90f + (seed % 21) / 100f;
                RemoveCollidersRecursive(go);
                return;
            }

            SpawnPrimitiveTreeVariant(parent, offset, ground, variantIndex, seed);
        }

        // プレハブ未設定時のフォールバック。variantIndex で葉の色相・大きさを、
        // seed で個体ごとの全体サイズをずらし、プレハブ無しでも見た目に変化を出す。
        // internal化（Session 11）: TilePropVisualBuilderからも再利用するため。
        internal static void SpawnPrimitiveTreeVariant(Transform parent, Vector3 offset, float ground,
                                                        int variantIndex, int seed)
        {
            float sizeMul  = 0.85f + (seed % 31) / 100f;           // 0.85〜1.15
            float crownMul = 0.90f + (variantIndex % 5) * 0.05f;   // 0.90〜1.10
            float hue      = 0.28f + (variantIndex % 10) * 0.012f; // 葉の色相を少しずつずらす

            var trunk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            trunk.transform.SetParent(parent);
            trunk.transform.localPosition = offset + new Vector3(0f, ground + 0.20f * sizeMul, 0f);
            trunk.transform.localScale    = new Vector3(0.12f, 0.22f, 0.12f) * sizeMul;
            SetPropMaterial(trunk, new Color(0.42f, 0.26f, 0.10f));

            var crown = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            crown.transform.SetParent(parent);
            crown.transform.localPosition = offset + new Vector3(0f, ground + 0.60f * sizeMul, 0f);
            crown.transform.localScale    = new Vector3(0.45f, 0.52f, 0.45f) * (sizeMul * crownMul);
            SetPropMaterial(crown, Color.HSVToRGB(hue, 0.75f, 0.42f));
        }

        // 実モデルプレハブを Instantiate したときに、タイルへのレイキャストを妨げないよう
        // 子階層すべての Collider を除去する（SetPropMaterial のプリミティブ版と対の処理）。
        // internal化（Session 11）: TilePropVisualBuilderからも再利用するため。
        internal static void RemoveCollidersRecursive(GameObject go)
        {
            foreach (var col in go.GetComponentsInChildren<Collider>())
                Object.Destroy(col);
        }

        // 黄金角スパイラル（Vogel法）でタイル内に花をまんべんなく散らす（木と同じ考え方）。
        // internal化（Session 11）: TilePropVisualBuilderからも同じ半径・角度定数を参照するため。
        internal const float FlowerGoldenAngleDeg = 137.50776f;
        internal const float FlowerMaxRadius      = 1.5f; // 花は木より footprint が小さいのでより外側まで使う

        // 花畑タイルはピクミンブルーム方式: 地面テクスチャ（TileType.groundTexture）に
        // 花畑の密な模様を描いておき、その上に Billboard を propCount 枚だけ「点在」させて
        // 花が沢山あるように錯覚させる。以前の草・低い植物・花・小石の大量プロップ配置は廃止。
        private void SpawnFlowers(TileType type, Transform parent)
        {
            int count = Mathf.Max(1, type.propCount);
            float baseRotation = Data.coord.q * 23f + Data.coord.r * 37f; // タイルごとに向きをずらす

            var positions = new Vector3[count];
            var seeds     = new int[count];
            for (int i = 0; i < count; i++)
            {
                // coord ベースの擬似乱数で位置を決定（再現性あり）
                int   seed   = Data.coord.q * 31 + Data.coord.r * 17 + i * 7;
                float rNorm  = count > 1 ? Mathf.Sqrt((i + 0.5f) / count) : 0f;
                float radius = Mathf.Max(0f, rNorm * FlowerMaxRadius + ((seed % 21) - 10) / 200f);
                float angle  = (i * FlowerGoldenAngleDeg + baseRotation + (seed % 21) - 10f) * Mathf.Deg2Rad;
                positions[i] = new Vector3(Mathf.Cos(angle) * radius, tileHeight + 0.02f, Mathf.Sin(angle) * radius);
                seeds[i]     = seed;
            }
            SpawnFlowerBillboards(parent, type.billboardSprite, positions, seeds);
        }

        /// <summary>
        /// TileType.propType/propCount/billboardSpriteではなく、TerrainVariantDefinition側の
        /// 値を情報源として花Billboardを生成する（複数要素タイル用）。位置計算のロジックはSpawnFlowersと共通。
        /// </summary>
        // precomputed（Session 10の領域割当結果）が渡された場合はそちらの位置をそのまま使う。
        // nullまたは空の場合は既存のComputeSpiralOffsetベースの生成にフォールバックする
        // （Tree/Flower要素が1つしかない複合タイル・従来挙動の完全維持のため）。
        private void SpawnFlowersForVariant(TerrainVariantDefinition variant, int propCount,
                                             float angleOffsetDeg, PositionedSeed[] precomputed, Transform parent)
        {
            if (precomputed != null && precomputed.Length > 0)
            {
                var precomputedPositions = new Vector3[precomputed.Length];
                var precomputedSeeds     = new int[precomputed.Length];
                for (int i = 0; i < precomputed.Length; i++)
                {
                    precomputedPositions[i] = precomputed[i].LocalOffset + new Vector3(0f, tileHeight + 0.02f, 0f);
                    precomputedSeeds[i]     = precomputed[i].Seed;
                }
                SpawnFlowerBillboards(parent, variant.billboardSprite, precomputedPositions, precomputedSeeds);
                return;
            }

            int count = Mathf.Max(1, propCount);
            float baseRotation = Data.coord.q * 23f + Data.coord.r * 37f + angleOffsetDeg;

            var positions = new Vector3[count];
            var seeds     = new int[count];
            for (int i = 0; i < count; i++)
            {
                int seed = Data.coord.q * 31 + Data.coord.r * 17 + i * 7 + Mathf.RoundToInt(angleOffsetDeg) * 13;
                positions[i] = ComputeSpiralOffset(i, count, seed, FlowerGoldenAngleDeg, FlowerMaxRadius, baseRotation)
                               + new Vector3(0f, tileHeight + 0.02f, 0f);
                seeds[i] = seed;
            }
            SpawnFlowerBillboards(parent, variant.billboardSprite, positions, seeds);
        }

        // ParticleSystem の Billboard レンダーモードでタイル1枚ぶんの花Billboardをまとめて生成する
        // （水流パーティクルと同じ仕組み。個別GameObjectを量産しないので軽量）。
        // internal化（Session 11）: TilePropVisualBuilderからも再利用するため。
        internal static void SpawnFlowerBillboards(Transform parent, Texture2D billboardSprite, Vector3[] positions, int[] seeds)
        {
            var go = new GameObject("FlowerBillboards");
            // worldPositionStays を false にする。true（既定）だとタイルの縮小スケール分だけ
            // ワールドスケールを維持しようとして localScale が不正な値に補正されてしまう
            // （橋オブジェクトで発生したのと同じ既知の不具合パターン）。
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;

            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop             = false;
            main.duration         = 1f;
            main.maxParticles     = Mathf.Max(1, positions.Length);
            main.startSpeed       = new ParticleSystem.MinMaxCurve(0f);
            main.startSize3D      = false;
            main.simulationSpace  = ParticleSystemSimulationSpace.Local;
            main.gravityModifier  = new ParticleSystem.MinMaxCurve(0f);
            main.startLifetime    = new ParticleSystem.MinMaxCurve(100000f); // 実質消えない静的表示

            var emission = ps.emission;
            emission.enabled = false; // 手動 Emit のみで配置する

            var shape = ps.shape;
            shape.enabled = false;

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            // 水パーティクルと同じ HorizontalBillboard（水平面のみカメラ追従、傾かない）にする。
            // 全軸追従の Billboard は、スクリーンショット用カメラの位置とパーティクルが
            // 実際に向いているカメラの角度がずれた際に板が真横を向き、
            // 縦に伸びた棘のような表示になる不具合があった。
            renderer.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            var mat = GetBillboardMaterial(billboardSprite);
            if (mat != null) renderer.material = mat;

            // 再生状態にしてから Emit する（Stop直後にEmitすると再生開始時にクリアされることがある）
            ps.Play();
            for (int i = 0; i < positions.Length; i++)
            {
                int seed = seeds[i];
                var ep = new ParticleSystem.EmitParams
                {
                    position      = positions[i],
                    velocity      = Vector3.zero,
                    startSize     = 0.40f + (seed % 21) / 100f, // 0.40〜0.60（タイル縮小スケール込みで見える大きさ）
                    rotation      = seed % 360,
                    startLifetime = 100000f,
                };
                ps.Emit(ep, 1);
            }
        }

        // TileType.billboardSprite または TerrainVariantDefinition.billboardSprite が設定されていればそれを使い、
        // 空欄ならコード生成の仮スプライトを使う
        private static Material GetBillboardMaterial(Texture2D billboardSprite)
        {
            Texture2D tex = billboardSprite != null ? billboardSprite : GetPlaceholderBillboardTexture();
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null || tex == null) return null;

            var mat = new Material(shader) { name = "FlowerBillboard_Runtime" };
            mat.SetFloat("_Surface", 1f);
            // _Surface=1だけではGPUのブレンド式が不透明のまま（既定値 _SrcBlend=One/_DstBlend=Zero/_ZWrite=1）
            // 残ってしまい、テクスチャのアルファ（花びらの透明部分）が無視され不透明な四角形として
            // 描画されていた。WorldBreathSystem.ForestBreathEffect.BuildMaterialと同じ値を明示する。
            mat.SetFloat("_Blend",    0f);
            mat.SetFloat("_SrcBlend", 5f);  // SrcAlpha
            mat.SetFloat("_DstBlend", 10f); // OneMinusSrcAlpha
            mat.SetFloat("_ZWrite",   0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.SetColor("_BaseColor", Color.white);
            mat.SetTexture("_BaseMap", tex);
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return mat;
        }

        // 仮の花Billboard画像をコードで生成してキャッシュする（実素材に差し替えるまでのフォールバック）。
        // 5枚花びら＋中心の黄色い円を、透明背景に半透明ブレンドで描く。
        private static Texture2D s_placeholderBillboardTex;

        private static Texture2D GetPlaceholderBillboardTexture()
        {
            if (s_placeholderBillboardTex != null) return s_placeholderBillboardTex;

            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name       = "FlowerBillboard_Placeholder",
                wrapMode   = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

            var   pixels      = new Color[size * size];
            var   center      = new Vector2(size * 0.5f, size * 0.5f);
            Color petalColor  = new Color(1.0f, 0.55f, 0.75f); // ピンク
            Color centerColor = new Color(1.0f, 0.85f, 0.15f); // 黄
            const int petalCount  = 5;
            float petalRadius = size * 0.28f;
            float petalDist   = size * 0.20f;
            float centerRadius = size * 0.14f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    float alpha = 0f;
                    Color col = petalColor;

                    for (int k = 0; k < petalCount; k++)
                    {
                        float ang = k * (360f / petalCount) * Mathf.Deg2Rad;
                        var petalCenter = center + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * petalDist;
                        float d = Vector2.Distance(p, petalCenter);
                        alpha = Mathf.Max(alpha, Mathf.Clamp01(1f - d / petalRadius));
                    }

                    float dc = Vector2.Distance(p, center);
                    float centerA = Mathf.Clamp01(1f - dc / centerRadius);
                    if (centerA > 0f)
                    {
                        col   = Color.Lerp(col, centerColor, centerA);
                        alpha = Mathf.Max(alpha, centerA);
                    }

                    pixels[y * size + x] = new Color(col.r, col.g, col.b, alpha);
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
            s_placeholderBillboardTex = tex;
            return tex;
        }

        private void SpawnHouse(Transform parent)
        {
            float ground = tileHeight + 0.01f;

            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.transform.SetParent(parent);
            body.transform.localPosition = new Vector3(0f, ground + 0.22f, 0f);
            body.transform.localScale    = new Vector3(0.52f, 0.44f, 0.52f);
            SetPropMaterial(body, new Color(0.90f, 0.82f, 0.65f));

            var roof = GameObject.CreatePrimitive(PrimitiveType.Cube);
            roof.transform.SetParent(parent);
            roof.transform.localPosition = new Vector3(0f, ground + 0.58f, 0f);
            roof.transform.localScale    = new Vector3(0.62f, 0.16f, 0.62f);
            roof.transform.localRotation = Quaternion.Euler(0f, 45f, 0f);
            SetPropMaterial(roof, new Color(0.68f, 0.22f, 0.12f));
        }

        private void SpawnWater(TileType type, Transform parent)
        {
            float inRadius = outerRadius * 0.866f;

            // 川の辺を探す優先順位:
            //   1. EdgeType.River が設定された辺
            //   2. Field・None・Forest 以外の辺（River_Bend など旧データ互換）
            //   3. 座標ハッシュによるランダム選択（ユーザー指定「6辺からランダム2辺」）
            var riverEdges   = new List<int>();
            var fallbackEdges = new List<int>();
            if (type != null)
            {
                for (int d = 0; d < 6; d++)
                {
                    var e = type.GetEdge(d);
                    if (e == EdgeType.River) riverEdges.Add(d);
                    else if (e != EdgeType.None && e != EdgeType.Field && e != EdgeType.Forest)
                        fallbackEdges.Add(d);
                }
            }

            int edgeA, edgeB;
            var src = riverEdges.Count >= 2 ? riverEdges
                    : fallbackEdges.Count >= 2 ? fallbackEdges
                    : null;

            if (src != null)
            {
                edgeA = src[0];
                edgeB = src[1];
            }
            else
            {
                // 座標ハッシュで再現性ある 2 辺を選択
                int h = Mathf.Abs(Data.coord.q * 31 + Data.coord.r * 17 + Data.coord.s * 7);
                edgeA = h % 6;
                edgeB = (edgeA + 1 + (h / 6) % 5) % 6;
            }

            var posA = EdgeCenter(edgeA, inRadius);
            var posB = EdgeCenter(edgeB, inRadius);
            CreateWaterFlow(parent, posA, posB);
        }

        // dir 方向の辺の中心をタイルローカル座標で返す（フラットトップ六角形）
        // s_DirToWorldAngle を使って ToWorldPosition と一致させる
        private static Vector3 EdgeCenter(int dir, float inRadius)
        {
            float angle = s_DirToWorldAngle[((dir % 6) + 6) % 6] * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(angle) * inRadius, 0f, Mathf.Sin(angle) * inRadius);
        }

        private void CreateWaterFlow(Transform parent, Vector3 edgeA, Vector3 edgeB)
        {
            float riverWidth = outerRadius * 0.5f;
            float bankOffset = riverWidth * 0.5f;
            float y          = tileHeight * 0.5f + 0.01f;

            // 2辺の中点がタイル中心に近い = 対向辺 = 直線。それ以外はタイル中心を制御点とするベジェ曲線
            bool    isStraight = ((edgeA + edgeB) * 0.5f).sqrMagnitude < 0.01f;
            Vector3 ctrl       = isStraight ? (edgeA + edgeB) * 0.5f : Vector3.zero;

            // 曲線を8分割でサンプリング
            const int N = 8;
            var pts = new Vector3[N + 1];
            for (int i = 0; i <= N; i++)
                pts[i] = QuadBezier(edgeA, ctrl, edgeB, (float)i / N);

            // 辺の法線方向（辺に垂直にタイル内へ向かう方向）
            // これで端のキューブの面が六角形の辺と完全に一致する
            var inwardA  = -edgeA.normalized;   // edgeA → タイル中心
            var outwardB =  edgeB.normalized;   // タイル中心 → edgeB
            var perpA    = new Vector3(-inwardA.z,  0f, inwardA.x);
            var perpB    = new Vector3(-outwardB.z, 0f, outwardB.x);

            // 隣接タイルとの継ぎ目の隙間をなくすため、端のキューブを境界から少しはみ出させる
            const float overshoot = 0.02f;

            // ── 端（edgeA側）: 辺上から overshoot だけ外へはみ出して配置 ──────
            float lenA = (pts[1] - edgeA).magnitude + 0.01f;
            var   rotA = Quaternion.LookRotation(new Vector3(inwardA.x, 0f, inwardA.z), Vector3.up);
            for (int side = -1; side <= 1; side += 2)
            {
                var bank = GameObject.CreatePrimitive(PrimitiveType.Cube);
                bank.transform.SetParent(parent);
                bank.transform.localPosition = new Vector3(
                    edgeA.x + perpA.x * bankOffset * side + inwardA.x * (lenA - overshoot) * 0.5f, y,
                    edgeA.z + perpA.z * bankOffset * side + inwardA.z * (lenA - overshoot) * 0.5f);
                bank.transform.localRotation = rotA;
                bank.transform.localScale    = new Vector3(0.04f, 0.03f, lenA + overshoot);
                SetPropMaterial(bank, new Color(0.25f, 0.50f, 0.20f));
            }

            // ── 中間: ベジェ曲線に追従する通常セグメント ──────────────────
            for (int i = 1; i <= N - 2; i++)
            {
                var seg  = pts[i + 1] - pts[i];
                var mid  = (pts[i] + pts[i + 1]) * 0.5f;
                var len  = seg.magnitude + 0.01f;
                var dir  = seg / len;
                var perp = new Vector3(-dir.z, 0f, dir.x);
                var rot  = Quaternion.LookRotation(new Vector3(dir.x, 0f, dir.z), Vector3.up);
                for (int side = -1; side <= 1; side += 2)
                {
                    var bank = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    bank.transform.SetParent(parent);
                    bank.transform.localPosition = new Vector3(
                        mid.x + perp.x * bankOffset * side, y,
                        mid.z + perp.z * bankOffset * side);
                    bank.transform.localRotation = rot;
                    bank.transform.localScale    = new Vector3(0.04f, 0.03f, len);
                    SetPropMaterial(bank, new Color(0.25f, 0.50f, 0.20f));
                }
            }

            // ── 端（edgeB側）: 辺上から overshoot だけ外へはみ出して配置 ──────
            float lenB = (edgeB - pts[N - 1]).magnitude + 0.01f;
            var   rotB = Quaternion.LookRotation(new Vector3(outwardB.x, 0f, outwardB.z), Vector3.up);
            for (int side = -1; side <= 1; side += 2)
            {
                var bank = GameObject.CreatePrimitive(PrimitiveType.Cube);
                bank.transform.SetParent(parent);
                bank.transform.localPosition = new Vector3(
                    edgeB.x + perpB.x * bankOffset * side - outwardB.x * (lenB - overshoot) * 0.5f, y,
                    edgeB.z + perpB.z * bankOffset * side - outwardB.z * (lenB - overshoot) * 0.5f);
                bank.transform.localRotation = rotB;
                bank.transform.localScale    = new Vector3(0.04f, 0.03f, lenB + overshoot);
                SetPropMaterial(bank, new Color(0.25f, 0.50f, 0.20f));
            }

            // ── パーティクル: 曲線を4分割してそれぞれにエミッターを配置 ────
            // 各エミッターが担当区間に沿って粒子を流し、合わせると自然な流れに見える
            const int psCount  = 4;
            float     segLen   = 0f;
            for (int i = 0; i < N; i++) segLen += (pts[i + 1] - pts[i]).magnitude;
            segLen /= psCount;

            for (int k = 0; k < psCount; k++)
            {
                float tm  = ((float)k + 0.5f) / psCount;
                float t0  = (float)k       / psCount;
                float t1  = (float)(k + 1) / psCount;
                var   pm  = QuadBezier(edgeA, ctrl, edgeB, tm);
                var   dir = (QuadBezier(edgeA, ctrl, edgeB, t1)
                           - QuadBezier(edgeA, ctrl, edgeB, t0)).normalized;

                float floorY = RiverChannelMeshBuilder.CenterlineHeight(tm, tileHeight);

                var go = new GameObject("WaterPS");
                go.transform.SetParent(parent);
                go.transform.localPosition = new Vector3(pm.x, floorY, pm.z);
                go.transform.localRotation = Quaternion.LookRotation(
                    new Vector3(dir.x, 0f, dir.z), Vector3.up);

                var ps  = go.AddComponent<ParticleSystem>();
                var mat = GetWaterMaterial();
                if (mat != null) go.GetComponent<ParticleSystemRenderer>().material = mat;
                // ローカル回転設定後に forward を取ると、タイル回転込みのワールド流れ方向になる
                SetupWaterParticles(ps, segLen, riverWidth, go.transform.forward);
                ps.Play();
            }
        }

        // 2次ベジェ曲線: P0→制御点P1→P2 を t∈[0,1] でサンプリング
        private static Vector3 QuadBezier(Vector3 p0, Vector3 p1, Vector3 p2, float t)
        {
            float mt = 1f - t;
            return mt * mt * p0 + 2f * mt * t * p1 + t * t * p2;
        }

        // worldFlowDir: ワールド座標での流れ方向ベクトル（タイル回転込み）。
        // World 座標系を使うことで、タイルを何枚繋いでも回転に関わらず向きを一方向に固定できる。
        private static void SetupWaterParticles(ParticleSystem ps, float segLen, float riverWidth, Vector3 worldFlowDir)
        {
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            const float flowSpeed = 0.5f;

            var main = ps.main;
            main.loop            = true;
            main.duration        = 3f;
            main.maxParticles    = 17; // 4つのPSで合計68
            main.startLifetime   = new ParticleSystem.MinMaxCurve(
                segLen / flowSpeed * 0.85f, segLen / flowSpeed * 1.15f);
            main.startSpeed      = new ParticleSystem.MinMaxCurve(0f);
            // 粒1つ1つを流れ方向に横長な楕円にする（X=長辺、Y=短辺。Z軸は使わない）
            main.startSize3D     = true;
            main.startSizeX      = new ParticleSystem.MinMaxCurve(0.14f, 0.24f);
            main.startSizeY      = new ParticleSystem.MinMaxCurve(0.05f, 0.09f);
            main.startSizeZ      = new ParticleSystem.MinMaxCurve(1f);
            main.startColor      = new ParticleSystem.MinMaxGradient(
                new Color(0.35f, 0.70f, 1.00f, 0.85f),
                new Color(0.65f, 0.90f, 1.00f, 0.95f));
            main.gravityModifier = new ParticleSystem.MinMaxCurve(0f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            // 常にカメラの向きに関係なく水面に寝かせる（Billboardだと立って見えてしまうため）。
            // 回転は流れ方向に合わせて、横長な向きが流れと揃うようにする。
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            float flowAngle = Mathf.Atan2(worldFlowDir.z, worldFlowDir.x);
            main.startRotation = new ParticleSystem.MinMaxCurve(flowAngle);

            var em = ps.emission;
            em.rateOverTime = 7f; // 4つ合計で 28/sec

            // 担当区間（1/4の長さ）の川幅のBox（ワールド座標系でも GO の向きに追従）
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Box;
            sh.scale     = new Vector3(riverWidth * 0.80f, 0.01f, segLen);

            // ワールド座標系で流れ方向に速度を設定（min/max は符号を保って大小を正しく並べる）
            float dx = worldFlowDir.x * flowSpeed;
            float dz = worldFlowDir.z * flowSpeed;
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space   = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(Mathf.Min(dx * 0.85f, dx * 1.15f),
                                                    Mathf.Max(dx * 0.85f, dx * 1.15f));
            vel.y = new ParticleSystem.MinMaxCurve(0f, 0f);
            vel.z = new ParticleSystem.MinMaxCurve(Mathf.Min(dz * 0.85f, dz * 1.15f),
                                                    Mathf.Max(dz * 0.85f, dz * 1.15f));

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f,    0f),   new GradientAlphaKey(0.85f, 0.08f),
                        new GradientAlphaKey(0.85f, 0.85f), new GradientAlphaKey(0f,    1f) });
            col.color = new ParticleSystem.MinMaxGradient(g);
        }

        // マテリアルを一度だけ生成してキャッシュ（毎回生成すると URP Render Graph の
        // 透明パス再コンパイルが走り、置いた瞬間フレームがグレーになるバグを防ぐ）
        private static Material s_waterMat;

        private static Material GetWaterMaterial()
        {
            if (s_waterMat != null) return s_waterMat;

            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null)
            {
                Debug.LogWarning("[HexTile] URP Particles/Unlit shader not found — water particles disabled");
                return null;
            }

            s_waterMat = new Material(shader) { name = "WaterFlow_Runtime" };
            // URP 17: キーワードベースの透明設定のみ使用。
            // _SrcBlend/_DstBlend を手動設定すると Render Graph の
            // ブレンドステート管理と競合してフレームが壊れることがある。
            s_waterMat.SetFloat("_Surface", 1f);
            s_waterMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            s_waterMat.SetColor("_BaseColor", Color.white);
            s_waterMat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return s_waterMat;
        }

        private static void SetPropMaterial(GameObject go, Color color)
        {
            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null) mr.material.color = color;
            // タイルへのレイキャストを妨げないようコライダーを除去
            var col = go.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);
        }

        // ── 世界プロップ管理 ─────────────────────────────────────────────

        private void SpawnWorldProps()
        {
            if (_worldType == null || _worldPropsRoot != null) return;
            _worldPropsRoot = new GameObject("WorldProps");
            _worldPropsRoot.transform.SetParent(transform);
            _worldPropsRoot.transform.localPosition = Vector3.zero;
            _worldPropsRoot.transform.localRotation = Quaternion.identity;
            SpawnPropsFor(_worldType, _worldPropsRoot.transform);
        }

        private void DestroyWorldProps()
        {
            if (_worldPropsRoot != null)
            {
                Object.Destroy(_worldPropsRoot);
                _worldPropsRoot = null;
            }
        }

        // ── アニメーション ───────────────────────────────────────────────

        private IEnumerator PlacementAnim()
        {
            float duration = 0.35f;
            float elapsed  = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float s = placementCurve.Evaluate(elapsed / duration);
                transform.localScale = Vector3.one * s;
                yield return null;
            }
            transform.localScale = Vector3.one;
        }

        // ── 接続管理 ─────────────────────────────────────────────────

        /// <summary>接続状態を記録する。ビジュアル演出は EventBus 経由で TileConnectionFX が担当。</summary>
        public void MarkConnectedEdge(int dir)
        {
            _connectedEdges[dir] = true;
        }

        public bool IsEdgeConnected(int dir) => _connectedEdges[((dir % 6) + 6) % 6];

        /// <summary>
        /// world方向dirの辺を「川として開放」する。タイル同士のカテゴリ一致(MarkConnectedEdge)とは独立に、
        /// その方向の辺自体がEdgeType.River同士で一致している場合にのみ呼ぶこと（HexGridManagerが判定）。
        /// </summary>
        public void MarkRiverEdgeOpen(int dir)
        {
            _riverEdgeOpen[((dir % 6) + 6) % 6] = true;
        }

        public bool IsRiverEdgeOpen(int dir) => _riverEdgeOpen[((dir % 6) + 6) % 6];

        /// <summary>ローカル方向インデックスの辺中心をワールド座標で返す（タイル回転の影響を受ける）。</summary>
        public Vector3 EdgeCenterWorld(int dir)
            => transform.TransformPoint(EdgeCenter(dir, outerRadius * 0.866f));

        /// <summary>
        /// ワールド方向インデックス worldDir のエッジ中心をワールド座標で返す。
        /// RiverFlowSystem の entry/exit（回転済み方向）と対応させるために使う。
        /// タイルの回転に関係なく、常にグリッドの worldDir 方向の辺位置を返す。
        /// </summary>
        public Vector3 GetWorldDirEdgePos(int worldDir)
        {
            float angle    = s_DirToWorldAngle[((worldDir % 6) + 6) % 6] * Mathf.Deg2Rad;
            float inRadius = outerRadius * 0.866f;
            return transform.position + new Vector3(Mathf.Cos(angle) * inRadius, 0f, Mathf.Sin(angle) * inRadius);
        }

        /// <summary>最初の WaterPS のワールド forward を返す（= 現在の流れ方向）。</summary>
        public Vector3 GetWaterFlowDir()
        {
            for (int i = 0; i < transform.childCount; i++)
            {
                var child = transform.GetChild(i);
                if (child.name == "WaterPS") return child.forward;
            }
            return transform.forward;
        }

        /// <summary>
        /// WaterPS の velocityOverLifetime を反転する。
        /// 隣タイルと接続したとき RiverFlowSystem が向きを揃えるために呼ぶ。
        /// </summary>
        public void ReverseWaterFlow()
        {
            for (int i = 0; i < transform.childCount; i++)
            {
                var child = transform.GetChild(i);
                if (child.name != "WaterPS") continue;
                var ps = child.GetComponent<ParticleSystem>();
                if (ps == null) continue;

                var vel = ps.velocityOverLifetime;
                float xMin = vel.x.constantMin, xMax = vel.x.constantMax;
                float zMin = vel.z.constantMin, zMax = vel.z.constantMax;
                // min/max を入れ替えて符号を反転
                vel.x = new ParticleSystem.MinMaxCurve(-xMax, -xMin);
                vel.z = new ParticleSystem.MinMaxCurve(-zMax, -zMin);

                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                ps.Play();
            }
        }

        public void SetRotation(int rotation)
        {
            Data = new TileData(Data.coord, Data.tileType, rotation);
            ApplyRotationVisual();
        }

        private void ApplyVisual()
        {
            if (tileRenderer == null) return;
            tileRenderer.enabled = true;
            if (Data.tileType != null)
            {
                tileRenderer.material.color = Data.tileType.tileColor;
                // groundTexture が設定されているタイルのみ地面に画像を貼る。未設定なら単色のまま
                // （tileColor 塗りつぶし）で、既存タイル種別の見た目には影響しない。
                if (Data.tileType.groundTexture != null)
                    tileRenderer.material.mainTexture = Data.tileType.groundTexture;
            }
            ApplyRotationVisual();
        }

        private void ApplyRotationVisual()
        {
            transform.rotation = Quaternion.Euler(0f, Data.rotation * 60f, 0f);
        }

        /// <summary>「置けるマス」として常時ハイライトするかを設定する。</summary>
        public void SetAvailable(bool available)
        {
            _isAvailable = available;
            if (IsPlaced) return;

            // 空き枠はワイヤーフレームで表示（solid mesh は非表示）
            if (tileRenderer != null) tileRenderer.enabled = false;
            if (meshCollider != null) meshCollider.enabled = available;
            if (_wireRoot    != null) _wireRoot.SetActive(available);

            if (available && _worldType != null)
                SpawnWorldProps();
        }

        public void Highlight(bool on, bool placeable = true)
        {
            if (tileRenderer == null) return;
            // このタイルが本来持つべき色（配置済み→タイル色、未配置→世界タイプ色）
            Color baseColor = Data.tileType != null ? Data.tileType.tileColor
                            : _worldType   != null ? _worldType.tileColor
                            : _defaultColor;

            if (!on || placeable)
            {
                // ホバー解除 or 配置可能: ゴーストタイルが視覚フィードバックを担うのでベースカラーを維持
                tileRenderer.material.color = baseColor;
                return;
            }
            // 配置不可: 赤みをつけてフィードバック
            tileRenderer.material.color = Color.Lerp(baseColor, Color.red, 0.55f);
        }
    }
}
