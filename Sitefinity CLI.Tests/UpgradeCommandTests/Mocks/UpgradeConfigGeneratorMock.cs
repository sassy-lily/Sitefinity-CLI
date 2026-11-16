using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Sitefinity_CLI.Model;
using Sitefinity_CLI.PackageManagement.Contracts;
using Sitefinity_CLI.PackageManagement.Implementations;

namespace Sitefinity_CLI.Tests.UpgradeCommandTests.Mocks
{
    internal class UpgradeConfigGeneratorMock : IUpgradeConfigGenerator
    {
        public IEnumerable<string> LastPackagesRemovedBeforeUpgrade { get; private set; }

        public Task<IEnumerable<PackageCompatibilityReport>> GenerateUpgradeConfig(IEnumerable<(string FilePath, Version Version)> projectFilePathsWithSitefinityVersion, NuGetPackage newSitefinityVersionPackageTree, string nugetConfigPath, IEnumerable<NuGetPackage> additionalPackagesToUpgrade, bool checkPackageCompatibility = true, IEnumerable<string> packagesRemovedBeforeUpgrade = null)
        {
            this.LastPackagesRemovedBeforeUpgrade = packagesRemovedBeforeUpgrade;

            return Task.FromResult<IEnumerable<PackageCompatibilityReport>>(new List<PackageCompatibilityReport>());
        }
    }
}
