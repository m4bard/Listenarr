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
using Microsoft.AspNetCore.Mvc;
using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Api.Features.Library
{
    /// <summary>
    /// Pins the current absence of an audiobook-delete notification trigger (tracker item 184,
    /// finding G2). There is no MediatR-style event bus and no AuthorDeletedEvent or
    /// BookDeletedEvent anywhere in the backend, so the only place this gap can be exercised is
    /// the real deletion pipeline the API controller drives:
    /// LibraryController.DeleteAudiobook -&gt; LibraryDeleteWorkflow -&gt; AudiobookDeletionCommitService.
    /// None of those three classes takes an INotificationService dependency (read,
    /// LibraryDeleteWorkflow.cs constructor and AudiobookDeletionCommitService.cs constructor).
    ///
    /// This is intentionally a regression test with no matching production change: item 184 is
    /// downstream of upstream PR #943, which is open and rewrites the notification dispatch
    /// files this finding would otherwise touch. This test pins the gap so it fails, on purpose,
    /// the day someone wires a delete notification into this path, forcing them to see and
    /// update it rather than the gap silently reappearing after being fixed once.
    ///
    /// Scope note: this covers only DeleteAudiobook's deleteFiles=false, deleteFolder=false
    /// branch, the plain "remove the library record" path. It does not cover the
    /// filesystem-deleting branches of LibraryDeleteWorkflow, BulkDeleteAudiobooks
    /// (LibraryBulkEditWorkflow), or a rename/retag notification, which are the rest of G2 and
    /// remain unpinned.
    /// </summary>
    [Trait("Area", "LibraryApi")]
    [Trait("Name", "LibraryDeleteNotificationGapRegressionTests")]
    [Trait("Category", "LibraryController")]
    public class LibraryDeleteNotificationGapRegressionTests : BaseTests
    {
        [Fact]
        [Trait("Method", "DeleteAudiobook")]
        [Trait("Scenario", "ExistingAudiobook_NeverCallsNotificationService")]
        public async Task DeleteAudiobook_ExistingAudiobook_NeverCallsNotificationService()
        {
            // Given: an unconfigured (loose) mock. Moq returns a completed Task by default for any
            // Task-returning member with no explicit Setup, so no stubbing is needed here, and
            // Invocations still records a call if one happens.
            var mockNotificationService = new Mock<INotificationService>();

            Init(services => services.WithSingleton(mockNotificationService.Object));
            var controller = _provider.GetRequiredService<LibraryController>();

            var audiobook = await _audiobookRepository.AddAsync(new AudiobookBuilder()
                .WithTitle("Test")
                .Build());

            // When
            var result = await controller.DeleteAudiobook(audiobook.Id);

            // Then
            // Apparatus-validity control: the delete must have actually happened. If the workflow
            // had short-circuited (bad mock wiring, an early return, a swallowed exception) this
            // would fail here rather than silently producing a false "zero notification calls"
            // pass on a delete that never ran.
            Assert.IsType<OkObjectResult>(result);
            var stillPresent = await _audiobookRepository.GetByIdAsync(audiobook.Id);
            Assert.Null(stillPresent);

            // The actual finding: a real, successful delete never touches INotificationService,
            // through any of its methods, at all.
            Assert.Empty(mockNotificationService.Invocations);
        }
    }
}
