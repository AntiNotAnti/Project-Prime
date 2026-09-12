using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using MphRead.Mods.Accounts;
using MphRead.Mods.Launcher.Gui;
using MphRead.Mods.Launcher.Presentation;
using MphRead.Mods.Launcher.Resources;
using MphRead.Mods.Update;
using Xunit;

namespace MphRead.Tests.Client;

[Collection(AvaloniaUiCollection.Name)]
public sealed class PrimeFoundationTests
{
    [Fact]
    public void PrimeUiCopyProvidesStronglyNamedNeutralStrings()
    {
        Assert.Equal("Play", PrimeUiCopy.Play_Title);
        Assert.Equal("No public lobbies are open right now.",
            PrimeUiCopy.Play_NoLobbies_Title);
        Assert.Equal("Updates are unavailable for local builds.",
            PrimeUiCopy.Update_LocalBuild);
    }

    [Fact]
    public void PrimeUserMessageHidesTechnicalUpdateAndAccountDiagnostics()
    {
        PrimeUserMessage local = PrimeUserMessage.ForUpdate(
            new UpdateCheckResult.NotApplicable("LocalBuild"));
        Assert.Equal(PrimeUserMessageSeverity.Info, local.Severity);
        Assert.Equal("Updates are unavailable for local builds.", local.Text);
        Assert.Equal("LocalBuild", local.DiagnosticCode);

        PrimeUserMessage verification = PrimeUserMessage.Translate(
            "Update verification failed. Project Prime was not modified.",
            PrimeUserMessageSeverity.Error);
        Assert.Equal(PrimeUserMessageSeverity.Error, verification.Severity);
        Assert.Equal("The update could not be verified. Project Prime was not modified.",
            verification.Text);

        PrimeUserMessage account = PrimeUserMessage.ForAccount(
            "POST /api/login returned 401: invalid credentials");
        Assert.Equal("Sign-in failed. Check your email and password.", account.Text);
        Assert.DoesNotContain("POST", account.Text, StringComparison.Ordinal);

        PrimeUserMessage rateLimited = PrimeUserMessage.ForAccountError(
            new AccountServiceException("rate_limited", AccountFailureKind.RateLimited,
                HttpStatusCode.TooManyRequests, "rate_limited"));
        Assert.Equal("Too many attempts. Try again shortly.", rateLimited.Text);
        Assert.Equal("Unable to connect right now. Check your connection and try again.",
            PrimeUserMessage.ForNetwork("connection refused").Text);
    }

    [Fact]
    public async Task LocalBuildUpdateCheckIsNotApplicableAndNeverRedErrors()
    {
        var client = new UpdateManifestClient(new NoRequestHandler(), rid: "win-x64");
        UpdateCheckResult result = await client.CheckAsync();
        UpdateCheckResult.NotApplicable notApplicable =
            Assert.IsType<UpdateCheckResult.NotApplicable>(result);
        Assert.Equal("LocalBuild", notApplicable.Reason);

        var coordinator = new UpdateCoordinator(client);
        Assert.Equal(result, await coordinator.CheckAsync());
        Assert.Equal(UpdateState.NotApplicable, coordinator.Status.State);
        Assert.Equal("Updates are unavailable for local builds.", coordinator.Status.Message);
    }

    [AvaloniaFact]
    public void RichEmptyStateKeepsActionsAndLegacyMessageShape()
    {
        var primary = PrimeControlFactory.Button("Retry", primary: true);
        var secondary = PrimeControlFactory.Button("Back", quiet: true);
        PrimeEmptyState empty = PrimeControlFactory.EmptyState(
            "No lobbies", "Refresh the list or host a new lobby.", primary,
            secondary, glyph: "○");

        Assert.Contains("prime-empty-state", empty.Classes);
        Assert.Equal("No lobbies", empty.Title);
        Assert.Equal("Refresh the list or host a new lobby.", empty.Body);
        Assert.Equal("○", empty.Glyph);
        Assert.Same(primary, empty.PrimaryAction);
        Assert.Same(secondary, empty.SecondaryAction);
        Assert.Single(empty.Children.OfType<WrapPanel>());

        PrimeEmptyState legacy = new("Nothing here yet.");
        Assert.Equal("Nothing here yet.", legacy.Body);
        Assert.Contains("prime-compact", legacy.Classes);
        Assert.Single(legacy.Children);
    }

    [AvaloniaFact]
    public void SemanticSurfaceFactoriesExposeStableClasses()
    {
        Assert.Contains("prime-hero-card",
            PrimeControlFactory.HeroCard(new Border()).Classes);
        Assert.Contains("prime-action-card",
            PrimeControlFactory.ActionCard(new Border()).Classes);
        Assert.Contains("prime-panel",
            PrimeControlFactory.Panel(new Border()).Classes);
        Assert.Contains("prime-compact-panel",
            PrimeControlFactory.CompactPanel(new Border()).Classes);
    }

    [AvaloniaFact]
    public void SelectionCanChangeWithoutTouchingFocusOrControlGeometry()
    {
        var row = new PrimeSelectedRow(new Border(), selected: false);
        Assert.False(row.IsSelected);
        Assert.DoesNotContain("prime-selected", row.Classes);

        row.SetSelected(true);
        Assert.True(row.IsSelected);
        Assert.Contains("prime-selected", row.Classes);
        row.SetSelected(false);
        Assert.False(row.IsSelected);
        Assert.DoesNotContain("prime-selected", row.Classes);
    }

    [Fact]
    public void FocusStylesKeepTheSelectionGeometryStable()
    {
        Dictionary<string, XElement> controls = LoadStyles(
            "src/Client/Launcher/Theme/PrimeControls.axaml");

        AssertSetter(controls, ":is(Button).prime-button", "BorderThickness", "1");
        AssertSetter(controls, ":is(Button).prime-button:focus", "BorderThickness", "1");
        AssertSetter(controls, ":is(Button).prime-button:focus-visible",
            "BorderThickness", "1");
        AssertSetter(controls, ":is(Button).prime-button.prime-quiet:focus",
            "BorderThickness", "1");
        AssertSetter(controls, "#NavPanel :is(Button).prime-button", "BorderThickness", "0,0,0,2");
        AssertSetter(controls, "#NavPanel :is(Button).prime-button:focus",
            "BorderThickness", "0,0,0,2");
        AssertSetter(controls, "#NavPanel :is(Button).prime-button:focus-visible",
            "BorderThickness", "0,0,0,2");
        AssertSetter(controls, "#NavPanel :is(Button).prime-button.prime-primary:focus",
            "BorderThickness", "0,0,0,2");
        AssertSetter(controls, ":is(Button).prime-tab", "BorderThickness", "0,0,0,2");
        AssertSetter(controls, ":is(Button).prime-tab:focus", "BorderThickness", "0,0,0,2");
        AssertSetter(controls, "ComboBox", "BorderThickness", "1");
        AssertSetter(controls, "ComboBox:focus", "BorderThickness", "1");
        AssertSetter(controls, "TextBox.prime-input, ComboBox.prime-input",
            "BorderThickness", "1");
        AssertSetter(controls, "TextBox.prime-input:focus, ComboBox.prime-input:focus",
            "BorderThickness", "1");
    }

    private static Dictionary<string, XElement> LoadStyles(string relativePath)
    {
        XDocument document = XDocument.Load(FindRepositoryFile(relativePath));
        return document.Descendants()
            .Where(element => element.Name.LocalName == "Style")
            .ToDictionary(element => element.Attribute("Selector")!.Value,
                StringComparer.Ordinal);
    }

    private static void AssertSetter(IReadOnlyDictionary<string, XElement> styles,
        string selector, string property, string expected)
    {
        XElement setter = styles[selector].Elements()
            .Single(element => element.Name.LocalName == "Setter"
                && element.Attribute("Property")?.Value == property);
        Assert.Equal(expected, setter.Attribute("Value")?.Value);
    }

    private static string FindRepositoryFile(string relativePath)
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory);
            directory != null; directory = directory.Parent)
        {
            string candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException(relativePath);
    }

    private sealed class NoRequestHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromException<HttpResponseMessage>(
                new InvalidOperationException("the local-build test must not make a request"));
    }
}
