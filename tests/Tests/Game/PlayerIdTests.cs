using System;
using System.Collections.Generic;
using System.Text.Json;
using MphRead.Identity;
using Xunit;

namespace MphRead.Tests
{
    public sealed class PlayerIdTests
    {
        private const string Text = "01234567-89ab-cdef-8123-456789abcdef";

        [Fact]
        public void PreservesAll128BitsAndValueEquality()
        {
            var guid = Guid.Parse(Text);
            var id = new PlayerId(guid);
            Assert.Equal(guid, id.Value);
            Assert.False(id.IsEmpty);
            Assert.Equal(id, PlayerId.Parse(Text.ToUpperInvariant()));
            Assert.Equal(id.GetHashCode(), PlayerId.Parse(Text).GetHashCode());
            Assert.NotEqual(id, PlayerId.Parse("01234567-89ab-cdef-8123-456789abcdee"));
            Assert.Equal(Text, id.ToString());
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("00000000-0000-0000-0000-000000000000")]
        [InlineData("0123456789abcdef8123456789abcdef")]
        [InlineData("{01234567-89ab-cdef-8123-456789abcdef}")]
        [InlineData(" 01234567-89ab-cdef-8123-456789abcdef")]
        [InlineData("01234567-89ab-cdef-8123-456789abcdef ")]
        [InlineData("01234567-89ab-cdef-8123-456789abcdeg")]
        public void RejectsEmptyMalformedAndAlternateFormats(string? text)
        {
            Assert.False(PlayerId.TryParse(text, out PlayerId id));
            Assert.True(id.IsEmpty);
            if (text == null) { Assert.Throws<ArgumentNullException>(() => PlayerId.Parse(text!)); }
            else { Assert.Throws<FormatException>(() => PlayerId.Parse(text)); }
        }

        [Fact]
        public void DefaultIsInvalidAndCannotBeWrittenAsAnIdentity()
        {
            Assert.Throws<ArgumentException>(() => new PlayerId(Guid.Empty));
            Assert.True(default(PlayerId).IsEmpty);
            Assert.Throws<JsonException>(() => JsonSerializer.Serialize(default(PlayerId)));
            Assert.Throws<JsonException>(() => JsonSerializer.Serialize(new Dictionary<PlayerId, int>
            {
                [default] = 1
            }));
        }

        [Fact]
        public void JsonIsCanonicalAndNullableAbsenceIsDistinct()
        {
            var id = PlayerId.Parse(Text);
            Assert.Equal('"' + Text + '"', JsonSerializer.Serialize(id));
            Assert.Equal(id, JsonSerializer.Deserialize<PlayerId>('"' + Text.ToUpperInvariant() + '"'));
            Assert.Null(JsonSerializer.Deserialize<PlayerId?>("null"));
            Assert.Equal("null", JsonSerializer.Serialize<PlayerId?>(null));
            Assert.Equal(id, JsonSerializer.Deserialize<PlayerId?>(JsonSerializer.Serialize<PlayerId?>(id)));
        }

        [Theory]
        [InlineData("null")]
        [InlineData("123")]
        [InlineData("true")]
        [InlineData("{}")]
        [InlineData("[]")]
        [InlineData("\"\"")]
        [InlineData("\"00000000-0000-0000-0000-000000000000\"")]
        [InlineData("\"0123456789abcdef8123456789abcdef\"")]
        public void JsonRejectsInvalidIdentityValues(string json)
        {
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<PlayerId>(json));
        }

        [Fact]
        public void DictionaryKeysUseTheSameContract()
        {
            var id = PlayerId.Parse(Text);
            string json = JsonSerializer.Serialize(new Dictionary<PlayerId, int> { [id] = 7 });
            Assert.Equal("{\"" + Text + "\":7}", json);
            Assert.Equal(7, JsonSerializer.Deserialize<Dictionary<PlayerId, int>>(json)![id]);
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Dictionary<PlayerId, int>>(
                "{\"00000000-0000-0000-0000-000000000000\":7}"));
        }

        [Fact]
        public void MissingObjectFieldStillRequiresBoundaryValidation()
        {
            // A converter cannot validate a property which never appears in JSON.
            // Future admission/report DTOs must require and validate their IDs.
            Assert.True(JsonSerializer.Deserialize<IdentityHolder>("{}")!.Id.IsEmpty);
        }

        public sealed record IdentityHolder(PlayerId Id);
    }
}
