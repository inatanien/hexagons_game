// 役割: WorldBreathSystem.LeafColorGradient（葉っぱVFX色の純粋関数）の単体テスト。
//       地面色統一でtileColorが白化しても葉色が白くならないことの回帰確認。

using NUnit.Framework;
using ElfVillage.Tiles;
using UnityEngine;

namespace ElfVillage.Tests
{
    public class WorldBreathLeafColorTests
    {
        [Test]
        public void LeafColorGradient_OldForestColor_ProducesGreenishGradient()
        {
            // 旧TileType_Forest.tileColor（地面色統一前の値）を入力した場合、
            // 緑〜黄緑系（G成分がR・Bより高い）のグラデーションになるべき。
            var oldForestColor = new Color(0.13f, 0.55f, 0.13f, 1f);
            var gradient = WorldBreathSystem.LeafColorGradient(oldForestColor);

            Color c1 = gradient.colorMin;
            Color c2 = gradient.colorMax;

            Assert.Greater(c1.g, c1.r, "colorMinはG成分がR成分より高い緑系であるべき");
            Assert.Greater(c1.g, c1.b, "colorMinはG成分がB成分より高い緑系であるべき");
            Assert.Greater(c2.g, c2.b, "colorMaxはG成分がB成分より高い黄緑系であるべき");
        }

        [Test]
        public void LeafColorGradient_WhiteInput_DoesNotStayWhite()
        {
            // 地面色統一でtileColorが白(1,1,1,1)になった場合でも、
            // このメソッドへ白を渡さなければ問題ないが、万一白が渡っても
            // 純粋関数自体は白のままにはならないこと（Color.Lerpの挙動確認）。
            var white = Color.white;
            var gradient = WorldBreathSystem.LeafColorGradient(white);

            Color c1 = gradient.colorMin;
            Color c2 = gradient.colorMax;

            Assert.IsFalse(ApproximatelyWhite(c1), "白入力でもcolorMinは白のままにならないはず");
            Assert.IsFalse(ApproximatelyWhite(c2), "白入力でもcolorMaxは白のままにならないはず");
        }

        private static bool ApproximatelyWhite(Color c)
        {
            const float eps = 0.01f;
            return Mathf.Abs(c.r - 1f) < eps && Mathf.Abs(c.g - 1f) < eps && Mathf.Abs(c.b - 1f) < eps;
        }
    }
}
