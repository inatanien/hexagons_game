// 役割: タイル群から「見えている六角形の縁」をワールド座標の輪として取り出す共通処理。
//       輪郭をなぞる光も、祝福の光柱も、同じ縁を基準にしたいのでここへ集約する。
//
//       ★システム間で座標列を受け渡さず、必要な側がそれぞれこれを呼ぶ。
//         演出どうしが互いの内部構造を知らずに済み、片方を作り替えても他方が壊れない。
//         外周の計算はクエスト達成の瞬間だけなので、二度計算しても負荷は問題にならない。
//
//       ★角の位置はタイルの実際の transform と outerRadius から作る。
//         HexGridManager.tileSize（グリッドの間隔）と HexTile.outerRadius（見た目の大きさ）は
//         別々に設定できるため、座標だけから角を求めると、
//         2つがずれている設定では演出だけが実際の六角形から浮く。

using System.Collections.Generic;
using UnityEngine;
using ElfVillage.HexGrid;

namespace ElfVillage.Tiles
{
    public static class TileOutlineGeometry
    {
        /// <summary>
        /// タイル群の外周を、ワールド座標の閉じた輪として返す。
        /// 欠けたタイル・重複したタイルは落とすので、呼び出し側で整える必要はない。
        /// </summary>
        /// <param name="lift">タイル上面からどれだけ浮かせるか。0だと面と重なってちらつく。</param>
        public static List<List<Vector3>> BuildWorldLoops(IReadOnlyList<HexTile> tiles, float lift)
        {
            var result = new List<List<Vector3>>();
            if (tiles == null || tiles.Count == 0) return result;

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
                    if (TryCornerWorldPosition(corner, byCoord, lift, out var point)) points.Add(point);

                if (points.Count >= 3) result.Add(points);
            }

            return result;
        }

        /// <summary>
        /// 外周の輪か（穴ではないか）。
        /// HexBoundaryBuilder が外周を反時計回り・穴を時計回りに揃えているので、
        /// 上から見た符号付き面積の符号を読むだけでよい。ここで包含関係を数え直さない。
        /// </summary>
        public static bool IsOuterLoop(IReadOnlyList<Vector3> loop)
        {
            if (loop == null || loop.Count < 3) return false;

            float sum = 0f;
            for (int i = 0; i < loop.Count; i++)
            {
                Vector3 a = loop[i];
                Vector3 b = loop[(i + 1) % loop.Count];
                sum += a.x * b.z - b.x * a.z;
            }
            return sum > 0f;
        }

        /// <summary>
        /// 角のワールド座標を、その角に集まるタイルのうち実在するものから求める。
        /// 実際のメッシュと同じ「中心 + 外接半径 × 60°×index」で置く。
        /// </summary>
        private static bool TryCornerWorldPosition(HexCorner corner, Dictionary<HexCoord, HexTile> tiles,
                                                    float lift, out Vector3 position)
        {
            foreach (var coord in new[] { corner.A, corner.B, corner.C })
            {
                if (!tiles.TryGetValue(coord, out var tile) || tile == null) continue;

                for (int i = 0; i < 6; i++)
                {
                    if (HexCorner.Of(coord, i) != corner) continue;

                    float angle = Mathf.Deg2Rad * (60f * i);
                    float y     = HexMeshBuilder.TopY(tile.TileHeight) + lift;

                    position = tile.transform.position + new Vector3(
                        tile.OuterRadius * Mathf.Cos(angle), y, tile.OuterRadius * Mathf.Sin(angle));
                    return true;
                }
            }

            position = default;
            return false;
        }
    }
}
