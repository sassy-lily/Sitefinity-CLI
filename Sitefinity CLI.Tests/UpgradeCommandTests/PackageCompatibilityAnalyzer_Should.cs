using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NuGet.Configuration;
using NuGet.Packaging.Core;
using NuGet.Versioning;
using Sitefinity_CLI;
using Sitefinity_CLI.Model;
using Sitefinity_CLI.PackageManagement.Contracts;
using Sitefinity_CLI.PackageManagement.Implementations;
using Sitefinity_CLI.Tests.NugetLicenseCommandTests.Mocks;

namespace SitefinityCLI.Tests.UpgradeCommandTests
{
    [TestClass]
    public class PackageCompatibilityAnalyzer_Should
    {
        private const string NativeChatConnector = Constants.NativeChatConnectorNuGetPackageId;
        private const string Connectivity = "Telerik.Sitefinity.Connectivity";

        private string projectDirectory;
        private string projectFilePath;
        private SitefinityPackageManagerMock packageManager;
        private DotnetCliClientStub dotnetCliClient;
        private PackageCompatibilityAnalyzer analyzer;

        [TestInitialize]
        public void Initialize()
        {
            this.projectDirectory = Path.Combine(Path.GetTempPath(), "sf-cli-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(this.projectDirectory);

            this.projectFilePath = Path.Combine(this.projectDirectory, "SitefinityWebApp.csproj");
            File.WriteAllText(
                this.projectFilePath,
                "<Project xmlns=\"http://schemas.microsoft.com/developer/msbuild/2003\"><PropertyGroup><TargetFrameworkVersion>v4.8</TargetFrameworkVersion></PropertyGroup></Project>");

            // The project is on Sitefinity 14.4.8146, where the NativeChat connector requires the exact version of the connectivity package.
            File.WriteAllText(
                Path.Combine(this.projectDirectory, Constants.PackagesConfigFileName),
                "<?xml version=\"1.0\" encoding=\"utf-8\"?><packages>" +
                $"<package id=\"{Constants.SitefinityAllNuGetPackageId}\" version=\"14.4.8146\" targetFramework=\"net48\" />" +
                $"<package id=\"{Connectivity}\" version=\"14.4.8146\" targetFramework=\"net48\" />" +
                $"<package id=\"{NativeChatConnector}\" version=\"14.4.8146\" targetFramework=\"net48\" />" +
                "</packages>");

            this.packageManager = new SitefinityPackageManagerMock();
            this.packageManager.PackageDependencies[$"{Constants.SitefinityAllNuGetPackageId}@15.4.8641"] = Dependencies((Connectivity, "[15.4.8641]"));
            this.packageManager.PackageDependencies[$"{NativeChatConnector}@14.4.8146"] = Dependencies((Connectivity, "[14.4.8146]"));

            // 14.4.8149 is the first version of the connector that has no dependencies.
            this.dotnetCliClient = new DotnetCliClientStub();
            this.dotnetCliClient.PackageVersions[NativeChatConnector] = new[] { "14.4.8146", "14.4.8149" };

            this.analyzer = new PackageCompatibilityAnalyzer(this.packageManager, new PackagesConfigFileEditor(), this.dotnetCliClient, NullLogger<PackageCompatibilityAnalyzer>.Instance);
        }

        [TestCleanup]
        public void Cleanup()
        {
            Directory.Delete(this.projectDirectory, true);
        }

        [TestMethod]
        public async Task UpgradeBlockingPackageAsPrerequisite_When_ItStaysInstalled()
        {
            PackageCompatibilityReport report = await this.Analyze(packagesRemovedBeforeUpgrade: null);

            Assert.AreEqual(1, report.Prerequisites.Count);
            Assert.AreEqual(NativeChatConnector, report.Prerequisites[0].Id);
            Assert.AreEqual("14.4.8149", report.Prerequisites[0].Version);
        }

        [TestMethod]
        public async Task IgnoreBlockingPackage_When_ItIsRemovedBeforeUpgrade()
        {
            PackageCompatibilityReport report = await this.Analyze(packagesRemovedBeforeUpgrade: new[] { NativeChatConnector });

            Assert.AreEqual(0, report.Prerequisites.Count);
            Assert.AreEqual(0, report.UnresolvedConflicts.Count);
            Assert.IsFalse(report.HasIssues);
        }

        [TestMethod]
        public async Task IgnoreBlockingPackage_When_ItIsRemovedBeforeUpgradeAndIdCasingDiffers()
        {
            PackageCompatibilityReport report = await this.Analyze(packagesRemovedBeforeUpgrade: new[] { NativeChatConnector.ToUpperInvariant() });

            Assert.IsFalse(report.HasIssues);
        }

        private Task<PackageCompatibilityReport> Analyze(IEnumerable<string> packagesRemovedBeforeUpgrade)
        {
            NuGetPackage[] packagesToUpgrade = new[] { new NuGetPackage(Constants.SitefinityAllNuGetPackageId, "15.4.8641") };

            return this.analyzer.Analyze(this.projectFilePath, packagesToUpgrade, new List<PackageSource>(), "NuGet.Config", packagesRemovedBeforeUpgrade);
        }

        private static IReadOnlyList<PackageDependency> Dependencies(params (string Id, string Range)[] dependencies)
        {
            return dependencies.Select(d => new PackageDependency(d.Id, VersionRange.Parse(d.Range))).ToList();
        }

        private class DotnetCliClientStub : IDotnetCliClient
        {
            public Dictionary<string, IEnumerable<string>> PackageVersions { get; } = new Dictionary<string, IEnumerable<string>>(StringComparer.OrdinalIgnoreCase);

            public IEnumerable<string> GetPackageVersionsInNugetSourcesUsingConfig(string sitefinityPackage, string nugetConfigFilePath)
            {
                return this.PackageVersions.TryGetValue(sitefinityPackage, out IEnumerable<string> versions) ? versions : Enumerable.Empty<string>();
            }

            public IEnumerable<string> GetPackageVersionsInNugetSources(string sitefinityPackage, string[] sources) => throw new NotSupportedException();

            public void InstallProjectTemplate(string path) => throw new NotSupportedException();

            public void UninstallProjectTemplate(string path) => throw new NotSupportedException();

            public void CreateProjectFromTemplate(string templateName, string projectName, string directory) => throw new NotSupportedException();

            public bool MigrateSlnToSlnx(string projectName, string directory) => throw new NotSupportedException();

            public void CreateSolution(string name, string directory, bool useSlnFormat) => throw new NotSupportedException();

            public void AddProjectToSolution(string solutionName, string projectDirectory, string projectName, bool useSln) => throw new NotSupportedException();

            public void AddPackageToProject(string projectPath, string packageName, string version) => throw new NotSupportedException();

            public void AddSourcesToNugetConfig(string[] sources, string filePath) => throw new NotSupportedException();

            public bool VersionExists(string version, string sitefinityPackage, string[] sources) => throw new NotSupportedException();

            public string GetLatestVersionInNugetSources(string[] sources, string sitefinityPackage) => throw new NotSupportedException();
        }
    }
}
