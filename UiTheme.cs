using System.Drawing;
using System.Windows.Forms;

namespace AramOverlay
{
    internal sealed class DarkMenuColorTable : ProfessionalColorTable
    {
        private static readonly Color Background = Color.FromArgb(16, 21, 30);
        private static readonly Color Hover = Color.FromArgb(38, 48, 64);
        private static readonly Color Border = Color.FromArgb(82, 66, 42);

        public override Color ToolStripDropDownBackground { get { return Background; } }
        public override Color ImageMarginGradientBegin { get { return Background; } }
        public override Color ImageMarginGradientMiddle { get { return Background; } }
        public override Color ImageMarginGradientEnd { get { return Background; } }
        public override Color MenuBorder { get { return Border; } }
        public override Color MenuItemBorder { get { return Border; } }
        public override Color MenuItemSelected { get { return Hover; } }
        public override Color MenuItemSelectedGradientBegin { get { return Hover; } }
        public override Color MenuItemSelectedGradientEnd { get { return Hover; } }
        public override Color SeparatorDark { get { return Color.FromArgb(48, 56, 70); } }
        public override Color SeparatorLight { get { return Color.FromArgb(26, 33, 44); } }
    }

    internal static class UiTheme
    {
        public static readonly Color Text = Color.FromArgb(235, 239, 246);
        public static readonly Color Muted = Color.FromArgb(160, 170, 185);
        public static readonly Color Gold = Color.FromArgb(232, 184, 92);
        public static readonly Color High = Color.FromArgb(123, 227, 161);
        public static readonly Color Low = Color.FromArgb(255, 142, 142);

        public static Font MenuFont(float size)
        {
            try
            {
                return new Font("Microsoft YaHei UI", size, FontStyle.Regular, GraphicsUnit.Point);
            }
            catch
            {
                return new Font("Microsoft YaHei", size, FontStyle.Regular, GraphicsUnit.Point);
            }
        }

        public static void ApplyMenu(ContextMenuStrip menu)
        {
            menu.BackColor = Color.FromArgb(16, 21, 30);
            menu.ForeColor = Text;
            menu.Font = MenuFont(9.0f);
            menu.ShowImageMargin = false;
            menu.Padding = new Padding(4, 6, 4, 6);
            menu.Renderer = new ToolStripProfessionalRenderer(new DarkMenuColorTable());
            foreach (ToolStripItem item in menu.Items)
            {
                item.ForeColor = Text;
                item.Padding = new Padding(10, 6, 10, 6);
            }
        }
    }
}
