# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## セッション開始手順

毎回以下の順で状況を確認してから作業を開始すること：

```bash
# 1. Git履歴確認
git log --oneline -10

# 2. 未コミット変更確認
git status

# 3. UnityMCPで現在のシーンとHierarchyを取得
# → mcp__UnityMCP__manage_scene(action="get_active")
# → mcp__UnityMCP__manage_scene(action="get_hierarchy")
```

## 開発ルール

- **既存機能を壊さない**
- **変更前に実装内容をユーザーに説明し、承認を得てから実装する**
- **ファイルを削除しない**
- **エラーが出たら原因を説明する**
- **1回の作業は1機能だけにする**
- **新しいスクリプトにはファイル冒頭（using の前）に `// 役割: ...` コメントを必ず入れる**

## 作業後の報告フォーマット

実装後に必ず以下をまとめて報告すること：

- 追加したファイル
- 変更したファイル
- 作成したGameObject
- Unity上で手動設定が必要なもの

## Git運用

コミット前に必ず以下を確認すること：

- [ ] Consoleエラーが0件
- [ ] 今回実装した機能をUnity上で動作確認
- [ ] 既存機能が壊れていないことを確認（リグレッションチェック）

確認後にコミットする。

```bash
git add .
git commit -m "PhaseN: 機能名"
git push
```

コンパイルエラーがないことを確認してからコミットする。

## Unity環境

- **Unity バージョン**: 6000.0.x（Unity 6）
- **レンダリング**: Universal Render Pipeline (URP) 17.0.4
- **Input System**: New Input System 1.19.0
- **MCP**: `com.coplaydev.unity-mcp`（`http://127.0.0.1:8080/mcp`）

## UnityMCP 接続

このプロジェクトのMCPサーバーは `D:\Unity\hexagons_game\hexagons_game` フォルダから起動したセッションでのみ利用可能。

```bash
# このディレクトリから claude を起動すること
cd D:\Unity\hexagons_game\hexagons_game
claude
```

## テスト実行

Unity Test Runner（EditMode）を使用：

```
# UnityMCP経由
mcp__UnityMCP__run_tests(mode="EditMode")

# または Unity Editor: Window > General > Test Runner > Run All
```

## プロジェクト概要

**精霊樹の森**（仮）— Dorfromantik風の癒し系六角形タイル配置シミュレーション。可愛い精霊たちが暮らす森を育てる。戦闘なし、のんびり発展。

## アーキテクチャ

### レイヤー構造（上から下）

```
View Layer          TileView / SpiritView / UIView（MonoBehaviour）
      ↕ EventBus
Presenter Layer     GameManager / TilePlacer / QuestController
      ↕ read/write
Domain Model        HexGrid / TileModel / QuestModel（純粋C#、テスト可能）
      ↕ load
Data Layer          ScriptableObject / JSON SaveData
```

**EventBus** が各システムを疎結合に繋ぐ。システム間の直接参照は禁止。

### アセンブリ依存関係

```
Core ← HexGrid ← Tiles ← Spirits
Core ← Quest
Tiles + Quest ← UI
Tiles + Quest ← Save
Editor（全参照、Editor専用）
Tests/EditMode（Core + HexGrid）
Tests/PlayMode（Core + HexGrid + Tiles）
```

各アセンブリの namespace は `ElfVillage.<AssemblyName>`。

### フォルダ構成

```
Assets/
  _Game/
    Scripts/
      Core/        EventBus・ServiceLocator・ゲームループ
      HexGrid/     Cube Coordinates座標系・グリッド管理
      Tiles/       TileModel・配置ロジック・エッジマッチング
      Spirits/     精霊システム（Phase 3）
      Quest/       QuestController・条件判定
      UI/          HUD・手札パネル
      Save/        JSON セーブ・ロード
    ScriptableObjects/
      TileDefinitions/   TileDefinitionSO（タイル定義）
      QuestData/         QuestDataSO
      SpiritData/        SpiritDataSO
    Prefabs/ Art/ Audio/ VFX/
  Editor/          エディタ拡張（Editor専用asmdef）
  Scenes/          Unity シーンファイル
  Tests/
    EditMode/      純粋C#の単体テスト
    PlayMode/      Unityランタイムのテスト
```

### Hex座標系

`HexCoord`（`ElfVillage.HexGrid`）は **Cube Coordinates**（q, r, s、常に q+r+s=0）。
フラットトップ配置。`ToWorldPosition(size)` / `FromWorldPosition(pos, size)` でUnityワールド座標と相互変換。

## 開発ロードマップ

| Phase | 内容 | 状態 |
|-------|------|------|
| 0 | フォルダ構成・asmdef・HexCoord実装 | ✅ 完了 |
| 1 | TileDefinitionSO・グリッド生成・タイル配置・エッジマッチング | 🔲 次 |
| 2 | クエスト・アート・BGM・VFX | 🔲 |
| 3 | 精霊システム | 🔲 |
| 4 | セーブ・ロード・ポリッシュ | 🔲 |

## 開発理念

このゲームで最も大切なのは

「癒し」

プレイヤーを急かさない。

ストレスを与えない。

眺めているだけでも楽しいゲームを目指す。

## 参考作品

- Dorfromantik
- Islanders
- Tiny Glade

## AIへのルール

勝手に

- 名前変更
- フォルダ移動
- リファクタリング
- asmdef変更
- Package追加

は禁止。

必ずユーザーに相談すること。

1クラス1責務

巨大クラスは禁止

200行を超えたら分割を検討

コメントは「なぜ」を書く

# 開発理念

このゲームは

「数字を増やすゲーム」

ではない。

「世界を好きになるゲーム」

である。

プレイヤーが

「もう一枚置きたい」

と思える気持ち良さを最優先する。

新しい機能を追加するときは

・癒されるか
・気持ちいいか
・世界観に合うか

を最初に考えること。

## ChatGPTとの役割分担

このプロジェクトでは、

- ゲームデザイン
- 世界観
- システム設計
- 実装方針
- レビュー

は ChatGPT と相談して決定する。

Claude Code は

- Unity実装
- コード生成
- リファクタリング
- UnityMCP操作

を担当する。

仕様が曖昧な場合は、勝手に決めずユーザーに確認すること。