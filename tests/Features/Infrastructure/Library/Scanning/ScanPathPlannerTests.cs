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
        public void CalculateBasePath_DedupesCaseOnlyDirectoriesUsingHostFilesystemRules()
        {
            var root = Path.Join(Path.GetTempPath(), "listenarr-scan-path-" + Guid.NewGuid().ToString("N"));
            var upper = Path.Join(root, "Book", "Track01.m4b");
            var lower = Path.Join(root, "book", "Track02.m4b");

            var basePath = ScanPathPlanner.CalculateBasePath([upper, lower]);

            if (OperatingSystem.IsWindows())
            {
                Assert.True(FileUtils.AreFilesystemPathsEquivalentForCurrentOs(Path.Join(root, "Book"), basePath));
                return;
            }

            Assert.True(FileUtils.AreFilesystemPathsEquivalentForCurrentOs(root, basePath));
        }
    }
}
