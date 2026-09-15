function Assert-ProofTestThrows([scriptblock]$Action) {
    $failed=$false
    try { & $Action | Out-Null } catch {
        if($_.Exception -is [Management.Automation.CommandNotFoundException]){throw}
        $failed=$true
    }
    Assert-Proof $failed 'Expected refusal.'
}
function Invoke-RetainedImageReportingTests {
    function Image {
        [BoundedProcessImage]::Describe(
            [ProcessImageRead]::new('C:\expected\tool.exe','image-observed',$null,1,$true,20),
            'C:\expected\tool.exe',[StringComparison]::Ordinal,'supervision-child-initial')
    }
    function Record($image) {
        New-RTerminalRecord @{exe='C:\expected\tool.exe';hostSha256=('1'*64)} @{imageObservation=$image}
    }
    function Reject([scriptblock]$change) {
        $value=Image;& $change $value
        Assert-ProofTestThrows { $null=Record $value }
    }
    $cases=@(
        @{name='valid';run={$r=Record (Image);Assert-Proof ($r.imageObservation.code -ceq 'image-observed') 'Image projection.'}},
        @{name='null';run={$r=Record $null;Assert-Proof ($null -eq $r.imageObservation) 'Optional historical diagnostic.'}},
        @{name='extra-key';run={Reject {param($d) $d['extra']=$true}}},
        @{name='missing-key';run={Reject {param($d) $null=$d.Remove('method')}}},
        @{name='wrong-type';run={Reject {param($d) $d['handleValid']='true'}}},
        @{name='unknown-role';run={Reject {param($d) $d['role']='foreign-process'}}},
        @{name='unknown-code';run={Reject {param($d) $d['code']='trusted'}}},
        @{name='wrong-flags';run={Reject {param($d) $d['flags']=1}}},
        @{name='query-count';run={Reject {param($d) $d['queries']=2}}},
        @{name='error-type';run={Reject {param($d) $d['nativeError']='5'}}},
        @{name='length-bound';run={Reject {param($d) $d['returnedChars']=32768}}},
        @{name='preview-bound';run={Reject {param($d) $d['observedPathPreview']='a'*257}}},
        @{name='json-bound';run={Reject {param($d) $d['expectedPathSha256']='a'*5000}}},
        @{name='reserved-binding';run={
            Assert-ProofTestThrows { $null=New-RTerminalRecord @{exe='host';hostSha256=('1'*64);imageObservation=$null} @{} }
        }},
        @{name='terminal-snapshot-freeze';run={
            $d=Image;$memory=@{terminal=$null;receipt=$null}
            Assert-ProofTestThrows {
              $null=Invoke-RTerminalPublication -Bindings @{exe='host';hostSha256=('1'*64)} `
                -Observations @{failure='original';imageObservation=$d} `
                -WriteTerminal {param($Bytes) $memory.terminal=$Bytes.Clone();$d['code']='image-mismatch';$true} `
                -WriteReceipt {param($Bytes) $memory.receipt=$Bytes.Clone();$true}
            }
            $terminal=ConvertFrom-RJson ([Text.Encoding]::UTF8.GetString($memory.terminal))
            $receipt=ConvertFrom-RJson ([Text.Encoding]::UTF8.GetString($memory.receipt))
            Assert-Proof ($terminal.imageObservation.code -ceq 'image-observed' -and
                $receipt.imageObservation.code -ceq 'image-observed' -and $d.code -ceq 'image-mismatch') 'Frozen first observation.'
        }},
        @{name='diagnostic-cannot-promote';run={
            $memory=@{verifies=0;receipt=$null}
            Assert-ProofTestThrows {
              $null=Invoke-RTerminalPublication -Bindings @{exe='host';hostSha256=('1'*64)} `
                -Observations @{failure='original';imageObservation=(Image)} `
                -WriteTerminal {param($Bytes) $true} `
                -VerifyOutput {$memory.verifies++;throw 'Verification must not be admitted.'} `
                -WriteReceipt {param($Bytes) $memory.receipt=$Bytes.Clone();$true}
            }
            $r=ConvertFrom-RJson ([Text.Encoding]::UTF8.GetString($memory.receipt))
            Assert-Proof (!$r.passed -and $memory.verifies -eq 0 -and $r.failure -ceq 'original') 'Diagnostic promoted failure.'
        }}
    )
    $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal);$executed=0
    foreach($case in $cases) {
        Assert-Proof ($seen.Add($case.name)) 'Unique image reporting case.'
        & $case.run;$executed++
    }
    Assert-Proof ($executed -eq 16) 'Image reporting inventory.'
    return $executed
}
function Invoke-PreparationExitTestObservation($Case) {
    # PowerShell binds $null to an empty string here; the native reader returns a true null.
    $source=if($Case.Contains('source')){$Case.source}else{
        [ProcessImageRead]::new([NullString]::Value,'native-error',31,1,$true,$null)
    }
    $exitValues=@($false,$true);$budgetValues=@(30,20,10)
    if($Case.Contains('exitValues')){$exitValues=$Case.exitValues}
    if($Case.Contains('budgetValues')){$budgetValues=$Case.budgetValues}
    $role=if($Case.Contains('role')){$Case.role}else{'prepare-initial'}
    $state=@{exits=0;reads=0;budgets=0;trace=[Collections.Generic.List[string]]::new()}
    $observation=if($Case.Contains('slot')){@{}+$Case.slot}else{@{}}
    $snapshot=$observation|ConvertTo-Json -Compress
    $result=$null;$failure=$null
    $hasExited={
        $state.trace.Add('exit');$index=$state.exits;$state.exits++
        if($index -ge $exitValues.Count){throw 'Unexpected exit callback.'}
        $value=$exitValues[$index]
        if($value -is [Exception]){throw $value}
        return ,$value
    }.GetNewClosure()
    $readImage={
        $state.trace.Add('image');$state.reads++
        if($source -is [Exception]){throw $source}
        return ,$source
    }.GetNewClosure()
    $clock={
        $state.trace.Add('budget');$index=$state.budgets;$state.budgets++
        if($index -ge $budgetValues.Count){throw 'Unexpected budget callback.'}
        $value=$budgetValues[$index]
        if($value -is [Exception]){throw $value}
        return ,$value
    }.GetNewClosure()
    try {
        $result=Get-ProcessImageObservation $hasExited $readImage $clock `
            'C:\expected\tool.exe' ([StringComparison]::OrdinalIgnoreCase) $role $observation
    } catch { $failure=$_.Exception.Message }
    return @{result=$result;failure=$failure;observation=$observation;source=$source;calls=$state;slotBefore=$snapshot}
}
function Invoke-PreparationExitObservationTests {
    $cases=[Collections.Generic.List[object]]::new()
    foreach($case in @(
        @{name='exit-after-native-error';code='exited-after-query-error';success=$true;exits=2;budgets=3},
        @{name='equal-positive-budgets';code='exited-after-query-error';success=$true;exits=2;budgets=3;budgetValues=@(30,30,30)},
        @{name='fractional-positive-budgets';code='exited-after-query-error';success=$true;exits=2;budgets=3;budgetValues=@(0.3,0.2,0.1)},
        @{name='already-exited-unchanged';code='exited-unobserved';success=$true;exitValues=@($true);reads=0;budgets=1},
        @{name='matching-image-unchanged';code='image-observed';success=$true;source=[ProcessImageRead]::new('C:\expected\tool.exe','image-observed',$null,1,$true,20)},
        @{name='mismatch-then-exit';code='image-mismatch';source=[ProcessImageRead]::new('C:\other\tool.exe','image-observed',$null,1,$true,17)},
        @{name='still-live';exitValues=@($false,$false);exits=2;budgets=3},
        @{name='invalid-retained-handle';source=[ProcessImageRead]::new([NullString]::Value,'native-error',31,1,$false,$null)},
        @{name='zero-queries';source=[ProcessImageRead]::new([NullString]::Value,'native-error',31,0,$true,$null)},
        @{name='multiple-queries';source=[ProcessImageRead]::new([NullString]::Value,'native-error',31,2,$true,$null)},
        @{name='negative-queries';source=[ProcessImageRead]::new([NullString]::Value,'native-error',31,-1,$true,$null)},
        @{name='error-with-path';source=[ProcessImageRead]::new('C:\other\tool.exe','native-error',31,1,$true,$null)},
        @{name='error-with-empty-path';source=[ProcessImageRead]::new('','native-error',31,1,$true,$null)},
        @{name='error-with-character-count';source=[ProcessImageRead]::new([NullString]::Value,'native-error',31,1,$true,1)},
        @{name='error-with-zero-character-count';source=[ProcessImageRead]::new([NullString]::Value,'native-error',31,1,$true,0)},
        @{name='source-exception';code='image-source-failed';source=[InvalidOperationException]::new('private source detail')},
        @{name='null-source';code='image-source-failed';source=$null},
        @{name='untyped-source';code='image-source-failed';source=[pscustomobject]@{Code='native-error';NativeError=31;Queries=1;HandleValid=$true;Path=$null;Characters=$null}},
        @{name='unknown-role';code=$null;role='foreign-process';reads=0;exits=0;budgets=0},
        @{name='role-case-mismatch';code=$null;role='Prepare-initial';reads=0;exits=0;budgets=0},
        @{name='consumed-slot';code='image-mismatch';slot=@{code='image-mismatch'};reads=0;exits=0;budgets=0},
        @{name='post-query-budget-increased';budgetValues=@(30,31,10)},
        @{name='post-exit-budget-increased';budgetValues=@(30,20,21);exits=2;budgets=3}
    )){$cases.Add($case)}
    foreach($role in @('prepare-cleanup','launch-runner-initial','launch-runner-cleanup','launch-app-retain',
        'launch-app-cleanup','run-self','run-app-initial','desktop-app','supervision-self',
        'supervision-child-initial','supervision-child-cleanup')){
        $cases.Add(@{name='role-'+$role;role=$role})
    }
    foreach($code in @('not-queried','invalid-handle','query-exception','invalid-image','image-mismatch',
        'image-deadline','exit-check-failed','exited-unobserved','image-source-failed','identity-refused',
        'exited-after-query-error','unknown-refusal')){
        $cases.Add(@{name='code-'+$code;code=$code;source=[ProcessImageRead]::new([NullString]::Value,$code,31,1,$true,$null)})
    }
    foreach($errorCase in @(@{name='null';value=$null},@{name='zero';value=0},@{name='access-denied';value=5},
        @{name='invalid-handle';value=6},@{name='other';value=87},@{name='negative';value=-1})){
        $cases.Add(@{name='native-error-'+$errorCase.name;source=[ProcessImageRead]::new([NullString]::Value,'native-error',$errorCase.value,1,$true,$null)})
    }
    foreach($valueCase in @(@{name='null';value=$null},@{name='string';value='true'},@{name='number';value=1},
        @{name='array';value=[object[]]@($true)},@{name='object';value=[pscustomobject]@{exited=$true}},
        @{name='exception';value=[InvalidOperationException]::new('private exit detail')})){
        $initial=[object[]]::new(1);$initial[0]=$valueCase.value
        $after=[object[]]::new(2);$after[0]=$false;$after[1]=$valueCase.value
        $cases.Add(@{name='initial-exit-'+$valueCase.name;code='exit-check-failed';exitValues=$initial;reads=0;budgets=1})
        $cases.Add(@{name='post-exit-'+$valueCase.name;code='exit-check-failed';exitValues=$after;exits=2;budgets=3})
    }
    foreach($budgetCase in @(@{name='zero';value=0},@{name='negative';value=-1},@{name='nan';value=[double]::NaN},
        @{name='positive-infinity';value=[double]::PositiveInfinity},@{name='negative-infinity';value=[double]::NegativeInfinity},
        @{name='null';value=$null},@{name='text';value='not-a-budget'},
        @{name='exception';value=[InvalidOperationException]::new('private budget detail')})){
        $cases.Add(@{name='initial-budget-'+$budgetCase.name;code='image-deadline';budgetValues=@($budgetCase.value);reads=0;exits=0;budgets=1})
        $cases.Add(@{name='post-query-budget-'+$budgetCase.name;budgetValues=@(30,$budgetCase.value)})
        $cases.Add(@{name='post-exit-budget-'+$budgetCase.name;budgetValues=@(30,20,$budgetCase.value);exits=2;budgets=3})
    }
    $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $names=[Collections.Generic.List[string]]::new()
    foreach($case in $cases){
        Assert-Proof ($seen.Add($case.name)) 'Unique preparation exit case.'
        $actual=Invoke-PreparationExitTestObservation $case
        $code=if($actual.observation.Contains('code')){$actual.observation.code}else{$null}
        $expectedCode=if($case.Contains('code')){$case.code}else{'native-error'}
        $reads=if($case.Contains('reads')){$case.reads}else{1}
        $exits=if($case.Contains('exits')){$case.exits}else{1}
        $budgets=if($case.Contains('budgets')){$case.budgets}else{2}
        Assert-Proof ($code -ceq $expectedCode) "$($case.name): diagnostic code."
        Assert-Proof ($actual.calls.reads -eq $reads -and $actual.calls.exits -eq $exits -and
            $actual.calls.budgets -eq $budgets) "$($case.name): callback counts."
        $expectedTrace=if($budgets -eq 3){'budget,exit,image,budget,exit,budget'}
            elseif($reads -eq 1){'budget,exit,image,budget'}
            elseif($exits -eq 1){'budget,exit'}elseif($budgets -eq 1){'budget'}else{''}
        Assert-Proof (($actual.calls.trace -join ',') -ceq $expectedTrace) "$($case.name): callback order."
        if($case.Contains('success') -and $case.success){
            Assert-Proof (!$actual.failure -and $null -ne $actual.result) "$($case.name): unexpected refusal: $($actual.failure)"
            $state=if($code -ceq 'image-observed'){'image-observed'}else{'exited-unobserved'}
            Assert-Proof ($actual.result.state -ceq $state -and $actual.result.attempts -eq $reads) "$($case.name): result."
            if($state -ceq 'exited-unobserved'){
                Assert-Proof ($null -eq $actual.result.path) "$($case.name): fabricated result image."
            }
        }else{
            Assert-Proof ($actual.failure -and $null -eq $actual.result) "$($case.name): failed open."
            Assert-Proof (!$actual.failure.Contains('private ')) "$($case.name): private exception detail."
        }
        if($code -ceq 'exited-after-query-error' -and $null -ne $actual.result){
            $before=if($case.Contains('budgetValues')){$case.budgetValues[0]}else{30}
            $after=if($case.Contains('budgetValues')){$case.budgetValues[2]}else{10}
            Assert-Proof ($actual.observation.nativeError -eq 31 -and $actual.observation.queries -eq 1 -and
                $actual.observation.handleValid -is [bool] -and $actual.observation.handleValid -and
                $actual.observation.remainingBeforeMs -eq $before -and
                $actual.observation.remainingAfterMs -eq $after -and
                $null -eq $actual.observation.returnedChars) 'Post-query exit evidence.'
        }
        if($reads -eq 1 -and $actual.source -is [ProcessImageRead] -and $code -cne 'image-source-failed'){
            Assert-Proof ($actual.observation.nativeError -ceq $actual.source.NativeError -and
                $actual.observation.queries -eq $actual.source.Queries) "$($case.name): native facts changed."
        }
        if($actual.observation.Count -eq 17 -and $code -cnotin @('image-observed','image-mismatch')){
            foreach($field in @('observedPathSha256','observedPathLength','observedPathPreview','previewTruncated')){
                Assert-Proof ($null -eq $actual.observation[$field]) "$($case.name): fabricated observed image."
            }
        }
        if($case.Contains('slot')){
            Assert-Proof (($actual.observation|ConvertTo-Json -Compress) -ceq $actual.slotBefore) 'Consumed slot changed.'
        }
        $names.Add($case.name)
    }
    return $names.ToArray()
}
function Invoke-PreparationExitReportingTests {
    function Image {
        $actual=Invoke-PreparationExitTestObservation @{}
        Assert-Proof (!$actual.failure -and $actual.result.state -ceq 'exited-unobserved') 'Modeled exit diagnostic required.'
        return ,$actual.observation
    }
    function Record($image) {
        New-RTerminalRecord @{exe='C:\expected\tool.exe';hostSha256=('1'*64)} @{imageObservation=$image}
    }
    $cases=[Collections.Generic.List[object]]::new()
    foreach($case in @(
        @{name='real-diagnostic-roundtrip';run={
            $d=Image;$record=Record $d;$bytes=ConvertTo-RPublicationBytes $record
            $roundtrip=ConvertFrom-RJson ([Text.Encoding]::UTF8.GetString($bytes))
            Assert-RImageObservation $roundtrip.imageObservation
            Assert-Proof (!$roundtrip.passed -and $null -eq $roundtrip.image -and
                $roundtrip.imageObservation.code -ceq 'exited-after-query-error' -and
                $roundtrip.imageObservation.nativeError -eq 31 -and $roundtrip.imageObservation.queries -eq 1 -and
                $null -eq $roundtrip.imageObservation.observedPathPreview) 'Exit diagnostic serialization.'
        }},
        @{name='equal-budget-roundtrip';run={
            $d=Image;$d.remainingAfterMs=$d.remainingBeforeMs
            $null=Record $d
        }},
        @{name='terminal-snapshot-preserves-native-error';run={
            $d=Image;$memory=@{terminal=$null;receipt=$null}
            Assert-ProofTestThrows {
                $null=Invoke-RTerminalPublication -Bindings @{exe='host';hostSha256=('1'*64)} `
                    -Observations @{failure='original';imageObservation=$d} `
                    -WriteTerminal {param($Bytes) $memory.terminal=$Bytes.Clone();$d.nativeError=5;$true} `
                    -WriteReceipt {param($Bytes) $memory.receipt=$Bytes.Clone();$true}
            }
            foreach($bytes in @($memory.terminal,$memory.receipt)){
                $r=ConvertFrom-RJson ([Text.Encoding]::UTF8.GetString($bytes))
                Assert-Proof (!$r.passed -and $r.failure -ceq 'original' -and
                    $r.imageObservation.code -ceq 'exited-after-query-error' -and
                    $r.imageObservation.nativeError -eq 31) 'Frozen query error lost.'
            }
            Assert-Proof ($d.nativeError -eq 5) 'Writer mutation fixture did not run.'
        }},
        @{name='diagnostic-alone-cannot-promote';run={
            $memory=@{verifies=0;receipt=$null}
            Assert-ProofTestThrows {
                $null=Invoke-RTerminalPublication -Bindings @{exe='host';hostSha256=('1'*64)} `
                    -Observations @{imageObservation=(Image)} -WriteTerminal {param($Bytes) $true} `
                    -VerifyOutput {$memory.verifies++;throw 'Output verification must not run.'} `
                    -WriteReceipt {param($Bytes) $memory.receipt=$Bytes.Clone();$true}
            }
            $r=ConvertFrom-RJson ([Text.Encoding]::UTF8.GetString($memory.receipt))
            Assert-Proof (!$r.passed -and $memory.verifies -eq 0 -and $null -eq $r.image -and
                $null -eq $r.exitCode -and $null -eq $r.exitConfirmed) 'Diagnostic fabricated process success.'
        }}
    )){$cases.Add($case)}
    foreach($role in @('prepare-cleanup','launch-runner-initial','launch-runner-cleanup','launch-app-retain',
        'launch-app-cleanup','run-self','run-app-initial','desktop-app','supervision-self',
        'supervision-child-initial','supervision-child-cleanup','foreign-process','Prepare-initial')){
        $cases.Add(@{name='forged-role-'+$role;field='role';value=$role})
    }
    foreach($case in @(
        @{name='null-native-error';field='nativeError';value=$null},
        @{name='other-native-error';field='nativeError';value=87},
        @{name='access-denied';field='nativeError';value=5},
        @{name='invalid-handle-error';field='nativeError';value=6},
        @{name='string-native-error';field='nativeError';value='31'},
        @{name='floating-native-error';field='nativeError';value=31.0},
        @{name='zero-queries';field='queries';value=0},
        @{name='multiple-queries';field='queries';value=2},
        @{name='string-queries';field='queries';value='1'},
        @{name='invalid-handle';field='handleValid';value=$false},
        @{name='unknown-handle';field='handleValid';value=$null},
        @{name='string-handle';field='handleValid';value='true'},
        @{name='returned-characters';field='returnedChars';value=1},
        @{name='observed-digest';field='observedPathSha256';value=('1'*64)},
        @{name='observed-length';field='observedPathLength';value=1},
        @{name='observed-preview';field='observedPathPreview';value='x'},
        @{name='empty-observed-preview';field='observedPathPreview';value=''},
        @{name='truncation-false';field='previewTruncated';value=$false},
        @{name='truncation-true';field='previewTruncated';value=$true},
        @{name='increasing-budget';field='remainingAfterMs';value=31},
        @{name='code-case-mismatch';field='code';value='Exited-after-query-error'}
    )){$cases.Add($case)}
    foreach($field in @('remainingBeforeMs','remainingAfterMs')){
        foreach($bad in @(@{name='zero';value=0},@{name='negative';value=-1},@{name='null';value=$null},
            @{name='nan';value=[double]::NaN},@{name='infinity';value=[double]::PositiveInfinity},
            @{name='negative-infinity';value=[double]::NegativeInfinity},@{name='string';value='1'},
            @{name='boolean';value=$true})){
            $cases.Add(@{name=$field+'-'+$bad.name;field=$field;value=$bad.value})
        }
    }
    foreach($field in @('schema','role','method','flags','code','queries','handleValid','nativeError',
        'capacity','returnedChars','expectedPathSha256','observedPathSha256','observedPathLength',
        'observedPathPreview','previewTruncated','remainingBeforeMs','remainingAfterMs')){
        $cases.Add(@{name='missing-'+$field;remove=$field})
    }
    $cases.Add(@{name='extra-exit-field';field='exitConfirmed';value=$true})
    $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $names=[Collections.Generic.List[string]]::new()
    foreach($case in $cases){
        Assert-Proof ($seen.Add($case.name)) 'Unique preparation exit reporting case.'
        if($case.Contains('run')){& $case.run}
        else{
            $d=Image
            if($case.Contains('remove')){$null=$d.Remove($case.remove)}else{$d[$case.field]=$case.value}
            $failed=$false
            try{$null=Record $d}catch{
                if($_.Exception -is [Management.Automation.CommandNotFoundException]){throw}
                $failed=$true
            }
            Assert-Proof $failed "$($case.name): forged exit diagnostic accepted."
        }
        $names.Add($case.name)
    }
    return $names.ToArray()
}
function Assert-PreparationExitCaseNames([string[]]$Names,[int]$Count,[string]$Sha256) {
    Assert-Proof ($Names.Count -eq $Count) 'Preparation exit case count.'
    $bytes=[Text.UTF8Encoding]::new($false).GetBytes(($Names -join "`n"))
    $actual=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
    Assert-Proof ($actual -ceq $Sha256) 'Preparation exit case names/order.'
}
function Invoke-ProofPureCase($Case) {
    if($Case.name -cin @('inventory-json-depth-bound','receipt-completion-depth-bound')){
        $null=& $Case.run 3>&1
    }else{
        & $Case.run
    }
}
function Assert-TestReceiptRetention([double]$External=100,[double]$Execution=105,[double]$Observed=105,[double]$ReceiptStart=105.25,[double]$ReceiptNow=105.5,[string]$FailureAt='',[switch]$Unknown,[switch]$FailedTerminal) {
    $memory=@{events=[Collections.Generic.List[string]]::new();terminal=$null;receipt=$null;error=$null;receiptAttempts=0}
    $observations=if($Unknown){@{}}else{
        @{pid=1;startTicks=1L;image='host';exitCode=0;exitConfirmed=$true;outputConfirmed=$true
          timedOut=$false;killAttempts=0;environmentCleared=$true;stdinClosed=$true
          drainingBeforeImageObservation=$true;failure=$null}
    }
    if($FailedTerminal){$observations.failure='Original process failure.';$observations.timedOut=$true;$observations.killAttempts=1;$observations.exitConfirmed=$null;$observations.exitCode=$null}
    $arguments=@{
        Bindings=@{schema='receipt-retention-test';exe='host';hostSha256=$sha}
        Observations=$observations
        WriteTerminal={
            param([byte[]]$Bytes)
            $memory.events.Add('terminal')
            Assert-RTerminalRetentionAdmission $Observed (Get-RTerminalRetentionDeadline $Observed) $Observed $Observed $Bytes.Length
            $memory.terminal=$Bytes.Clone()
            return $true
        }
        VerifyOutput={
            $memory.events.Add('verify')
            Assert-R ($null -ne $memory.terminal) 'Optional work preceded retained facts.'
            Assert-ROptionalPublicationAdmission $Execution $External
            throw 'Injected optional verifier failure.'
        }
        WriteInventory={param([byte[]]$Bytes) throw 'Failed verification must not publish an inventory.'}
        CompleteReceipt={
            param($Receipt)
            $memory.events.Add('complete')
            Assert-ROptionalPublicationAdmission $Execution $External
            throw 'Injected optional completion failure.'
        }
        WriteReceipt={
            param([byte[]]$Bytes)
            $memory.events.Add('receipt')
            $memory.receiptAttempts++
            Assert-R ($memory.receiptAttempts -eq 1) 'Receipt retry is prohibited.'
            $deadline=Get-RReceiptRetentionDeadline $Observed $ReceiptStart
            Assert-RReceiptRetentionAdmission $Observed $ReceiptStart $deadline $ReceiptStart $ReceiptNow $Bytes.Length
            $memory.receipt=$Bytes.Clone()
            if($FailureAt -ceq 'overrun'){Assert-RReceiptRetentionAdmission $Observed $ReceiptStart $deadline $ReceiptNow $deadline $Bytes.Length}
            if($FailureAt -ceq 'throw'){throw 'Injected receipt acknowledgment failure.'}
            if($FailureAt -ceq 'missing'){return}
            if($FailureAt -ceq 'extra'){return @($true,$true)}
            return ($FailureAt -cne 'false')
        }
    }
    try{$null=Invoke-RTerminalPublication @arguments}catch{$memory.error=[string]$_.Exception.Message}
    $expected=if($Unknown -or $FailedTerminal){@('terminal','complete','receipt')}else{@('terminal','verify','complete','receipt')}
    Assert-REqual $memory.events.ToArray() $expected 'retained facts, optional attempts, final failure receipt ordering'
    Assert-R ($memory.receiptAttempts -eq 1 -and $null -ne $memory.error -and $null -ne $memory.terminal -and $null -ne $memory.receipt) 'Failure receipt retention/throw required.'
    $terminal=Read-TestPublication $memory.terminal;$final=Read-TestPublication $memory.receipt
    Assert-R (!$terminal.passed -and $terminal.terminalEvidenceOnly -and !$final.passed -and $final.terminalPublicationConfirmed -and !$final.outputVerificationConfirmed -and !$final.inventoryPublicationConfirmed -and $null -ne $final.receiptCompletionFailure) 'False verification/completion success.'
    Assert-R ($final.terminalEvidenceSha256 -ceq [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($memory.terminal)).ToLowerInvariant()) 'Retained terminal bytes changed.'
    foreach($key in $observations.Keys){if($key -cne 'failure'){Assert-REqual $terminal[$key] $final[$key] 'observed receipt facts unchanged'}}
    if($FailedTerminal){Assert-R ($final.failure -ceq 'Original process failure.') 'Optional exhaustion replaced original failure.'}
    if($Unknown){foreach($key in @('pid','startTicks','exitCode','exitConfirmed','outputConfirmed','timedOut')){Assert-R ($null -eq $final[$key]) 'Unknown no-child facts reconstructed.'}}
    if($Execution -ge $External+5){Assert-R ($final.receiptCompletionFailure -ceq 'Optional publication deadline exhausted.') 'Optional exhaustion missing from final receipt.'}
    if($FailureAt -cin @('false','missing','extra')){Assert-R ($memory.error -ceq 'Durable final receipt publication was not acknowledged.') 'Invalid acknowledgment accepted.'}
    if($FailureAt -ceq 'throw'){Assert-R ($memory.error -ceq 'Injected receipt acknowledgment failure.') 'Thrown acknowledgment replaced.'}
    if($FailureAt -ceq 'overrun'){Assert-R ($memory.error -ceq 'Terminal retention deadline/backward time.') 'Reporting overrun accepted.'}
}
function Get-LaterRestageCases {
    @(
        @{name='receipt-retention-deadline-zero';reject=$false;run={Assert-R ((Get-RReceiptRetentionDeadline 0 0) -eq 5) 'No-child receipt deadline.'}}
        @{name='receipt-retention-deadline-pure-105';reject=$false;run={Assert-R ((Get-RReceiptRetentionDeadline 105 105) -eq 110) 'Pure failure receipt deadline.'}}
        @{name='receipt-retention-deadline-compatibility-315';reject=$false;run={Assert-R ((Get-RReceiptRetentionDeadline 315 315) -eq 320) 'Compatibility failure receipt deadline.'}}
        @{name='receipt-retention-negative-terminal-time';reject=$true;run={Get-RReceiptRetentionDeadline -1 0}}
        @{name='receipt-retention-nan-terminal-time';reject=$true;run={Get-RReceiptRetentionDeadline ([double]::NaN) 0}}
        @{name='receipt-retention-infinite-terminal-time';reject=$true;run={Get-RReceiptRetentionDeadline ([double]::PositiveInfinity) 0}}
        @{name='receipt-retention-backward-start';reject=$true;run={Get-RReceiptRetentionDeadline 105 104}}
        @{name='receipt-retention-nan-start';reject=$true;run={Get-RReceiptRetentionDeadline 0 ([double]::NaN)}}
        @{name='receipt-retention-infinite-start';reject=$true;run={Get-RReceiptRetentionDeadline 0 ([double]::PositiveInfinity)}}
        @{name='receipt-retention-cutoff-equality';reject=$true;run={Assert-RReceiptRetentionAdmission 105 106 111 110 111 1}}
        @{name='receipt-retention-before-cutoff';reject=$false;run={Assert-RReceiptRetentionAdmission 105 106 111 110 110.999 1048576}}
        @{name='receipt-retention-deadline-extension';reject=$true;run={Assert-RReceiptRetentionAdmission 105 106 112 106 107 1}}
        @{name='receipt-retention-negative-bytes';reject=$true;run={Assert-RReceiptRetentionAdmission 0 0 5 0 1 -1}}
        @{name='receipt-retention-oversize-bytes';reject=$true;run={Assert-RReceiptRetentionAdmission 0 0 5 0 1 1048577}}
        @{name='receipt-retention-backward-now';reject=$true;run={Assert-RReceiptRetentionAdmission 105 106 111 108 107 1}}
        @{name='receipt-retention-nan-now';reject=$true;run={Assert-RReceiptRetentionAdmission 0 0 5 0 ([double]::NaN) 1}}
        @{name='receipt-retention-expired-pure-105';reject=$false;run={Assert-TestReceiptRetention}}
        @{name='receipt-retention-expired-compatibility-315';reject=$false;run={Assert-TestReceiptRetention -External 310 -Execution 315 -Observed 315 -ReceiptStart 315.25 -ReceiptNow 315.5}}
        @{name='receipt-retention-after-optional-consumed-terminal-window';reject=$false;run={Assert-TestReceiptRetention -Observed 10 -ReceiptStart 120 -ReceiptNow 120.25}}
        @{name='receipt-retention-no-child-unstarted-execution';reject=$false;run={Assert-TestReceiptRetention -Execution 0 -Observed 12 -ReceiptStart 12.25 -ReceiptNow 12.5 -Unknown}}
        @{name='receipt-retention-original-process-failure-preserved';reject=$false;run={Assert-TestReceiptRetention -FailedTerminal}}
        @{name='receipt-retention-false-ack';reject=$false;run={Assert-TestReceiptRetention -FailureAt 'false'}}
        @{name='receipt-retention-missing-ack';reject=$false;run={Assert-TestReceiptRetention -FailureAt 'missing'}}
        @{name='receipt-retention-extra-ack';reject=$false;run={Assert-TestReceiptRetention -FailureAt 'extra'}}
        @{name='receipt-retention-thrown-ack';reject=$false;run={Assert-TestReceiptRetention -FailureAt 'throw'}}
        @{name='receipt-retention-io-overrun-after-bytes';reject=$false;run={Assert-TestReceiptRetention -FailureAt 'overrun'}}
        @{name='external-deadline-zero';reject=$false;run={Assert-R (!(Test-RExternalDeadlineExceeded 0 310)) 'Zero elapsed deadline.'}}
        @{name='external-deadline-pure-before';reject=$false;run={Assert-R (!(Test-RExternalDeadlineExceeded 99.999 100)) 'Pure before deadline.'}}
        @{name='external-deadline-pure-equal';reject=$false;run={Assert-R (Test-RExternalDeadlineExceeded 100 100) 'Pure equality deadline.'}}
        @{name='external-deadline-stage-before';reject=$false;run={Assert-R (!(Test-RExternalDeadlineExceeded 309.999 310)) 'Stage before deadline.'}}
        @{name='external-deadline-stage-equal';reject=$false;run={Assert-R (Test-RExternalDeadlineExceeded 310 310) 'Stage equality deadline.'}}
        @{name='external-deadline-stage-after';reject=$false;run={Assert-R (Test-RExternalDeadlineExceeded 310.001 310) 'Stage after deadline.'}}
        @{name='external-deadline-negative';reject=$true;run={Test-RExternalDeadlineExceeded -1 310}}
        @{name='external-deadline-nan';reject=$true;run={Test-RExternalDeadlineExceeded ([double]::NaN) 310}}
        @{name='external-deadline-infinite';reject=$true;run={Test-RExternalDeadlineExceeded ([double]::PositiveInfinity) 310}}
        @{name='external-deadline-invalid-bound';reject=$true;run={Test-RExternalDeadlineExceeded 0 311}}
    )
}
function Invoke-LaterProductionTests([string]$Root) {
    . (Get-PinnedProofLibrary -Library NonCopySupervisor)
    . (Get-PinnedProofLibrary -Library RestageSupervisor)
    $cases=0
    foreach($writer in @('Write-NonCopyReceipt','Write-RestageReceipt')){
        foreach($previous in @(-1,[double]::NaN,[double]::PositiveInfinity,106)){
            $path=Join-Path $Root ($writer+'-clock-bridge-'+$cases+'.json')
            $window=@{terminalPrevious=[double]$previous;start=105.0;deadline=110.0;previous=105.0;attempted=$false}
            Assert-ProofTestThrows {
                & $writer -Path $path -Bytes ([byte[]]@(123,125)) -Window $window -ReadClock {105} -Phase Final -ExternalSeconds 100
            }
            Assert-Proof (![IO.File]::Exists($path)) 'Invalid clock bridge allocated a final receipt.'
            $cases++
        }
        $path=Join-Path $Root ($writer+'-after-consumed-terminal.json')
        $window=@{terminalPrevious=10.0;start=120.0;deadline=125.0;previous=120.0;attempted=$false}
        $ack=& $writer -Path $path -Bytes ([byte[]]@(123,125)) -Window $window -ReadClock {120.25} -Phase Final -ExternalSeconds 100
        Assert-Proof ($ack -is [bool] -and $ack -and [IO.File]::ReadAllText($path) -ceq '{}') 'Optional work consumed failure-report retention.'
        $cases++
    }
    foreach($name in @('inventory-json-depth-bound','receipt-completion-depth-bound')){
        $output=@(Invoke-ProofPureCase @{name=$name;run={Write-Warning 'Deliberate depth fixture'}} 3>&1)
        Assert-Proof ($output.Count -eq 0) 'Expected case-local warning leaked.'
        $cases++
    }
    $output=@(Invoke-ProofPureCase @{name='unexpected-warning';run={Write-Warning 'Unexpected warning fixture'}} 3>&1)
    Assert-Proof ($output.Count -eq 1 -and $output[0] -is [Management.Automation.WarningRecord] -and
        $output[0].Message -ceq 'Unexpected warning fixture') 'Unexpected warnings were suppressed.'
    $cases++
    return $cases
}
function Invoke-ProofBooleanTests {
    . (Join-Path $PSScriptRoot 'Configuration.ps1')
    Assert-ProofBooleanFields @{yes=$true;no=$false} @('yes') @('no')
    $cases=1
    foreach($value in @('false','true',0,1,$null,@())){
        foreach($positive in @($true,$false)){
            $errorMessage=$null
            try{
                if($positive){Assert-ProofBooleanFields @{yes=$value;no=$false} @('yes') @('no')}
                else{Assert-ProofBooleanFields @{yes=$true;no=$value} @('yes') @('no')}
            }catch{$errorMessage=$_.Exception.Message}
            Assert-Proof ($errorMessage -cmatch '^Required (true|false) Boolean: (yes|no)$') 'Wrong Boolean refusal or missing implementation.'
        }
        $cases++
    }
    return $cases
}
function Write-ProofTestBytes([string]$Path,[byte[]]$Bytes) {
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($Path))
    $stream=[IO.File]::Open($Path,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
    try {$stream.Write($Bytes);$stream.Flush($true)}finally{$stream.Dispose()}
    return @{path=$Path;bytes=$Bytes.Length;sha256=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($Bytes)).ToLowerInvariant()}
}
function Write-ProofTestJson([string]$Path,$Value) {
    Write-ProofTestBytes $Path ([Text.UTF8Encoding]::new($false).GetBytes((ConvertTo-Json -InputObject $Value -Depth 32 -Compress)))
}
function Get-ProofTestCompileReferences {
    $names=@(
        'Microsoft.Win32.Primitives.dll','System.Text.RegularExpressions.dll','System.Runtime.dll',
        'System.Runtime.InteropServices.dll','System.Runtime.Extensions.dll','System.ComponentModel.Primitives.dll',
        'System.ComponentModel.dll','System.ComponentModel.TypeConverter.dll','System.Collections.dll',
        'System.Collections.Concurrent.dll','System.Diagnostics.Process.dll','System.Diagnostics.Debug.dll',
        'System.Diagnostics.TraceSource.dll','System.Drawing.Primitives.dll','System.IO.dll',
        'System.IO.FileSystem.dll','System.Security.Cryptography.dll','System.Text.Encoding.Extensions.dll',
        'System.Threading.dll','System.Threading.Thread.dll','System.Threading.ThreadPool.dll',
        'System.Threading.Tasks.dll','System.Memory.dll','System.ObjectModel.dll','System.Console.dll','System.Linq.dll'
    )
    foreach($name in $names){
        $path=Join-Path (Join-Path $PSHOME 'ref') $name
        Assert-ProofPath $path -Existing
        $file=[IO.FileInfo]::new($path)
        Assert-Proof ($file.Length -gt 0 -and $file.Length -le 268435456) 'Test compiler reference length.'
        $row=@{path=$path;bytes=$file.Length;sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()}
        $null=Read-ProofPinnedBytes $row 268435456
        $row
    }
}
function New-ProofRestageFixture([string]$Root,[ValidateSet('None','PurePassed','CompilePassed','BindingAccepted','OuterSource','HistoryExtra','HistoryMissing')][string]$InvalidType='None',[object[]]$CompileReferences) {
    if($PSBoundParameters.ContainsKey('CompileReferences')){
        Assert-Proof ($null -ne $CompileReferences -and $CompileReferences.Count -eq 26) 'Test compiler reference inventory.'
        $expected=@(Get-ProofTestCompileReferences)
        for($i=0;$i -lt 26;$i++){
            $row=$CompileReferences[$i]
            Assert-ProofPin $row
            Assert-Proof ($row.path -ceq $expected[$i].path -and $row.bytes -eq $expected[$i].bytes -and
                $row.sha256 -ceq $expected[$i].sha256) 'Test compiler reference pin.'
            $null=Read-ProofPinnedBytes $row 268435456
        }
    }
    $prior='1'*32;$head='2'*40;$tree='3'*40
    $b=Join-Path $Root 'build';$d=Join-Path $Root 'desktop';$r=Join-Path $Root 'history'
    $donor=Join-Path $d ('inputs-'+$prior)
    $evidence=Join-Path $Root 'evidence';$output=Join-Path $Root 'output'
    foreach($path in @($b,$d,$r,$donor,$evidence,$output)){[void][IO.Directory]::CreateDirectory($path)}
    $hostPin=Write-ProofTestBytes (Join-Path $Root 'host\pwsh.exe') ([Text.Encoding]::UTF8.GetBytes('synthetic data; never executable'))
    $apps=@(for($i=0;$i -lt 594;$i++){
        $name="file-$i.dat";$pin=Write-ProofTestBytes (Join-Path $donor ('app\'+$name)) ([byte[]]@($i%256))
        @{path=$name;bytes=$pin.bytes;sha256=$pin.sha256}
    })
    $literal='<root><item><key>Language</key><value>en</value></item><item><key>func_auto_update</key><value>0</value></item></root>'
    $configPin=Write-ProofTestBytes (Join-Path $donor 'app\config\config.xml') ([Text.Encoding]::UTF8.GetBytes($literal))
    $configRow=@{path='config\config.xml';bytes=$configPin.bytes;sha256=$configPin.sha256}
    $runtime=@(for($i=0;$i -lt 7;$i++){
        $name="runtime-$i.dat";$pin=Write-ProofTestBytes (Join-Path $donor ('support\'+$name)) ([byte[]]@(7))
        @{path=$name;bytes=$pin.bytes;sha256=$pin.sha256}
    })
    $runtimePins=@(foreach($row in $runtime){@{path=(Join-Path ([IO.Path]::GetDirectoryName($hostPin.path)) $row.path);bytes=$row.bytes;sha256=$row.sha256}})
    $fixtures=@(foreach($name in @('zipdb-invalid.zip','zipdb-proof.gba','zipdb-valid.zip')){
        $pin=Write-ProofTestBytes (Join-Path $donor ("desktop-proof-$prior\"+$name)) ([Text.Encoding]::UTF8.GetBytes('synthetic bytes; not a ROM/archive'))
        @{path=$name;bytes=$pin.bytes;sha256=$pin.sha256}
    })
    $refs=@(for($i=0;$i -lt 26;$i++){@{path=(Join-Path $Root ("host\ref-$i.dat"));bytes=1;sha256=('4'*64)}})
    if($PSBoundParameters.ContainsKey('CompileReferences')){$refs=@($CompileReferences | ForEach-Object {$_.Clone()})}
    $manifest=[ordered]@{schema='windows-desktop-bounded-input-v1';applicationSource=$head;runId=$prior;syntheticRomFormat='fe8u-synthetic-huffman-v1';
        powershellHost=@{path=$hostPin.path;sha256=$hostPin.sha256};appFiles=@($apps)+@($configRow);fixtures=$fixtures;
        runtimeAssemblies=$runtime;compileReferences=$refs;installedFiles=@(
            @{path='proof\PATCH_offline.txt';bytes=1;sha256=('5'*64)},@{path='proof\payload.bin';bytes=1;sha256=('6'*64)})}
    $manifestPin=Write-ProofTestJson (Join-Path $donor 'input-manifest.json') $manifest
    $metadata=@{tools=@(1..13|ForEach-Object {$hostPin});runtimeAssemblies=$runtimePins;compileReferences=$refs;assets=@(1..15|ForEach-Object {$configRow})}
    $metadataPin=Write-ProofTestJson (Join-Path $b 'metadata.json') $metadata
    $historyEvidence=@{inputManifest=$manifestPin;metadata=$metadataPin}
    foreach($source in @(@{name='bSource';root=$b;count=7},@{name='dSource';root=$d;count=10})){
        $rows=@(for($i=0;$i -lt $source.count;$i++){
            $name="source-$i.txt";$pin=Write-ProofTestBytes (Join-Path $source.root $name) ([byte[]]@(1))
            @{path=$name;bytes=$pin.bytes;sha256=$pin.sha256}
        })
        $value=@{files=$rows}
        if($source.name -ceq 'dSource'){$value.externalSourceDependencies=@($hostPin,$hostPin,$hostPin)}
        $historyEvidence[$source.name]=Write-ProofTestJson (Join-Path $source.root 'source-manifest.json') $value
    }
    $preflightRow=@{path=$hostPin.path;bytes=$hostPin.bytes;sha256=$hostPin.sha256;nonReparseAncestry=$true}
    $historyEvidence.preflight=Write-ProofTestJson (Join-Path $r 'preflight.json') @{runId=$prior;passed=$true;rows=@(1..99|ForEach-Object {$preflightRow});installedPins=46}
    $trx='<?xml version="1.0" encoding="utf-8"?><TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><ResultSummary><Counters total="4" executed="4" passed="4" failed="0" /></ResultSummary></TestRun>'
    $tests=@(foreach($configuration in @('Debug','Release')){
        $name=$configuration+'.trx';$pin=Write-ProofTestBytes (Join-Path $b ('build-attempt\'+$name)) ([Text.Encoding]::UTF8.GetBytes(([string][char]0xfeff)+$trx))
        @{configuration=$configuration;passedCases=4;trx=@{path=$name;bytes=$pin.bytes;sha256=$pin.sha256}}
    })
    $outputs=@{applicationSource=$head;worktreeHead=$head;appFiles=@($apps)+@(
        @{path='excluded-a.dat';bytes=0;sha256=('0'*64)},@{path='excluded-b.dat';bytes=0;sha256=('0'*64)});
        generatorFiles=@(1..8|ForEach-Object {$configRow});projectionFiles=$apps;tests=$tests}
    $historyEvidence.outputs=Write-ProofTestJson (Join-Path $b 'outputs.json') $outputs
    $validationRoot=Join-Path $b ('validate-'+$prior)
    $validationRows=@(foreach($name in @('Pure.json','Compile.json','Policy.compile-only.dll','Desktop.compile-only.dll','a.dat','b.dat','c.dat','d.dat','e.dat','f.dat','g.dat')){
        $value=switch($name){
            'Pure.json' {@{passed=$true;cases=522;bindingCases=22;nativeCalls=$false;appLaunched=$false}}
            'Compile.json' {@{passed=$true;nativeCalls=$false;appLaunched=$false;runtimeBindings=@(1..7|ForEach-Object {@{accepted=$true;actualSha256=('6'*64);expectedSha256=('6'*64);actualIdentity='synthetic';expectedIdentity='synthetic'}})}}
            default {@{synthetic=$true}}
        }
        if($name -ceq 'Pure.json' -and $InvalidType -ceq 'PurePassed'){$value.passed='false'}
        if($name -ceq 'Compile.json' -and $InvalidType -ceq 'CompilePassed'){$value.passed='false'}
        if($name -ceq 'Compile.json' -and $InvalidType -ceq 'BindingAccepted'){$value.runtimeBindings[0].accepted='false'}
        $pin=Write-ProofTestJson (Join-Path $validationRoot $name) $value
        @{path=$name;bytes=$pin.bytes;sha256=$pin.sha256}
    })
    $gate='https://github.com/laqieer/FEBuilderGBA/issues/1#issuecomment-1'
    $grant='https://github.com/laqieer/FEBuilderGBA/issues/1#issuecomment-2'
    $stages=[Collections.Generic.List[object]]::new();$configuredReceipts=[Collections.Generic.List[object]]::new()
    foreach($stage in @('Validate','Build','Inputs')){
        $binding=@{stage=$stage;runId=$prior;sourceManifestSha256=$historyEvidence.bSource.sha256;helperSourceManifestSha256=$historyEvidence.dSource.sha256;
            toolMetadataSha256=$metadataPin.sha256;sourceGateReference=$gate;authorizationReference=$grant;head=$head;tree=$tree}
        $receipt=$binding.Clone();$receipt.Remove('head');$receipt.Remove('tree')
        $receipt.schema='windows-desktop-preparation-receipt-v1';$receipt.passed=$true;$receipt.failure=$null
        $receipt.before=[ordered]@{head=$head;tree=$tree;clean=$true};$receipt.after=[ordered]@{head=$head;tree=$tree;clean=$true}
        $receipt.processes=@(1..(@{Validate=28;Build=32;Inputs=27}[$stage])|ForEach-Object {
            @{passed=$true;exitCode=0;timedOut=$false;pid=1;startTicks=1L;exe=$hostPin.path;imageObservation=@{path=$hostPin.path}}
        })
        if($stage -ceq 'Validate'){$receipt.processImageCases=10;$receipt.validationFiles=$validationRows}
        if($stage -ceq 'Build'){$receipt.outputManifestSha256=$historyEvidence.outputs.sha256}
        if($stage -ceq 'Inputs'){
            $provenanceRows=@(foreach($row in $apps){$copy=$row.Clone();$copy.source=Join-Path $b ('publish\'+$row.path);$copy})
            $configProof=$configRow.Clone();$configProof.source='reviewed prepare.ps1 literal; fresh owned projection only'
            $historyEvidence.projection=Write-ProofTestJson (Join-Path $r 'projection.json') @{
                buildReceiptSha256=$historyEvidence.build.sha256;validationReceiptSha256=$historyEvidence.validate.sha256;
                sourceManifestSha256=$historyEvidence.bSource.sha256;helperSourceManifestSha256=$historyEvidence.dSource.sha256;
                worktreeHead=$head;applicationSource=$head;files=@($provenanceRows)+@($configProof)}
            $receipt.inputManifestSha256=$manifestPin.sha256;$receipt.projectionProvenanceSha256=$historyEvidence.projection.sha256
        }
        $label=$stage.ToLowerInvariant()
        $historyEvidence[$label]=Write-ProofTestJson (Join-Path $r ($label+'.json')) $receipt
        $binding.pin=$historyEvidence[$label];$configuredReceipts.Add($binding)
        $auth=Write-ProofTestBytes (Join-Path $r ($label+'-authorization.txt')) ([Text.Encoding]::UTF8.GetBytes('Synthetic caller approval metadata. Not an authorization.'))
        $grantPin=Write-ProofTestJson (Join-Path $r ($label+'-grant.json')) @{
            stage=$stage;runId=$prior;authorizationReference=$grant;authorizationSha256=$auth.sha256;sourceGateReference=$gate}
        $outer=$receipt.Clone();$outer.Remove('before');$outer.Remove('after');$outer.Remove('processes')
        foreach($key in @('exitConfirmed','outputConfirmed','environmentCleared','stdinClosed','drainingBeforeImageObservation','sourceFreezesReverified')){$outer[$key]=$true}
        if($InvalidType -ceq 'OuterSource'){$outer.sourceFreezesReverified='false'}
        $outer.timedOut=$false;$outer.killAttempts=0;$outer.exitCode=0;$outer.exe=$hostPin.path;$outer.image=$hostPin.path
        $outer.hostSha256=$hostPin.sha256;$outer.pid=1;$outer.startTicks=1L;$outer.stageReceiptPath=$historyEvidence[$label].path
        $outer.stageReceiptSha256=$historyEvidence[$label].sha256;$outer.authorizationSha256=$auth.sha256
        if($stage -ceq 'Validate'){$outer.bindingCases=22;$outer.pureCases=522;$outer.runtimeBindingsAccepted=7;$outer.outputOnlyDlls=2}
        $outerPin=Write-ProofTestJson (Join-Path $r ($label+'-outer.json')) $outer
        $stages.Add(@{stage=$stage;receiptPath=$historyEvidence[$label].path;receiptSha256=$historyEvidence[$label].sha256;
            authorizationReference=$grant;authorizationPath=$auth.path;authorizationSha256=$auth.sha256;
            grantReceiptPath=$grantPin.path;grantReceiptSha256=$grantPin.sha256;outerReceiptPath=$outerPin.path;outerReceiptSha256=$outerPin.sha256})
    }
    $historyEvidence.handoff=Write-ProofTestJson (Join-Path $r 'handoff.json') @{runId=$prior;stages=$stages.ToArray()}
    $historyEvidence.refused=Write-ProofTestJson (Join-Path $r 'refused.json') @{
        run_id=$prior;passed=$false;input_manifest_sha256=$manifestPin.sha256;source_manifest_sha256=$historyEvidence.dSource.sha256;readiness=@{Ready=$false}}
    $history=[ordered]@{schema='windows-desktop-restage-source-v1';status='synthetic-history';root=$r;
        planReference=$gate;planGateReference=$grant;planBoardSha256=('0'*64);priorId=$prior;
        applicationHead=$head;applicationTree=$tree;priorSourceGate=$gate;files=@();evidence=$historyEvidence;
        pureCaseNames=@('synthetic-history');limits=@{stageSeconds=300};externalClosureRule='Synthetic evidence only; never executed.'}
    if($InvalidType -ceq 'HistoryExtra'){$history.unexpected=$true}
    if($InvalidType -ceq 'HistoryMissing'){$history.Remove('status')}
    $historyPin=Write-ProofTestJson (Join-Path $r 'history.json') $history
    $sourceRows=@(foreach($name in @('Desktop.cs','Policy.cs','Policy.Tests.cs','Readiness.cs','RuntimeBinding.ps1','RuntimeBinding.Tests.ps1',
        'ProcessImage.ps1','ProcessImage.Tests.ps1','prepare.ps1','validate-helper.ps1','run.ps1','launch.ps1','test-pure.ps1',
        'Configuration.ps1','Configuration.Tests.ps1','restage\RestagePolicy.ps1','restage\restage.ps1','restage\test-pure.ps1',
        'supervision\NonCopySupervisor.ps1','supervision\RestageSupervisor.ps1')){
        $bytes=[IO.File]::ReadAllBytes((Join-Path $PSScriptRoot $name))
        @{path=$name;bytes=$bytes.Length;sha256=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()}
    })
    $sourcePin=Write-ProofTestJson (Join-Path $Root 'source.json') @{files=$sourceRows}
    $total=[long]((@($manifest.appFiles)+@($runtime)+@($fixtures)|Measure-Object bytes -Sum).Sum)
    $configuration=@{schema='offline-patch-import-proof-v1';outputRoot=$output;evidenceRoot=$evidence;applicationSource=$head;host=$hostPin;sourceManifest=$sourcePin;
        approval=@{sourceGateReference=$gate;authorizationReference=$grant;authorizationSha256=('7'*64)};
        machine=@{systemRoot=(Join-Path $Root 'system');dotnetRoot=(Join-Path $Root 'dotnet');gitPath=(Join-Path $Root 'git.exe');
            programFiles=(Join-Path $Root 'program-files');programFilesX86=(Join-Path $Root 'program-files-x86')};
        preparation=$null;restage=@{priorId=$prior;donorRoot=$donor;manifest=$manifestPin;metadata=$metadataPin;history=$historyPin;
            receipts=$configuredReceipts.ToArray();expected=@{payload=$manifestPin;files=605;bytes=$total}}}
    $configurationPin=Write-ProofTestJson (Join-Path $Root 'configuration.json') $configuration
    return @{config=$configuration;pin=$configurationPin;manifest=$manifest}
}
function Invoke-CompilerReferenceFixtureTests([string]$Root) {
    if(!$IsWindows){return 0}
    $cases=0
    $references=@(Get-ProofTestCompileReferences)
    Assert-Proof ($references.Count -eq 26) 'Real reference fixture count.';$cases++
    $plain=New-ProofRestageFixture (Join-Path $Root 'reference-default')
    Assert-Proof ($plain.manifest.compileReferences.Count -eq 26 -and
        @($plain.manifest.compileReferences | Where-Object {$_.bytes -ne 1 -or $_.path -notmatch 'ref-[0-9]+\.dat$'}).Count -eq 0) 'Default reference fixtures changed.';$cases++
    $real=New-ProofRestageFixture (Join-Path $Root 'reference-real') -CompileReferences $references
    $config=Read-ProofPinnedJson $real.pin
    $manifest=Read-ProofPinnedJson $config.restage.manifest
    $metadata=Read-ProofPinnedJson $config.restage.metadata
    foreach($rows in @(@($manifest.compileReferences),@($metadata.compileReferences))){
        Assert-Proof ($rows.Count -eq 26) 'Derived reference fixture count.'
        for($i=0;$i -lt 26;$i++){
            Assert-Proof ($rows[$i].path -ceq $references[$i].path -and $rows[$i].sha256 -ceq $references[$i].sha256 -and
                $rows[$i].bytes -eq $references[$i].bytes) 'Derived reference pin changed.'
            $null=Read-ProofPinnedBytes $rows[$i] 268435456
        }
        $cases++
    }
    $history=Read-ProofPinnedJson $config.restage.history
    Assert-Proof ($history.evidence.inputManifest.sha256 -ceq $config.restage.manifest.sha256 -and
        $history.evidence.metadata.sha256 -ceq $config.restage.metadata.sha256) 'Reference history was not derived coherently.';$cases++
    foreach($receipt in $config.restage.receipts){
        $value=Read-ProofPinnedJson $receipt.pin
        Assert-Proof ($value.toolMetadataSha256 -ceq $config.restage.metadata.sha256) 'Stage reference metadata mismatch.'
    };$cases++
    $missing=Join-Path $Root 'reference-missing'
    Assert-ProofTestThrows {New-ProofRestageFixture $missing -CompileReferences @($references | Select-Object -First 25)}
    Assert-Proof (![IO.Directory]::Exists($missing)) 'Missing references allocated a fixture.';$cases++
    $changed=@($references | ForEach-Object {$_.Clone()});$changed[0].sha256='0'*64
    $invalid=Join-Path $Root 'reference-invalid'
    Assert-ProofTestThrows {New-ProofRestageFixture $invalid -CompileReferences $changed}
    Assert-Proof (![IO.Directory]::Exists($invalid)) 'Invalid reference allocated a fixture.';$cases++
    return $cases
}
function Invoke-ReportingWriterTests([string]$Root) {
    . (Get-PinnedProofLibrary -Library NonCopySupervisor)
    . (Get-PinnedProofLibrary -Library RestageSupervisor)
    $cases=0
    foreach($writer in @('Write-NonCopyReceipt','Write-RestageReceipt')) {
        foreach($external in @(100,310)){
            foreach($failureAt in @('verify','inventory','completion')){
                $prefix=Join-Path $Root ($writer+'-orchestration-'+$external+'-'+$failureAt)
                $testTime=@{now=10.0}
                $terminalWindow=@{terminalPrevious=0.0;start=10.0;deadline=15.0;previous=10.0;attempted=$false}
                $observations=@{pid=1;startTicks=1L;image='synthetic';exitCode=0;exitConfirmed=$true;outputConfirmed=$true;timedOut=$false;
                    killAttempts=0;environmentCleared=$true;stdinClosed=$true;drainingBeforeImageObservation=$true;failure=$null}
                Assert-ProofTestThrows {
                    $null=Invoke-RTerminalPublication -Bindings @{exe='synthetic';hostSha256=('1'*64)} -Observations $observations -WriteTerminal {
                        param($Bytes)
                        & $writer -Path ($prefix+'-terminal.json') -Bytes $Bytes -Window $terminalWindow -ReadClock {$testTime.now} -Phase Terminal -ExternalSeconds $external
                    } -VerifyOutput {
                        if($failureAt -ceq 'verify'){$testTime.now=$external+5;Assert-ROptionalPublicationAdmission $testTime.now $external}
                        @{passed=$true;rows=@();details=@{}}
                    } -WriteInventory {
                        param($Bytes)
                        $testTime.reads=0
                        Write-ProofInventory ($prefix+'-inventory.json') $Bytes {
                            $testTime.reads++
                            if($failureAt -ceq 'inventory' -and $testTime.reads -eq 2){$testTime.now=$external+5}
                            $testTime.now
                        } $external
                    } -CompleteReceipt {
                        param($Receipt)
                        $testTime.now=$external+5
                        Assert-ROptionalPublicationAdmission $testTime.now $external
                        @{}
                    } -WriteReceipt {
                        param($Bytes)
                        $window=@{terminalPrevious=$terminalWindow.previous;start=[double]$testTime.now;deadline=([double]$testTime.now+5);previous=[double]$testTime.now;attempted=$false}
                        & $writer -Path ($prefix+'-receipt.json') -Bytes $Bytes -Window $window -ReadClock {$testTime.now} -Phase Final -ExternalSeconds $external
                    }
                }
                $receipt=Read-ProofJsonFile ($prefix+'-receipt.json')
                Assert-Proof (!$receipt.passed -and $receipt.terminalPublicationConfirmed -and $receipt.exitCode -eq 0 -and
                    !$receipt.timedOut -and $receipt.killAttempts -eq 0 -and $receipt.reportingFailure) 'Optional failure lost/damaged final observations.'
                Assert-Proof ($terminalWindow.start -eq 10 -and $terminalWindow.deadline -eq 15 -and $terminalWindow.attempted) 'Terminal reporting window renewed.'
                $cases++
            }
        }
        foreach($elapsed in @(105,315)) {
            foreach($phase in @('Terminal','Final')) {
                $path=Join-Path $Root ($writer+'-'+$elapsed+'-'+$phase+'.json')
                $bytes=ConvertTo-RPublicationBytes @{passed=$false;failure='optional completion expired';exitCode=$null;timedOut=$true}
                $window=@{terminalPrevious=0.0;start=[double]$elapsed;deadline=([double]$elapsed+5);previous=[double]$elapsed;attempted=$false}
                $operations=[Collections.Generic.List[string]]::new()
                $ack=& $writer -Path $path -Bytes $bytes -Window $window -ReadClock {$elapsed} -Phase $phase -ExternalSeconds ($elapsed-5) -CheckOperation {param($step) $operations.Add($step);$true}
                Assert-Proof ($ack -is [bool] -and $ack) 'Writer must acknowledge exactly Boolean true.'
                Assert-Proof (([Convert]::ToHexString([IO.File]::ReadAllBytes($path))) -ceq [Convert]::ToHexString($bytes)) 'Writer changed exact bytes.'
                Assert-Proof (($operations -join ',') -ceq 'admission,write,flush,close,hash,acknowledgement') 'Durability/verification order.'
                Assert-ProofTestThrows { & $writer -Path $path -Bytes $bytes -Window $window -ReadClock {$elapsed} -Phase $phase -ExternalSeconds ($elapsed-5) }
                $fresh=@{terminalPrevious=0.0;start=[double]$elapsed;deadline=([double]$elapsed+5);previous=[double]$elapsed;attempted=$false}
                Assert-ProofTestThrows { & $writer -Path $path -Bytes $bytes -Window $fresh -ReadClock {$elapsed} -Phase $phase -ExternalSeconds ($elapsed-5) }
                $cases++
            }
        }
        foreach($failure in @('write','flush','hash','acknowledgement')) {
            $path=Join-Path $Root ($writer+'-'+$failure+'.json')
            $window=@{terminalPrevious=0.0;start=105.0;deadline=110.0;previous=105.0;attempted=$false}
            Assert-ProofTestThrows {
                & $writer -Path $path -Bytes ([byte[]]@(123,125)) -Window $window -ReadClock {105} -Phase Final -ExternalSeconds 100 -CheckOperation {param($step) $step -cne $failure}
            }
            Assert-Proof $window.attempted 'Failure did not consume writer admission.'
            $cases++
        }
        foreach($kind in @('missing','string','extra')){
            $path=Join-Path $Root ($writer+'-ack-'+$kind+'.json')
            $window=@{terminalPrevious=0.0;start=105.0;deadline=110.0;previous=105.0;attempted=$false}
            Assert-ProofTestThrows {
                & $writer -Path $path -Bytes ([byte[]]@(123,125)) -Window $window -ReadClock {105} -Phase Final -ExternalSeconds 100 -CheckOperation {
                    param($step)
                    if($step -cne 'acknowledgement'){$true}
                    elseif($kind -ceq 'string'){'true'}
                    elseif($kind -ceq 'extra'){$true;$true}
                }
            }
            Assert-Proof ([IO.File]::ReadAllText($path) -ceq '{}') 'Failed acknowledgement discarded written evidence.';$cases++
        }
        $path=Join-Path $Root ($writer+'-real-hash-mismatch.json')
        $window=@{terminalPrevious=0.0;start=105.0;deadline=110.0;previous=105.0;attempted=$false}
        Assert-ProofTestThrows {
            & $writer -Path $path -Bytes ([byte[]]@(123,125)) -Window $window -ReadClock {105} -Phase Final -ExternalSeconds 100 -CheckOperation {
                param($step)
                if($step -ceq 'hash'){[IO.File]::WriteAllBytes($path,([byte[]]@(91,93)))}
                $true
            }
        };$cases++
        $path=Join-Path $Root ($writer+'-maximum.json')
        $window=@{terminalPrevious=0.0;start=105.0;deadline=110.0;previous=105.0;attempted=$false}
        $ack=& $writer -Path $path -Bytes ([byte[]]::new(1048576)) -Window $window -ReadClock {105} -Phase Final -ExternalSeconds 100
        Assert-Proof ($ack -is [bool] -and $ack -and ([IO.FileInfo]$path).Length -eq 1048576) 'Inclusive reporting byte cap.';$cases++
        $path=Join-Path $Root ($writer+'-deadline.json')
        $window=@{terminalPrevious=0.0;start=105.0;deadline=110.0;previous=105.0;attempted=$false}
        Assert-ProofTestThrows { & $writer -Path $path -Bytes ([byte[]]@(123,125)) -Window $window -ReadClock {110} -Phase Final -ExternalSeconds 100 }
        $cases++
        $window=@{terminalPrevious=0.0;start=105.0;deadline=110.0;previous=105.0;attempted=$false}
        Assert-ProofTestThrows { & $writer -Path $path -Bytes ([byte[]]::new(1048577)) -Window $window -ReadClock {105} -Phase Final -ExternalSeconds 100 }
        $cases++
    }
    return $cases
}
function Invoke-ConfigurationIntegrationTests([string]$Root) {
    . (Get-PinnedProofLibrary -Library NonCopySupervisor)
    $fixture=New-ProofRestageFixture (Join-Path $Root 'physical')
    $config=$fixture.config;$pin=$fixture.pin;$cases=0
    $parsed=Read-ProofConfiguration $pin.path $pin.sha256 -Purpose Restage
    $null=Assert-ProofSource $parsed $PSScriptRoot;$cases++
    $deadlineRecord=@{timedOut=$false;passed=$false}
    Assert-ProofProcessDeadline $deadlineRecord 9.99 10;$cases++
    Assert-ProofTestThrows {Assert-ProofProcessDeadline $deadlineRecord 10 10}
    Assert-Proof ($deadlineRecord.timedOut -and !$deadlineRecord.passed) 'Exact deadline became success.';$cases++
    Assert-ProofTestThrows {Assert-ProofProcessDeadline $deadlineRecord 1 10};$cases++
    $cases+=Invoke-ProofBooleanTests
    $adapterError=$null
    try {
        Invoke-PinnedTestMode -Root $Root -Mode Restage -Values @{configuration=$pin.path;configurationSha256=$pin.sha256;newGuiId=('a'*32)}
    }catch{$adapterError=$_.Exception.Message}
    Assert-Proof ($adapterError -ceq 'Pinned PowerShell 7 host required.') 'Actual restage adapter lost declared configuration before the no-spawn host gate.'
    $cases++
    $before=@([IO.Directory]::GetFileSystemEntries($config.outputRoot)).Count+@([IO.Directory]::GetFileSystemEntries($config.evidenceRoot)).Count
    Assert-ProofTestThrows {Read-ProofConfiguration '' ('0'*64)};$cases++
    Assert-ProofTestThrows {Read-ProofConfiguration (Join-Path $Root 'missing.json') ('0'*64)};$cases++
    $json=[IO.File]::ReadAllText($pin.path)
    foreach($bad in @('{}',$json.Replace('"schema":','"Schema":null,"schema":'),$json.Replace('"schema":','"command":"not allowed","schema":'),
        $json.Replace('"schema":"offline-patch-import-proof-v1"','"schema":[]'),$json.Replace('"files":605','"files":"605"'))){
        $badPin=Write-ProofTestBytes (Join-Path $Root ('bad-'+$cases+'.json')) ([Text.Encoding]::UTF8.GetBytes($bad))
        Assert-ProofTestThrows {Read-ProofConfiguration $badPin.path $badPin.sha256 -Purpose Restage};$cases++
    }
    Assert-Proof ($before -eq (@([IO.Directory]::GetFileSystemEntries($config.outputRoot)).Count+@([IO.Directory]::GetFileSystemEntries($config.evidenceRoot)).Count)) 'Configuration rejection allocated output.'
    $cases++
    $relocated=New-PinnedProofFixture (Join-Path $Root 'relocated source')
    $readOnly=Invoke-PinnedTestMode -Fixture $relocated -Mode PrerequisitesChild -Values @{
        configuration=$pin.path;configurationSha256=$pin.sha256;priorId=$config.restage.priorId} | ConvertFrom-Json -AsHashtable
    Assert-Proof ($readOnly.passed -and !$readOnly.copiesPerformed) 'Relocated production prerequisites.';$cases++
    $null=Confirm-ProofChildResult $config $pin.sha256 'ReadOnlyPrerequisites' '' $readOnly {};$cases++
    $stringProjection=$readOnly.Clone()
    foreach($key in @('startedUtc','completedUtc')){$stringProjection[$key]=([DateTime]$readOnly[$key]).ToString('o')}
    $null=Confirm-ProofChildResult $config $pin.sha256 'ReadOnlyPrerequisites' '' $stringProjection {};$cases++
    $dateProjection=$readOnly.Clone()
    foreach($key in @('startedUtc','completedUtc')){$dateProjection[$key]=[DateTime]$readOnly[$key]}
    $null=Confirm-ProofChildResult $config $pin.sha256 'ReadOnlyPrerequisites' '' $dateProjection {};$cases++
    foreach($value in @($null,1,$true,'not a timestamp')){
        $badTiming=$readOnly.Clone();$badTiming.startedUtc=$value
        Assert-ProofTestThrows {Confirm-ProofChildResult $config $pin.sha256 'ReadOnlyPrerequisites' '' $badTiming {}}
        $cases++
    }
    $newId='8'*32
    New-ProofRestageClaim $config $pin.sha256 $newId
    $report=Invoke-PinnedTestMode -Root $Root -Mode RestageChild -Values @{
        configuration=$pin.path;configurationSha256=$pin.sha256;priorId=$config.restage.priorId;newGuiId=$newId} | ConvertFrom-Json -AsHashtable
    Assert-Proof ($report.passed -and $report.payloadFiles -eq 605 -and $report.payloadBytes -eq $config.restage.expected.bytes) 'Production fresh copy failed.';$cases++
    $rows=@(Confirm-ProofChildResult $config $pin.sha256 'Restage' $newId $report {})
    Assert-Proof ($rows.Count -eq 608) 'Production supervisor inventory did not verify every output.';$cases++
    Assert-ProofTestThrows {New-ProofRestageClaim $config $pin.sha256 $newId};$cases++
    Assert-ProofTestThrows {
        Invoke-PinnedTestMode -Root $Root -Mode RestageChild -Values @{
            configuration=$pin.path;configurationSha256=$pin.sha256;priorId=$config.restage.priorId;newGuiId=$newId}
    };$cases++
    foreach($group in @(@{prefix='app';rows=$fixture.manifest.appFiles},@{prefix='support';rows=$fixture.manifest.runtimeAssemblies},
        @{prefix=('desktop-proof-'+$config.restage.priorId);rows=$fixture.manifest.fixtures})){
        foreach($row in $group.rows){
            $path=Join-Path $config.restage.donorRoot ($group.prefix+'\'+$row.path)
            $null=Read-ProofPinnedBytes @{path=$path;bytes=$row.bytes;sha256=$row.sha256}
        }
    };$cases++
    $corruptPath=Join-Path $config.restage.donorRoot 'app\file-0.dat'
    [IO.File]::WriteAllBytes($corruptPath,([byte[]]@(255)))
    $failedId='9'*32;New-ProofRestageClaim $config $pin.sha256 $failedId
    Assert-ProofTestThrows {
        Invoke-PinnedTestMode -Root $Root -Mode RestageChild -Values @{
            configuration=$pin.path;configurationSha256=$pin.sha256;priorId=$config.restage.priorId;newGuiId=$failedId}
    };$cases++
    $failed=Read-ProofJsonFile (Join-Path $config.evidenceRoot ('restage-'+$failedId+'.result.json'))
    Assert-Proof (!$failed.passed -and !$failed.copiesPerformed -and $failed.failure -and
        ![IO.Path]::Exists((Join-Path $config.outputRoot ('inputs-'+$failedId)))) 'Production failure output/admission preservation.';$cases++
    foreach($invalidType in @('PurePassed','CompilePassed','BindingAccepted','OuterSource','HistoryExtra','HistoryMissing')){
        $badFixture=New-ProofRestageFixture (Join-Path $Root ('bad-history-'+$invalidType)) $invalidType
        Assert-ProofTestThrows {
            Invoke-PinnedTestMode -Root $Root -Mode PrerequisitesChild -Values @{
                configuration=$badFixture.pin.path;configurationSha256=$badFixture.pin.sha256;priorId=$badFixture.config.restage.priorId}
        }
        Assert-Proof (@([IO.Directory]::GetFileSystemEntries($badFixture.config.outputRoot)).Count -eq 0) 'Malformed history allocated output.'
        $cases++
    }
    return $cases
}
