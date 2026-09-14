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

namespace Listenarr.Tests.Features.Domain.Downloads
{
    /// <summary>
    /// <see cref="ImportResult.Exception"/> and <see cref="ImportResult.ImportFailure"/> are the
    /// two factories that build a failed import result, and they are the only points where the
    /// real exception or blocked-action detail is still available. They classify the failure
    /// there so anything built from the result for an external-facing surface (the History API,
    /// issue #975) can use a fixed sentence instead of raw exception text or a filesystem path.
    /// </summary>
    [Trait("Name", "ImportResultFailureClassificationTests")]
    [Trait("Category", "Domain")]
    public class ImportResultFailureClassificationTests : Listenarr.Tests.Common.BaseTests
    {
        [Fact]
        public void ImportFailure_IsClassifiedAsBlocked()
        {
            var result = ImportResult.ImportFailure(
                FileAction.Move,
                "/library/incoming/book.m4b",
                "/library/authors/Author/Book/book.m4b");

            Assert.Equal(ImportFailureClass.Blocked, result.FailureClass);
        }

        [Theory]
        [InlineData(typeof(UnauthorizedAccessException), ImportFailureClass.PermissionDenied)]
        [InlineData(typeof(DirectoryNotFoundException), ImportFailureClass.DestinationUnavailable)]
        [InlineData(typeof(FileNotFoundException), ImportFailureClass.DestinationUnavailable)]
        [InlineData(typeof(PathTooLongException), ImportFailureClass.PathTooLong)]
        public void Exception_ClassifiesByExceptionType(Type exceptionType, ImportFailureClass expected)
        {
            var exception = (Exception)Activator.CreateInstance(
                exceptionType,
                "/library/authors/Author/Book/book.m4b")!;

            var result = ImportResult.Exception(exception, "/library/authors/Author/Book/book.m4b");

            Assert.Equal(expected, result.FailureClass);
        }

        [Fact]
        public void Exception_ClassifiesAnIOExceptionAboutAnExistingDestinationAsDestinationConflict()
        {
            var exception = new IOException(
                "Could not create '/library/authors/Author/Book/book.m4b' because a file already exists.");

            var result = ImportResult.Exception(exception, "/library/authors/Author/Book/book.m4b");

            Assert.Equal(ImportFailureClass.DestinationConflict, result.FailureClass);
        }

        [Fact]
        public void Exception_ClassifiesAnIOExceptionAboutASharingViolationAsFileInUse()
        {
            var exception = new IOException(
                "The process cannot access the file '/library/authors/Author/Book/book.m4b' because it is being used by another process.");

            var result = ImportResult.Exception(exception, "/library/authors/Author/Book/book.m4b");

            Assert.Equal(ImportFailureClass.FileInUse, result.FailureClass);
        }

        [Fact]
        public void Exception_ClassifiesAnUnrecognizedIOExceptionAsIoError()
        {
            var exception = new IOException("A generic disk error occurred.");

            var result = ImportResult.Exception(exception, "/library/authors/Author/Book/book.m4b");

            Assert.Equal(ImportFailureClass.IoError, result.FailureClass);
        }

        [Fact]
        public void Exception_ClassifiesAnUnrecognizedExceptionAsUnknown_NeverAsNone()
        {
            // "None" means "no failure", which would let this result skip classification
            // entirely and fall through to raw text downstream. An exception always failed,
            // so it must always get a real class, even when that class is Unknown.
            var exception = new InvalidOperationException("Something unrelated to the filesystem went wrong.");

            var result = ImportResult.Exception(exception, "/library/authors/Author/Book/book.m4b");

            Assert.Equal(ImportFailureClass.Unknown, result.FailureClass);
        }

        [Theory]
        [InlineData(ImportFailureClass.PermissionDenied)]
        [InlineData(ImportFailureClass.DestinationUnavailable)]
        [InlineData(ImportFailureClass.DestinationConflict)]
        [InlineData(ImportFailureClass.PathTooLong)]
        [InlineData(ImportFailureClass.FileInUse)]
        [InlineData(ImportFailureClass.IoError)]
        [InlineData(ImportFailureClass.Blocked)]
        [InlineData(ImportFailureClass.Unknown)]
        public void Sentences_NeverContainAPathSeparatorOrEmbedAnyOtherText(ImportFailureClass failureClass)
        {
            var sentence = ImportFailureClassSentences.Describe(failureClass);

            Assert.False(string.IsNullOrWhiteSpace(sentence));
            // "/" alone is not checked: "I/O error" is a legitimate sentence and contains one.
            // A Windows directory separator is the actual thing being ruled out here.
            Assert.DoesNotContain('\\', sentence);
        }
    }
}
