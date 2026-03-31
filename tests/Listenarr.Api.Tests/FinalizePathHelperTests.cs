using System;
using System.IO;
using Listenarr.Api.Services;
using Listenarr.Domain.Models;
using Xunit;

namespace Listenarr.Api.Tests
{
    public class FinalizePathHelperTests
    {
        [Fact]
        public void BuildMultiFileDestination_WithAuthorInTitle_SplitsAuthorAndTitle()
        {
            var settings = new ApplicationSettings { OutputPath = Path.Join("C:", "Library") };
            var download = new Download { Title = "William Faulkner - The Sound and the Fury", Artist = null, Series = null };

            var dest = FinalizePathHelper.BuildMultiFileDestination(settings, download, "William Faulkner - The Sound and the Fury");

            Assert.Contains("William Faulkner", dest);
            Assert.Contains("The Sound and the Fury", dest);
            Assert.StartsWith(Path.Join("C:", "Library"), dest, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void BuildMultiFileDestination_WithSeries_IncludesSeriesFolder()
        {
            var settings = new ApplicationSettings { OutputPath = Path.Join("C:", "Library") };
            var download = new Download { Title = "The Sound and the Fury", Artist = "William Faulkner", Series = "Modern Classics" };

            var dest = FinalizePathHelper.BuildMultiFileDestination(settings, download, "The Sound and the Fury");

            // Expect: C:\Library\William Faulkner\Modern Classics\The Sound and the Fury
            Assert.StartsWith(Path.Join("C:", "Library"), dest, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(Path.Join("William Faulkner"), dest);
            Assert.Contains(Path.Join("Modern Classics"), dest);
            Assert.Contains(Path.Join("The Sound and the Fury"), dest);
        }
    }
}

