/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Listenarr.Domain.Downloads
{
    /// <summary>
    /// The identity of one release, as a type rather than as a string.
    ///
    /// The string it wraps is unchanged: a key minted by <see cref="ReleaseIdentity"/>, either
    /// "btih:" plus a normalised v1 info-hash or "title:" plus a normalised release title. Nothing
    /// about the column, the JSON, or the download metadata moves, which is why this needs no
    /// migration.
    ///
    /// What changes is what the compiler will accept. A release key travels through five hops
    /// between the search result it is derived from and the blocklist row it ends up in, and every
    /// one of those hops used to be a bare string among other bare strings: a title, a reason, a
    /// source name, a client id. Two of the three defects this feature has had were a key being
    /// derived in the wrong place, and neither was a case the type system was asked about. It is
    /// now: a string will not pass where an identifier is expected. The ways to derive one from
    /// release fields are <see cref="ReleaseIdentity.For(Listenarr.Domain.Search.SearchResult)"/>
    /// and <see cref="ReleaseIdentity.ForGrabbed"/>, and those are the only two, which is the part
    /// that was going wrong. <see cref="FromStorage"/> is separate and public, because the EF
    /// converter lives in another assembly and has to reach it; it rebuilds a key that already
    /// exists rather than composing one out of fields.
    ///
    /// The parsing lives here too, because the format is this type's business. Asking a key
    /// whether it carries an info-hash was two private helpers on ReleaseIdentity that each
    /// re-derived the prefix by hand.
    /// </summary>
    [JsonConverter(typeof(ReleaseIdentifierJsonConverter))]
    public readonly record struct ReleaseIdentifier
    {
        private readonly string? _key;

        private ReleaseIdentifier(string key) => _key = key;

        /// <summary>
        /// The stored form, exactly as it goes into the column and the download metadata.
        ///
        /// Throws on a default-constructed value. A struct always has a default, so "no key" is
        /// representable however hard this tries; what it must not be is silently storable. The
        /// nullable form <c>ReleaseIdentifier?</c> is what every producer here returns, so the
        /// only way to reach this throw is to declare one and never assign it.
        /// </summary>
        public string Key => _key
            ?? throw new InvalidOperationException(
                "A default ReleaseIdentifier carries no key. Derive one through ReleaseIdentity.");

        /// <summary>
        /// Whether this carries no usable key: the default value, or a row whose column is empty.
        /// Both are checked, because BlockAsync guards the write with this and the column is only
        /// NOT NULL, which an empty string satisfies.
        /// </summary>
        public bool IsEmpty => string.IsNullOrEmpty(_key);

        /// <summary>
        /// Rebuild an identifier from its stored form, for the EF value converter and for a test
        /// naming a key literally.
        ///
        /// Nothing is refused here, including an empty string and an unrecognised prefix. This is
        /// the read path, and the read sits on the search path: one malformed row that threw would
        /// take out searching for that book entirely, which is a far worse outcome than the row
        /// itself. A key that matches no prefix already matches nothing, because both accessors
        /// below return nothing for it and the comparison falls back to the row's own title and
        /// size columns.
        ///
        /// An earlier version of this threw on an empty string, which produced exactly the failure
        /// this paragraph argues against: a single row with an empty column, reachable by a
        /// hand-edited database or a restored backup, and GetForAudiobookAsync throwing on every
        /// call for that book. Measured before it was removed. The guard that matters is on the
        /// write, where <see cref="Key"/> throws rather than storing a default, and where
        /// BlockAsync refuses an <see cref="IsEmpty"/> identifier.
        /// </summary>
        public static ReleaseIdentifier FromStorage(string stored) => new(stored);

        /// <summary>The normalised info-hash this key names, or null when it is not a hash key.</summary>
        public string? InfoHash => _key is not null
            && _key.StartsWith(ReleaseIdentity.InfoHashPrefix, StringComparison.Ordinal)
                ? ReleaseIdentity.NormalizeInfoHash(_key[ReleaseIdentity.InfoHashPrefix.Length..])
                : null;

        /// <summary>
        /// The normalised title this key names, or an empty string when it is not a title key.
        /// Empty rather than null because every caller compares it, and an empty string compares
        /// equal to nothing.
        /// </summary>
        public string TitleKey => _key is not null
            && _key.StartsWith(ReleaseIdentity.TitlePrefix, StringComparison.Ordinal)
                ? _key[ReleaseIdentity.TitlePrefix.Length..]
                : string.Empty;

        internal static ReleaseIdentifier ForInfoHash(string normalizedInfoHash) =>
            new(ReleaseIdentity.InfoHashPrefix + normalizedInfoHash);

        internal static ReleaseIdentifier ForTitle(string normalizedTitle) =>
            new(ReleaseIdentity.TitlePrefix + normalizedTitle);

        public override string ToString() => _key ?? string.Empty;
    }

    /// <summary>
    /// Keeps the identifier a JSON string.
    ///
    /// BlocklistController returns <see cref="BlockedRelease"/> rows straight out, so without this
    /// the wrapping alone would turn a field that was <c>"btih:..."</c> into <c>{}</c> for every
    /// caller of GET /api/v1/blocklist/audiobook/{id}. That is a breaking API change bought for
    /// nothing, and it would have been introduced by a refactor that touched no controller.
    /// </summary>
    public sealed class ReleaseIdentifierJsonConverter : JsonConverter<ReleaseIdentifier>
    {
        public override ReleaseIdentifier Read(
            ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            ReleaseIdentifier.FromStorage(reader.GetString() ?? string.Empty);

        // ToString rather than Key, so a default value serialises as "" instead of throwing, and
        // Read accepts "" back. The two halves have to agree: a converter that emits a value it
        // cannot parse is a landmine for the first caller that round-trips one.
        public override void Write(
            Utf8JsonWriter writer, ReleaseIdentifier value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString());
    }
}
