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
        /// Whether a freeleech wedge spent on this release would buy nothing. MyAnonamouse marks
        /// three kinds of free: "free" for everyone, "personal_freeleech" for this account, and
        /// "fl_vip" for members whose class is VIP or Elite VIP.
        ///
        /// Prowlarr qualifies the third on the member's class, looked up once per response from
        /// /jsonLoad.php and cached for an hour
        /// (src/NzbDrone.Core/Indexers/Definitions/MyAnonamouse.cs, HasUserVip). That lookup is not
        /// reproduced here, and the reason is not that there is nowhere to put it. A cache is
        /// available and the provider that calls this already holds the cookie the request needs.
        /// The reason is that the request itself cannot be checked: nobody working on this has an
        /// account on the tracker, so reproducing Prowlarr's shape would add a second unverifiable
        /// behaviour in order to settle a question about the first.
        ///
        /// So fl_vip withholds the wedge from everyone, VIP or not. That errs the way the asymmetry
        /// points: a non-VIP member misses a wedge on a VIP-freeleech release and pays ratio for it,
        /// where the other way round destroys a consumable the member bought. It is deliberately
        /// not counted as freeleech for display, because it is not free to a non-VIP reader.
        /// </summary>
        private static bool WedgeWouldBeWasted(JsonElement item)
        {
            return ReadBooleanFlag(item, "free")
                || ReadBooleanFlag(item, "personal_freeleech")
                || ReadBooleanFlag(item, "fl_vip");
        }

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
