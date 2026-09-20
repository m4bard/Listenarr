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

using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Application.Audiobooks.Matching
{
    /// <summary>
    /// Regression coverage for the blank-cutoff search loop
    /// (disabling "Enable Quality Upgrades" stores a blank CutoffQuality, and the evaluator
    /// must treat that the same way QualityMatcher.MeetsCutoff/LabelMeetsCutoff and
    /// AudiobookStatusEvaluator already do: satisfied, not "keep searching forever").
    ///
    /// A CutoffQuality that names a rung the profile no longer lists is a different case and
    /// must still mean "keep searching" - several tests here exist specifically to fail if that
    /// half of the original guard is deleted instead of narrowed to the blank case only.
    ///
    /// Sections 6 and 7 cover the two answers that CHANGED when the guard stopped looking the
    /// cutoff up itself and started calling QualityMatcher.ResolveCutoff: a cutoff naming a
    /// present-but-disallowed rung, and a cutoff differing from its rung only in case.
    /// </summary>
    [Trait("Name", nameof(AudiobookQualityCutoffEvaluatorTests))]
    [Trait("Category", "Application")]
    public class AudiobookQualityCutoffEvaluatorTests : BaseTests
    {
        private static QualityProfileBuilder StructuredProfile() =>
            new QualityProfileBuilder().WithName("Structured").WithStructuredDefaults();

        private static AudiobookFile CreateFile(int audiobookId, string codec, int bitrateBitsPerSecond)
        {
            var file = AudiobookFile.CreateUnresolved($"/library/book-{audiobookId}.m4b");
            file.AudiobookId = audiobookId;
            file.Codec = codec;
            file.Bitrate = bitrateBitsPerSecond;
            return file;
        }

        private static Download CreateDownload(int audiobookId, DownloadStatus status) =>
            new DownloadBuilder()
                .WithAudiobookId(audiobookId)
                .WithStatus(status)
                .Build();

        private static (Mock<IDownloadRepository> Downloads, Mock<IAudiobookFileRepository> Files) MockRepositories(
            int audiobookId,
            List<Download> downloads,
            List<AudiobookFile> files)
        {
            var downloadRepository = new Mock<IDownloadRepository>(MockBehavior.Strict);
            downloadRepository
                .Setup(r => r.GetByAudiobookIdAsync(audiobookId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(downloads);

            var fileRepository = new Mock<IAudiobookFileRepository>(MockBehavior.Strict);
            fileRepository
                .Setup(r => r.GetByAudiobookIdAsync(audiobookId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(files);

            return (downloadRepository, fileRepository);
        }

        // ---- 1. Blank cutoff is satisfied, with a control that a real unmet cutoff is not ----

        [Fact]
        public async Task IsQualityCutoffMetAsync_BlankCutoff_FileMatchesRung_IsMet()
        {
            var audiobook = new Audiobook
            {
                Id = 1001,
                QualityProfile = StructuredProfile().WithCutoff("").Build()
            };
            var files = new List<AudiobookFile> { CreateFile(audiobook.Id, "aac", 256_000) };
            var (downloads, fileRepo) = MockRepositories(audiobook.Id, new List<Download>(), files);

            var met = await AudiobookQualityCutoffEvaluator.IsQualityCutoffMetAsync(
                audiobook, downloads.Object, fileRepo.Object);

            Assert.True(met);
        }

        [Fact]
        public async Task IsQualityCutoffMetAsync_RealCutoffAboveFile_Control_IsNotMet()
        {
            // Control for the test above: same profile shape and same file, but a real cutoff the
            // file does not meet. Must come out false where the blank case comes out true.
            var audiobook = new Audiobook
            {
                Id = 1002,
                QualityProfile = StructuredProfile().WithCutoff("AAC 320kbps").Build()
            };
            var files = new List<AudiobookFile> { CreateFile(audiobook.Id, "aac", 256_000) };
            var (downloads, fileRepo) = MockRepositories(audiobook.Id, new List<Download>(), files);

            var met = await AudiobookQualityCutoffEvaluator.IsQualityCutoffMetAsync(
                audiobook, downloads.Object, fileRepo.Object);

            Assert.False(met);
        }

        // ---- 2. A cutoff naming a rung the profile no longer lists still means keep searching ----

        [Fact]
        public async Task IsQualityCutoffMetAsync_CutoffNamesMissingRung_ActiveDownload_IsNotMet()
        {
            // An active Downloading/ImportPending download is treated as met unconditionally,
            // without ever consulting the cutoff (see the test below). So this is the input that
            // actually distinguishes "guard narrowed" from "guard deleted wholesale": if the
            // missing-rung guard is removed instead of narrowed, this flips to true because the
            // download loop is reached and its unconditional branch fires.
            var audiobook = new Audiobook
            {
                Id = 1003,
                QualityProfile = StructuredProfile().WithCutoff("Nonexistent 999kbps").Build()
            };
            var downloadList = new List<Download> { CreateDownload(audiobook.Id, DownloadStatus.Downloading) };
            var (downloads, fileRepo) = MockRepositories(audiobook.Id, downloadList, new List<AudiobookFile>());

            var met = await AudiobookQualityCutoffEvaluator.IsQualityCutoffMetAsync(
                audiobook, downloads.Object, fileRepo.Object);

            Assert.False(met);
        }

        // ---- 3. No files and no downloads: not met, so first acquisition still searches ----

        [Fact]
        public async Task IsQualityCutoffMetAsync_BlankCutoff_NoFilesNoDownloads_IsNotMet()
        {
            // The earlier "nothing yet" return (no downloads, no files) must still fire before the
            // blank-cutoff case is even considered, otherwise a blank cutoff would wrongly mark a
            // book with nothing downloaded as already satisfied and it would never be searched.
            var audiobook = new Audiobook
            {
                Id = 1004,
                QualityProfile = StructuredProfile().WithCutoff("").Build()
            };
            var (downloads, fileRepo) = MockRepositories(
                audiobook.Id, new List<Download>(), new List<AudiobookFile>());

            var met = await AudiobookQualityCutoffEvaluator.IsQualityCutoffMetAsync(
                audiobook, downloads.Object, fileRepo.Object);

            Assert.False(met);
        }

        // ---- 4. Blank cutoff with an active download is met ----

        [Fact]
        public async Task IsQualityCutoffMetAsync_BlankCutoff_ActiveDownloading_IsMet()
        {
            var audiobook = new Audiobook
            {
                Id = 1005,
                QualityProfile = StructuredProfile().WithCutoff("").Build()
            };
            var downloadList = new List<Download> { CreateDownload(audiobook.Id, DownloadStatus.Downloading) };
            var (downloads, fileRepo) = MockRepositories(audiobook.Id, downloadList, new List<AudiobookFile>());

            var met = await AudiobookQualityCutoffEvaluator.IsQualityCutoffMetAsync(
                audiobook, downloads.Object, fileRepo.Object);

            Assert.True(met);
        }

        [Fact]
        public async Task IsQualityCutoffMetAsync_BlankCutoff_ActiveImportPending_IsMet()
        {
            var audiobook = new Audiobook
            {
                Id = 1006,
                QualityProfile = StructuredProfile().WithCutoff("").Build()
            };
            var downloadList = new List<Download> { CreateDownload(audiobook.Id, DownloadStatus.ImportPending) };
            var (downloads, fileRepo) = MockRepositories(audiobook.Id, downloadList, new List<AudiobookFile>());

            var met = await AudiobookQualityCutoffEvaluator.IsQualityCutoffMetAsync(
                audiobook, downloads.Object, fileRepo.Object);

            Assert.True(met);
        }

        // ---- 5. The evaluator and QualityMatcher.MeetsCutoff must agree on a blank cutoff ----

        [Fact]
        public async Task IsQualityCutoffMetAsync_BlankCutoff_AgreesWithQualityMatcherMeetsCutoff()
        {
            var profile = StructuredProfile().WithCutoff("").Build();
            var audiobook = new Audiobook { Id = 1007, QualityProfile = profile };
            var file = CreateFile(audiobook.Id, "mp3", 96_000); // deliberately below every MP3 rung
            var files = new List<AudiobookFile> { file };
            var (downloads, fileRepo) = MockRepositories(audiobook.Id, new List<Download>(), files);

            var evaluatorResult = await AudiobookQualityCutoffEvaluator.IsQualityCutoffMetAsync(
                audiobook, downloads.Object, fileRepo.Object);

            var matcherInput = new AudioQualityInput
            {
                Codec = file.Codec,
                Container = file.Container,
                Format = file.Format,
                BitrateBitsPerSecond = file.Bitrate,
                Path = file.Path
            };
            var matcherResult = QualityMatcher.MeetsCutoff(matcherInput, profile);

            Assert.True(matcherResult);
            Assert.Equal(matcherResult, evaluatorResult);
        }

        // ---- 6. A cutoff naming a rung that exists but is not Allowed must not be met -------
        //
        // The evaluator used to look the cutoff up itself, with a plain FirstOrDefault and no
        // Allowed filter, while QualityMatcher.FindAllowedRung (behind MeetsCutoff and
        // LabelMeetsCutoff, both called further down the same method) always filtered on Allowed.
        // The file and completed-download paths below reached "not met" either way, because
        // QualityMatcher refused the cutoff even when the evaluator's own guard had accepted it.
        // The active-download path did not: see section 7, which is the answer that changed.

        private static QualityProfile DisallowedCutoffProfile() =>
            new QualityProfileBuilder()
                .WithName("DisallowedCutoff")
                .WithQuality("AAC 320kbps", 1, codec: "AAC", bitrate: 320, allowed: false)
                .WithQuality("AAC 256kbps", 2, codec: "AAC", bitrate: 256)
                .WithCutoff("AAC 320kbps")
                .Build();

        private static Download CreateCompletedDownload(int audiobookId, string quality)
        {
            var download = new DownloadBuilder()
                .WithAudiobookId(audiobookId)
                .WithStatus(DownloadStatus.Completed)
                .Build();
            download.SetMetadata("Quality", quality);
            return download;
        }

        [Fact]
        public async Task IsQualityCutoffMetAsync_CutoffNamesDisallowedRung_ExistingFile_IsNotMet()
        {
            var audiobook = new Audiobook
            {
                Id = 1008,
                QualityProfile = DisallowedCutoffProfile()
            };
            // A file AT the cutoff rung, so the only thing keeping this false is Allowed=false
            // on that rung. An earlier version of this test used a 256kbps file, one rung below
            // the 320kbps cutoff, and therefore passed for the wrong reason: it still passed with
            // the cutoff rung flipped to Allowed=true, which is the one condition it is named for.
            var files = new List<AudiobookFile> { CreateFile(audiobook.Id, "aac", 320_000) };
            var (downloads, fileRepo) = MockRepositories(audiobook.Id, new List<Download>(), files);

            var met = await AudiobookQualityCutoffEvaluator.IsQualityCutoffMetAsync(
                audiobook, downloads.Object, fileRepo.Object);

            Assert.False(met);
        }

        [Fact]
        public async Task IsQualityCutoffMetAsync_CutoffNamesDisallowedRung_CompletedDownload_IsNotMet()
        {
            var audiobook = new Audiobook
            {
                Id = 1009,
                QualityProfile = DisallowedCutoffProfile()
            };
            // Labelled AT the cutoff rung, for the reason given on the test above.
            var downloadList = new List<Download> { CreateCompletedDownload(audiobook.Id, "AAC 320kbps") };
            var (downloads, fileRepo) = MockRepositories(audiobook.Id, downloadList, new List<AudiobookFile>());

            var met = await AudiobookQualityCutoffEvaluator.IsQualityCutoffMetAsync(
                audiobook, downloads.Object, fileRepo.Object);

            Assert.False(met);
        }

        [Fact]
        public async Task IsQualityCutoffMetAsync_DisallowedCutoffControl_SameShapeButAllowed_IsMet()
        {
            // Control for the two tests above, and it differs from DisallowedCutoffProfile in
            // exactly one field: allowed: true on the cutoff rung. Same rungs, same cutoff label,
            // same file. It must come out the other way, which is what proves the False above
            // comes from Allowed and not from a bitrate relationship or some other accident.
            // The earlier version of this control also moved the cutoff from "AAC 320kbps" to
            // "AAC 256kbps", so it was the file reaching the cutoff, not Allowed, that made it
            // true; that made both it and the two tests it was controlling for prove nothing.
            var profile = new QualityProfileBuilder()
                .WithName("AllowedCutoffControl")
                .WithQuality("AAC 320kbps", 1, codec: "AAC", bitrate: 320, allowed: true)
                .WithQuality("AAC 256kbps", 2, codec: "AAC", bitrate: 256)
                .WithCutoff("AAC 320kbps")
                .Build();
            var audiobook = new Audiobook { Id = 1010, QualityProfile = profile };
            var files = new List<AudiobookFile> { CreateFile(audiobook.Id, "aac", 320_000) };
            var (downloads, fileRepo) = MockRepositories(audiobook.Id, new List<Download>(), files);

            var met = await AudiobookQualityCutoffEvaluator.IsQualityCutoffMetAsync(
                audiobook, downloads.Object, fileRepo.Object);

            Assert.True(met);
        }

        [Fact]
        public async Task CutoffNamesDisallowedRung_EvaluatorAndQualityMatcher_BothSayNotMet()
        {
            // Both layers, same profile, same file, asserted together: this is the agreement that
            // delegating the lookup is supposed to guarantee, so it is asserted rather than
            // assumed. The earlier version of this test only ever called QualityMatcher, which
            // could not have caught the evaluator drifting from it. It shares its inputs with
            // _ExistingFile_IsNotMet above on purpose: same input, different property asserted.
            var profile = DisallowedCutoffProfile();
            var audiobook = new Audiobook { Id = 1011, QualityProfile = profile };
            var file = CreateFile(audiobook.Id, "aac", 320_000);
            var (downloads, fileRepo) = MockRepositories(
                audiobook.Id, new List<Download>(), new List<AudiobookFile> { file });

            var evaluatorResult = await AudiobookQualityCutoffEvaluator.IsQualityCutoffMetAsync(
                audiobook, downloads.Object, fileRepo.Object);
            var matcherResult = QualityMatcher.MeetsCutoff(
                new AudioQualityInput { Codec = "aac", BitrateBitsPerSecond = 320_000 }, profile);

            Assert.False(matcherResult);
            Assert.Equal(matcherResult, evaluatorResult);
        }

        // ---- 7. The two answers that CHANGED ------------------------------------------------
        //
        // Both of these fail against the previous guard, which is the point of them. The guard
        // resolved the cutoff with `profile.Qualities.FirstOrDefault(q => q.Quality == CutoffQuality)`:
        // no Allowed filter, and an ordinal, case-sensitive string comparison. QualityMatcher's
        // FindAllowedRung filters on Allowed and compares OrdinalIgnoreCase. The guard therefore
        // accepted cutoffs QualityMatcher rejects, and rejected cutoffs QualityMatcher accepts.

        [Fact]
        public async Task IsQualityCutoffMetAsync_CutoffNamesDisallowedRung_ActiveDownload_IsNotMet()
        {
            // FLIPPED: was true, is now false.
            //
            // An active Downloading/ImportPending download short-circuits to "met" at the top of
            // the download loop without consulting the cutoff at all. Under the old guard a
            // disallowed cutoff rung was still found, the guard passed, and that short-circuit
            // fired. So the same broken profile answered "not met" for a file (section 6) and
            // "met" for an in-flight download - the cutoff's own resolvability decided by whether
            // a download happened to be running.
            //
            // The new answer matches what the profile actually says, and it matches the answer
            // this suite already demands for a cutoff naming a rung that is ABSENT
            // (IsQualityCutoffMetAsync_CutoffNamesMissingRung_ActiveDownload_IsNotMet, section 2).
            // Deleting a rung and un-ticking it are the same misconfiguration; they now get the
            // same answer.
            var audiobook = new Audiobook
            {
                Id = 1012,
                QualityProfile = DisallowedCutoffProfile()
            };
            var downloadList = new List<Download> { CreateDownload(audiobook.Id, DownloadStatus.Downloading) };
            var (downloads, fileRepo) = MockRepositories(audiobook.Id, downloadList, new List<AudiobookFile>());

            var met = await AudiobookQualityCutoffEvaluator.IsQualityCutoffMetAsync(
                audiobook, downloads.Object, fileRepo.Object);

            Assert.False(met);
        }

        [Fact]
        public async Task IsQualityCutoffMetAsync_ActiveDownloadControl_CutoffRungAllowed_IsMet()
        {
            // Control for the test above, and it must come out the other way. Same rungs, same
            // cutoff label, same active Downloading download; the only difference is Allowed=true
            // on the cutoff rung. If this also came out false, the test above would be measuring
            // "active downloads no longer count", not "a disallowed cutoff does not resolve".
            var profile = new QualityProfileBuilder()
                .WithName("AllowedCutoffActiveDownloadControl")
                .WithQuality("AAC 320kbps", 1, codec: "AAC", bitrate: 320, allowed: true)
                .WithQuality("AAC 256kbps", 2, codec: "AAC", bitrate: 256)
                .WithCutoff("AAC 320kbps")
                .Build();
            var audiobook = new Audiobook { Id = 1013, QualityProfile = profile };
            var downloadList = new List<Download> { CreateDownload(audiobook.Id, DownloadStatus.Downloading) };
            var (downloads, fileRepo) = MockRepositories(audiobook.Id, downloadList, new List<AudiobookFile>());

            var met = await AudiobookQualityCutoffEvaluator.IsQualityCutoffMetAsync(
                audiobook, downloads.Object, fileRepo.Object);

            Assert.True(met);
        }

        private static QualityProfile CaseMismatchedCutoffProfile() =>
            new QualityProfileBuilder()
                .WithName("CaseMismatchedCutoff")
                .WithQuality("AAC 320kbps", 1, codec: "AAC", bitrate: 320)
                .WithQuality("AAC 256kbps", 2, codec: "AAC", bitrate: 256)
                .WithCutoff("aac 320KBPS")
                .Build();

        [Fact]
        public async Task IsQualityCutoffMetAsync_CaseMismatchedCutoff_FileAtCutoff_IsMet()
        {
            // FLIPPED: was false, is now true.
            //
            // "aac 320KBPS" names the "AAC 320kbps" rung. QualityMatcher has always agreed it
            // does; the evaluator's own ordinal comparison did not, so the guard returned false
            // before the file was ever looked at, and the book was searched again on every cycle
            // with a file that already met its cutoff. That is the same failure the blank-cutoff
            // work on this stack's base commit fixed, reached by a different route.
            //
            // Asserted against QualityMatcher.MeetsCutoff rather than against a bare true, because
            // agreeing with QualityMatcher is the actual requirement.
            var profile = CaseMismatchedCutoffProfile();
            var audiobook = new Audiobook { Id = 1014, QualityProfile = profile };
            var file = CreateFile(audiobook.Id, "aac", 320_000);
            var (downloads, fileRepo) = MockRepositories(
                audiobook.Id, new List<Download>(), new List<AudiobookFile> { file });

            var evaluatorResult = await AudiobookQualityCutoffEvaluator.IsQualityCutoffMetAsync(
                audiobook, downloads.Object, fileRepo.Object);
            var matcherResult = QualityMatcher.MeetsCutoff(
                new AudioQualityInput { Codec = "aac", BitrateBitsPerSecond = 320_000 }, profile);

            Assert.True(matcherResult);
            Assert.Equal(matcherResult, evaluatorResult);
        }

        [Fact]
        public async Task IsQualityCutoffMetAsync_CaseMismatchedCutoffControl_FileBelowCutoff_IsNotMet()
        {
            // Control for the test above, and it must come out the other way. Identical profile
            // and identical case-mismatched cutoff; only the file is worse.
            //
            // What it does NOT do is discriminate on the guard: neutering the guard entirely
            // leaves this passing, because the ordering is enforced by QualityMatcher.MeetsCutoff
            // and never by the guard. What it pins is that a case-mismatched cutoff is still a
            // cutoff once resolved, rather than becoming a blanket "met". Nothing else in the
            // suite covered cutoff case-insensitivity at all before this section.
            var profile = CaseMismatchedCutoffProfile();
            var audiobook = new Audiobook { Id = 1015, QualityProfile = profile };
            var files = new List<AudiobookFile> { CreateFile(audiobook.Id, "aac", 256_000) };
            var (downloads, fileRepo) = MockRepositories(audiobook.Id, new List<Download>(), files);

            var met = await AudiobookQualityCutoffEvaluator.IsQualityCutoffMetAsync(
                audiobook, downloads.Object, fileRepo.Object);

            Assert.False(met);
        }

        [Fact]
        public async Task IsQualityCutoffMetAsync_CaseMismatchedCutoff_CompletedDownloadAtCutoff_IsMet()
        {
            // FLIPPED: was false, is now true, and this is the shape most likely to be seen.
            //
            // Completed is NOT in the set of statuses AutomaticSearchService.ProcessAudiobookAsync
            // returns early on, so unlike the disallowed-rung flip this one is reachable from
            // automatic search: a book whose only copy is a completed download at its cutoff was
            // being searched again on every cycle purely because the cutoff label's case differed
            // from its rung's.
            var profile = CaseMismatchedCutoffProfile();
            var audiobook = new Audiobook { Id = 1016, QualityProfile = profile };
            var downloadList = new List<Download> { CreateCompletedDownload(audiobook.Id, "AAC 320kbps") };
            var (downloads, fileRepo) = MockRepositories(audiobook.Id, downloadList, new List<AudiobookFile>());

            var evaluatorResult = await AudiobookQualityCutoffEvaluator.IsQualityCutoffMetAsync(
                audiobook, downloads.Object, fileRepo.Object);
            var matcherResult = QualityMatcher.LabelMeetsCutoff("AAC 320kbps", profile);

            Assert.True(matcherResult);
            Assert.Equal(matcherResult, evaluatorResult);
        }

        [Fact]
        public async Task IsQualityCutoffMetAsync_CaseMismatchedCutoffControl_CompletedDownloadBelowCutoff_IsNotMet()
        {
            // Control for the test above, and it must come out the other way: same profile, same
            // case-mismatched cutoff, a completed download one rung below it.
            var profile = CaseMismatchedCutoffProfile();
            var audiobook = new Audiobook { Id = 1017, QualityProfile = profile };
            var downloadList = new List<Download> { CreateCompletedDownload(audiobook.Id, "AAC 256kbps") };
            var (downloads, fileRepo) = MockRepositories(audiobook.Id, downloadList, new List<AudiobookFile>());

            var met = await AudiobookQualityCutoffEvaluator.IsQualityCutoffMetAsync(
                audiobook, downloads.Object, fileRepo.Object);

            Assert.False(met);
        }
    }
}
