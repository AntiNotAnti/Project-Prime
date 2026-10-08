using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace MphRead.Mods.Launcher.RmlUi.Host
{
    public enum RmlUiBindingKind { Text, Boolean }

    public readonly record struct RmlUiBindingValue(RmlUiBindingKind Kind, string Text, bool Boolean)
    {
        public static RmlUiBindingValue FromText(string value) => new(RmlUiBindingKind.Text, value ?? "", false);
        public static RmlUiBindingValue FromBoolean(bool value) => new(RmlUiBindingKind.Boolean, "", value);
    }

    /// <summary>
    /// A copied, immutable presentation snapshot. Input drafts are deliberately
    /// excluded: only an explicit submit/apply action changes an editable field.
    /// </summary>
    public sealed class RmlUiBindingSnapshot
    {
        public RmlUiDocumentToken Document { get; }
        public long Revision { get; }
        public IReadOnlyDictionary<string, RmlUiBindingValue> Bindings { get; }

        public RmlUiBindingSnapshot(RmlUiDocumentToken document, long revision,
            IEnumerable<KeyValuePair<string, RmlUiBindingValue>> bindings)
        {
            if (document.Generation == 0 || document.DocumentId == 0)
                throw new ArgumentException("An active document token is required.", nameof(document));
            if (revision < 0) throw new ArgumentOutOfRangeException(nameof(revision));
            var copy = new Dictionary<string, RmlUiBindingValue>(StringComparer.Ordinal);
            foreach (var binding in bindings)
            {
                if (String.IsNullOrWhiteSpace(binding.Key))
                    throw new ArgumentException("A binding name is required.", nameof(bindings));
                if (binding.Value.Kind is not (RmlUiBindingKind.Text or RmlUiBindingKind.Boolean))
                    throw new ArgumentException("Unknown binding kind.", nameof(bindings));
                copy.Add(binding.Key, binding.Value);
            }
            Document = document;
            Revision = revision;
            Bindings = new ReadOnlyDictionary<string, RmlUiBindingValue>(copy);
        }
    }
}
