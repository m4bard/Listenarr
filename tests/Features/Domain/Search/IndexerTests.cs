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

using System.Reflection;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Domain.Search;

/// <summary>
/// Guards the removal of the vestigial <c>Indexer.Tags</c> field (tracker #129):
/// it had no UI control to set it and no backend code ever read it, so it was
/// removed from the domain model. The column itself is left in place, unmapped,
/// on existing SQLite databases rather than dropped via a migration - that
/// choice is verified separately by
/// <c>SqliteMigrationSchemaTests.MigrationHistory_HasNoPendingModelChanges</c>,
/// which fails if the EF model snapshot and the compiled domain model disagree.
/// This test exists so a future PR can't silently reintroduce the property.
/// </summary>
[Trait("Name", "IndexerTests")]
[Trait("Category", "Domain")]
public sealed class IndexerTests : BaseTests
{
    [Fact]
    public void Indexer_HasNoTagsProperty()
    {
        var property = typeof(Indexer).GetProperty(
            "Tags",
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

        Assert.Null(property);
    }
}
