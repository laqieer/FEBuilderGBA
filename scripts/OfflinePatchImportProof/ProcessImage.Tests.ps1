$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'ProcessImage.ps1')
$script:processImageCases=0
function CheckImageCase(
    [string]$Name,[object]$Value,[bool]$Exited,[double[]]$Budget,
    [string]$Code,[int]$Reads
) {
    $state=@{reads=0;budget=0}
    $getter={
        $state.reads++
        if($Value -is [Exception]) { throw $Value }
        return $Value
    }.GetNewClosure()
    $clock={
        $index=[Math]::Min($state.budget,$Budget.Length-1);$state.budget++
        return $Budget[$index]
    }.GetNewClosure()
    $hasExited={return $Exited}.GetNewClosure()
    $observation=@{};$result=$null;$failure=$null
    try {
        $result=Get-ProcessImageObservation $hasExited $getter $clock 'C:\expected\tool.exe' `
            ([StringComparison]::OrdinalIgnoreCase) prepare-initial $observation
    } catch { $failure=$_.Exception.Message }
    if($observation.code -cne $Code -or $state.reads -ne $Reads) { throw "$Name observation/read count." }
    if($Code -cin @('image-observed','exited-unobserved')) {
        if($failure -or $result.state -cne $Code) { throw "$Name unexpectedly failed: $failure" }
        if($Code -ceq 'exited-unobserved' -and $null -ne $result.path) { throw 'Fabricated exit image.' }
    } elseif(!$failure -or $null -ne $result) { throw "$Name failed open." }
    $script:processImageCases++
    return ,$observation
}
$matching=[ProcessImageRead]::new('C:\expected\tool.exe','image-observed',$null,1,$true,20)
$null=CheckImageCase 'Already exited' $null $true @(30) exited-unobserved 0
$null=CheckImageCase 'Matching image' $matching $false @(30) image-observed 1
$moduleTrap=[ProcessImageRead]::new('C:\expected\tool.exe','image-observed',$null,1,$true,20)
$moduleTrap | Add-Member -MemberType ScriptProperty -Name FileName -Value { throw 'Module getter must not be read.' }
$null=CheckImageCase 'Executable image is not the module list' $moduleTrap $false @(30) image-observed 1
$wrong=[ProcessImageRead]::new('C:\other\tool.exe','image-observed',$null,1,$true,17)
$initial=CheckImageCase 'Initial mismatch is durable before cleanup' $wrong $false @(30) image-mismatch 1
$frozen=$initial | ConvertTo-Json -Compress
$cleanup=@{}
$null=Get-ProcessImageObservation {$false} {$matching} {30} 'C:\expected\tool.exe' `
    ([StringComparison]::OrdinalIgnoreCase) prepare-cleanup $cleanup
if(($initial|ConvertTo-Json -Compress) -cne $frozen -or $cleanup.code -cne 'image-observed') { throw 'Cleanup replaced first failure.' }
$rejected=$false
try { $null=Get-ProcessImageObservation {$false} {$matching} {30} 'C:\expected\tool.exe' `
    ([StringComparison]::OrdinalIgnoreCase) prepare-cleanup $initial } catch { $rejected=$true }
if(!$rejected -or ($initial|ConvertTo-Json -Compress) -cne $frozen) { throw 'Consumed observation slot was reused.' }
$null=CheckImageCase 'Expired before query' $matching $false @(0) image-deadline 0
$null=CheckImageCase 'Late matching query is not success' $matching $false @(30,0) image-deadline 1
$case=[ProcessImageRead]::new('c:\EXPECTED\TOOL.exe','image-observed',$null,1,$true,20)
$null=CheckImageCase 'Windows path case' $case $false @(30) image-observed 1
$null=CheckImageCase 'Unavailable query is not an exit or retry' `
    ([ProcessImageRead]::new($null,'native-error',6,1,$true,$null)) $false @(30) native-error 1
$null=CheckImageCase 'Unexpected source exception stays refused' `
    ([InvalidOperationException]::new('not for disclosure')) $false @(30) image-source-failed 1
$denied=CheckImageCase 'Access denied stays refused despite deadline' `
    ([ProcessImageRead]::new($null,'native-error',5,1,$true,$null)) $false @(30,0) native-error 1
if($denied.nativeError -ne 5 -or $null -ne $denied.observedPathPreview) { throw 'Access error evidence lost/leaked.' }
if($script:processImageCases -ne 10) { throw 'All ten process-image cases must run.' }
$script:processImageCases
