using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Sitefinity_CLI.Model;
using Sitefinity_CLI.PackageManagement.Implementations;
using Sitefinity_CLI.Services.Contracts;

namespace Sitefinity_CLI.Tests.UpgradeCommandTests.Mocks
{
    internal class SitefinityNugetPackageServiceMock : ISitefinityNugetPackageService
    {
        public string LatestVersion { get; set; }

        /// <summary>
        /// The dependency tree of the Sitefinity version that the project is upgraded to.
        /// </summary>
        public NuGetPackage UpgradePackage { get; set; } = new NuGetPackage();

        public string GetLatestSitefinityVersion()
        {
            return this.LatestVersion;
        }

        public Task<IEnumerable<NuGetPackage>> PrepareAdditionalPackages(UpgradeOptions options)
        {
            return Task.FromResult(new List<NuGetPackage>().AsEnumerable());
        }

        public Task<NuGetPackage> PrepareSitefinityUpgradePackage(UpgradeOptions options, IEnumerable<string> sitefinityProjectFilePaths)
        {
            return Task.FromResult(this.UpgradePackage);
        }

        public void SyncProjectReferencesWithPackages(IEnumerable<string> projectFilePaths, string solutionFolder)
        {
        }
    }
}
