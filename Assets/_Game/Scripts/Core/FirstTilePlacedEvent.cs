// 役割: 最初のタイルが配置されたことを Core レイヤーに通知するイベント。
//       Tiles→Core の依存を避けるため Core 側に定義し、HexGridManager が発行する。

namespace ElfVillage.Core
{
    public sealed class FirstTilePlacedEvent { }
}
