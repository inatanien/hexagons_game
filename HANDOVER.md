---

# 精霊樹の森 — 引き継ぎドキュメント

## 現在の実装状況

- **Unity バージョン**: Unity 6 (6000.0.x)
- **レンダリング**: URP 17.0.4
- **入力システム**: New Input System 1.19.0
- **アクティブシーン**: `Assets/Scenes/Phase0_v003.unity`
- **UnityMCP コンソール**: エラー・警告ともに 0 件（正常）

---

## 完了済み機能

### Phase 0 — プロジェクト基盤
- [x] Cube Coordinates ヘックスグリッド（`HexCoord.cs`）
- [x] Assembly Definition 階層: `Core ← HexGrid ← Tiles`
- [x] ScriptableObject によるタイル定義（`TileType.cs`）
- [x] EditMode テスト環境

### Phase 1 — ヘックスタイル配置
- [x] プロシージャルヘックスメッシュ生成（`HexMeshBuilder.cs`、フラットトップ・高さあり）
- [x] タイル配置ロジック（`HexGridManager.cs`）
- [x] エッジマッチング接続判定（`EdgeMatcher.cs`、6方向チェック）
- [x] ホバー時ハイライト（配置可=緑、不可=赤）
- [x] マウスホイールでタイル回転（0〜5）
- [x] 左クリックで配置確定
- [x] グリッド半径 11（397 タイル）

### カメラ
- [x] RTSピボット方式カメラ（`CameraController.cs`）
- [x] 右ドラッグ: パン
- [x] マウスホイール: ズーム
- [x] 中ドラッグ: 水平回転
- [x] Q/E キー: 60° スナップ回転
- [x] 指数減衰 Lerp スムージング
- [x] **カーソル基準ズーム**（今セッションで実装）
- [x] **カーソル基準オービット回転**（今セッションで実装）

---

## 未実装機能

- [ ] タイルの種類追加（道・川・村など）
- [ ] タイル上への建物・木オブジェクト配置
- [ ] スコアリングシステム
- [ ] ゲームループ（デッキ・タイル供給）
- [ ] UI（現在のタイルプレビュー、スコア表示）
- [ ] セーブ/ロード
- [ ] サウンド

---

## 現在の Phase

**Phase 1 完了済み**。コアメカニクス（グリッド・配置・エッジマッチング・カメラ）が動作する状態。次は Phase 2（ゲームコンテンツ・ループ）へ進める段階。

---

## 次に実装すべき内容

優先度順：

1. **タイル種類の拡充** — 道・川・村・山などの `TileType` ScriptableObject を追加し、エッジ定義を充実させる
2. **デッキ＆タイル供給システム** — ランダム or 重み付きでタイルを引いてくる仕組み
3. **UI** — 次に置くタイルのプレビュー表示、残り枚数表示
4. **タイル上オブジェクト** — 配置済みタイルに木・家などのプロップを自動配置
5. **スコアリング** — 接続ボーナス（同種エッジの連続マッチ）

---

## 注意事項

### コーディング規約
- 全スクリプトの冒頭（`using` より前）に `// 役割: ...` コメントを必ず入れる
- 名前空間: `ElfVillage.<AssemblyName>`（例: `ElfVillage.Core`、`ElfVillage.Tiles`）
- 実装前に必ず **1.現状分析 → 2.実装計画提示 → 3.承認 → 4.実装** の順で進める

### Assembly Definition
| asmdef | 依存 |
|--------|------|
| `_Game.Scripts.Core` | `Unity.InputSystem` |
| `_Game.Scripts.HexGrid` | （なし） |
| `_Game.Scripts.Tiles` | `Core`, `HexGrid`, `Unity.InputSystem` |
| `_Game.Tests.EditMode` | `Core`, `HexGrid`, `Tiles`, TestRunner 系 |

### カメラ設計ポイント
- `_targetPivot` / `_targetYaw` / `_targetDistance`（目標値）と実描画値を分離しているため、スムージングが壊れないよう両方を正しく更新すること
- `TryGetGroundPoint()` は Y=0 平面との交点計算。タイルが Y=0 以外に移動する場合は要修正

---

## Git の状態

```
ブランチ: main（origin/main と同期済み）

未コミット（ステージなし）:
  modified:   Assets/_Game/Scripts/Core/CameraController.cs  ← 今セッションの変更
  modified:   開発メモ.txt

未追跡:
  Assets/Scenes/Phase0_v003.unity        ← 最新シーン
  Assets/Scenes/Phase0_v003.unity.meta
  Assets/Screenshots/
```

**次セッション開始時にコミット推奨**:
```bash
git add Assets/_Game/Scripts/Core/CameraController.cs \
        Assets/Scenes/Phase0_v003.unity \
        Assets/Scenes/Phase0_v003.unity.meta \
        開発メモ.txt
git commit -m "Feat: カーソル基準ズーム・オービット回転"
```

---

## UnityMCP の状態

- エラー・警告: **0 件**（正常）
- コンパイル: 問題なし
- VS Code 版 Claude Code でも UnityMCP（MCP サーバー）を設定すれば同様に利用可能

---

## 今後の実装手順

```
Phase 2: ゲームコンテンツ
  └─ 2-1: TileType ScriptableObject 追加（道・川・村・山）
  └─ 2-2: デッキシステム（TileDeck.cs）
  └─ 2-3: 現在タイルのプレビューUI

Phase 3: オブジェクト配置
  └─ 3-1: タイル上プロップ（木・家）の自動配置
  └─ 3-2: アニメーション・エフェクト

Phase 4: スコア・ゲームループ
  └─ 4-1: エッジマッチスコア計算
  └─ 4-2: ゲーム終了条件
  └─ 4-3: セーブ/ロード
```