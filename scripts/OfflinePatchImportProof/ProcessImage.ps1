function Get-ProcessImageObservation(
    [scriptblock]$HasExited,[scriptblock]$ReadMainModule,
    [scriptblock]$RemainingMilliseconds,[scriptblock]$DelayMilliseconds,[string]$ExpectedPath
) {
    $attempts=0
    while($true) {
        if(& $HasExited) {
            return [pscustomobject]@{state='exited-unobserved';path=$null;attempts=$attempts}
        }
        if((& $RemainingMilliseconds) -le 0) { throw 'Process image observation deadline exceeded.' }
        $attempts++
        try { $module=& $ReadMainModule }
        catch [InvalidOperationException] {
            if(!(& $HasExited)) { throw }
            return [pscustomobject]@{state='exited-unobserved';path=$null;attempts=$attempts}
        }
        if($null -ne $module) {
            if($module.FileName -ine $ExpectedPath) { throw 'Started executable mismatch.' }
            return [pscustomobject]@{state='image-observed';path=$module.FileName;attempts=$attempts}
        }
        # MainModule can be absent while the retained process is still starting.
        $remaining=[double](& $RemainingMilliseconds)
        if($remaining -le 0) { throw 'Process image observation deadline exceeded.' }
        & $DelayMilliseconds ([int][Math]::Min(10,[Math]::Ceiling($remaining)))
    }
}
