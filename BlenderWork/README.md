# BlenderWork

Blender の作業ファイル置き場。

## なぜ Assets/ の外なのか

Unity は `.blend` を**自動でインポートする**（`.gitignore` の
「By default unity supports Blender asset imports」参照）。
`Assets/` の中に作業ファイルを置くと、途中経過の `.blend` まで
アセットとして取り込まれ、インポート時間とプロジェクトが汚れる。

そのため作業は `Assets/` の外で行い、**完成品だけを `Assets/` へコピー**する。

## フォルダの役割

| フォルダ | 入れるもの |
|---|---|
| `Source/` | 加工前の元データ。Tripo などの生出力（`.glb`） |
| `Blend/` | Blender の作業ファイル（`.blend`） |
| `Export/` | Unity へ渡す完成品（`.glb` / `.fbx`） |
| `Scripts/` | 自動処理用の Python スクリプト |

## Git の扱い

`Scripts/` と この README **だけ**をコミットする。
モデルの実データ（`.glb` / `.fbx` / `.blend`）は数十MBになるため
`.gitignore` で除外している。Git LFS を導入していないので、
一度コミットすると履歴から消せない点に注意。

必要になったら LFS 導入を検討する。

## スクリプトの使い方

### decimate.py — 減面

高密度メッシュを複数の目標三角形数へ落として書き出す。

```bash
"C:/Program Files (x86)/Steam/steamapps/common/Blender/blender.exe" \
  --background --factory-startup \
  --python BlenderWork/Scripts/decimate.py \
  -- <入力.glb> <出力フォルダ> <目標三角形数をカンマ区切り>
```

例:

```bash
... --python BlenderWork/Scripts/decimate.py \
  -- BlenderWork/Source/TripoCottage_Blue_v2.glb BlenderWork/Export 20000,8000,3000,1500
```

`RESULT` で始まる行に、元の面数・各出力の実面数・ファイルサイズが出る。

## 環境メモ

- Blender **5.2.0 LTS**（Steam版）
  `C:\Program Files (x86)\Steam\steamapps\common\Blender\blender.exe`
- BlenderMCP アドオン導入済み（ポート 9876）
  `%APPDATA%\Blender Foundation\Blender\5.2\scripts\addons\blender_mcp_addon.py`
  対話操作したい場合に使う。バッチ処理には不要
