using System;
using System.Collections.Generic;
using System.IO;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.Settings;
using Xunit;

namespace MphRead.Tests.Client;

[Collection("Match baseline globals")]
public sealed class SettingsDirtyTrackerTests
{
    [Fact]
    public void TracksChangedFieldsAndBecomesCleanWhenDraftReturnsToBaseline()
    {
        var draft = new Dictionary<string, string?>
        {
            ["graphics.field-of-view"] = "78",
            ["audio.music"] = "50"
        };
        var tracker = new SettingsDirtyTracker(() => draft);

        Assert.False(tracker.IsDirty);
        Assert.Equal(0, tracker.ChangedCount);

        draft["graphics.field-of-view"] = "80";
        tracker.Refresh();

        Assert.True(tracker.IsDirty);
        Assert.Equal(1, tracker.ChangedCount);

        draft["graphics.field-of-view"] = "78";
        tracker.Refresh();

        Assert.False(tracker.IsDirty);
        Assert.Equal(0, tracker.ChangedCount);
    }

    [Fact]
    public void SuppressionDefersComparisonUntilTheOwnerRefreshes()
    {
        var draft = new Dictionary<string, string?> { ["controls.preset"] = "Original" };
        var tracker = new SettingsDirtyTracker(() => draft);

        using (tracker.Suppress())
        {
            draft["controls.preset"] = "Enhanced";
            tracker.Refresh();
            Assert.False(tracker.IsDirty);
            Assert.True(tracker.IsSuppressed);
        }

        Assert.False(tracker.IsSuppressed);
        tracker.Refresh();
        Assert.True(tracker.IsDirty);
        Assert.Equal(1, tracker.ChangedCount);
    }

    [Fact]
    public void MarkSavedMovesBaselineWithoutOwningTheDraftSnapshot()
    {
        var draft = new Dictionary<string, string?> { ["player.name"] = "Player" };
        var tracker = new SettingsDirtyTracker(() => draft);
        draft["player.name"] = "Hunter";
        tracker.Refresh();

        tracker.MarkSaved();

        Assert.False(tracker.IsDirty);
        Assert.Equal(0, tracker.ChangedCount);
        draft["player.name"] = "Player";
        tracker.Refresh();
        Assert.True(tracker.IsDirty);
    }

    [Fact]
    public void MarkDiscardedClearsPresentationStateWithoutReplacingBaseline()
    {
        var draft = new Dictionary<string, string?> { ["graphics.fov"] = "78" };
        var tracker = new SettingsDirtyTracker(() => draft);
        draft["graphics.fov"] = "80";
        tracker.Refresh();

        tracker.MarkDiscarded();

        Assert.False(tracker.IsDirty);
        Assert.Equal(0, tracker.ChangedCount);
        tracker.Refresh();
        Assert.True(tracker.IsDirty);
    }

    [Fact]
    public void NestedSuppressionScopesRemainSuppressedUntilTheOuterScopeEnds()
    {
        var draft = new Dictionary<string, string?> { ["system.logs"] = "off" };
        var tracker = new SettingsDirtyTracker(() => draft);

        using (tracker.Suppress())
        {
            using (tracker.Suppress())
            {
                draft["system.logs"] = "on";
                tracker.Refresh();
            }
            Assert.True(tracker.IsSuppressed);
            tracker.Refresh();
            Assert.False(tracker.IsDirty);
        }

        tracker.Refresh();
        Assert.True(tracker.IsDirty);
    }

    [Fact]
    public void OnlinePresenceDefaultsOnPersistsAndExposesAChangeSeam()
    {
        string previousDirectory = LauncherPrefs.Directory;
        bool previousValue = LauncherPrefs.ShowOnlinePresence;
        string directory = Path.Combine(Path.GetTempPath(),
            "project-prime-presence-" + Guid.NewGuid().ToString("N"));
        int notifications = 0;
        EventHandler changed = (_, _) => notifications++;
        try
        {
            Directory.CreateDirectory(directory);
            LauncherPrefs.Directory = directory;
            LauncherPrefs.ShowOnlinePresence = false;
            LauncherPrefs.Save();
            Assert.Contains("show_online_presence=false", LauncherPrefs.GetSaveLines());

            // Loading and assignment are initialization/programmatic state
            // changes, not a request to update a connected Node.
            LauncherPrefs.ShowOnlinePresenceChanged += changed;
            LauncherPrefs.ShowOnlinePresence = true;
            LauncherPrefs.Load();

            Assert.False(LauncherPrefs.ShowOnlinePresence);
            Assert.Equal(0, notifications);
            LauncherPrefs.ShowOnlinePresence = true;
            Assert.Equal(0, notifications);
            LauncherPrefs.Save();
            Assert.Equal(0, notifications);

            LauncherPrefs.ShowOnlinePresence = false;
            Assert.Equal(0, notifications);
            Assert.True(LauncherPrefs.Save(notifyPresenceChange: true));
            Assert.Equal(1, notifications);

            // Saving the same value does not publish a redundant update.
            Assert.True(LauncherPrefs.Save(notifyPresenceChange: true));
            Assert.Equal(1, notifications);

            LauncherPrefs.ShowOnlinePresence = true;
            LauncherPrefs.Directory = Path.Combine(directory, "missing");
            Assert.False(LauncherPrefs.Save(notifyPresenceChange: true));
            Assert.Equal(1, notifications);
            Assert.Contains("show_online_presence=false",
                File.ReadAllLines(Path.Combine(directory, "launcher.txt")));

            // A retry after the write succeeds publishes the still-pending
            // user change exactly once.
            LauncherPrefs.Directory = directory;
            Assert.True(LauncherPrefs.Save(notifyPresenceChange: true));
            Assert.Equal(2, notifications);
        }
        finally
        {
            LauncherPrefs.ShowOnlinePresenceChanged -= changed;
            LauncherPrefs.ShowOnlinePresence = previousValue;
            LauncherPrefs.Directory = directory;
            LauncherPrefs.Save();
            LauncherPrefs.Directory = previousDirectory;
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
