/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU Affero General Public License for more details.
 *
 * You should have received a copy of the GNU Affero General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 */

using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace Listenarr.Api.Features.Library;

/// <summary>
/// Reads the refresh-metadata request body as raw JSON before the strict-typed model binder gets
/// a chance to silently drop a field it does not recognize. The default System.Text.Json binder
/// ignores an unmapped property instead of rejecting it, so a caller who typed "audiobookId"
/// instead of "authorId" previously got <see cref="MetadataRefreshRequest.AuthorId"/> bound to
/// null and a whole-library run started, with no way to tell that apart from a request that
/// genuinely meant the whole library.
/// </summary>
internal static class MetadataRefreshRequestReader
{
    private static readonly string[] ValidPropertyNames = { "authorId", "force" };

    /// <summary>
    /// Validates and reads <paramref name="requestBody"/>. Exactly one of the two return values
    /// is non-null: a successful read returns the request (possibly null, meaning the
    /// whole-library defaults), and a rejected body returns the 400 to send back as-is.
    /// </summary>
    public static (MetadataRefreshRequest? Request, IActionResult? Error) Read(JsonElement? requestBody)
    {
        if (requestBody is null || requestBody.Value.ValueKind == JsonValueKind.Null)
        {
            // No body at all, or an explicit JSON null, is the existing whole-library-with-
            // defaults request and must keep working exactly as before.
            return (null, null);
        }

        var body = requestBody.Value;
        if (body.ValueKind != JsonValueKind.Object)
        {
            // An array or a bare scalar is a shape mismatch outside this bug's scope. A plain
            // 400 is enough so nothing downstream NullReferenceExceptions on it.
            return (null, new BadRequestObjectResult(new
            {
                message = "The refresh-metadata request body must be a JSON object.",
                code = "metadata_refresh_invalid_body"
            }));
        }

        var hasAnyProperty = false;
        foreach (var property in body.EnumerateObject())
        {
            hasAnyProperty = true;
            var isKnown = false;
            foreach (var validName in ValidPropertyNames)
            {
                if (string.Equals(validName, property.Name, StringComparison.OrdinalIgnoreCase))
                {
                    isKnown = true;
                    break;
                }
            }

            if (!isKnown)
            {
                return (null, new BadRequestObjectResult(new
                {
                    message = $"Unknown field '{property.Name}' in refresh-metadata request. " +
                        "The only valid fields are 'authorId' and 'force'.",
                    code = "metadata_refresh_unknown_field"
                }));
            }
        }

        if (!hasAnyProperty)
        {
            // An empty object is the same whole-library-with-defaults request as no body at all.
            return (null, null);
        }

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        return (JsonSerializer.Deserialize<MetadataRefreshRequest>(body.GetRawText(), options), null);
    }
}
