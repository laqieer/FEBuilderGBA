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
    $Observation.queries=1
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
    if($Observation.code -cne 'image-observed') { throw "Retained process image refused: $($Observation.code)." }
    return [pscustomobject]@{state='image-observed';path=$image.Path;attempts=$image.Queries}
}
