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
using System.ComponentModel.DataAnnotations;
using Listenarr.Tests.Common;
using Microsoft.EntityFrameworkCore;
using DataAnnotationsValidationResult = System.ComponentModel.DataAnnotations.ValidationResult;

namespace Listenarr.Tests.Features.Domain.Search
{
    [Trait("Name", "IndexerCategorySelectionTests")]
    [Trait("Category", "Domain")]
    public sealed class IndexerCategorySelectionTests : BaseTests
    {
        [Theory]
        [InlineData("3030")]
        [InlineData("3030,3040")]
        [InlineData(" 3030 , 3040 ")]
        [InlineData("3030,,")]
        [InlineData("abc,3030")]
        [InlineData("0")]
        public void HasUsableCategory_ValueNamingANumericCategory_IsUsable(string categories)
        {
            Assert.True(IndexerCategorySelection.HasUsableCategory(categories));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(",")]
        [InlineData(",,,")]
        [InlineData("abc")]
        [InlineData("all")]
        [InlineData("-1")]
        public void HasUsableCategory_ValueNamingNoCategory_IsNotUsable(string? categories)
        {
            // A Newznab cat= parameter is a list of numeric ids, so a value carrying no number
            // cannot narrow a search however it is spelled. "[]" is covered by the JSON-array case
            // below, which the storage format has never actually supported.
            Assert.False(IndexerCategorySelection.HasUsableCategory(categories));
        }

        [Fact]
        public void HasUsableCategory_JsonArrayForm_IsNotUsable()
        {
            // The entity's doc comment used to claim a JSON array was accepted. No reader ever
            // parsed one: the request builder passes the stored string straight into cat=, so
            // "[3030]" reaches the indexer as literal junk.
            Assert.False(IndexerCategorySelection.HasUsableCategory("[]"));
            Assert.False(IndexerCategorySelection.HasUsableCategory("[3030]"));
        }

        [Theory]
        [InlineData("Newznab")]
        [InlineData("Torznab")]
        [InlineData("newznab")]
        [InlineData("  torznab  ")]
        public void RequiresCategories_ImplementationsThatSendCat_RequireThem(string implementation)
        {
            Assert.True(IndexerCategorySelection.RequiresCategories(implementation));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("InternetArchive")]
        [InlineData("MyAnonamouse")]
        [InlineData("Custom")]
        public void RequiresCategories_ImplementationsThatIgnoreCategories_DoNot(string? implementation)
        {
            Assert.False(IndexerCategorySelection.RequiresCategories(implementation));
        }

        [Fact]
        public void AudiobookDefault_IsTheNewznabAudiobookCategory()
        {
            // 3030 is Audio/Audiobook, the only audiobook category in the Newznab standard.
            // Readarr's wider default adds 7020 (Books/EBook) and 8010 (Other/Misc), neither of
            // which belongs in an audiobook-only manager.
            Assert.Equal("3030", IndexerCategorySelection.AudiobookDefault);
            Assert.True(IndexerCategorySelection.HasUsableCategory(IndexerCategorySelection.AudiobookDefault));
        }

        [Fact]
        public void NewIndexer_StartsOnTheAudiobookDefault()
        {
            Assert.Equal(IndexerCategorySelection.AudiobookDefault, new Indexer().Categories);
        }

        [Fact]
        public void Validation_AttachesTheFailureToTheCategoriesField()
        {
            var indexer = new Indexer { Implementation = "Newznab", Categories = "" };
            var results = new List<DataAnnotationsValidationResult>();

            var isValid = Validator.TryValidateObject(
                indexer,
                new ValidationContext(indexer),
                results,
                validateAllProperties: true);

            Assert.False(isValid);
            var failure = Assert.Single(results);
            Assert.Equal(nameof(Indexer.Categories), Assert.Single(failure.MemberNames));
        }

        [Fact]
        public void CategoriesColumn_StaysNullable()
        {
            // The guard on the real risk in this change. [Required] would have been the obvious
            // way to express it, but Entity Framework reads that annotation when it builds the
            // model: the column would become NOT NULL and every indexer already stored without
            // categories would stop loading. Measured on this tree, [Required] here also fails
            // ten existing tests in SqliteMigrationSchemaTests.
            using var scope = _provider.CreateScope();
            var contextFactory = scope.ServiceProvider
                .GetRequiredService<IDbContextFactory<ListenArrDbContext>>();
            using var context = contextFactory.CreateDbContext();

            var property = context.Model
                .FindEntityType(typeof(Indexer))!
                .FindProperty(nameof(Indexer.Categories))!;

            Assert.True(property.IsNullable, "Indexer.Categories must stay nullable.");
        }
    }
}
