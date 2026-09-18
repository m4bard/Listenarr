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
    /// now: a string will not pass where an identifier is expected, and the only ways to make one
    /// are <see cref="ReleaseIdentity.For(Listenarr.Domain.Search.SearchResult)"/>,
    /// <see cref="ReleaseIdentity.ForGrabbed"/> and reading a row back.
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

        /// <summary>Whether this is the default, keyless value rather than a real identity.</summary>
        public bool IsEmpty => _key is null;

        /// <summary>
        /// Rebuild an identifier from its stored form, for the EF value converter and for a test
        /// naming a key literally.
        ///
        /// Deliberately not validated against the pinned prefixes. An unrecognised prefix is not a
        /// reason to throw on the read path: the blocklist read sits on the search path, so a
        /// single malformed row would take out searching for that book entirely. A key that
        /// matches no prefix already fails to match anything, because both accessors below return
        /// nothing for it and the comparison falls back to the row's own title and size columns.
        /// Refusing loudly here would trade a row that does nothing for a book that cannot be
        /// searched.
        /// </summary>
        public static ReleaseIdentifier FromStorage(string stored) =>
            string.IsNullOrWhiteSpace(stored)
                ? throw new ArgumentException(
                    "A blocklist row cannot carry an empty release identifier.", nameof(stored))
                : new ReleaseIdentifier(stored);

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

        public override void Write(
            Utf8JsonWriter writer, ReleaseIdentifier value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString());
    }
}
