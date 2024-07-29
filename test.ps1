[CmdletBinding(DefaultParameterSetName = 'RegularBuild')]
param (
    [ValidateSet("debug", "release")]
    [string]$Configuration = 'debug',
    [int]$BuildNumber,
    [switch]$SkipCommon,
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

$TestErrors = @()
$CommonSolution = Join-Path $PSScriptRoot "NuGet.Server.Common.sln"
$CommonProjects = Get-SolutionProjects $CommonSolution
$GallerySolution = Join-Path $PSScriptRoot "NuGetGallery.sln"
$GalleryProjects = Get-SolutionProjects $GallerySolution
$JobsSolution = Join-Path $PSScriptRoot "NuGet.Jobs.sln"
$JobsProjects = Get-SolutionProjects $JobsSolution
$ExcludeTestProjects =
    "tests\Validation.PackageSigning.Helpers\Tests.ContextHelpers.csproj"

Invoke-BuildStep 'Cleaning test results' { Clear-Tests } `
    -ev +TestErrors

$TestCounter = @{ Count = 0 }
	
Invoke-BuildStep 'Running common tests' {
        $CommonTestProjects = $CommonProjects | Where-Object { $_.IsTest }

        $CommonTestProjects | ForEach-Object {
            $TestResultFile = Join-Path $PSScriptRoot "Results.$($TestCounter.Count).xml"
            Trace-Log "Testing $($_.Path)"
            dotnet test $_.Path --no-restore --no-build --configuration $Configuration "-l:trx;LogFileName=$TestResultFile"
            if (-not (Test-Path $TestResultFile)) {
                Write-Error "The test run failed to produce a result file";
                exit 1;
            }
            $TestCounter.Count++
        }
    } `
    -skip:$SkipCommon `
    -ev +TestErrors

Invoke-BuildStep 'Running gallery tests' {
        $GalleryTestProjects = $GalleryProjects | Where-Object { $_.IsTest }
        
        $GalleryTestProjects | ForEach-Object {
            $TestResultFile = Join-Path $PSScriptRoot "Results.$($TestCounter.Count).xml"
            Trace-Log "Testing $($_.Path)"
            dotnet test $_.Path --no-restore --no-build --configuration $Configuration "-l:trx;LogFileName=$TestResultFile"
            if (-not (Test-Path $TestResultFile)) {
                Write-Error "The test run failed to produce a result file";
                exit 1;
            }
            $TestCounter.Count++
        }

        Write-Host "Ensuring the EntityFramework version can be discovered."
        . (Join-Path $PSScriptRoot "tools\Update-Databases.ps1") -MigrationTargets @("FakeMigrationTarget")
    } `
    -skip:$SkipGallery `
    -ev +TestErrors

Invoke-BuildStep 'Running jobs tests' {
        $JobsTestProjects = $JobsProjects `
            | Where-Object { $_.IsTest } `
            | Where-Object { $ExcludeTestProjects -notcontains $_.RelativePath }
        
        $JobsTestProjects | ForEach-Object {
            $TestResultFile = Join-Path $PSScriptRoot "Results.$($TestCounter.Count).xml"
            Trace-Log "Testing $($_.Path)"
            dotnet test $_.Path --no-restore --no-build --configuration $Configuration "-l:trx;LogFileName=$TestResultFile"
            if (-not (Test-Path $TestResultFile)) {
                Write-Error "The test run failed to produce a result file";
                exit 1;
            }
            $TestCounter.Count++
        }
    } `
    -skip:$SkipJobs `
    -ev +TestErrors

Trace-Log ('-' * 60)

## Calculating Build time
$endTime = [DateTime]::UtcNow
Trace-Log "Build #$BuildNumber ended at $endTime"
Trace-Log "Time elapsed $(Format-ElapsedTime ($endTime - $startTime))"

Trace-Log ('=' * 60)

if ($TestErrors) {
    $ErrorLines = $TestErrors | ForEach-Object { ">>> $($_.Exception.Message)" }
    Error-Log "Tests completed with $($TestErrors.Count) error(s):`r`n$($ErrorLines -join "`r`n")" -Fatal
}

Write-Host ("`r`n" * 3)
