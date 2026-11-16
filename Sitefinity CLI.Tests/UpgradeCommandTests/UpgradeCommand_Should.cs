using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NuGet.Packaging.Core;
using NuGet.Versioning;
using Sitefinity_CLI;
using Sitefinity_CLI.Commands;
using Sitefinity_CLI.Model;
using Sitefinity_CLI.PackageManagement.Contracts;
using Sitefinity_CLI.PackageManagement.Implementations;
using Sitefinity_CLI.Services;
using Sitefinity_CLI.Services.Contracts;
using Sitefinity_CLI.Tests.NugetLicenseCommandTests.Mocks;
using Sitefinity_CLI.Tests.UpgradeCommandTests;
using Sitefinity_CLI.Tests.UpgradeCommandTests.Mocks;
using Sitefinity_CLI.VisualStudio;

namespace SitefinityCLI.Tests.UpgradeCommandTests
{
    [TestClass]
    public class UpgradeCommand_Should
    {
        private ISitefinityPackageManager sitefinityPackageManager;
        private IVisualStudioService visualStudioService;
        private ISitefinityProjectService sitefinityProjectService;
        private ISitefinityConfigService sitefinityConfigService;
        private ICsProjectFileEditor csProjectFileEditor;
        private ILogger<UpgradeCommand> logger;
        private IProjectConfigFileEditor projectConfigFileEditor;
        private IUpgradeConfigGenerator upgradeConfigGenerator;
        private IVisualStudioWorker visualStudioWorker;
        private IHttpClientFactory httpClientFactory;
        private ServiceProvider serviceProvider;
        private IPromptService promptService;
        private ISitefinityNugetPackageService sitefinityNugetPackageService;

        [TestInitialize]
        public void Initialize()
        {
            var services = new ServiceCollection();
            services.AddHttpClient();
            services.AddTransient<ICsProjectFileEditor, CsProjectFileEditor>();
            services.AddTransient<ISitefinityProjectService, SitefinityProjectServiceMock>();
            services.AddTransient<INuGetDependencyParser, NuGetV2DependencyParser>();
            services.AddTransient<INuGetDependencyParser, NuGetV3DependencyParser>();
            services.AddTransient<INugetProvider, NuGetV2Provider>();
            services.AddTransient<INugetProvider, NuGetV3Provider>();
            services.AddTransient<INuGetApiClient, NuGetApiClient>();
            services.AddTransient<INuGetCliClient, NuGetCliClient>();
            services.AddTransient<IDotnetCliClient, DotnetCliClient>();
            services.AddTransient<IPackagesConfigFileEditor, PackagesConfigFileEditor>();
            services.AddTransient<IProjectConfigFileEditor, ProjectConfigFileEditor>();
            services.AddTransient<IUpgradeConfigGenerator, UpgradeConfigGeneratorMock>();
            services.AddTransient<ISitefinityConfigService, SitefinityConfigServiceMock>();
            services.AddTransient<ISitefinityNugetPackageService, SitefinityNugetPackageServiceMock>();
            services.AddSingleton<ISitefinityPackageManager, SitefinityPackageManagerMock>();
            services.AddSingleton<IVisualStudioWorker, VisualStudioWorker>();
            services.AddSingleton<IVisualStudioService, VisualStudioServiceMock>();
            services.AddSingleton<IPromptService, PromptServiceMock>();
            services.AddSingleton<IVisualStudioWorkerFactory, VisualStuidoWorkerFactory>();

            this.serviceProvider = services.BuildServiceProvider();

            this.sitefinityNugetPackageService = serviceProvider.GetService<ISitefinityNugetPackageService>();
            this.sitefinityConfigService = serviceProvider.GetService<ISitefinityConfigService>();
            this.sitefinityProjectService = serviceProvider.GetService<ISitefinityProjectService>();
            this.visualStudioService = serviceProvider.GetService<IVisualStudioService>();
            this.sitefinityPackageManager = serviceProvider.GetService<ISitefinityPackageManager>();
            this.csProjectFileEditor = serviceProvider.GetService<ICsProjectFileEditor>();
            this.logger = serviceProvider.GetService<ILogger<UpgradeCommand>>();
            this.projectConfigFileEditor = serviceProvider.GetService<IProjectConfigFileEditor>();
            this.upgradeConfigGenerator = serviceProvider.GetService<IUpgradeConfigGenerator>();
            this.visualStudioWorker = serviceProvider.GetService<IVisualStudioWorker>();
            this.httpClientFactory = serviceProvider.GetService<IHttpClientFactory>();
            this.promptService = serviceProvider.GetService<IPromptService>();
        }

        [TestMethod]
        public async Task Throw_When_SolutionPathIsNotFound()
        {
            var upgradeComamnd = new UpgradeCommandSut(sitefinityNugetPackageService, visualStudioService, logger, promptService, sitefinityProjectService, sitefinityConfigService, upgradeConfigGenerator, sitefinityPackageManager);
            string path = "wrongSolutionpath";
            try
            {
                upgradeComamnd.SolutionPath = path;
                await upgradeComamnd.Execute();
            }
            catch (FileNotFoundException e)
            {
                string fullPath = Path.GetFullPath(path);
                Assert.AreEqual($"File \"{fullPath}\" not found", e.Message);
            }
        }

        [TestMethod]
        public async Task SolutionPathIsSetCorrect_When_SlnSolutionPathCommandIsPassedRelatively()
        {
            var upgradeCommand = new UpgradeCommandSut(sitefinityNugetPackageService, visualStudioService, logger, promptService, sitefinityProjectService, sitefinityConfigService, upgradeConfigGenerator, sitefinityPackageManager);
            string workingDirectory = Directory.GetCurrentDirectory();

            try
            {
                string newWorkingDirectory = Path.Combine(workingDirectory, "UpgradeCommandTests");
                string solutionPath = Path.Combine("Mocks", "fakeSln.sln");

                upgradeCommand.SolutionPath = solutionPath;
                upgradeCommand.Version = "15.1.8325";
                upgradeCommand.SkipPrompts = true;
                Directory.SetCurrentDirectory(newWorkingDirectory);
                await upgradeCommand.Execute();

                Assert.AreEqual(Path.Combine(newWorkingDirectory, solutionPath), upgradeCommand.SolutionPath);
            }
            finally
            {
                Directory.SetCurrentDirectory(workingDirectory);
            }

        }

        [TestMethod]
        public async Task SolutionPathIsSetCorrect_When_SlnSolutionPathCommandIsPassedFull()
        {
            var upgradeCommand = new UpgradeCommandSut(sitefinityNugetPackageService, visualStudioService, logger, promptService, sitefinityProjectService, sitefinityConfigService, upgradeConfigGenerator, sitefinityPackageManager);
            string workingDirectory = Directory.GetCurrentDirectory();
            string solutionPath = Path.Combine(workingDirectory, "UpgradeCommandTests", "Mocks", "fakeSln.sln");

            upgradeCommand.SolutionPath = solutionPath;
            upgradeCommand.Version = "15.1.8325";
            upgradeCommand.SkipPrompts = true;
            await upgradeCommand.Execute();

            Assert.AreEqual(solutionPath, upgradeCommand.SolutionPath);
        }

        [TestMethod]
        public async Task SolutionPathIsSetCorrect_When_SlnxSolutionPathCommandIsPassedRelatively()
        {
            var upgradeCommand = new UpgradeCommandSut(sitefinityNugetPackageService, visualStudioService, logger, promptService, sitefinityProjectService, sitefinityConfigService, upgradeConfigGenerator, sitefinityPackageManager);
            string workingDirectory = Directory.GetCurrentDirectory();

            try
            {
                string newWorkingDirectory = Path.Combine(workingDirectory, "UpgradeCommandTests");
                string solutionPath = Path.Combine("Mocks", "fakeSlnx.slnx");

                upgradeCommand.SolutionPath = solutionPath;
                upgradeCommand.Version = "15.1.8325";
                upgradeCommand.SkipPrompts = true;
                Directory.SetCurrentDirectory(newWorkingDirectory);
                await upgradeCommand.Execute();

                Assert.AreEqual(Path.Combine(newWorkingDirectory, solutionPath), upgradeCommand.SolutionPath);
            }
            finally
            {
                Directory.SetCurrentDirectory(workingDirectory);
            }
        }

        [TestMethod]
        public async Task SolutionPathIsSetCorrect_When_SlnxSolutionPathCommandIsPassedFull()
        {
            var upgradeCommand = new UpgradeCommandSut(sitefinityNugetPackageService, visualStudioService, logger, promptService, sitefinityProjectService, sitefinityConfigService, upgradeConfigGenerator, sitefinityPackageManager);
            string workingDirectory = Directory.GetCurrentDirectory();
            string solutionPath = Path.Combine(workingDirectory, "UpgradeCommandTests", "Mocks", "fakeSlnx.slnx");

            upgradeCommand.SolutionPath = solutionPath;
            upgradeCommand.Version = "15.1.8325";
            upgradeCommand.SkipPrompts = true;
            await upgradeCommand.Execute();

            Assert.AreEqual(solutionPath, upgradeCommand.SolutionPath);
        }

        [TestMethod]
        public async Task ScheduleLegacyCentaurRemoval_When_UpgradingFromBefore15_4_8640To15_4_8640OrLater()
        {
            ((SitefinityProjectServiceMock)this.sitefinityProjectService).SFVersion = new Version(15, 4, 8639);
            ((SitefinityPackageManagerMock)this.sitefinityPackageManager).InstalledPackages.Add(Constants.TelerikCentaurAllNuGetPackageId);
            
            UpgradeCommandSut upgradeCommand = this.CreateUpgradeCommand("15.4.8650");

            await upgradeCommand.Execute();

            Assert.IsTrue(((VisualStudioServiceMock)this.visualStudioService).LastUpgradeOptions.DeprecatedPackagesList
                .Contains(Constants.TelerikCentaurAllNuGetPackageId));
        }

        [TestMethod]
        public async Task DoNotScheduleLegacyCentaurRemoval_When_TargetIsBefore15_4_8640()
        {
            ((SitefinityProjectServiceMock)this.sitefinityProjectService).SFVersion = new Version(15, 4, 8638);
            ((SitefinityPackageManagerMock)this.sitefinityPackageManager).InstalledPackages.Add(Constants.TelerikCentaurAllNuGetPackageId);
            UpgradeCommandSut upgradeCommand = this.CreateUpgradeCommand("15.4.8639");

            await upgradeCommand.Execute();

            Assert.IsFalse(((VisualStudioServiceMock)this.visualStudioService).LastUpgradeOptions.DeprecatedPackagesList
                .Contains(Constants.TelerikCentaurAllNuGetPackageId));
        }

        [TestMethod]
        public async Task DoNotScheduleLegacyCentaurRemoval_When_CurrentVersionIs15_4_8640OrLater()
        {
            ((SitefinityProjectServiceMock)this.sitefinityProjectService).SFVersion = new Version(15, 4, 8640);
            ((SitefinityPackageManagerMock)this.sitefinityPackageManager).InstalledPackages.Add(Constants.TelerikCentaurAllNuGetPackageId);
            UpgradeCommandSut upgradeCommand = this.CreateUpgradeCommand("15.4.8650");

            await upgradeCommand.Execute();

            Assert.IsFalse(((VisualStudioServiceMock)this.visualStudioService).LastUpgradeOptions.DeprecatedPackagesList
                .Contains(Constants.TelerikCentaurAllNuGetPackageId));
        }

            [TestMethod]
            public async Task DoNotScheduleLegacyCentaurRemoval_When_Upgrading15_2_8442WithoutLegacyPackage()
            {
                ((SitefinityProjectServiceMock)this.sitefinityProjectService).SFVersion = new Version(15, 2, 8442);

                UpgradeCommandSut upgradeCommand = this.CreateUpgradeCommand("15.4.8650");

                await upgradeCommand.Execute();

                Assert.IsFalse(((VisualStudioServiceMock)this.visualStudioService).LastUpgradeOptions.DeprecatedPackagesList
                .Contains(Constants.TelerikCentaurAllNuGetPackageId));
            }

            [TestMethod]
            public async Task DoNotScheduleLegacyCentaurRemoval_When_LegacyPackageIsNotInstalled()
            {
                ((SitefinityProjectServiceMock)this.sitefinityProjectService).SFVersion = new Version(15, 4, 8639);
                UpgradeCommandSut upgradeCommand = this.CreateUpgradeCommand("15.4.8650");

                await upgradeCommand.Execute();

                Assert.IsFalse(((VisualStudioServiceMock)this.visualStudioService).LastUpgradeOptions.DeprecatedPackagesList
                .Contains(Constants.TelerikCentaurAllNuGetPackageId));
            }

        [TestMethod]
        public async Task RemoveLegacyCentaurBeforeUpgradeWithItsDependencies_When_ItIsScheduledForRemoval()
        {
            ((SitefinityProjectServiceMock)this.sitefinityProjectService).SFVersion = new Version(14, 4, 8146);
            ((SitefinityPackageManagerMock)this.sitefinityPackageManager).InstalledPackages.Add(Constants.TelerikCentaurAllNuGetPackageId);
            UpgradeCommandSut upgradeCommand = this.CreateUpgradeCommand("15.4.8641");

            await upgradeCommand.Execute();

            UpgradeOptions upgradeOptions = ((VisualStudioServiceMock)this.visualStudioService).LastUpgradeOptions;
            Assert.IsTrue(upgradeOptions.PackagesToRemoveWithDependencies.Contains(Constants.TelerikCentaurAllNuGetPackageId));
            Assert.IsFalse(upgradeOptions.PackagesToRemoveAfterUpgrade.Contains(Constants.TelerikCentaurAllNuGetPackageId));
        }

        [TestMethod]
        public async Task RemoveNativeChatConnectorBeforeUpgradeKeepingItsDependencies_When_NewVersionDoesNotDependOnIt()
        {
            ((SitefinityProjectServiceMock)this.sitefinityProjectService).SFVersion = new Version(14, 4, 8146);
            this.InstallPackage(Constants.NativeChatConnectorNuGetPackageId, new PackageDependency("Telerik.Sitefinity.Connectivity", VersionRange.Parse("[14.4.8146]")));
            ((SitefinityNugetPackageServiceMock)this.sitefinityNugetPackageService).UpgradePackage = CreateSitefinityPackageTree("15.4.8641");
            UpgradeCommandSut upgradeCommand = this.CreateUpgradeCommand("15.4.8641");

            await upgradeCommand.Execute();

            UpgradeOptions upgradeOptions = ((VisualStudioServiceMock)this.visualStudioService).LastUpgradeOptions;
            Assert.IsTrue(upgradeOptions.PackagesToRemoveBeforeUpgrade.Contains(Constants.NativeChatConnectorNuGetPackageId));
            Assert.IsTrue(upgradeOptions.PackagesToRemoveKeepingDependencies.Contains(Constants.NativeChatConnectorNuGetPackageId));
            Assert.IsFalse(upgradeOptions.PackagesToRemoveWithDependencies.Contains(Constants.NativeChatConnectorNuGetPackageId));
        }

        [TestMethod]
        public async Task ExcludePackagesRemovedBeforeUpgradeFromCompatibilityCheck_When_NativeChatConnectorIsRemoved()
        {
            ((SitefinityProjectServiceMock)this.sitefinityProjectService).SFVersion = new Version(14, 4, 8146);
            this.InstallPackage(Constants.NativeChatConnectorNuGetPackageId, new PackageDependency("Telerik.Sitefinity.Connectivity", VersionRange.Parse("[14.4.8146]")));
            this.InstallPackage("AntiXSS");
            ((SitefinityNugetPackageServiceMock)this.sitefinityNugetPackageService).UpgradePackage = CreateSitefinityPackageTree("15.4.8641");
            UpgradeCommandSut upgradeCommand = this.CreateUpgradeCommand("15.4.8641", removeDeprecatedPackages: true);

            await upgradeCommand.Execute();

            List<string> packagesRemovedBeforeUpgrade = ((UpgradeConfigGeneratorMock)this.upgradeConfigGenerator).LastPackagesRemovedBeforeUpgrade.ToList();
            Assert.IsTrue(packagesRemovedBeforeUpgrade.Contains(Constants.NativeChatConnectorNuGetPackageId));
            Assert.IsFalse(packagesRemovedBeforeUpgrade.Contains("AntiXSS"), "A package removed after the upgrade is still installed during it.");
        }

        [TestMethod]
        public async Task DoNotRemoveNativeChatConnector_When_NewVersionDependsOnIt()
        {
            ((SitefinityProjectServiceMock)this.sitefinityProjectService).SFVersion = new Version(14, 4, 8146);
            this.InstallPackage(Constants.NativeChatConnectorNuGetPackageId, new PackageDependency("Telerik.Sitefinity.Connectivity", VersionRange.Parse("[14.4.8146]")));
            NuGetPackage upgradePackage = CreateSitefinityPackageTree("15.3.8500");
            upgradePackage.Dependencies.Add(new NuGetPackage(Constants.NativeChatConnectorNuGetPackageId, "15.3.8500"));
            ((SitefinityNugetPackageServiceMock)this.sitefinityNugetPackageService).UpgradePackage = upgradePackage;
            UpgradeCommandSut upgradeCommand = this.CreateUpgradeCommand("15.3.8500", removeDeprecatedPackages: true);

            await upgradeCommand.Execute();

            Assert.IsFalse(((VisualStudioServiceMock)this.visualStudioService).LastUpgradeOptions.DeprecatedPackagesList
                .Contains(Constants.NativeChatConnectorNuGetPackageId));
        }

        [TestMethod]
        public async Task DoNotRemoveNativeChatConnectorWithoutDependencies_When_DeprecatedPackagesRemovalIsNotRequested()
        {
            ((SitefinityProjectServiceMock)this.sitefinityProjectService).SFVersion = new Version(14, 4, 8149);
            this.InstallPackage(Constants.NativeChatConnectorNuGetPackageId);
            ((SitefinityNugetPackageServiceMock)this.sitefinityNugetPackageService).UpgradePackage = CreateSitefinityPackageTree("15.4.8641");
            UpgradeCommandSut upgradeCommand = this.CreateUpgradeCommand("15.4.8641");

            await upgradeCommand.Execute();

            Assert.IsFalse(((VisualStudioServiceMock)this.visualStudioService).LastUpgradeOptions.DeprecatedPackagesList
                .Contains(Constants.NativeChatConnectorNuGetPackageId));
        }

        [TestMethod]
        public async Task RemoveNativeChatConnectorWithoutDependenciesAfterUpgrade_When_DeprecatedPackagesRemovalIsRequested()
        {
            ((SitefinityProjectServiceMock)this.sitefinityProjectService).SFVersion = new Version(14, 4, 8149);
            this.InstallPackage(Constants.NativeChatConnectorNuGetPackageId);
            ((SitefinityNugetPackageServiceMock)this.sitefinityNugetPackageService).UpgradePackage = CreateSitefinityPackageTree("15.4.8641");
            UpgradeCommandSut upgradeCommand = this.CreateUpgradeCommand("15.4.8641", removeDeprecatedPackages: true);

            await upgradeCommand.Execute();

            UpgradeOptions upgradeOptions = ((VisualStudioServiceMock)this.visualStudioService).LastUpgradeOptions;
            Assert.IsTrue(upgradeOptions.PackagesToRemoveAfterUpgrade.Contains(Constants.NativeChatConnectorNuGetPackageId));
            Assert.IsFalse(upgradeOptions.PackagesToRemoveKeepingDependencies.Contains(Constants.NativeChatConnectorNuGetPackageId));
        }

        [TestMethod]
        public async Task RemoveDeprecatedPackageAfterUpgrade_When_ItHasNoDependencies()
        {
            ((SitefinityProjectServiceMock)this.sitefinityProjectService).SFVersion = new Version(14, 4, 8146);
            this.InstallPackage("AntiXSS");
            UpgradeCommandSut upgradeCommand = this.CreateUpgradeCommand("15.4.8641", removeDeprecatedPackages: true);

            await upgradeCommand.Execute();

            UpgradeOptions upgradeOptions = ((VisualStudioServiceMock)this.visualStudioService).LastUpgradeOptions;
            Assert.IsTrue(upgradeOptions.PackagesToRemoveAfterUpgrade.Contains("AntiXSS"));
            Assert.IsFalse(upgradeOptions.PackagesToRemoveBeforeUpgrade.Contains("AntiXSS"));
        }

        [TestMethod]
        public async Task RemoveDeprecatedPackageBeforeUpgrade_When_ItHasDependencies()
        {
            ((SitefinityProjectServiceMock)this.sitefinityProjectService).SFVersion = new Version(13, 3, 7600);
            this.InstallPackage("CsvHelper", new PackageDependency("Microsoft.CSharp", VersionRange.Parse("4.0.1")));
            UpgradeCommandSut upgradeCommand = this.CreateUpgradeCommand("15.4.8641", removeDeprecatedPackages: true);

            await upgradeCommand.Execute();

            UpgradeOptions upgradeOptions = ((VisualStudioServiceMock)this.visualStudioService).LastUpgradeOptions;
            Assert.IsTrue(upgradeOptions.PackagesToRemoveWithDependencies.Contains("CsvHelper"));
            Assert.IsFalse(upgradeOptions.PackagesToRemoveAfterUpgrade.Contains("CsvHelper"));
        }

        [TestMethod]
        public async Task RemoveDeprecatedPackageBeforeUpgrade_When_ItsDependenciesCannotBeRead()
        {
            ((SitefinityProjectServiceMock)this.sitefinityProjectService).SFVersion = new Version(14, 4, 8146);
            SitefinityPackageManagerMock packageManager = (SitefinityPackageManagerMock)this.sitefinityPackageManager;
            packageManager.InstalledPackages.Add("linqtotwitterNET40");
            packageManager.InstalledPackageDependencies["linqtotwitterNET40"] = null;
            UpgradeCommandSut upgradeCommand = this.CreateUpgradeCommand("15.4.8641", removeDeprecatedPackages: true);

            await upgradeCommand.Execute();

            UpgradeOptions upgradeOptions = ((VisualStudioServiceMock)this.visualStudioService).LastUpgradeOptions;
            Assert.IsTrue(upgradeOptions.PackagesToRemoveWithDependencies.Contains("linqtotwitterNET40"));
            Assert.IsFalse(upgradeOptions.PackagesToRemoveAfterUpgrade.Contains("linqtotwitterNET40"));
        }

        [TestMethod]
        public void PassEachGroupOfDeprecatedPackagesToTheUpgradeScriptInItsOwnParameter()
        {
            UpgradeOptions upgradeOptions = new UpgradeOptions("fakeSln.sln", "15.4.8641", true, true, null, null, false, null);
            upgradeOptions.DeprecatedPackagesList.AddRange(new[] { Constants.TelerikCentaurAllNuGetPackageId, Constants.NativeChatConnectorNuGetPackageId, "AntiXSS", "Progress.Sitefinity.Ecommerce" });
            upgradeOptions.PackagesToRemoveKeepingDependencies.Add(Constants.NativeChatConnectorNuGetPackageId);
            upgradeOptions.PackagesToRemoveAfterUpgrade.AddRange(new[] { "AntiXSS", "Progress.Sitefinity.Ecommerce" });

            List<string> scriptParameters = VisualStudioService.GetUpgradeScriptParameters(upgradeOptions);

            CollectionAssert.AreEqual(
                new[]
                {
                    "-PackagesToRemove Telerik.Centaur.All",
                    "-PackagesToRemoveKeepingDependencies Progress.Sitefinity.NativeChatConnector",
                    "-PackagesToRemoveAfterUpgrade AntiXSS, Progress.Sitefinity.Ecommerce"
                },
                scriptParameters);
        }

        [TestMethod]
        public void PassNoParametersToTheUpgradeScript_When_NoPackagesAreRemoved()
        {
            UpgradeOptions upgradeOptions = new UpgradeOptions("fakeSln.sln", "15.4.8641", true, true, null, null, false, null);

            List<string> scriptParameters = VisualStudioService.GetUpgradeScriptParameters(upgradeOptions);

            Assert.AreEqual(0, scriptParameters.Count);
        }

        private void InstallPackage(string packageId, params PackageDependency[] dependencies)
        {
            SitefinityPackageManagerMock packageManager = (SitefinityPackageManagerMock)this.sitefinityPackageManager;
            packageManager.InstalledPackages.Add(packageId);
            packageManager.InstalledPackageDependencies[packageId] = dependencies;
        }

        private static NuGetPackage CreateSitefinityPackageTree(string version)
        {
            NuGetPackage sitefinityAll = new NuGetPackage(Constants.SitefinityAllNuGetPackageId, version);
            NuGetPackage sitefinityCoreModules = new NuGetPackage(Constants.SitefinityCoreModulesNuGetPackageId, version);
            sitefinityCoreModules.Dependencies.Add(new NuGetPackage("Telerik.Sitefinity.Connectivity", version));
            sitefinityAll.Dependencies.Add(sitefinityCoreModules);

            return sitefinityAll;
        }

        private UpgradeCommandSut CreateUpgradeCommand(string targetVersion, bool removeDeprecatedPackages)
        {
            UpgradeCommandSut upgradeCommand = this.CreateUpgradeCommand(targetVersion);
            upgradeCommand.RemoveDeprecatedPackages = removeDeprecatedPackages;

            return upgradeCommand;
        }

        private UpgradeCommandSut CreateUpgradeCommand(string targetVersion)
        {
            UpgradeCommandSut upgradeCommand = new UpgradeCommandSut(this.sitefinityNugetPackageService, this.visualStudioService, this.logger, this.promptService, this.sitefinityProjectService, this.sitefinityConfigService, this.upgradeConfigGenerator, this.sitefinityPackageManager)
            {
                SolutionPath = Path.Combine(Directory.GetCurrentDirectory(), "UpgradeCommandTests", "Mocks", "fakeSln.sln"),
                Version = targetVersion,
                SkipPrompts = true,
                AcceptLicense = true
            };

            return upgradeCommand;
        }
    }
}
