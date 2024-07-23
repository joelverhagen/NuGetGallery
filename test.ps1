[CmdletBinding(DefaultParameterSetName = 'RegularBuild')]
param (
    [ValidateSet("debug", "release")]
    [string]$Configuration = 'debug',
    [int]$BuildNumber,
    [switch]$SkipGallery,
    [switch]$SkipJobs
)

trap {
    Write-Host "BUILD FAILED: $_" -ForegroundColor Red
    Write-Host "ERROR DETAILS:" -ForegroundColor Red
    Write-Host $_.Exception -ForegroundColor Red
    Write-Host ("`r`n" * 3)
    exit 1
}

. "$PSScriptRoot\build\common.ps1"

Write-Host ("`r`n" * 3)
Trace-Log ('=' * 60)

$startTime = [DateTime]::UtcNow
if (-not $BuildNumber) {
    $BuildNumber = Get-BuildNumber
}
Trace-Log "Build #$BuildNumber started at $startTime"

$BuildErrors = @()
$xUnitExe = (Join-Path $PSScriptRoot "packages\xunit.runner.console\tools\net472\xunit.console.exe")
$TestCount = 0

Invoke-BuildStep 'Running gallery tests' {
        $GalleryTestAssemblies = `
            "tests\AccountDeleter.Facts\bin\$Configuration\net472\AccountDeleter.Facts.dll", `
            "tests\GitHubVulnerabilities2Db.Facts\bin\$Configuration\net472\GitHubVulnerabilities2Db.Facts.dll", `
            "tests\GitHubVulnerabilities2v3.Facts\bin\$Configuration\GitHubVulnerabilities2v3.Facts.dll", `
            "tests\NuGet.Services.DatabaseMigration.Facts\bin\$Configuration\net472\NuGet.Services.DatabaseMigration.Facts.dll", `
            "tests\NuGet.Services.Entities.Tests\bin\$Configuration\net472\NuGet.Services.Entities.Tests.dll", `
            "tests\NuGetGallery.Core.Facts\bin\$Configuration\NuGetGallery.Core.Facts.dll", `
            "tests\NuGetGallery.Facts\bin\$Configuration\NuGetGallery.Facts.dll", `
            "tests\VerifyMicrosoftPackage.Facts\bin\$Configuration\NuGet.VerifyMicrosoftPackage.Facts.dll"

        $GalleryTestAssemblies | ForEach-Object {
            $TestResultFile = Join-Path $PSScriptRoot "Results.$TestCount.xml"
            & $xUnitExe (Join-Path $PSScriptRoot $_) -xml $TestResultFile
            if (-not (Test-Path $TestResultFile)) {
                Write-Error "The test run failed to produce a result file";
                exit 1;
            }
            $TestCount++
        }

        Write-Host "Ensuring the EntityFramework version can be discovered."
        . (Join-Path $PSScriptRoot "tools\Update-Databases.ps1") -MigrationTargets @("FakeMigrationTarget")
    } `
    -skip:$SkipGallery `
    -ev +BuildErrors


Invoke-BuildStep 'Running jobs tests' {
        $JobsTestProjects =
            "tests\NuGet.Services.SearchService.Core.Tests\NuGet.Services.SearchService.Core.Tests.csproj"

        $JobsTestAssemblies =
            "tests\CatalogMetadataTests\bin\$Configuration\CatalogMetadataTests.dll",
            "tests\CatalogTests\bin\$Configuration\CatalogTests.dll",
            "tests\Monitoring.PackageLag.Tests\bin\$Configuration\Monitoring.PackageLag.Tests.dll",
            "tests\NgTests\bin\$Configuration\NgTests.dll",
            "tests\NuGet.Jobs.Catalog2Registration.Tests\bin\$Configuration\NuGet.Jobs.Catalog2Registration.Tests.dll",
            "tests\NuGet.Jobs.Common.Tests\bin\$Configuration\NuGet.Jobs.Common.Tests.dll",
            "tests\NuGet.Jobs.GitHubIndexer.Tests\bin\$Configuration\NuGet.Jobs.GitHubIndexer.Tests.dll",
            "tests\NuGet.Protocol.Catalog.Tests\bin\$Configuration\NuGet.Protocol.Catalog.Tests.dll",
            "tests\NuGet.Services.AzureSearch.Tests\bin\$Configuration\NuGet.Services.AzureSearch.Tests.dll",
            "tests\NuGet.Services.Revalidate.Tests\bin\$Configuration\NuGet.Services.Revalidate.Tests.dll",
            "tests\NuGet.Services.Validation.Orchestrator.Tests\bin\$Configuration\NuGet.Services.Validation.Orchestrator.Tests.dll",
            "tests\SplitLargeFiles.Tests\bin\$Configuration\NuGet.Tools.SplitLargeFiles.Tests.dll",
            "tests\StatusAggregator.Tests\bin\$Configuration\StatusAggregator.Tests.dll",
            "tests\Tests.CredentialExpiration\bin\$Configuration\Tests.CredentialExpiration.dll",
            "tests\Tests.Gallery.Maintenance\bin\$Configuration\Tests.Gallery.Maintenance.dll",
            "tests\Tests.Stats.AggregateCdnDownloadsInGallery\bin\$Configuration\Tests.Stats.AggregateCdnDownloadsInGallery.dll",
            "tests\Tests.Stats.AzureCdnLogs.Common\bin\$Configuration\Tests.Stats.AzureCdnLogs.Common.dll",
            "tests\Tests.Stats.CDNLogsSanitizer\bin\$Configuration\Tests.Stats.CDNLogsSanitizer.dll",
            "tests\Tests.Stats.CollectAzureCdnLogs\bin\$Configuration\Tests.Stats.CollectAzureCdnLogs.dll",
            "tests\Tests.Stats.CollectAzureChinaCDNLogs\bin\$Configuration\Tests.Stats.CollectAzureChinaCDNLogs.dll",
            "tests\Tests.Stats.ImportAzureCdnStatistics\bin\$Configuration\Tests.Stats.ImportAzureCdnStatistics.dll",
            "tests\Validation.Common.Job.Tests\bin\$Configuration\Validation.Common.Job.Tests.dll",
            "tests\Validation.PackageSigning.Core.Tests\bin\$Configuration\Validation.PackageSigning.Core.Tests.dll",
            "tests\Validation.PackageSigning.ProcessSignature.Tests\bin\$Configuration\Validation.PackageSigning.ProcessSignature.Tests.dll",
            "tests\Validation.PackageSigning.RevalidateCertificate.Tests\bin\$Configuration\Validation.PackageSigning.RevalidateCertificate.Tests.dll",
            "tests\Validation.PackageSigning.ScanAndSign.Tests\bin\$Configuration\Validation.PackageSigning.ScanAndSign.Tests.dll",
            "tests\Validation.PackageSigning.ValidateCertificate.Tests\bin\$Configuration\Validation.PackageSigning.ValidateCertificate.Tests.dll",
            "tests\Validation.Symbols.Core.Tests\bin\$Configuration\Validation.Symbols.Core.Tests.dll",
            "tests\Validation.Symbols.Tests\bin\$Configuration\Validation.Symbols.Tests.dll"
        
        $JobsTestProjects | ForEach-Object {
            $TestResultFile = Join-Path $PSScriptRoot "Results.$TestCount.xml"
            dotnet test (Join-Path $PSScriptRoot $_) --no-restore --no-build --configuration $Configuration "-l:trx;LogFileName=$TestResultFile"
            if (-not (Test-Path $TestResultFile)) {
                Write-Error "The test run failed to produce a result file";
                exit 1;
            }
            $TestCount++
        }
        
        $JobsTestAssemblies | ForEach-Object {
            $TestResultFile = Join-Path $PSScriptRoot "Results.$TestCount.xml"
            & $xUnitExe (Join-Path $PSScriptRoot $_) -xml $TestResultFile
            if (-not (Test-Path $TestResultFile)) {
                Write-Error "The test run failed to produce a result file";
                exit 1;
            }
            $TestCount++
    }
    } `
    -skip:$SkipJobs `
    -ev +BuildErrors

Trace-Log ('-' * 60)

## Calculating Build time
$endTime = [DateTime]::UtcNow
Trace-Log "Build #$BuildNumber ended at $endTime"
Trace-Log "Time elapsed $(Format-ElapsedTime ($endTime - $startTime))"

Trace-Log ('=' * 60)

if ($BuildErrors) {
    $ErrorLines = $BuildErrors | ForEach-Object { ">>> $($_.Exception.Message)" }
    Error-Log "Tests completed with $($BuildErrors.Count) error(s):`r`n$($ErrorLines -join "`r`n")" -Fatal
}

Write-Host ("`r`n" * 3)
