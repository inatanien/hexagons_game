// 役割: 川タイルの水流演出。単体配置時は HexTile の WaterPS を停止（滞留）、
//       川タイル同士が River エッジで接続した瞬間に WaterPS を再開させる。
//       以後の上流・下流タイルへ連鎖伝播する。

using System.Collections.Generic;
using UnityEngine;
using ElfVillage.Core;
using ElfVillage.HexGrid;

namespace ElfVillage.Tiles
{
    public class RiverFlowSystem : MonoBehaviour
    {
        [Header("川タイル種別")]
        [SerializeField] private TileType[] _riverTypes;

        private readonly Dictionary<HexTile, TileFlowState> _states   = new();
        private readonly Dictionary<HexCoord, HexTile>       _coordMap = new();
        private Material _mat;

        private void Awake()    => _mat = BuildMat();
        private void OnEnable()
        {
            EventBus.Subscribe<TilePlacedEvent>(OnTilePlaced);
            EventBus.Subscribe<TileConnectedEvent>(OnTileConnected);
        }
        private void OnDisable()
        {
            EventBus.Unsubscribe<TilePlacedEvent>(OnTilePlaced);
            EventBus.Unsubscribe<TileConnectedEvent>(OnTileConnected);
        }
        private void OnDestroy() { foreach (var s in _states.Values) s.Destroy(); }

        // ─── 川タイル配置 ──────────────────────────────────────────────────
        // TileConnectedEvent より後に発火するため、接続済みタイルは IsFlowing になっている

        private void OnTilePlaced(TilePlacedEvent evt)
        {
            if (!IsRiver(evt.TileType)) return;

            // OnTileConnected で先行登録された可能性があるためチェック
            if (!_states.TryGetValue(evt.Tile, out var state))
            {
                state = new TileFlowState();
                _states[evt.Tile]    = state;
                _coordMap[evt.Coord] = evt.Tile;
            }

            if (!state.IsFlowing)
            {
                // 接続なし → WaterPS を止めて滞留 PS を表示
                ControlTileWaterPS(evt.Tile, play: false);
                // タイル天面 (tileHeight≒0.15f) より上に配置する
                state.StagnantPS = CreateStagnantPS(evt.Tile.transform.position + Vector3.up * 0.22f);
            }
        }

        // ─── 川×川接続 → 流れの確立・伝播 ──────────────────────────────────
        // TilePlacedEvent より先に発火するため配置タイルを先行登録してから処理する

        private void OnTileConnected(TileConnectedEvent evt)
        {
            if (!IsRiver(evt.TileType)) return;

            // 配置タイルを先行登録（未登録なら）
            if (!_states.TryGetValue(evt.PlacedTile, out var sx))
            {
                sx = new TileFlowState();
                _states[evt.PlacedTile]              = sx;
                _coordMap[evt.PlacedTile.Data.coord] = evt.PlacedTile;
            }

            foreach (var edge in evt.Edges)
            {
                if (!IsRiver(edge.Neighbor.Data.tileType)) continue;
                if (!_states.TryGetValue(edge.Neighbor, out var sy)) continue;

                int dXY = edge.Direction;
                int dYX = (dXY + 3) % 6;

                // River エッジ同士の接続のみ流れを確立する
                if (evt.PlacedTile.Data.GetEdge(dXY) != EdgeType.River) continue;
                if (edge.Neighbor.Data.GetEdge(dYX)   != EdgeType.River) continue;

                if (!sx.IsFlowing && !sy.IsFlowing)
                {
                    // 初接続：配置タイル(X)→隣(Y)方向で流れを確立
                    Activate(sx, evt.PlacedTile, OtherRiverDir(evt.PlacedTile, dXY), dXY);
                    Activate(sy, edge.Neighbor,  dYX, OtherRiverDir(edge.Neighbor, dYX));
                    Cascade(evt.PlacedTile, upstream: true);
                    Cascade(edge.Neighbor,  upstream: false);
                }
                else if (sy.IsFlowing && !sx.IsFlowing)
                {
                    if (dYX == sy.ExitDir)
                    {
                        // X は Y の下流
                        Activate(sx, evt.PlacedTile, dXY, OtherRiverDir(evt.PlacedTile, dXY));
                        Cascade(evt.PlacedTile, upstream: false);
                    }
                    else
                    {
                        // X は Y の上流（← ケース）
                        Activate(sx, evt.PlacedTile, OtherRiverDir(evt.PlacedTile, dXY), dXY);
                        Cascade(evt.PlacedTile, upstream: true);
                    }
                }
                // 両方すでに流れ中：変更なし
            }
        }

        // ─── 流れの活性化 ──────────────────────────────────────────────────

        private void Activate(TileFlowState s, HexTile tile, int entry, int exit)
        {
            if (s.IsFlowing) return;
            s.EntryDir  = entry;
            s.ExitDir   = exit;
            s.IsFlowing = true;
            // 滞留 PS を破棄
            if (s.StagnantPS != null) { Object.Destroy(s.StagnantPS.gameObject); s.StagnantPS = null; }
            // HexTile の WaterPS を再開（停止していた場合も、これから止める予定の場合も共に有効化）
            ControlTileWaterPS(tile, play: true);
        }

        // ─── 流れの連鎖伝播 ────────────────────────────────────────────────

        private void Cascade(HexTile origin, bool upstream)
        {
            if (!_states.TryGetValue(origin, out var s)) return;
            int nextDir = upstream ? s.EntryDir : s.ExitDir;
            if (nextDir < 0) return;

            var nc = origin.Data.coord.Neighbor(nextDir);
            if (!_coordMap.TryGetValue(nc, out var next)) return;
            if (!_states.TryGetValue(next, out var ns) || ns.IsFlowing) return;
            if (next.Data.GetEdge((nextDir + 3) % 6) != EdgeType.River) return;

            int en, ex;
            if (upstream)
            {
                ex = (nextDir + 3) % 6;
                en = OtherRiverDir(next, ex);
            }
            else
            {
                en = (nextDir + 3) % 6;
                ex = OtherRiverDir(next, en);
            }

            Activate(ns, next, en, ex);
            Cascade(next, upstream);
        }

        // ─── ヘルパー ────────────────────────────────────────────────────────

        private int OtherRiverDir(HexTile tile, int exclude)
        {
            for (int d = 0; d < 6; d++)
                if (d != exclude && tile.Data.GetEdge(d) == EdgeType.River) return d;
            return -1;
        }

        private bool IsRiver(TileType t)
        {
            if (t == null || _riverTypes == null) return false;
            foreach (var rt in _riverTypes) if (rt == t) return true;
            return false;
        }

        // HexTile.SpawnWater が生成した "WaterPS" を表示/非表示で制御する
        // GetComponentsInChildren は不確かなため直接子を走査する
        private static void ControlTileWaterPS(HexTile tile, bool play)
        {
            int childCount = tile.transform.childCount;
            for (int i = 0; i < childCount; i++)
            {
                Transform child = tile.transform.GetChild(i);
                if (child.name != "WaterPS") continue;
                child.gameObject.SetActive(play);
                if (play)
                {
                    var ps = child.GetComponent<ParticleSystem>();
                    if (ps != null) ps.Play();
                }
            }
        }

        // ─── 滞留 PS 生成 ────────────────────────────────────────────────────

        private ParticleSystem CreateStagnantPS(Vector3 pos)
        {
            var go = new GameObject("RiverStagnant");
            go.transform.SetParent(transform);
            go.transform.position = pos;
            var ps  = go.AddComponent<ParticleSystem>();
            if (_mat != null) go.GetComponent<ParticleSystemRenderer>().material = _mat;

            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop            = true;
            main.duration        = 3f;
            main.maxParticles    = 20;
            main.startSpeed      = new ParticleSystem.MinMaxCurve(0f);
            main.startLifetime   = new ParticleSystem.MinMaxCurve(2f, 4f);
            main.startSize       = new ParticleSystem.MinMaxCurve(0.08f, 0.16f);
            main.startColor      = new ParticleSystem.MinMaxGradient(
                new Color(0.55f, 0.82f, 1f, 0.70f), new Color(0.75f, 0.92f, 1f, 0.55f));
            main.gravityModifier = new ParticleSystem.MinMaxCurve(0f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var em = ps.emission; em.rateOverTime = 5f;

            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Box;
            sh.scale     = new Vector3(1.5f, 0.05f, 1.5f);

            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space   = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);
            vel.y = new ParticleSystem.MinMaxCurve(0f, 0f);
            vel.z = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f),
                        new GradientAlphaKey(1f, 0.8f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(g);

            ps.Play();
            return ps;
        }

        private static Material BuildMat()
        {
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                      ?? Shader.Find("Sprites/Default");
            if (shader == null) return null;
            var mat = new Material(shader) { name = "RiverStagnant_Runtime" };
            mat.SetFloat("_Surface", 1f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.SetColor("_BaseColor", Color.white);
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return mat;
        }

        // ─── 状態クラス ──────────────────────────────────────────────────────

        private sealed class TileFlowState
        {
            public int  EntryDir  = -1;
            public int  ExitDir   = -1;
            public bool IsFlowing;
            public ParticleSystem StagnantPS;
            public void Destroy()
            {
                if (StagnantPS != null) Object.Destroy(StagnantPS.gameObject);
            }
        }
    }
}
