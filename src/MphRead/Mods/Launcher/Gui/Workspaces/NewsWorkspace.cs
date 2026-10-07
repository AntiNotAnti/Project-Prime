#if MPHREAD_AVALONIA
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using MphRead.Mods.Launcher.Core;

namespace MphRead.Mods.Launcher.Gui
{
    internal sealed class NewsWorkspace : UserControl
    {
        internal const string DiscordUrl = NewsController.DiscordUrl;

        public NewsWorkspace(PrimeOverlayHost overlays, INewsProvider? provider = null)
        {
            var dispatches = (provider ?? new BundledNewsProvider()).Read();
            var root = new Grid
            {
                Margin = PrimeMetrics.PageMargin,
                RowDefinitions = new("Auto,*"),
                RowSpacing = 14
            };

            var filters = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6
            };
            var filterButtons = new List<PrimeTabButton>();
            var articleButtons = new List<(NewsDispatch Article, PrimeButton Button)>();
            var feed = new StackPanel { Spacing = 8 };

            var categoryBadge = new ContentControl();
            var heroSignal = PrimeChrome.Eyebrow("COMMUNITY UPLINK // READY");
            var headline = PrimeChrome.HeroTitle("");
            var summary = PrimeChrome.Text("", 15, PrimeTheme.TextSecondaryBrush);
            var heroCopy = new StackPanel
            {
                Spacing = 10,
                VerticalAlignment = VerticalAlignment.Bottom,
                MaxWidth = 760
            };
            PrimeMotionHandle? heroCopyMotion = null;
            NewsDispatch? selected = null;

            var read = new PrimeButton("OPEN TRANSMISSION →",
                () => { if (selected != null) Details(selected); }, primary: true);
            ControllerNav.Identify(read, "news.read");

            heroCopy.Children.Add(categoryBadge);
            heroCopy.Children.Add(heroSignal);
            heroCopy.Children.Add(headline);
            heroCopy.Children.Add(summary);
            heroCopy.Children.Add(new Border { MinHeight = 10 });
            heroCopy.Children.Add(read);

            var hero = new PrimeHeroPanel(heroCopy, MapShot.For("MP11 BREAKTHROUGH"), minHeight: 280);

            void Select(NewsDispatch article, bool animate = true)
            {
                selected = article;
                headline.Text = article.Title;
                summary.Text = article.Summary;
                heroSignal.Text = $"COMMUNITY UPLINK // {article.Category}";
                categoryBadge.Content = new PrimeBadge(article.Category);
                foreach (var (item, button) in articleButtons)
                    button.Selected = ReferenceEquals(item, article) || item == article;
                if (animate && TopLevel.GetTopLevel(heroCopy) != null)
                {
                    heroCopyMotion?.Cancel();
                    heroCopyMotion = PrimeMotion.Enter(heroCopy, 5, 0.14);
                }
            }

            void Show(string category)
            {
                feed.Children.Clear();
                articleButtons.Clear();
                feed.Children.Add(PrimeChrome.Eyebrow("HOME // TRANSMISSION FEED"));
                feed.Children.Add(PrimeChrome.Title("LATEST DISPATCHES"));
                feed.Children.Add(PrimeChrome.Text(
                    "Select a transmission to feature it on the command deck.",
                    PrimeTypography.BodySmall, PrimeTheme.TextSecondaryBrush));

                NewsDispatch[] articles = dispatches
                    .Where(d => category == "ALL" || d.Category == category)
                    .ToArray();

                int index = 0;
                foreach (NewsDispatch item in articles)
                {
                    NewsDispatch article = item;
                    var card = new PrimeButton(
                        item.Title,
                        $"{item.Category}  //  {item.Summary}",
                        () => Select(article),
                        compact: false);
                    ControllerNav.Identify(card, "news.article." + index++);
                    articleButtons.Add((article, card));
                    feed.Children.Add(card);
                }

                read.IsVisible = articles.Length > 0;
                if (articles.Length > 0)
                {
                    Select(articles[0], animate: false);
                }
                else
                {
                    selected = null;
                    headline.Text = "NO DISPATCHES YET";
                    summary.Text = "Check back for project news and announcements.";
                    heroSignal.Text = "COMMUNITY UPLINK // STANDBY";
                    categoryBadge.Content = new PrimeBadge(category);
                }

                foreach (PrimeTabButton button in filterButtons)
                    button.Selected = button.Label == category;
            }

            void Details(NewsDispatch item)
            {
                overlays.Show(new PrimePanel(PrimeChrome.Stack(
                    new PrimeBadge(item.Category),
                    PrimeChrome.HeroTitle(item.Title),
                    PrimeChrome.Text(item.Detail),
                    new PrimeButton("CLOSE", overlays.Close))),
                    PrimeModalSize.Medium);
            }

            foreach (string filter in new[] { "ALL", "NEWS", "PATCH NOTES", "ANNOUNCEMENTS" })
            {
                string category = filter;
                var button = new PrimeTabButton(filter, () => Show(category));
                filterButtons.Add(button);
                filters.Children.Add(button);
            }

            var discord = new PrimeButton("JOIN THE DISCORD ↗", () =>
            {
                if (!Mods.Update.Updater.OpenLink(DiscordUrl))
                {
                    overlays.Show(new PrimePanel(PrimeChrome.Stack(
                        PrimeChrome.Title("JOIN THE DISCORD"),
                        PrimeChrome.Text("Open this invite in your browser:"),
                        new SelectableTextBlock
                        {
                            Text = DiscordUrl,
                            Foreground = PrimeTheme.HighlightBrush
                        },
                        new PrimeButton("CLOSE", overlays.Close))),
                        PrimeModalSize.Small);
                }
            }, primary: true);
            ControllerNav.Identify(discord, "news.discord");

            var commandBar = PrimeChrome.Columns("*,Auto", filters, discord);
            root.Children.Add(commandBar);

            var feedScroll = new ScrollViewer
            {
                Content = feed,
                HorizontalScrollBarVisibility =
                    Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
            };
            var feedPanel = new PrimePanel(feedScroll, raised: true)
            {
                Padding = new Thickness(14)
            };

            var body = PrimeChrome.Columns("1.75*,1*", hero, feedPanel);
            Grid.SetRow(body, 1);
            root.Children.Add(body);

            Content = root;
            Show("ALL");
        }
    }
}
#endif
