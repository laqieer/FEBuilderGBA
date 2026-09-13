$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'ProcessImage.ps1')
$script:processImageCases=0
function CheckImageCase(
    [string]$name,[bool[]]$exitValues,[object[]]$moduleValues,
    [string]$expectedState,[int]$expectedReads,[int]$expectedSleeps,[string]$expectedFailure=''
) {
    $exits=[Collections.Generic.Queue[bool]]::new()
    foreach($value in $exitValues) { $exits.Enqueue($value) }
    $modules=[Collections.Generic.Queue[object]]::new()
    foreach($value in $moduleValues) { $modules.Enqueue($value) }
    $state=[pscustomobject]@{elapsed=0;reads=0;sleeps=0}
    $hasExited={
        if($exits.Count -gt 1) { return $exits.Dequeue() }
        return $exits.Peek()
    }.GetNewClosure()
    $readModule={
        $state.reads++
        if($modules.Count -eq 0) { throw 'Unexpected image read.' }
        $value=if($modules.Count -gt 1) { $modules.Dequeue() } else { $modules.Peek() }
        if($value -is [Exception]) { throw $value }
        return $value
    }.GetNewClosure()
    $remaining={ 30-$state.elapsed }.GetNewClosure()
    $delay={ param([int]$milliseconds) $state.sleeps++; $state.elapsed+=$milliseconds }.GetNewClosure()
    $result=$null; $failure=$null
    try {
        $result=Get-ProcessImageObservation $hasExited $readModule $remaining $delay 'C:\expected\tool.exe'
    } catch { $failure=$_.Exception.Message }
    if($expectedFailure) {
        if(!$failure -or $failure -notlike $expectedFailure) { throw "$name expected '$expectedFailure', got '$failure'." }
    } else {
        if($failure) { throw "$name unexpectedly failed: $failure" }
        if($result.state -cne $expectedState) { throw "$name returned the wrong state." }
        if($expectedState -ceq 'image-observed' -and $result.path -ine 'C:\expected\tool.exe') { throw "$name did not bind the expected image." }
        if($expectedState -ceq 'exited-unobserved' -and $null -ne $result.path) { throw "$name fabricated an image observation." }
    }
    if($state.reads -ne $expectedReads -or $state.sleeps -ne $expectedSleeps) { throw "$name took unexpected probe/wait steps." }
    $script:processImageCases++
}
$matching=[pscustomobject]@{FileName='C:\expected\tool.exe'}
CheckImageCase 'Already exited' @($true) @() 'exited-unobserved' 0 0
CheckImageCase 'Matching image' @($false) @($matching) 'image-observed' 1 0
CheckImageCase 'Startup module not ready' @($false) @($null,$matching) 'image-observed' 2 1
CheckImageCase 'Exit during observation' @($false,$true) @($null) 'exited-unobserved' 1 1
CheckImageCase 'Unavailable until deadline' @($false) @($null) '' 3 3 '*image observation deadline*'
CheckImageCase 'Wrong image rejected' @($false) @([pscustomobject]@{FileName='C:\other\tool.exe'}) '' 1 0 '*executable mismatch*'
CheckImageCase 'Windows path case' @($false) @([pscustomobject]@{FileName='c:\EXPECTED\TOOL.exe'}) 'image-observed' 1 0
CheckImageCase 'Exited during getter' @($false,$true) @([InvalidOperationException]::new('Process exited during image query.')) 'exited-unobserved' 1 0
CheckImageCase 'Unexpected live getter error' @($false) @([InvalidOperationException]::new('Live process query failure.')) '' 1 0 '*Live process query failure*'
CheckImageCase 'Access denied remains failure' @($false) @([ComponentModel.Win32Exception]::new(5,'Image access denied.')) '' 1 0 '*Image access denied*'
if($script:processImageCases -ne 10) { throw 'All ten process-image cases must run.' }
$script:processImageCases
