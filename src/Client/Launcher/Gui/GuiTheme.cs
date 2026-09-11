using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;

namespace MphRead.Mods.Launcher.Gui
{
    /// <summary>
    /// The front screen's palette and metrics, in Avalonia terms.
    ///
    /// These values mirror <c>PrimeColors.axaml</c> so code-built launcher
    /// pages and XAML chrome are one product. A focused test keeps the two
    /// representations synchronized.
    ///
    /// No DPI scaling here, unlike the WinForms theme: Avalonia lays out in
    /// device-independent pixels and scales the whole visual tree itself, which
    /// is the one piece of per-monitor work LauncherTheme.S had to do by hand.
    /// </summary>
    internal static class GuiTheme
    {
        public static readonly Color Ink = Color.FromRgb(9, 12, 16);
        public static readonly Color Panel = Color.FromRgb(21, 25, 30);
        public static readonly Color PanelLight = Color.FromRgb(28, 33, 39);
        public static readonly Color Gunmetal = Color.FromRgb(52, 59, 67);
        public static readonly Color Edge = Gunmetal;
        public static readonly Color Text = Color.FromRgb(228, 231, 234);
        public static readonly Color TextDim = Color.FromRgb(155, 163, 170);
        public static readonly Color Brand = Color.FromRgb(242, 154, 46);
        public static readonly Color BrandStrong = Color.FromRgb(255, 180, 65);
        public static readonly Color BrandSurface = Color.FromRgb(48, 34, 20);
        public static readonly Color Tech = Color.FromRgb(25, 207, 230);
        public static readonly Color TechStrong = Color.FromRgb(81, 230, 245);
        public static readonly Color Success = Color.FromRgb(101, 214, 138);
        public static readonly Color Warning = Color.FromRgb(255, 176, 87);
        public static readonly Color Error = Color.FromRgb(255, 100, 105);

        // Compatibility names for the existing code-built controls. New code
        // should choose Brand, Tech, or an explicit state semantic.
        public static readonly Color Accent = Brand;
        public static readonly Color Warm = Warning;
        public static readonly Color Good = Success;
        public static readonly Color Bad = Error;

        public static readonly IBrush InkBrush = new SolidColorBrush(Ink);
        public static readonly IBrush PanelBrush = new SolidColorBrush(Panel);
        public static readonly IBrush PanelLightBrush = new SolidColorBrush(PanelLight);
        public static readonly IBrush EdgeBrush = new SolidColorBrush(Edge);
        public static readonly IBrush TextBrush = new SolidColorBrush(Text);
        public static readonly IBrush TextDimBrush = new SolidColorBrush(TextDim);
        public static readonly IBrush BrandBrush = new SolidColorBrush(Brand);
        public static readonly IBrush BrandStrongBrush = new SolidColorBrush(BrandStrong);
        public static readonly IBrush BrandSurfaceBrush = new SolidColorBrush(BrandSurface);
        public static readonly IBrush TechBrush = new SolidColorBrush(Tech);
        public static readonly IBrush TechStrongBrush = new SolidColorBrush(TechStrong);
        public static readonly IBrush GunmetalBrush = new SolidColorBrush(Gunmetal);
        public static readonly IBrush SuccessBrush = new SolidColorBrush(Success);
        public static readonly IBrush WarningBrush = new SolidColorBrush(Warning);
        public static readonly IBrush ErrorBrush = new SolidColorBrush(Error);

        public static readonly IBrush AccentBrush = BrandBrush;
        public static readonly IBrush WarmBrush = WarningBrush;
        public static readonly IBrush GoodBrush = SuccessBrush;
        public static readonly IBrush BadBrush = ErrorBrush;

        /// <summary>
        /// What the pause menu and the in-game settings lay over the match.
        ///
        /// Dark enough to read a menu on and clear enough to watch through,
        /// because the match behind it is still running -- a networked one
        /// cannot be paused, and hiding it would be a lie. A compositor that
        /// refuses window transparency renders this opaque instead, which
        /// costs the view and nothing else.
        /// </summary>
        public static readonly IBrush ScrimBrush =
            new SolidColorBrush(Color.FromArgb(196, Ink.R, Ink.G, Ink.B));

        /// <summary>
        /// The display face. Inter is embedded in the build rather than looked
        /// up on the system: the WinForms theme can ask for Bahnschrift and
        /// fall back through four more faces because Windows is known to have
        /// them, and there is no equivalent list that every Linux install has.
        /// A launcher that renders in whatever the fontconfig default happens
        /// to be is a launcher that looks different on every distribution.
        /// </summary>
        public static readonly FontFamily Display = new("avares://Avalonia.Fonts.Inter/Assets#Inter");

        public static Typeface Face(bool bold) => new(Display,
            FontStyle.Normal, bold ? FontWeight.SemiBold : FontWeight.Normal);

        /// <summary>
        /// The window's icon -- the cherry mark alone, not the wordmark: a
        /// title bar, taskbar entry and alt-tab thumbnail are all small and
        /// square, and the wide banner would either be squeezed unreadable or
        /// cropped to nothing. Lazy for the same reason the splash's copy of
        /// the wordmark is: decoded once, and a build missing the asset gets
        /// no icon rather than a crash before the window exists.
        ///
        /// Named AppIcon rather than WindowIcon: this is a
        /// <c>Lazy&lt;Avalonia.Controls.WindowIcon?&gt;</c>, and giving it the
        /// same name as the type it holds is the kind of thing that reads fine
        /// today and confuses whoever edits it next.
        /// </summary>
        public static readonly Lazy<WindowIcon?> AppIcon = new(() =>
        {
            try
            {
                using Stream stream = AssetLoader.Open(
                    new Uri("avares://ProjectPrime/Assets/project-prime-mark.png"));
                return new WindowIcon(stream);
            }
            catch (Exception)
            {
                return null;
            }
        });

        /// <summary>Blend towards white or black, for hover and pressed states.</summary>
        public static Color Shade(Color color, double amount)
        {
            double t = amount < 0 ? -amount : amount;
            int target = amount >= 0 ? 255 : 0;
            return Color.FromArgb(color.A,
                (byte)(color.R + (target - color.R) * t),
                (byte)(color.G + (target - color.G) * t),
                (byte)(color.B + (target - color.B) * t));
        }

        /// <summary>
        /// A rounded rectangle as a geometry, for the card and button shapes.
        /// Avalonia has RoundedRect on DrawingContext, so this exists only for
        /// the places that need the path itself.
        /// </summary>
        public static RoundedRect Round(Rect rect, double radius)
            => new(rect, radius);
    }
}
