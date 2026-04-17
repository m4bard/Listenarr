using Listenarr.Api.Services;
using Listenarr.Domain.Utils;
using Xunit;

namespace Listenarr.Api.Tests
{
    public class FinalizePathHelperTests
    {
        [Fact]
        public void BuildMultiFileDestination_WithAuthorInTitle_SplitsAuthorAndTitle()
        {
            var outputPath = FileUtils.GetAbsolutePath("Library");
            var settings = new ApplicationSettings { OutputPath = outputPath };
            var download = new Download { Title = "William Faulkner - The Sound and the Fury", Artist = null, Series = null };

            var dest = FinalizePathHelper.BuildMultiFileDestination(settings, download, "William Faulkner - The Sound and the Fury");

            Assert.Contains("William Faulkner", dest);
            Assert.Contains("The Sound and the Fury", dest);
            Assert.StartsWith(outputPath, dest, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void BuildMultiFileDestination_WithSeries_IncludesSeriesFolder()
        {
            var outputPath = FileUtils.GetAbsolutePath("Library");
            var settings = new ApplicationSettings { OutputPath = outputPath };
            var download = new Download { Title = "The Sound and the Fury", Artist = "William Faulkner", Series = "Modern Classics" };

            var dest = FinalizePathHelper.BuildMultiFileDestination(settings, download, "The Sound and the Fury");

            Assert.StartsWith(outputPath, dest, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("William Faulkner", dest);
            Assert.Contains("Modern Classics", dest);
            Assert.Contains("The Sound and the Fury", dest);
        }
    }
}

