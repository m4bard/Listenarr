/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

using System.Text;
using System.Text.RegularExpressions;

namespace Listenarr.Infrastructure.HostedServices.Search
{
    /// <summary>
    /// The conservative gate that decides whether a release is a given book. The automatic
    /// search fallback and recent-release discovery both go through it, so they cannot
    /// disagree about what a release name means.
    /// </summary>
    internal static class ReleaseTitleIdentity
    {
        // Only "Title" and "Author - Title" release layouts (with optional trailing
        // year/format suffixes) are parsed. titleKey is the normalized title that remains.
        public static bool TryParse(string? title, out string? parsedAuthor, out string titleKey)
        {
            parsedAuthor = null;
            titleKey = string.Empty;

            if (string.IsNullOrWhiteSpace(title))
            {
                return false;
            }

            var parts = Regex.Split(title.Trim(), @"\s+[-–—]\s+").ToList();

            // Drop trailing year / format suffix segments ("... - 2017", "... - [M4B]").
            while (parts.Count > 1 && Regex.IsMatch(
                parts[^1],
                @"^(?:\d{4}|\[?(?:m4b|mp3|flac|aac|ogg|opus)\]?)$",
                RegexOptions.IgnoreCase))
            {
                parts.RemoveAt(parts.Count - 1);
            }

            if (parts.Count is < 1 or > 2)
            {
                return false;
            }

            parsedAuthor = parts.Count == 2 ? parts[0] : null;
            titleKey = NormalizeIdentity(parts[^1]);
            return true;
        }

        // The complete remaining title must match the book exactly after normalization, and
        // at least one piece of corroborating evidence (parsed author, Artist field, or ASIN)
        // must agree, with any conflicting evidence vetoing.
        public static bool IsBook(
            SearchResult result,
            string? bookTitle,
            IReadOnlyCollection<string>? bookAuthors,
            string? bookAsin)
        {
            if (string.IsNullOrWhiteSpace(bookTitle)
                || !TryParse(result.Title, out var parsedAuthor, out var titleKey)
                || !string.Equals(titleKey, NormalizeIdentity(bookTitle), StringComparison.Ordinal))
            {
                return false;
            }

            // Conflicting ASIN vetoes selection even when another field matches.
            var hasComparableAsin = !string.IsNullOrWhiteSpace(result.Asin)
                && !string.IsNullOrWhiteSpace(bookAsin);
            var asinMatches = hasComparableAsin && string.Equals(
                result.Asin?.Trim(),
                bookAsin?.Trim(),
                StringComparison.OrdinalIgnoreCase);
            if (hasComparableAsin && !asinMatches)
            {
                return false;
            }

            // A parsed "Author - Title" author that doesn't match the book vetoes.
            var authorMatches = parsedAuthor != null && bookAuthors != null
                && bookAuthors.Any(expected =>
                    !string.IsNullOrWhiteSpace(expected)
                    && NormalizeIdentity(expected) == NormalizeIdentity(parsedAuthor));
            if (parsedAuthor != null && !authorMatches)
            {
                return false;
            }

            // The Artist field can be a provider guess, so a populated field alone is not
            // confirmation — but a present, non-"Unknown" Artist that conflicts vetoes too.
            var hasComparableArtist = !string.IsNullOrWhiteSpace(result.Artist)
                && !string.Equals(result.Artist.Trim(), "Unknown Author", StringComparison.OrdinalIgnoreCase)
                && bookAuthors != null
                && bookAuthors.Any(expected => !string.IsNullOrWhiteSpace(expected));
            var artistMatches = hasComparableArtist
                && bookAuthors!.Any(expected =>
                    !string.IsNullOrWhiteSpace(expected)
                    && NormalizeIdentity(expected) == NormalizeIdentity(result.Artist!));
            if (hasComparableArtist && !artistMatches)
            {
                return false;
            }

            // Require at least one corroborating signal; uncorroborated authorless results skip.
            return authorMatches || artistMatches || asinMatches;
        }

        public static string NormalizeIdentity(string value) =>
            Regex.Replace(
                value.Normalize(NormalizationForm.FormC).ToUpperInvariant(),
                @"[^\p{L}\p{N}]+",
                " ").Trim();
    }
}
