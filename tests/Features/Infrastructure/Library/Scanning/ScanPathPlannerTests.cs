/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

namespace Listenarr.Tests.Features.Infrastructure.Library.Scanning
{
    [Trait("Name", "ScanPathPlannerTests")]
    [Trait("Category", "Infrastructure")]
    public class ScanPathPlannerTests
    {
        [Fact]
        public void CalculateBasePath_DedupesCaseOnlyDirectoriesUsingResolvedSemantics()
        {
            var root = Path.Join(Path.GetTempPath(), "listenarr-scan-path-" + Guid.NewGuid().ToString("N"));
            var upper = Path.Join(root, "Book", "Track01.m4b");
            var lower = Path.Join(root, "book", "Track02.m4b");

            var insensitive = new FileSystemPathSemantics(
                FileSystemPathSemantics.CurrentHostDefault.Syntax,
                FileSystemCaseSensitivity.Insensitive);
            var sensitive = insensitive with
            {
                CaseSensitivity = FileSystemCaseSensitivity.Sensitive
            };

            var insensitiveBasePath = ScanPathPlanner.CalculateBasePath(
                [upper, lower],
                insensitive);
            var sensitiveBasePath = ScanPathPlanner.CalculateBasePath(
                [upper, lower],
                sensitive);

            Assert.True(FileSystemPathIdentity.AreEquivalent(
                Path.Join(root, "Book"),
                insensitiveBasePath,
                insensitive));
            Assert.True(FileSystemPathIdentity.AreEquivalent(
                root,
                sensitiveBasePath,
                sensitive));
        }
    }
}
