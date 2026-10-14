#if MPHREAD_AVALONIA
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using MphRead.Mods.Campaign;

namespace MphRead.Mods.Launcher.Gui
{
    /// <summary>
    /// The result surface reached after an actual Gorea ending movie has
    /// finished. It lives in the existing launcher, not a second frontend,
    /// and displays no cartridge art or source-owned game assets.
    /// </summary>
    internal sealed class CampaignEndingView : UserControl
    {
        public event EventHandler? Finished;
        private readonly TextBlock _detail;
        private readonly ScrollViewer _credits;
        private readonly PrimeButton _creditsButton;

        public CampaignEndingView(CampaignEndingSummary result)
        {
            _detail = PrimeChrome.Text("", 13, PrimeTheme.TextSecondaryBrush);
            _credits = new ScrollViewer
            {
                Content = BuildCredits(),
                MaxHeight = 330,
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                IsVisible = false
            };
            _creditsButton = new PrimeButton("VIEW PROJECT PRIME CREDITS", ToggleCredits);
            ControllerNav.Identify(_creditsButton, "campaign.ending.credits");
            var done = new PrimeButton("RETURN TO CAMPAIGN FILE SELECT",
                () => Finished?.Invoke(this, EventArgs.Empty), primary: true);
            ControllerNav.Identify(done, "campaign.ending.continue");
            _detail.Text = result.Kind == CampaignEndingKind.Complete
                ? "The final encounter is complete."
                : "The mission has ended, but Gorea's final encounter remains unfinished.";

            string completion = result.CompletionPercent is int percent
                ? percent + "%" : "NOT AVAILABLE";
            var body = PrimeChrome.Stack(
                new PrimeBadge("CAMPAIGN // ORIGINAL ENDING CINEMATIC COMPLETE"),
                PrimeChrome.Title(result.Heading),
                _detail,
                PrimeChrome.Eyebrow(result.GoreaResult),
                PrimeChrome.Text($"EXPEDITION FILE {result.Slot:00}  //  "
                    + (result.SaveCommitted ? "PROGRESS SAVED" : "SAVE NOT REQUESTED"),
                    12, result.SaveCommitted ? PrimeTheme.TextSecondaryBrush : PrimeTheme.WarningBrush, data: true),
                PrimeChrome.Text($"COMPLETION: {completion}\n"
                    + $"OCTOLITHS: {result.Octoliths} / 8\n"
                    + $"DEATHS: {result.Deaths}\n"
                    + $"HUNTERS / GUARDIANS DEFEATED: {result.HunterKills}\n"
                    + $"ENEMY DEFEATS: {result.EnemyKills}",
                    15, PrimeTheme.TextBrush, data: true),
                PrimeChrome.Text("The original game footage comes from your own cartridge dump. "
                    + "These are Project Prime's results and attribution screens, not a replacement "
                    + "for the Nintendo DS frontend's animated credits.",
                    12, PrimeTheme.TextSecondaryBrush),
                _creditsButton,
                _credits,
                done);
            Content = new PrimePanel(new ScrollViewer
            {
                Content = body,
                MaxHeight = 650,
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
            });
        }

        private void ToggleCredits()
        {
            _credits.IsVisible = !_credits.IsVisible;
            _creditsButton.Label = _credits.IsVisible ? "HIDE PROJECT PRIME CREDITS"
                : "VIEW PROJECT PRIME CREDITS";
        }

        private static Control BuildCredits()
        {
            var panel = new StackPanel { Spacing = 8, Margin = new Thickness(0, 4) };
            panel.Children.Add(PrimeChrome.Title("PROJECT PRIME CREDITS"));
            panel.Children.Add(PrimeChrome.Text(Mods.Credits.Summary,
                12, PrimeTheme.TextSecondaryBrush));
            panel.Children.Add(PrimeChrome.Text("Fruity Prime  //  " + Mods.Credits.Author,
                12, PrimeTheme.TextBrush, data: true));
            foreach (Mods.Credits.Entry entry in Mods.Credits.Entries)
            {
                panel.Children.Add(PrimeChrome.Eyebrow(entry.Who));
                panel.Children.Add(PrimeChrome.Text(entry.What, 12, PrimeTheme.TextSecondaryBrush));
            }
            panel.Children.Add(PrimeChrome.Text("Metroid Prime Hunters and its original game credits "
                + "are owned by Nintendo and its respective creators. No game data is distributed.",
                12, PrimeTheme.TextSecondaryBrush));
            return panel;
        }
    }
}
#endif
