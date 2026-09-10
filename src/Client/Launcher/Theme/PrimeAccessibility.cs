using System;
using Avalonia;
using Avalonia.Automation;
using MphRead.Mods.Input;

namespace MphRead.Mods.Launcher.Theme;

/// <summary>Severity used when exposing a shell status to assistive clients.</summary>
public enum PrimeStatusKind
{
    Info,
    Success,
    Warning,
    Error
}

/// <summary>
/// Small accessibility contract for code-native and XAML-created controls.
/// Avalonia owns the platform bridge; this helper only supplies meaningful,
/// stable names and live-region semantics.
/// </summary>
public static class PrimeAccessibility
{
    public static void SetName(StyledElement element, string name)
    {
        ArgumentNullException.ThrowIfNull(element);
        AutomationProperties.SetName(element, Validate(name, nameof(name)));
    }

    public static void SetDescription(StyledElement element, string description)
    {
        ArgumentNullException.ThrowIfNull(element);
        AutomationProperties.SetHelpText(element,
            Validate(description, nameof(description)));
    }

    /// <summary>
    /// Applies a readable status label and a polite/assertive live-region hint.
    /// Errors are assertive because they can require an immediate correction;
    /// routine status changes do not interrupt the player.
    /// </summary>
    public static void SetStatus(StyledElement element, string status,
        PrimeStatusKind kind = PrimeStatusKind.Info)
    {
        ArgumentNullException.ThrowIfNull(element);
        string value = Validate(status, nameof(status));
        AutomationProperties.SetName(element, value);
        AutomationProperties.SetItemStatus(element, value);
        AutomationProperties.SetLiveSetting(element,
            kind == PrimeStatusKind.Error
                ? AutomationLiveSetting.Assertive
                : AutomationLiveSetting.Polite);
    }

    private static string Validate(string value, string parameterName)
    {
        if (String.IsNullOrWhiteSpace(value))
            throw new ArgumentException("An accessible label is required.",
                parameterName);
        return value.Trim();
    }
}

/// <summary>
/// Prime shell prompts are a presentation facade over the normalized input
/// mapping. The family-specific labels stay in <see cref="ControllerGlyphs"/>
/// so the shell cannot drift from settings and gameplay prompts.
/// </summary>
public static class PrimeControllerGlyphs
{
    public static string Label(GamepadButtons button, ControllerFamily family)
        => ControllerGlyphs.Label(button, family);

    public static string Prompt(string action, GamepadButtons button,
        ControllerFamily family)
    {
        if (String.IsNullOrWhiteSpace(action))
            throw new ArgumentException("An action label is required.",
                nameof(action));
        return $"{action.Trim()} ({Label(button, family)})";
    }
}
