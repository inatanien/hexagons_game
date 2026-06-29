# 精霊樹の森 — 引き継ぎドキュメント

> 他のAIに現状を伝えるためのまとめ。Claude Code が実装を担当し、ChatGPT が設計・レビューを担当する体制。

---

## 基本情報

| 項目 | 内容 |
|------|------|
| Unity バージョン | 6000.0.77f1 (Unity 6) |
| レンダリング | URP 17.0.4 |
| 入力システム | New Input System 1.19.0 |
| アクティブシーン | `Assets/Scenes/Phase1_v002.unity` |
| コンソールエラー | 0件（正常） |

---

## ゲームコンセプト

**精霊樹の森**（仮）— Dorfromantik風の癒し系六角形タイル配置シミュレーション。  
戦闘なし。のんびりタイルを置いて森を育てる。「世界を好きになるゲーム」。

---

## 現在の実装状況

### コアシステム（完成済み）

| システム | ファイル | 概要 |
|----------|----------|------|
| Hex座標系 | `HexCoord.cs` | Cube Coordinates (q,r,s)、フラットトップ |
| グリッド管理 | `HexGridManager.cs` | 半径11（397タイル）、配置・取得 |
| タイル本体 | `HexTile.cs` | メッシュ生成・プロップ配置・川フロー |
| エッジマッチング | `EdgeMatcher.cs` | 6方向接続判定 |
| カメラ | `CameraController.cs` | RTSピボット、パン/ズーム/回転 |
| デッキ・手札UI | `TileDeck.cs` / `HandUI.cs` | タイル供給・手札表示 |
| 配置プレビュー | `TilePlacementPreview.cs` | ホバー時ハイライト（緑/赤） |
| タイル接続FX | `TileConnectionFX.cs` | 接続時エフェクト |
| EventBus | `EventBus.cs` | システム間疎結合イベント |

### タイル種別（ScriptableObject）

| アセット名 | 種別 | プロップ |
|-----------|------|---------|
| TileType_Forest | 森 | 木（複数本） |
| TileType_Forest_Edge | 森の縁 | 木（少なめ） |
| TileType_Field | 花畑 | 花（茎＋花びら＋中心） |
| TileType_Village | 村 | 家 |
| TileType_Road_Straight | 道（直線） | なし |
| TileType_Road_Bend | 道（カーブ） | なし |
| TileType_River_Straight | 川（直線） | 水流パーティクル |
| TileType_River_Bend | 川（カーブ） | 水流パーティクル |
| TileType_River_Wide_Bend | 川（広カーブ） | 水流パーティクル |

### エフェクトシステム（WorldBreath GO に集約）

| コンポーネント | トリガー | エフェクト |
|--------------|---------|-----------|
| `WorldBreathSystem` | 森クラスター成長 | 葉パーティクルが舞う |
| `BirdFlightSystem` | 森クラスター | 鳥が飛ぶ |
| `FireflySystem` | 夜/条件 | 蛍が光る |
| `SynergyEvaluator` | 地形シナジー | シナジーイベント発行 |
| `ForestGrowthEvaluator` | 森タイル配置 | TerrainGrowthEvent発行 |
| `RiverFlowSystem` | 川タイル配置・接続 | 水流方向を確定・伝播 |
| `RiverGrowthEvaluator` | 川タイル8枚以上連結 | RiverClusterEvent発行 |
| `FishSystem` | RiverClusterEvent受信 | 魚が泳ぐ・跳ねる |
| `FlowerClusterEvaluator` | 花畑タイル3枚以上連結 | FlowerClusterEvent発行 |
| `FlowerPetalSystem` | FlowerClusterEvent受信 | 花びらが舞う（段階的に色追加） |

### 花びらシステムの段階

| 枚数 | 追加される色 |
|------|------------|
| 3枚〜 | 黄色 |
| 4枚〜 | 青 |
| 5枚〜 | 紫 |
| 6枚〜 | 赤 |
| 7枚〜 | ピンク |

### 川フローシステムの仕様

- タイルを置いた**瞬間**に流れ方向が確定する
- 複数の川タイルを繋げると上流・下流方向が自動伝播（BFS Cascade）
- タイルを回転させても常に一方向に流れる（`GetWorldDirEdgePos` で座標系ミスマッチ解決済み）

---

## アーキテクチャ

```
View Layer      HexTile / HandUI / TileConnectionFX（MonoBehaviour）
      ↕ EventBus
Presenter       HexGridManager / TilePlacer / WorldBreath系
      ↕
Domain          HexCoord / TileData / TileType（純粋C#/ScriptableObject）
```

**イベント一覧**:
- `TilePlacedEvent` — タイル配置時
- `TileConnectedEvent` — タイル接続時（エッジマッチ成立）
- `TerrainGrowthEvent<T>` — 地形クラスター成長時
- `RiverClusterEvent` — 川8枚以上連結時
- `FlowerClusterEvent` — 花畑3枚以上連結時
- `TerrainSynergyEvent` — 地形シナジー発生時
- `FirstTilePlacedEvent` — 最初のタイル配置時

---

## フォルダ構成（主要部分）

```
Assets/
  _Game/
    Scripts/
      Core/       EventBus, ServiceLocator, ゲームループ
      HexGrid/    HexCoord（Cube Coordinates）
      Tiles/      HexTile, HexGridManager, 各エフェクトシステム
    ScriptableObjects/
      TileDefinitions/  TileType_*.asset（9種）
    Scenes/
      Phase1_v002.unity  ← 現在のメインシーン
```

---

## 未実装・今後の課題

- [ ] クエストシステム（QuestController）
- [ ] セーブ/ロード
- [ ] BGM・SE
- [ ] 精霊システム（Phase 3 予定）
- [ ] スコアリング
- [ ] ゲーム終了条件

---

## コーディング規約

- 全スクリプト冒頭（`using` より前）に `// 役割: ...` コメント必須
- 名前空間: `ElfVillage.Tiles` / `ElfVillage.Core` / `ElfVillage.HexGrid`
- 1クラス1責務・200行超えたら分割検討
- **実装前に必ず承認を得てから実装する**
- EventBus経由でシステム間通信（直接参照禁止）
