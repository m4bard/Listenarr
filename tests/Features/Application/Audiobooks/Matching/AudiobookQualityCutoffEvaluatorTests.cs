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
    }
}
