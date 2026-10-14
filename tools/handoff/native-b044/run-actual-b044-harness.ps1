param([switch]$FreshOwned)
$ErrorActionPreference='Stop'
$root=$PSScriptRoot
$base=Split-Path $root
. (Join-Path $root 'cold-main-identity.ps1')
. (Join-Path $root 'canonical-runtime-pins.ps1')
$canonicalManifestPath=Join-Path $base 'movecost-caption-reattach-postcommit-provenance.json'
$canonicalManifestHash=(Get-FileHash $canonicalManifestPath).Hash
$manifest=Get-Content $canonicalManifestPath -Raw|ConvertFrom-Json
if($manifest.head -ne 'b044fb98b53148ebb91b20f67ff5d2eda905eb5c') {throw 'Current source manifest refused'}
if((& git -C $manifest.worktree rev-parse HEAD) -ne $manifest.head -or (& git -C $manifest.worktree rev-parse 'HEAD^{tree}') -ne $manifest.tree) {throw 'Current source/tree refused'}
$wrapperPins=@(GetCanonicalB044RuntimePins $canonicalManifestPath $canonicalManifestHash)
$template=Get-Content (Join-Path $base 'native-2213\live.ps1') -Raw
$tokens=$null;$errors=$null
$ast=[System.Management.Automation.Language.Parser]::ParseInput($template,[ref]$tokens,[ref]$errors)
if($errors.Count) {throw 'Proven host AST refused'}
$native=$ast.Find({param($n) $n -is [System.Management.Automation.Language.CommandAst] -and $n.GetCommandName() -eq 'Add-Type' -and $n.Extent.Text.Contains('public sealed class LiveGuardMetadata')},$true)
$ready=$ast.Find({param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq 'Ready'},$true)
if(!$native -or !$ready) {throw 'Exact actor guard missing'}
$ipctext=(Get-Content (Join-Path $base 'native-2221\ipc.ps1') -Raw).Replace('$PSScriptRoot',("'"+$root+"'"))
$modtext=Get-Content (Join-Path $base 'native-2213\modal-discovery.ps1') -Raw
$baseprocs=(Get-Content (Join-Path $base 'native-2213\procedures.ps1') -Raw).Replace(". (Join-Path `$root 'modal-discovery.ps1')",'. ([ScriptBlock]::Create($modtext))')
$procedureText=Get-Content (Join-Path $base 'native-2221\procedures.ps1') -Raw
$procedureText=$procedureText.Replace(". (Join-Path (Join-Path (Split-Path `$PSScriptRoot) 'native-2213') 'procedures.ps1')",'. ([ScriptBlock]::Create($baseprocs))').Replace('7434563cbc521e95b3d02d11c101bd323c700cbf',$manifest.head)
$procedureText=$procedureText.Replace("$"+"script:startupWizardStagePath=Join-Path `$root 'startup-wizard-stage743.json'","throw 'Automatic startup Close not admitted; no manual hold authorized'")
$procedureText=$procedureText.Replace("$"+"script:focusStagePath=Join-Path `$root 'manual-focus-stage743.json'","throw 'Exact main foreground not admitted; no manual hold authorized'")
foreach($source in @($ipctext,$modtext,$baseprocs,$procedureText)) {
 $tokens=$null;$errors=$null
 $null=[System.Management.Automation.Language.Parser]::ParseInput($source,[ref]$tokens,[ref]$errors)
 if($errors.Count) {throw ($errors.Message -join '; ')}
}
. ([ScriptBlock]::Create($ipctext))
if((AssertOwnedInbox $root) -ne $root) {throw 'Actual isolated producer/receiver directory refused'}
'ACTUAL_TEST_HARNESS_START Head='+$manifest.head+' HostPID='+$PID+' UTC='+[DateTime]::UtcNow.ToString('o')
& ([ScriptBlock]::Create($native.Extent.Text))
. ([ScriptBlock]::Create($ready.Extent.Text))
try {$admission=Ready} catch {
 'ACTUAL_HARNESS_STOP_BEFORE_UIA_APP_COPY '+($_.Exception.Message)
 'MANUAL_PARENT_APP37352_UNTOUCHED=True'
 exit 2
}
$parentReady=$admission.CompletedUTC
$template=$template.Replace($native.Extent.Text,'')
$template=$template.Replace('$root = $PSScriptRoot',('$root = '''+$root+'''')).Replace('427602d91f49e5d908775d21a4926e81507a4a73',$manifest.head)
$tokens=$null;$errors=$null
$preparedAst=[System.Management.Automation.Language.Parser]::ParseInput($template,[ref]$tokens,[ref]$errors)
if($errors.Count) {throw 'Rebound template AST refused'}
$pins=$preparedAst.Find({param($n) $n -is [System.Management.Automation.Language.AssignmentStatementAst] -and $n.Left.Extent.Text -eq '$runtimePins'},$true)
if(!$pins -or !$template.Contains($pins.Extent.Text)) {throw 'Exact rebound runtime verifier pin binding refused'}
$template=$template.Replace($pins.Extent.Text,'$runtimePins=@(GetCanonicalB044RuntimePins $canonicalManifestPath $canonicalManifestHash)')
$template=$template.Replace(". (Join-Path `$root 'ipc.ps1')",'. ([ScriptBlock]::Create($ipctext))').Replace(". (Join-Path `$root 'procedures.ps1')",'. ([ScriptBlock]::Create($procedureText))')
if(!$FreshOwned) {
$launchLine='$script:app=Start-Process $exe -ArgumentList @(''--rom'',"`"$copy`"") -WorkingDirectory $root -PassThru'
if(!$template.Contains($launchLine)) {throw 'Exact reviewed host launch boundary changed'}
$template=$template.Replace($launchLine,'$script:app=$script:protectedParentApp'+"`n OpenPrivateROMPublicB044")
$template=$template.Replace(' [LiveNative]::LimitOwnedLifetime($app)','')
$template=$template.Replace(' RecordOptionsBeforeLaunch $exe',"'ADOPTED_PARENT_CONFIG_PRESERVED_NO_SNAPSHOT_OVERRIDE=True'")
$template=$template.Replace(' if($app -and !$app.HasExited) {',' if($app -and $app.Id -ne 37352 -and !$app.HasExited) {')
$template=$template.Replace(' try { PreserveOptionsAfterCleanup } catch { "OPTIONS_CLEANUP_ERROR $($_.Exception.Message)" }',"'ADOPTED_PARENT_OPTIONS_NOT_OVERWRITTEN=True'")
}
$resolve=@'
 ResolveManualParentAppB044
'@
if(!$FreshOwned) {
$template=$template.Replace("try {`r`n `$null=Ready`r`n `$work=","try {`r`n `$null=Ready`r`n$resolve`r`n `$work=")
if(!$template.Contains('ResolveManualParentAppB044')) {$template=$template.Replace("try {`n `$null=Ready`n `$work=","try {`n `$null=Ready`n$resolve`n `$work=")}
if(!$template.Contains('ResolveManualParentAppB044')) {throw 'Actual host startup resolution hook refused'}
}
$startup=". ([ScriptBlock]::Create(`$procedureText))`r`n Tree`r`n"
if(!$template.Contains($startup)) {$startup=". ([ScriptBlock]::Create(`$procedureText))`n Tree`n"}
if(!$template.Contains($startup)) {throw 'Actual host post-discovery hook refused'}
$template=$template.Replace($startup,$startup+" DismissExpectedContentRepoStartup743`n EnsureManualMainFocus743`n RunOrdinaryWritesAndGlobalUndo743`n")
function ResolveManualParentAppB044 {
 $null=Ready
 $manual=Get-Process -Id 37352 -ErrorAction SilentlyContinue
 if(!$manual) {throw 'Authorised parent manual app37352 no longer running; no duplicate launch'}
 $expectedPath=$manifest.artifacts[0].absolute_path
 if($manual.MainModule.FileName -ne $expectedPath) {throw 'Parent manual PID exact executable identity refused'}
 $started=$manual.StartTime.ToUniversalTime()
 'PARENT_MANUAL_ACTUAL_PID=37352 StartedUTC='+$started.ToString('o')+' ExactPath=True'
 $script:app=$manual
 $script:protectedParentApp=$manual
 try {
 . ([ScriptBlock]::Create($procedureText))
 DiscoverColdMainB044
 $script:mainHwnd=$script:hwnd
 DismissExpectedContentRepoStartup743
 $null=Ready
 Owned
 $element=[System.Windows.Automation.AutomationElement]::FromHandle($mainHwnd)
 $reportedPID=$element.GetCurrentPropertyValue([System.Windows.Automation.AutomationElement]::ProcessIdProperty,$true)
 if($reportedPID -isnot [int] -or $reportedPID -ne 37352 -or $manual.StartTime.ToUniversalTime() -ne $started -or $element.Current.Name -ne 'FEBuilderGBA') {throw 'Manual app no-ROM/title/process-start state not proven; leave it untouched'}
 $status=ById 'Main_Status_Label'
 if($status.Current.Name -ne 'No ROM loaded') {throw 'Parent public no-ROM state not exact; no copy/open'}
 'PARENT_MANUAL_NO_ROM_MAIN_ADOPTED_UNCLOSED=True'
 } finally {$script:app=$null}
}
$tokens=$null;$errors=$null
$null=[System.Management.Automation.Language.Parser]::ParseInput($template,[ref]$tokens,[ref]$errors)
if($errors.Count) {throw ($errors.Message -join '; ')}
& ([ScriptBlock]::Create($template)) -ParentReadyUtc $parentReady
