using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MphRead.Identity
{
    /// <summary>A registered account's stable identity, independent of its name or
    /// current connection. Default is invalid; use PlayerId? for a guest or bot.
    /// Consumers must also reject default IDs in objects with missing fields.</summary>
    [JsonConverter(typeof(PlayerIdJsonConverter))]
    public readonly record struct PlayerId
    {
        public Guid Value { get; }
        public bool IsEmpty => Value == Guid.Empty;

        public PlayerId(Guid value)
        {
            if (value == Guid.Empty)
            {
                throw new ArgumentException("A registered player ID cannot be empty.", nameof(value));
            }
            Value = value;
        }

        /// <summary>Reads a nonempty, hyphenated UUID with no surrounding whitespace.</summary>
        public static PlayerId Parse(string text)
        {
            ArgumentNullException.ThrowIfNull(text);
            return TryParse(text, out PlayerId id)
                ? id : throw new FormatException("Expected a nonempty player UUID in D format.");
        }

        public static bool TryParse(string? text, out PlayerId id)
        {
            id = default;
            if (text?.Length != 36 || !Guid.TryParseExact(text, "D", out Guid value)
                || value == Guid.Empty)
            {
                return false;
            }
            id = new PlayerId(value);
            return true;
        }

        public override string ToString() => Value.ToString("D");
    }

    /// <summary>Uses one canonical UUID string for values and dictionary keys.
    /// Empty, null and non-string IDs fail rather than becoming account identities.</summary>
    public sealed class PlayerIdJsonConverter : JsonConverter<PlayerId>
    {
        public override PlayerId Read(ref Utf8JsonReader reader, Type typeToConvert,
            JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.String
                || !PlayerId.TryParse(reader.GetString(), out PlayerId id))
            {
                throw new JsonException("Expected a nonempty player UUID string in D format.");
            }
            return id;
        }

        public override void Write(Utf8JsonWriter writer, PlayerId value, JsonSerializerOptions options)
        {
            RequireIdentity(value);
            writer.WriteStringValue(value.ToString());
        }

        public override PlayerId ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert,
            JsonSerializerOptions options)
        {
            if (!PlayerId.TryParse(reader.GetString(), out PlayerId id))
            {
                throw new JsonException("Expected a nonempty player UUID property name in D format.");
            }
            return id;
        }

        public override void WriteAsPropertyName(Utf8JsonWriter writer, PlayerId value,
            JsonSerializerOptions options)
        {
            RequireIdentity(value);
            writer.WritePropertyName(value.ToString());
        }

        private static void RequireIdentity(PlayerId value)
        {
            if (value.IsEmpty) { throw new JsonException("An empty player ID cannot be serialized."); }
        }
    }
}
