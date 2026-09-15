function Get-ProcessImageObservation(
    [scriptblock]$HasExited,[scriptblock]$ReadImage,[scriptblock]$RemainingMilliseconds,
    [string]$ExpectedPath,[StringComparison]$Comparison,[string]$Role,
    [Collections.IDictionary]$Observation
) {
    if($null -eq $Observation -or $Observation.Count -ne 0) { throw 'Image observation slot already consumed.' }
    $initial=[BoundedProcessImage]::NewObservation($Role,$ExpectedPath)
    foreach($key in $initial.Keys) { $Observation[$key]=$initial[$key] }
    try { $before=[double](& $RemainingMilliseconds) }
    catch { $Observation.code='image-deadline';throw 'Retained process image refused: image-deadline.' }
    if([double]::IsFinite($before)) { $Observation.remainingBeforeMs=$before }
    if(![double]::IsFinite($before) -or $before -le 0) {
        $Observation.code='image-deadline';throw 'Retained process image refused: image-deadline.'
    }
    try { $exited=& $HasExited }
    catch { $Observation.code='exit-check-failed';throw 'Retained process image refused: exit-check-failed.' }
    if($exited -isnot [bool]) {
        $Observation.code='exit-check-failed';throw 'Retained process image refused: exit-check-failed.'
    }
    if($exited) {
        $Observation.code='exited-unobserved'
        return [pscustomobject]@{state='exited-unobserved';path=$null;attempts=0}
    }
    $Observation.queries=1;$image=$null
    try {
        $image=& $ReadImage
        $description=[BoundedProcessImage]::Describe($image,$ExpectedPath,$Comparison,$Role)
        foreach($key in $description.Keys) { $Observation[$key]=$description[$key] }
        $Observation.remainingBeforeMs=$before
    } catch {
        $Observation.code='image-source-failed'
    }
    try { $after=[double](& $RemainingMilliseconds) }
    catch { $after=[double]::NaN }
    if([double]::IsFinite($after)) { $Observation.remainingAfterMs=$after }
    if((![double]::IsFinite($after) -or $after -le 0) -and $Observation.code -ceq 'image-observed') {
        $Observation.code='image-deadline'
    }
    $mayObserveExit=$Role -ceq 'prepare-initial' -and $Observation.code -ceq 'native-error' -and
        $image -is [ProcessImageRead] -and $image.Code -ceq 'native-error' -and
        $image.NativeError -eq 31 -and $image.HandleValid -and $image.Queries -eq 1 -and
        $null -eq $image.Path -and $null -eq $image.Characters -and
        $Observation.role -ceq 'prepare-initial' -and $Observation.nativeError -eq 31 -and
        $Observation.handleValid -is [bool] -and $Observation.handleValid -and $Observation.queries -eq 1 -and
        $null -eq $Observation.returnedChars -and $null -eq $Observation.observedPathSha256 -and
        $null -eq $Observation.observedPathLength -and $null -eq $Observation.observedPathPreview -and
        $null -eq $Observation.previewTruncated
    if($mayObserveExit -and [double]::IsFinite($after) -and $after -gt 0 -and $after -le $before){
        $postExit=$null
        try { $postExit=& $HasExited }
        catch { $Observation.code='exit-check-failed' }
        if($postExit -isnot [bool]) { $Observation.code='exit-check-failed' }
        try { $exitAfter=[double](& $RemainingMilliseconds) }
        catch { $exitAfter=[double]::NaN }
        $Observation.remainingAfterMs=$null
        if([double]::IsFinite($exitAfter)) { $Observation.remainingAfterMs=$exitAfter }
        if($Observation.code -ceq 'native-error' -and $postExit -is [bool] -and $postExit -and
            [double]::IsFinite($exitAfter) -and $exitAfter -gt 0 -and $exitAfter -le $after){
            # Exit is independently observed; the failed query still supplies no image identity.
            $Observation.code='exited-after-query-error'
            return [pscustomobject]@{state='exited-unobserved';path=$null;attempts=1}
        }
    }
    if($Observation.code -cne 'image-observed') { throw "Retained process image refused: $($Observation.code)." }
    return [pscustomobject]@{state='image-observed';path=$image.Path;attempts=$image.Queries}
}
