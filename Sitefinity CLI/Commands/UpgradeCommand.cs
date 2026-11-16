using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Threading.Tasks;
using HandlebarsDotNet;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using NuGet.Configuration;
using NuGet.Packaging.Core;
using Sitefinity_CLI.Commands.Validators;
using Sitefinity_CLI.Model;
using Sitefinity_CLI.PackageManagement.Contracts;
using Sitefinity_CLI.PackageManagement.Implementations;
using Sitefinity_CLI.Services.Contracts;

namespace Sitefinity_CLI.Commands
{
    [HelpOption]
    [Command(Constants.UpgradeCommandName, Constants.UpgradeCommandDescription)]
    internal class UpgradeCommand : NugetLicenseCommand
    {
        [Argument(1, Description = Constants.VersionToOptionDescription)]
        [UpgradeVersionValidator]
        public string Version { get; set; }

        [Option(Constants.SkipPrompts, Description = Constants.SkipPromptsDescription)]
        public bool SkipPrompts { get; set; }

        [Option(Constants.AdditionalPackages, Description = Constants.AdditionalPackagesDescription)]
        public string AdditionalPackagesString { get; set; }

        [Option(Constants.RemoveDeprecatedPackages, Description = Constants.RemoveDeprecatedPackagesDescription)]
        public bool RemoveDeprecatedPackages { get; set; }

        [Option(Constants.RemoveDeprecatedPackagesExcept, Description = Constants.RemoveDeprecatedPackagesExceptDescription)]
        public string RemoveDeprecatedPackagesExcept { get; set; }

        [Option(Constants.SkipPrerequisitesCheck, Description = Constants.SkipPrerequisitesCheckDescription)]
        public bool SkipPrerequisitesCheck { get; set; }

        public UpgradeCommand(
            ISitefinityNugetPackageService sitefinityPackageService,
            IVisualStudioService visualStudioService,
            ILogger<UpgradeCommand> logger,
            IPromptService promptService,
            ISitefinityProjectService sitefinityProjectService,
            ISitefinityConfigService sitefinityConfigService,
            IUpgradeConfigGenerator upgradeConfigGenerator,
            ISitefinityPackageManager sitefinityPackageManager)
            : base(promptService, logger, sitefinityPackageManager)
        {

            this.sitefinityPackageService = sitefinityPackageService;
            this.visualStudioService = visualStudioService;
            this.logger = logger;
            this.promptService = promptService;
            this.sitefinityProjectService = sitefinityProjectService;
            this.sitefinityConfigService = sitefinityConfigService;
            this.upgradeConfigGenerator = upgradeConfigGenerator;
            this.sitefinityPackageManager = sitefinityPackageManager;
        }

        protected async Task<int> OnExecuteAsync(CommandLineApplication app)
        {
            try
            {
                await this.ExecuteUpgrade();
                return 0;
            }
            catch (Exception ex)
            {
                this.logger.LogError($"Error during upgrade: {ex.Message}");
                return 1;
            }
        }

        protected virtual async Task ExecuteUpgrade()
        {
            bool isSuccess = this.Validate();

            if (!isSuccess)
            {
                return;
            }

            if (string.IsNullOrEmpty(this.Version))
            {
                this.Version = this.sitefinityPackageService.GetLatestSitefinityVersion();
                this.logger.LogInformation(string.Format(Constants.LatestVersionFound, this.Version));
            }

            UpgradeOptions upgradeOptions = new(
                this.SolutionPath,
                this.Version,
                this.SkipPrompts,
                this.AcceptLicense,
                this.NugetConfigPath,
                this.AdditionalPackagesString,
                this.RemoveDeprecatedPackages,
                this.RemoveDeprecatedPackagesExcept);

            this.logger.LogInformation(Constants.SearchingProjectForReferencesMessage);

            IEnumerable<(string FilePath, Version Version)> projectFilePathsWithSitefinityVersion = this.sitefinityProjectService.GetSitefinityProjectPathsFromSolution(this.SolutionPath)
                .Select(p => (FilePath: p, Version: this.sitefinityProjectService.GetSitefinityVersion(p)))
                .Where(p =>
                {
                    if (upgradeOptions.Version <= p.Version)
                    {
                        this.logger.LogWarning(string.Format(Constants.VersionIsGreaterThanOrEqual, Path.GetFileName(p.FilePath), p.Version, upgradeOptions.Version));

                        return false;
                    }

                    return true;
                })
                .ToList();

            if (!projectFilePathsWithSitefinityVersion.Any())
            {
                Utils.WriteLine(Constants.NoProjectsFoundToUpgradeWarningMessage, ConsoleColor.Yellow);
                return;
            }

            bool targetUsesPackagesWithoutLegacyCentaur = upgradeOptions.Version >= Constants.CentaurPackageRemovalVersion;
            bool upgradesFromSitefinityWithLegacyCentaur = projectFilePathsWithSitefinityVersion.Any(project =>
                project.Version != null &&
                project.Version < Constants.CentaurPackageRemovalVersion &&
                this.sitefinityPackageManager.PackageExists(Constants.TelerikCentaurAllNuGetPackageId, project.FilePath));
            
            if (targetUsesPackagesWithoutLegacyCentaur && upgradesFromSitefinityWithLegacyCentaur && !upgradeOptions.DeprecatedPackagesList.Contains(Constants.TelerikCentaurAllNuGetPackageId, StringComparer.OrdinalIgnoreCase))
            {
                upgradeOptions.DeprecatedPackagesList.Add(Constants.TelerikCentaurAllNuGetPackageId);
            }

            List<string> sitefinityProjectsFilePaths = projectFilePathsWithSitefinityVersion.Select(p => p.FilePath).ToList();
            this.logger.LogInformation(string.Format(Constants.NumberOfProjectsWithSitefinityReferencesFoundSuccessMessage, sitefinityProjectsFilePaths.Count));
            this.logger.LogInformation(string.Format(Constants.CollectionSitefinityPackageTreeMessage, this.Version));

            NuGetPackage upgradePackage = await this.sitefinityPackageService.PrepareSitefinityUpgradePackage(upgradeOptions, sitefinityProjectsFilePaths);

            // The removal is planned here, because it depends on the dependency tree of the new Sitefinity version.
            await this.PlanDeprecatedPackagesRemoval(upgradeOptions, sitefinityProjectsFilePaths, upgradePackage);

            if (upgradeOptions.DeprecatedPackagesList.Count > 0)
            {
                List<string> packagesToRemoveBeforeUpgrade = upgradeOptions.PackagesToRemoveBeforeUpgrade.ToList();
                if (packagesToRemoveBeforeUpgrade.Count > 0)
                {
                    this.logger.LogInformation("Deprecated packages that will be removed as part of the upgrade: {DeprecatedPackages}", string.Join(", ", packagesToRemoveBeforeUpgrade));
                }

                if (upgradeOptions.PackagesToRemoveAfterUpgrade.Count > 0)
                {
                    this.logger.LogInformation("Deprecated packages that will be removed after the upgrade: {DeprecatedPackages}", string.Join(", ", upgradeOptions.PackagesToRemoveAfterUpgrade));
                }

                if (!upgradeOptions.SkipPrompts)
                {
                    if (packagesToRemoveBeforeUpgrade.Count > 0)
                    {
                        Utils.WriteLine($"{Environment.NewLine}{Constants.UninstallingPackagesWarning}{Environment.NewLine}{string.Join(Environment.NewLine, packagesToRemoveBeforeUpgrade)}", ConsoleColor.DarkYellow);
                    }

                    if (upgradeOptions.PackagesToRemoveAfterUpgrade.Count > 0)
                    {
                        Utils.WriteLine($"{Environment.NewLine}{Constants.UninstallingPackagesAfterUpgradeWarning}{Environment.NewLine}{string.Join(Environment.NewLine, upgradeOptions.PackagesToRemoveAfterUpgrade)}", ConsoleColor.DarkYellow);
                    }

                    if (!this.promptService.PromptYesNo(Constants.ProceedWithUpgradeMessage))
                    {
                        return;
                    }
                }
            }

            bool isLicenseAccepted = await this.PromptLicenseForPackage(upgradePackage.Id, upgradePackage.Version);

            if (!isLicenseAccepted)
            {
                return;
            }

            IEnumerable<NuGetPackage> additionalPackagesToUpgrade = await this.sitefinityPackageService.PrepareAdditionalPackages(upgradeOptions);

            foreach (NuGetPackage package in additionalPackagesToUpgrade)
            {
                if (package != null)
                {
                    bool additionalPackageLicenseAccepted = await this.PromptLicenseForPackage(package.Id, package.Version);

                    if (!additionalPackageLicenseAccepted)
                    {
                        return;
                    }
                }
            }

            IEnumerable<PackageCompatibilityReport> compatibilityReports = await this.upgradeConfigGenerator.GenerateUpgradeConfig(projectFilePathsWithSitefinityVersion, upgradePackage, upgradeOptions.NugetConfigPath, additionalPackagesToUpgrade.ToList(), !this.SkipPrerequisitesCheck, upgradeOptions.PackagesToRemoveBeforeUpgrade.ToList());
            if (!this.ReportPackageCompatibilityIssues(compatibilityReports, upgradeOptions.SkipPrompts))
            {
                return;
            }

            this.sitefinityProjectService.PrepareProjectFilesForUpgrade(upgradeOptions, sitefinityProjectsFilePaths);

            IDictionary<string, string> configsWithoutSitefinity = this.sitefinityConfigService.GetConfigurtaionsForProjectsWithoutSitefinity(this.SolutionPath);

            this.visualStudioService.ExecuteVisualStudioUpgrade(upgradeOptions);

            this.sitefinityConfigService.RestoreConfigurationValues(configsWithoutSitefinity);

            this.sitefinityPackageService.SyncProjectReferencesWithPackages(sitefinityProjectsFilePaths, Path.GetDirectoryName(this.SolutionPath));

            this.sitefinityProjectService.RestoreBackupFilesAfterUpgrade(upgradeOptions);

            this.logger.LogInformation(string.Format(Constants.UpgradeSuccessMessage, this.SolutionPath, this.Version));
        }

        /// <summary>
        /// Decides which deprecated packages are uninstalled before the upgrade and which after it.
        /// A package with dependencies can pin versions that the upgrade has to change, so it is removed before the upgrade.
        /// A package without dependencies cannot block the upgrade, so it is removed after it, when no installed package requires it anymore.
        /// </summary>
        private async Task PlanDeprecatedPackagesRemoval(UpgradeOptions upgradeOptions, IReadOnlyCollection<string> projectFilePaths, NuGetPackage upgradePackage)
        {
            IEnumerable<PackageSource> packageSources = this.sitefinityPackageManager.GetNugetPackageSources(upgradeOptions.NugetConfigPath);

            // Sitefinity stopped depending on the NativeChat connector in a patch of every minor version, so the dependency tree
            // of the new version is checked instead of a version number. An installed connector that is left behind requires
            // the exact old version of Telerik.Sitefinity.Connectivity, which makes the upgrade of the Sitefinity packages fail.
            bool isNativeChatConnectorLeftBehind =
                projectFilePaths.Any(p => this.sitefinityPackageManager.PackageExists(Constants.NativeChatConnectorNuGetPackageId, p)) &&
                !ContainsPackage(upgradePackage, Constants.NativeChatConnectorNuGetPackageId);

            bool isNativeChatConnectorRemovedAfterUpgrade = isNativeChatConnectorLeftBehind &&
                await this.CanBeRemovedAfterUpgrade(Constants.NativeChatConnectorNuGetPackageId, projectFilePaths, packageSources);

            // A connector that blocks the upgrade is always removed. One that does not is removed only on request, like the other deprecated packages.
            bool isRemovalRequested = this.RemoveDeprecatedPackages || !string.IsNullOrEmpty(this.RemoveDeprecatedPackagesExcept);
            if (isNativeChatConnectorLeftBehind &&
                (!isNativeChatConnectorRemovedAfterUpgrade || isRemovalRequested) &&
                !upgradeOptions.DeprecatedPackagesList.Contains(Constants.NativeChatConnectorNuGetPackageId, StringComparer.OrdinalIgnoreCase))
            {
                upgradeOptions.DeprecatedPackagesList.Add(Constants.NativeChatConnectorNuGetPackageId);
            }

            foreach (string packageId in upgradeOptions.DeprecatedPackagesList)
            {
                // The legacy Centaur package is scheduled only when it blocks the upgrade.
                if (packageId.Equals(Constants.TelerikCentaurAllNuGetPackageId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                bool isNativeChatConnector = packageId.Equals(Constants.NativeChatConnectorNuGetPackageId, StringComparison.OrdinalIgnoreCase);
                bool canBeRemovedAfterUpgrade = isNativeChatConnector
                    ? isNativeChatConnectorRemovedAfterUpgrade
                    : await this.CanBeRemovedAfterUpgrade(packageId, projectFilePaths, packageSources);

                if (canBeRemovedAfterUpgrade)
                {
                    upgradeOptions.PackagesToRemoveAfterUpgrade.Add(packageId);
                }
                else if (isNativeChatConnector)
                {
                    // Its dependency, Telerik.Sitefinity.Connectivity, is required by the other Sitefinity packages and must stay installed.
                    upgradeOptions.PackagesToRemoveKeepingDependencies.Add(packageId);
                }
            }
        }

        /// <returns>True if the package is installed and none of its installed versions has dependencies.</returns>
        private async Task<bool> CanBeRemovedAfterUpgrade(string packageId, IEnumerable<string> projectFilePaths, IEnumerable<PackageSource> packageSources)
        {
            bool isInstalled = false;
            foreach (string projectFilePath in projectFilePaths)
            {
                if (!this.sitefinityPackageManager.PackageExists(packageId, projectFilePath))
                {
                    continue;
                }

                isInstalled = true;

                IReadOnlyList<PackageDependency> dependencies = null;
                try
                {
                    dependencies = await this.sitefinityPackageManager.GetInstalledPackageDependencies(packageId, projectFilePath, packageSources);
                }
                catch (Exception ex)
                {
                    this.logger.LogDebug(ex, "Unable to get the dependencies of package '{PackageId}' installed in '{ProjectFilePath}'.", packageId, projectFilePath);
                }

                // Unknown dependencies are treated as existing ones, which keeps the removal before the upgrade.
                if (dependencies == null || dependencies.Count > 0)
                {
                    return false;
                }
            }

            return isInstalled;
        }

        private static bool ContainsPackage(NuGetPackage packageTree, string packageId)
        {
            if (packageTree == null)
            {
                return false;
            }

            if (packageId.Equals(packageTree.Id, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return packageTree.Dependencies != null && packageTree.Dependencies.Any(d => ContainsPackage(d, packageId));
        }

        /// <returns>False if the user canceled the upgrade.</returns>
        private bool ReportPackageCompatibilityIssues(IEnumerable<PackageCompatibilityReport> compatibilityReports, bool skipPrompts)
        {
            List<PackageCompatibilityReport> reportsWithIssues = (compatibilityReports ?? Enumerable.Empty<PackageCompatibilityReport>()).Where(r => r.HasIssues).ToList();
            if (!reportsWithIssues.Any())
            {
                return true;
            }

            foreach (PackageCompatibilityReport report in reportsWithIssues)
            {
                Utils.WriteLine($"{Environment.NewLine}Project '{report.ProjectFilePath}':", ConsoleColor.DarkYellow);

                if (report.Prerequisites.Any())
                {
                    Utils.WriteLine(Constants.PrerequisitePackagesWarning, ConsoleColor.DarkYellow);
                    foreach (NuGetPackage prerequisite in report.Prerequisites)
                    {
                        Utils.WriteLine($"  {prerequisite.Id} -> {prerequisite.Version}", ConsoleColor.DarkYellow);
                        foreach (PackageConflict conflict in report.ResolvedConflicts.Where(c => c.BlockingPackageId.Equals(prerequisite.Id, StringComparison.OrdinalIgnoreCase)))
                        {
                            Utils.WriteLine($"    {conflict}", ConsoleColor.DarkYellow);
                        }
                    }
                }

                if (report.UnresolvedConflicts.Any())
                {
                    Utils.WriteLine(Constants.UnresolvedPackageConflictsWarning, ConsoleColor.Yellow);
                    foreach (PackageConflict conflict in report.UnresolvedConflicts)
                    {
                        Utils.WriteLine($"  {conflict}", ConsoleColor.Yellow);
                    }
                }
            }

            return skipPrompts || this.promptService.PromptYesNo(Constants.ProceedWithUpgradeMessage);
        }

        private bool Validate()
        {
            bool isSuccess = true;
            if (!Path.IsPathFullyQualified(this.SolutionPath))
            {
                this.SolutionPath = Path.GetFullPath(this.SolutionPath);
            }

            if (!File.Exists(this.SolutionPath))
            {
                throw new FileNotFoundException(string.Format(Constants.FileNotFoundMessage, this.SolutionPath));
            }

            if (!this.SkipPrompts && !this.promptService.PromptYesNo(Constants.UpgradeWarning))
            {
                this.logger.LogInformation(Constants.UpgradeWasCanceled);
                isSuccess = false;
            }

            return isSuccess;
        }


        private readonly ISitefinityNugetPackageService sitefinityPackageService;
        private readonly IVisualStudioService visualStudioService;
        private readonly ILogger<UpgradeCommand> logger;
        private readonly IPromptService promptService;
        private readonly ISitefinityProjectService sitefinityProjectService;
        private readonly ISitefinityConfigService sitefinityConfigService;
        private readonly IUpgradeConfigGenerator upgradeConfigGenerator;
        private readonly ISitefinityPackageManager sitefinityPackageManager;
    }
}
