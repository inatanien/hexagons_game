// 役割: 複合タイル（TileType.elements）内で、各要素（Tree/Flower等）を六角形内の
//       「自然な担当領域」へ振り分けるための純粋関数群（Session 10）。
//       MonoBehaviour・UnityEngine.Randomに依存せず、同じ入力からは常に同じ結果を返す。
//       候補位置は正規化空間（半径0〜1）で生成し、PropTypeごとのmaxRadiusへのスケールは
//       呼び出し側（HexTile）が割当後に行う。

using UnityEngine;

namespace ElfVillage.Tiles
{
    public static class ElementRegionLayout
    {
        /// <summary>正規化空間（半径0〜1、タイル中心が原点）の候補位置1件ぶん。</summary>
        public readonly struct Candidate
        {
            public readonly float NormX;
            public readonly float NormZ;
            public readonly int   Seed;

            public Candidate(float normX, float normZ, int seed)
            {
                NormX = normX;
                NormZ = normZ;
                Seed  = seed;
            }
        }

        /// <summary>
        /// 文字列から決定論的・プロセス非依存な安定ハッシュを得る。
        /// string.GetHashCode()はランタイムのハッシュランダム化の影響を受けうるため使わない。
        /// </summary>
        public static int StableStringHash(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            unchecked
            {
                int hash = 17;
                foreach (char c in s) hash = hash * 31 + c;
                return hash;
            }
        }

        /// <summary>
        /// タイル座標・TileType識別値から、このタイル1枚ぶんの境界方向（度、0〜360）を決める。
        /// 要素配列全体で共有する値で、UnityEngine.Randomは使用しない。
        /// </summary>
        public static float ComputeBoundaryDirectionDeg(int coordQ, int coordR, int typeIdentityHash)
        {
            int h   = coordQ * 92821 + coordR * 68917 + typeIdentityHash * 131;
            int deg = ((h % 360) + 360) % 360;
            return deg;
        }

        /// <summary>
        /// candidateCount件ぶんの正規化候補位置（半径0〜1）を、既存のゴールデンアングル・
        /// スパイラル式と同じ考え方で生成する。角度計算のみを共有し、半径スケールは
        /// 呼び出し側が要素ごとのmaxRadiusで事後的に行う。
        /// </summary>
        public static Candidate[] GenerateNormalizedCandidates(
            int candidateCount, int coordQ, int coordR, int typeIdentityHash,
            float goldenAngleDeg, float baseRotationDeg)
        {
            if (candidateCount <= 0) return new Candidate[0];

            var result = new Candidate[candidateCount];
            for (int i = 0; i < candidateCount; i++)
            {
                int seed = ComputeCandidateSeed(coordQ, coordR, i, typeIdentityHash);

                float rNorm = candidateCount > 1 ? Mathf.Sqrt((i + 0.5f) / candidateCount) : 0f;
                // 既存のジッター幅（((seed/21)%21-10)/200 度相当）と同じ大きさ感を維持する
                float jitterDeg = ((seed / 21) % 21 - 10) / 20f;
                float angleDeg  = i * goldenAngleDeg + baseRotationDeg + jitterDeg;
                float rad       = angleDeg * Mathf.Deg2Rad;

                result[i] = new Candidate(Mathf.Cos(rad) * rNorm, Mathf.Sin(rad) * rNorm, seed);
            }
            return result;
        }

        /// <summary>
        /// 各候補について、境界方向への投影スコアを求める。
        /// 小さな決定論的ノイズを加えることで、スコアが近い（=境界付近の）候補同士のみ
        /// 順序が入れ替わりうるようにし、完全な直線分割に見えるのを防ぐ。
        /// </summary>
        public static float[] ComputeScores(
            Candidate[] candidates, float boundaryDirDeg,
            int coordQ, int coordR, int typeIdentityHash)
        {
            float rad  = boundaryDirDeg * Mathf.Deg2Rad;
            float dirX = Mathf.Cos(rad);
            float dirZ = Mathf.Sin(rad);

            var scores = new float[candidates.Length];
            for (int i = 0; i < candidates.Length; i++)
            {
                float dot = candidates[i].NormX * dirX + candidates[i].NormZ * dirZ;

                // ノイズ用に候補本体とは別系統のseedを使う（同じ乗数ハッシュ様式）。
                int   noiseSeed = ComputeCandidateSeed(coordQ, coordR, i, typeIdentityHash * 7 + 3);
                float noise     = ((noiseSeed % 21) - 10) / 400f; // ±0.025程度の小さな揺らぎ

                scores[i] = dot + noise;
            }
            return scores;
        }

        /// <summary>
        /// スコア昇順にindexを並べ替える。完全な同点はindex昇順でタイブレークするため、
        /// 常に一意な全順序となり、ソート結果は決定論的になる。
        /// </summary>
        public static int[] SortIndicesByScore(float[] scores)
        {
            var indices = new int[scores.Length];
            for (int i = 0; i < indices.Length; i++) indices[i] = i;

            System.Array.Sort(indices, (a, b) =>
            {
                float sa = scores[a];
                float sb = scores[b];
                if (sa < sb) return -1;
                if (sa > sb) return 1;
                return a.CompareTo(b); // 完全同点はindex昇順
            });
            return indices;
        }

        /// <summary>
        /// score順に並んだindex列を、要素ごとのcountsに従って連続区間へ切り出す。
        /// counts合計はsortedIndices.Lengthと一致する前提（呼び出し側が保証する）。
        /// 万一超過する入力が来ても例外を出さないよう安全にクランプする。
        /// </summary>
        public static int[][] PartitionByCounts(int[] sortedIndices, int[] counts)
        {
            var result = new int[counts.Length][];
            int cursor = 0;
            for (int e = 0; e < counts.Length; e++)
            {
                int remaining = Mathf.Max(0, sortedIndices.Length - cursor);
                int c         = Mathf.Clamp(counts[e], 0, remaining);
                var chunk     = new int[c];
                for (int k = 0; k < c; k++) chunk[k] = sortedIndices[cursor + k];
                cursor    += c;
                result[e]  = chunk;
            }
            return result;
        }

        /// <summary>
        /// 候補位置・ノイズ計算で共有する決定論的シード（既存コードと同じ乗数ハッシュ様式）。
        /// 既存のSpawnTrees等と同様にMathf.Absで非負に正規化する
        /// （seed % slotCount を配列添字（PickTreeVariantFrom等）に使う箇所があり、
        /// 負のseedだと負の添字になり例外を起こすため）。int.MinValueの反転（依然負）にも
        /// 備えて最終的に0以上へクランプする。
        /// </summary>
        private static int ComputeCandidateSeed(int coordQ, int coordR, int index, int typeIdentityHash)
        {
            unchecked
            {
                int raw = coordQ * 92821 + coordR * 68917 + index * 40361 + typeIdentityHash * 131;
                int abs = Mathf.Abs(raw);
                return abs < 0 ? 0 : abs; // int.MinValueのAbsは依然負になるための保険
            }
        }
    }
}
