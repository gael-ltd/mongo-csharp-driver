#addin "nuget:?package=Cake.FileHelpers"
#addin "nuget:?package=Cake.Git"
#addin "nuget:?package=Cake.Incubator&version=2.0.0"
#tool "nuget:?package=GitVersion.CommandLine"
#tool "nuget:?package=xunit.runner.console"
#load buildhelpers.cake

using System.Text.RegularExpressions;
using System.Linq;

var target = Argument("target", "Default");
var configuration = Argument("configuration", "Release");

var gitVersion = GitVersion();

var solutionDirectory = MakeAbsolute(Directory("./"));
var artifactsDirectory = solutionDirectory.Combine("artifacts");
var artifactsBinDirectory = artifactsDirectory.Combine("bin");
var artifactsBinNet45Directory = artifactsBinDirectory.Combine("net45");
var artifactsBinNetStandard15Directory = artifactsBinDirectory.Combine("netstandard1.5");
var artifactsDocsDirectory = artifactsDirectory.Combine("docs");
var artifactsDocsApiDocsDirectory = artifactsDocsDirectory.Combine("ApiDocs-" + gitVersion.SemVer);
var artifactsDocsRefDocsDirectory = artifactsDocsDirectory.Combine("RefDocs-" + gitVersion.SemVer);
var artifactsPackagesDirectory = artifactsDirectory.Combine("packages");
var docsDirectory = solutionDirectory.Combine("Docs");
var docsApiDirectory = docsDirectory.Combine("Api");
var srcDirectory = solutionDirectory.Combine("src");
var testsDirectory = solutionDirectory.Combine("tests");
var toolsDirectory = solutionDirectory.Combine("Tools");
var toolsHugoDirectory = toolsDirectory.Combine("Hugo");

var solutionFile = solutionDirectory.CombineWithFilePath("CSharpDriver.sln");
var srcProjectNames = new[]
{
    "MongoDB.Bson",
    "MongoDB.Driver.Core",
    "MongoDB.Driver",
    "MongoDB.Driver.Legacy",
    "MongoDB.Driver.GridFS"
};

Task("Default")
    .IsDependentOn("Build")
	.IsDependentOn("Package");

Task("Build")
    .IsDependentOn("BuildNet45")
    .IsDependentOn("BuildNetStandard15");
	
Task("BuildNet45")
    .Does(() =>
    {
        NuGetRestore(solutionFile);
        GlobalAssemblyInfo.OverwriteGlobalAssemblyInfoFile(Context, solutionDirectory, configuration, gitVersion);
        DotNetBuild(solutionFile, settings => settings
            .SetConfiguration(configuration)
            .SetVerbosity(Verbosity.Minimal)
            .WithProperty("TargetFrameworkVersion", "v4.5"));

        EnsureDirectoryExists(artifactsBinNet45Directory);
        foreach (var projectName in srcProjectNames)
        {
            var projectDirectory = srcDirectory.Combine(projectName);
            var outputDirectory = projectDirectory.Combine("bin").Combine(configuration);
            foreach (var extension in new [] { ".dll", ".pdb", ".xml" })
            {
                var outputFileName = projectName + extension;
                var outputFile = outputDirectory.CombineWithFilePath(outputFileName);
                var artifactFile = artifactsBinNet45Directory.CombineWithFilePath(outputFileName);
                CopyFile(outputFile, artifactFile);
            }
        }

        foreach (var dnsClientFileName in new[] { "DnsClient.dll", "DnsClient.xml"})
        {
            var sourceDirectory = srcDirectory.Combine("MongoDB.Driver.Core").Combine("bin").Combine("Release");
            var sourceFile = sourceDirectory.CombineWithFilePath(dnsClientFileName);
            var destinationFile = artifactsBinNet45Directory.CombineWithFilePath(dnsClientFileName);
            CopyFile(sourceFile, destinationFile);
        }
    })
    .Finally(() =>
    {
        GlobalAssemblyInfo.RestoreGlobalAssemblyInfoFile(Context, solutionDirectory);
    });

Task("BuildNetStandard15")
    .Does(() =>
    {
        var dotNetProjectDirectories = srcProjectNames.Select(
            projectName=>srcDirectory.Combine(projectName+".Dotnet"));

        foreach (var directory in dotNetProjectDirectories) 
        {
            DotNetCoreRestore(directory.ToString());
        }
        GlobalAssemblyInfo.OverwriteGlobalAssemblyInfoFile(Context, solutionDirectory, configuration, gitVersion);
        var settings= new DotNetCoreBuildSettings { Configuration = configuration };
        foreach (var directory in dotNetProjectDirectories) 
        {
            DotNetCoreBuild(directory.ToString(), settings);
        }
        EnsureDirectoryExists(artifactsBinNetStandard15Directory);
        foreach (var projectName in srcProjectNames)
        {
            var projectDirectory = srcDirectory.Combine(projectName + ".Dotnet");
            var outputDirectory = projectDirectory.Combine("bin").Combine(configuration).Combine("netstandard1.5");
            foreach (var extension in new [] { ".dll", ".pdb", ".xml" })
            {
                var outputFileName = projectName + extension;
                var outputFile = outputDirectory.CombineWithFilePath(outputFileName);
                var artifactFile = artifactsBinNetStandard15Directory.CombineWithFilePath(outputFileName);
                CopyFile(outputFile, artifactFile);
            }
        }
    })
    .Finally(() =>
    {
        GlobalAssemblyInfo.RestoreGlobalAssemblyInfoFile(Context, solutionDirectory);
    });

Task("Package")
    .IsDependentOn("PackageNugetPackages");

Task("PackageNugetPackages")
    .IsDependentOn("BuildNet45")
    .IsDependentOn("BuildNetStandard15")
    .Does(() =>
    {
        EnsureDirectoryExists(artifactsPackagesDirectory);
        CleanDirectory(artifactsPackagesDirectory);

        var packageVersion = gitVersion.MajorMinorPatch;

        var nuspecFiles = GetFiles("./Build/*.nuspec");
        foreach (var nuspecFile in nuspecFiles)
        {
            var tempNuspecFilename = nuspecFile.GetFilenameWithoutExtension().ToString() + "." + packageVersion + ".nuspec";
            var tempNuspecFile = artifactsPackagesDirectory.CombineWithFilePath(tempNuspecFilename);

            CopyFile(nuspecFile, tempNuspecFile);
            ReplaceTextInFiles(tempNuspecFile.ToString(), "@driverPackageVersion@", packageVersion);
            ReplaceTextInFiles(tempNuspecFile.ToString(), "@solutionDirectory@", solutionDirectory.FullPath);

            NuGetPack(tempNuspecFile, new NuGetPackSettings
            {
                OutputDirectory = artifactsPackagesDirectory,
                Symbols = true
            });

            // DeleteFile(tempNuspecFile);
        }
    });

Task("DumpGitVersion")
    .Does(() =>
    {
        Information(gitVersion.Dump());
    });

RunTarget(target);
