/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

namespace Listenarr.Tests.Features.Domain.Audiobooks.Rules
{
    [Trait("Name", "MultiFileImportPlannerTests")]
    [Trait("Category", "Domain")]
    public class MultiFileImportPlannerTests
    {
        [Fact]
        public void BuildPlans_DedupesCaseOnlyPathsUsingHostFilesystemRules()
        {
            var root = Path.Join(Path.GetTempPath(), "listenarr-planner-" + Guid.NewGuid().ToString("N"));
            var upperPath = Path.Join(root, "Chapter01.m4b");
            var lowerPath = Path.Join(root, "chapter01.m4b");

            var plans = MultiFileImportPlanner.BuildPlans([
                (upperPath, (string?)null),
                (lowerPath, (string?)null)
            ]);

            Assert.Equal(OperatingSystem.IsWindows() ? 1 : 2, plans.Count);
        }

        [Fact]
        public void BuildStableNamingNumbers_UsesHostFilesystemIdentityForPathKeys()
        {
            var root = Path.Join(Path.GetTempPath(), "listenarr-planner-" + Guid.NewGuid().ToString("N"));
            var upperPath = Path.Join(root, "Chapter01.m4b");
            var lowerPath = Path.Join(root, "chapter01.m4b");
            var plans = MultiFileImportPlanner.BuildPlans([
                (upperPath, (string?)null),
                (lowerPath, (string?)null)
            ]);

            var numbers = MultiFileImportPlanner.BuildStableNamingNumbers(plans, plan => plan.SequenceNumber);

            Assert.Equal(OperatingSystem.IsWindows() ? 1 : 2, numbers.Count);
        }
    }
}
