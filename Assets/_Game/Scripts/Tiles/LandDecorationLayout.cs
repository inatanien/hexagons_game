// 役割: 「川タイルの陸地部分へ木や花を生やす」ための配置計算（純粋関数のみ）。
//
//       ★これは見た目だけの計算であり、ゲーム属性とは無関係。
//         ここで置かれる木や花は、接続判定・カテゴリ判定・デッキ・シナジー・
//         成長エフェクト・TerrainEffectWeight のいずれにも参加しない。
//         参加させないことは TileType.landDecoration が elements とは別の
//         データ軸であることで構造的に保証されている（このクラスは elements を一切見ない）。
//
//       ★考え方
//         候補点生成 → 川の中心線までの距離を測る → 近すぎる候補を捨てる → 残りへ配置。
//         捨てた分を別の候補で補充することはしない。
//         直線の川は陸地を多く食うので木が減り、曲がりは陸地が残るので木が多い、という
//         密度一定の自然な結果になる（形状ごとに最終本数が変わるのは仕様）。
//
//       ★形状で分岐しない
//         川の形は RiverChannelLayout が (edgeA, ctrl, edgeB) として渡してくれる。
//         直線・曲がり・緩カーブのどれであっても、ここのコードは同じ経路を通る。

using System.Collections.Generic;
using UnityEngine;

namespace ElfVillage.Tiles
{
    public static class LandDecorationLayout
    {
        /// <summary>配置1件ぶん。タイルローカルのXZオフセットと、個体差を決める種。</summary>
        public readonly struct Placement
        {
            public readonly Vector3 LocalOffset;
            public readonly int     Seed;

            public Placement(Vector3 localOffset, int seed)
            {
                LocalOffset = localOffset;
                Seed        = seed;
            }
        }

        /// <summary>
        /// 候補を candidateCount 個生成し、川へ近すぎるものを除外した結果を返す。
        ///
        /// clearance は引数で受け取る。木と花では板の大きさが違い、適切な余白も違うため、
        /// ここに固定値を持たせない。
        /// </summary>
        /// <param name="hasChannel">川が無いタイル（false）では除外を行わず全候補を返す</param>
        public static List<Placement> ComputePlacements(
            int candidateCount, int coordQ, int coordR,
            float goldenAngleDeg, float maxRadius,
            bool hasChannel, Vector3 edgeA, Vector3 ctrl, Vector3 edgeB, float clearance)
        {
            var result = new List<Placement>();
            if (candidateCount <= 0) return result;

            // 森タイルの木と同じ黄金角スパイラル。同じ座標なら常に同じ並びになる。
            float baseRotation = coordQ * 23f + coordR * 37f;

            for (int i = 0; i < candidateCount; i++)
            {
                int seed = ComputeSeed(coordQ, coordR, i);
                var offset = HexTile.ComputeSpiralOffset(i, candidateCount, seed,
                                                          goldenAngleDeg, maxRadius, baseRotation);

                if (hasChannel && RiverChannelLayout.IsTooCloseToChannel(offset, edgeA, ctrl, edgeB, clearance))
                    continue;   // 川の中・岸ぎわは捨てる（補充はしない）

                result.Add(new Placement(offset, seed));
            }
            return result;
        }

        /// <summary>
        /// 候補ごとの決定論的な種。既存の木・花と同じ乗数ハッシュ様式にそろえてある。
        /// seed % 配列長 を添字に使う箇所があるため、必ず0以上にする。
        /// </summary>
        public static int ComputeSeed(int coordQ, int coordR, int index)
        {
            unchecked
            {
                int raw = coordQ * 92821 + coordR * 68917 + index * 40361;
                int abs = Mathf.Abs(raw);
                return abs < 0 ? 0 : abs;   // int.MinValue の Abs は依然負になるための保険
            }
        }
    }
}
