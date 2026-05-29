namespace Listenarr.Application.Extensions
{
    /// <summary>
    /// Extension methods for System.Text.Json types
    /// </summary>
    public static class JsonExtensions
    {
        /// <summary>
        /// Gets a property value from a JsonElement, returning defaultValue if the property doesn't exist or is null.
        /// </summary>
        public static T GetPropertyOrDefault<T>(this System.Text.Json.JsonElement element, string propertyName, T defaultValue = default!)
        {
            if (element.TryGetProperty(propertyName, out var prop) && prop.ValueKind != System.Text.Json.JsonValueKind.Null)
            {
                try
                {
                    return System.Text.Json.JsonSerializer.Deserialize<T>(prop.GetRawText()) ?? defaultValue;
                }
                catch (Exception caughtEx_1) when (caughtEx_1 is not OperationCanceledException && caughtEx_1 is not OutOfMemoryException && caughtEx_1 is not StackOverflowException)
                {
                    return defaultValue;
                }
            }
            return defaultValue;
        }

        /// <summary>
        /// Returns a property as <see cref="double"/> when its
        /// <see cref="System.Text.Json.JsonValueKind"/> is
        /// <see cref="System.Text.Json.JsonValueKind.Number"/>; otherwise returns
        /// <paramref name="defaultValue"/>.
        /// </summary>
        /// <remarks>
        /// Reads numeric fields directly into a <see cref="double"/> without going
        /// through a string, so <see cref="System.Text.Json.JsonElement.GetString"/>
        /// is never called on a Number (which throws by design and broke NZBGet
        /// queue polling before issue #618 was fixed).
        /// </remarks>
        public static double GetDoubleOrDefault(this System.Text.Json.JsonElement element, string propertyName, double defaultValue = 0d)
        {
            if (!element.TryGetProperty(propertyName, out var prop) || prop.ValueKind != System.Text.Json.JsonValueKind.Number)
            {
                return defaultValue;
            }
            return prop.TryGetDouble(out var d) ? d : defaultValue;
        }
    }
}
