/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 */
using System.Text.Json;

namespace Listenarr.Application.Search.Indexers.MyAnonamouse
{
    public static partial class MyAnonamouseResponseParser
    {
        /// <summary>
        /// MyAnonamouse sends its boolean-ish item fields as true/false, as 0/1, or as the strings
        /// "0"/"1"/"true"/"false" depending on the field, so accept all three.
        /// </summary>
        private static bool ReadBooleanFlag(JsonElement item, string propertyName)
        {
            if (!item.TryGetProperty(propertyName, out var element))
            {
                return false;
            }

            return element.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Number => element.TryGetInt64(out var number) && number != 0,
                JsonValueKind.String => bool.TryParse(element.GetString(), out var parsed)
                    ? parsed
                    : long.TryParse(element.GetString(), out var numeric) && numeric != 0,
                _ => false
            };
        }
    }
}
