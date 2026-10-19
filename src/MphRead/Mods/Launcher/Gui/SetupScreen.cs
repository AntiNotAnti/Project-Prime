using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace MphRead.Mods.Launcher.Gui
{
    /// <summary>
    /// The one thing a fresh install needs before anything else can happen:
    /// the player's own cartridge dump.
    ///
    /// Shown *instead of* the front screen while there is nothing to load,
    /// rather than as one entry among five that are all refused. A menu whose
    /// entries do nothing is a program that looks broken; one screen asking
    /// for one file is a program waiting for you.
    ///
    /// <para>
    /// <b>One way to answer it: the platform's own file dialog.</b> There used
    /// to be a path to type beside the button, on the reading that somebody
    /// who knows where their dump is would rather paste it than walk a tree to
    /// it. That is one offer too many on the one screen that has exactly one
    /// thing to do -- a text box beside a button makes a player decide which
    /// half of the screen the answer is in before they can give it -- and on a
    /// phone it cannot work at all, since the picker hands back a
    /// <c>content://</c> document with no path behind it (see
    /// <see cref="RunSetup"/>) and the keyboard covers half the screen to ask
    /// for one. It is gone on every platform.
    /// </para>
    ///
    /// <para>
    /// The cost is stated rather than worked around: a Linux box with neither
    /// zenity nor kdialog has no dialog for the button to open (see
    /// <see cref="NativeFilePicker"/>, and note the desktop heads draw these
    /// screens with Avalonia's headless backend, so the toolkit's own
    /// <c>StorageProvider</c> is not available to them). The button says so
    /// and names what to install, which is a sentence a player can act on;
    /// every other platform, including Android, has one.
    /// </para>
    /// </summary>
    internal sealed class SetupScreen : UserControl
    {
        /// <summary>Raised when the files are there, or when the player backed out.</summary>
        public event EventHandler? Closed;

        private readonly Note _log = new("", lines: 0);
        private readonly ProgressRow _progress = new();
        private readonly UiMark _choose;
        private readonly UiMark _back;
        private readonly UiWord _previews;
        private readonly SetupViewWorkScope _work = new();
        private void CloseOnce() { if (_work.TryClose()) Closed?.Invoke(this, EventArgs.Empty); }

        public SetupScreen()
        {
            Background = Brushes.Transparent;
            Focusable = true;

            var body = new StackPanel { Spacing = 8 };
            body.Children.Add(new Note(Mods.Branding.Name
                + " needs your own Metroid Prime Hunters cartridge dump. It unpacks what "
                + "it needs next to this program and leaves the file alone. No game data "
                + "is included in this download, and none is downloaded.", lines: 0));
            if (GameFiles.InProcessSetup)
            {
                body.Children.Add(new Note("The unpacked files land in " + GameFiles.Root
                    + " -- this device's own folder for the app, which shows up over USB "
                    + "under Android/data. Files already copied there are found without "
                    + "picking anything.", GuiTheme.TextDim, lines: 0));
            }
            // Previews are rendered here, from the files that are here. A run
            // can be interrupted and files can arrive after one, so asking for
            // the missing ones has to be possible without setting up again.
            _previews = new UiWord("Render map previews", 15, colour: GuiTheme.TextDim);
            _previews.Click += async (_, _) => await RenderPreviews();
            body.Children.Add(_previews);
            _progress.IsVisible = false;
            body.Children.Add(_progress);
            body.Children.Add(new ScrollViewer
            {
                Height = 160,
                Content = _log,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            });

            _choose = new UiMark(UiMark.Shape.Accept, "choose your .nds file");
            _choose.Click += async (_, _) => await ChooseRom();
            _back = new UiMark(UiMark.Shape.Cancel, "back")
            {
                // Nothing to go back to until there is something to play.
                IsVisible = GameFiles.Ready
            };
            _back.Click += (_, _) => CloseOnce();

            var holder = new ScrollViewer
            {
                Content = body,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            Content = UiLayout.Page(overGame: false, UiLayout.WellSettings,
                "game files", strip: null, body: holder, no: _back, yes: _choose);
            RefreshPreviewEntry();
            if (GameFiles.SetupInProgress) _log.Text = GameFiles.SetupWorking;
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            _work.Resume();
            var focusTicket = _work.Begin();
            _choose.IsEnabled = true; _choose.Label = "choose your .nds file";
            _previews.IsEnabled = true; _back.IsVisible |= GameFiles.Ready;
            RefreshPreviewEntry();
            LauncherBackdrop.Set(LauncherBackdropScene.Setup);
            Dispatcher.UIThread.Post(() => { if (_work.Current(focusTicket)) _choose.Focus(); }, DispatcherPriority.Background);
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            _work.Retire();
            base.OnDetachedFromVisualTree(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.Escape && _back.IsVisible)
            {
                CloseOnce();
                e.Handled = true;
                return;
            }
            base.OnKeyDown(e);
        }

        /// <summary>
        /// Ask for the cartridge dump, however this platform can be asked.
        ///
        /// Android has a real windowing backend and so a real
        /// <c>StorageProvider</c>. The desktop heads draw their screens with
        /// the headless one and have none, so they ask the operating system
        /// directly -- see <see cref="NativeFilePicker"/>, which is also where
        /// what happened before this is written down.
        /// </summary>
        private async Task ChooseRom()
        {
            var pickerTicket = _work.Begin();
            if (!_work.Current(pickerTicket)) return;
            TopLevel? top = TopLevel.GetTopLevel(this);
            if (top == null)
            {
                return;
            }
            if (!top.StorageProvider.CanOpen)
            {
                if (!NativeFilePicker.Available)
                {
                    // The one thing this screen cannot answer for itself.
                    // There is no typed path to fall back on any more -- see
                    // the class note -- so it says what to install and stops.
                    _log.Text = "This desktop has no file dialog to open. "
                        + "Install zenity or kdialog and press this again.";
                    return;
                }
                string? chosen = await NativeFilePicker.OpenFile(
                    "Your Metroid Prime Hunters cartridge dump",
                    "Nintendo DS ROM", "nds");
                if (!_work.Current(pickerTicket)) return;
                _back.IsVisible |= GameFiles.Ready;
                RefreshPreviewEntry();
                if (chosen != null)
                {
                    await RunSetup(chosen, null);
                }
                return;
            }
            var options = new FilePickerOpenOptions
            {
                Title = "Your Metroid Prime Hunters cartridge dump",
                AllowMultiple = false
            };
            if (!OperatingSystem.IsAndroid())
            {
                // Patterns are what Windows, Linux and the browser filter on.
                // Android filters by MIME type, and .nds has none -- inventing
                // one there produces a picker in which every file is refused.
                options.FileTypeFilter = new[]
                {
                    new FilePickerFileType("Nintendo DS ROM") { Patterns = new[] { "*.nds" } },
                    new FilePickerFileType("Every file") { Patterns = new[] { "*" } }
                };
            }
            IReadOnlyList<IStorageFile> picked =
                await top.StorageProvider.OpenFilePickerAsync(options);
            if (!_work.Current(pickerTicket)) return;
            if (picked.Count == 0)
            {
                return;
            }
            await RunSetup(picked[0].TryGetLocalPath(), picked[0]);
        }

        /// <summary>
        /// Unpack the file that was picked, however it was picked.
        ///
        /// <paramref name="path"/> is a real path when there is one;
        /// <paramref name="file"/> is the toolkit's handle, which on Android
        /// is a content:// document with no path behind it and has to be
        /// copied before the extractor can read it.
        /// </summary>
        private async Task RunSetup(string? path, IStorageFile? file)
        {
            if (path == null && file == null)
            {
                return;
            }
            var ticket = _work.Begin();
            if (!_work.Current(ticket)) return;
            string expectedRoot = GameFiles.Root;
            _choose.IsEnabled = false;
            _choose.Label = "working...";
            _log.Text = "";
            var progress = new SetupProgress();
            _progress.IsVisible = true;
            _progress.Set(0, "Starting");
            string? scratch = null;
            if (path == null && file != null)
            {
                // Android hands back a content:// document with no path behind
                // it. Copying is the only way to give the extractor a file, and
                // it is the player's own cartridge dump, so it is copied into
                // the app's directory and deleted afterwards.
                _log.Text = "Copying the file onto this device...";
                try
                {
                    scratch = Path.Combine(GameFiles.Root, "picked.nds");
                    await using (Stream source = await file.OpenReadAsync())
                    await using (var target = File.Create(scratch))
                    {
                        await source.CopyToAsync(target);
                    }
                    path = scratch;
                }
                catch (Exception ex)
                {
                    if (!_work.Current(ticket)) return;
                    _log.Text = $"The file could not be read: {ex.Message}";
                    Ready();
                    return;
                }
            }
            if (path == null)
            {
                _log.Text = "That file could not be opened.";
                Ready();
                return;
            }
            string romPath = path;
            // Cancellation retires callbacks/admission; it never undoes an
            // extraction already started by the existing GameFiles owner.
            var progressQueue = new SetupViewProgress();
            bool ok = false;
            try
            {
                if (!_work.Current(ticket)) return;
                ok = await GameFiles.RunSetupAsync(romPath, line =>
                {
                    if (ticket.Token.IsCancellationRequested || !progressQueue.Report(line)) return;
                    Dispatcher.UIThread.Post(() =>
                    {
                        var update = progressQueue.Drain();
                        if (!_work.Current(ticket)) return;
                        foreach (string text in update.Lines) _log.Text = Tail(_log.Text, text);
                        _progress.Set(update.Fraction, update.Stage);
                    });
                }, expectedRoot, ticket.Token);
            }
            catch (OperationCanceledException) { }
            if (scratch != null)
            {
                try
                {
                    File.Delete(scratch);
                }
                catch (IOException)
                {
                    // A copy left behind is untidy, not a failure worth saying.
                }
            }
            if (!_work.Current(ticket)) return;
            if (ok && !String.Equals(expectedRoot, GameFiles.Root, StringComparison.Ordinal))
            { _log.Text = Tail(_log.Text, "Extraction finished for the captured folder; the active destination changed."); ok = false; }
            // Game-file setup ends when the cartridge data is valid and ready.
            // Map previews are cosmetic and can involve separate GL worker processes;
            // never make a successful extraction wait for them.
            progress.Finish(ok);
            _progress.Set(progress.Fraction, progress.Stage);
            Ready();
            _log.Text = Tail(_log.Text, ok ? "Ready to play." : "Setup did not finish.");
            RefreshPreviewEntry();
            if (ok)
            {
                _progress.IsVisible = false;
                _back.IsVisible = true;
                CloseOnce();
            }
        }

        private void Ready()
        {
            _choose.IsEnabled = true;
            _choose.Label = "choose your .nds file";
        }

        private async Task RenderPreviews()
        {
            var ticket = _work.Begin();
            if (!_work.Current(ticket)) return;
            _previews.IsEnabled = false;
            _previews.Text = "Rendering...";
            await RenderMissing(null, ticket);
            if (!_work.Current(ticket)) return;
            _previews.IsEnabled = true;
            _previews.Text = "Render map previews";
            RefreshPreviewEntry();
        }

        /// <summary>
        /// The pictures are made here, on this machine, from the files that
        /// were just unpacked. Nothing is downloaded and no picture ships with
        /// the program.
        /// </summary>
        private async Task RenderMissing(SetupProgress? progress, SetupViewWorkScope.Ticket ticket)
        {
            if (!ThumbnailHost.CanRender)
            {
                return;
            }
            _log.Text = Tail(_log.Text, "Rendering map previews...");
            var reports = new SetupViewProgress();
            try
            {
                await ThumbnailHost.RenderMissingAsync(line =>
                {
                    if (ticket.Token.IsCancellationRequested || !reports.Report(line)) return;
                    Dispatcher.UIThread.Post(() =>
                    {
                        var update = reports.Drain();
                        if (!_work.Current(ticket)) return;
                        foreach (string text in update.Lines) _log.Text = Tail(_log.Text, text);
                    });
                }, ticket.Token);
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            { if (_work.Current(ticket)) _log.Text = Tail(_log.Text, "Previews did not finish: " + error.Message); }
        }

        /// <summary>How many previews are still to render, or nothing to say.</summary>
        private void RefreshPreviewEntry()
        {
            if (!GameFiles.Ready || !ThumbnailHost.CanRender)
            {
                _previews.IsVisible = false;
                return;
            }
            int missing = ThumbnailGenerator.MissingThumbnails().Count;
            _previews.IsVisible = missing > 0;
            _previews.IsEnabled = missing > 0;
        }

        /// <summary>Keep the last few lines; the extraction prints hundreds.</summary>
        private static string Tail(string? existing, string line)
        {
            string[] lines = ((existing ?? "") + "\n" + line)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries);
            return String.Join("\n", lines[Math.Max(0, lines.Length - 8)..]);
        }
    }
}
