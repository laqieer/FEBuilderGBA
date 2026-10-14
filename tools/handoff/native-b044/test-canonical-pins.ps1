$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'canonical-runtime-pins.ps1')
$path=Join-Path (Split-Path $PSScriptRoot) 'movecost-caption-reattach-postcommit-provenance.json'
$hash=(Get-FileHash $path).Hash
$wrapper=@(GetCanonicalB044RuntimePins $path $hash)
$embedded=@(GetCanonicalB044RuntimePins $path $hash)
if($wrapper.Count -ne 4 -or $embedded.Count -ne 4) {throw 'Both verifier paths must attest all4 actual artifacts'}
$m=Get-Content $path -Raw|ConvertFrom-Json
foreach($a in $m.artifacts) {
 $f=Get-Item $a.absolute_path
 $actual=[pscustomobject]@{Path=$f.FullName;Length=$f.Length;Hash=(Get-FileHash $f.FullName).Hash;FileVersion=$f.VersionInfo.FileVersion;ProductVersion=$f.VersionInfo.ProductVersion}
 foreach($case in @('WrongHash','WrongHead','WrongPath')) {
  $copy=$actual|Select-Object *
  $head=$m.head
  switch($case){'WrongHash'{$copy.Hash='0000'};'WrongHead'{$head='7434563cbc521e95b3d02d11c101bd323c700cbf'};'WrongPath'{$copy.Path=Join-Path $PSScriptRoot $a.name}}
  $refused=$false
  try{AssertB044ArtifactRecord $a $copy $head $m.worktree}catch{$refused=$true}
  if(!$refused){throw ('Canonical negative failed: '+$case+' '+$a.name)}
 }
}
foreach($file in Get-ChildItem $PSScriptRoot -Filter '*.ps1') {
 $t=$null;$e=$null;$null=[System.Management.Automation.Language.Parser]::ParseFile($file.FullName,[ref]$t,[ref]$e)
 if($e.Count){throw ($e.Message -join '; ')}
}
'CANONICAL_WRAPPER_AND_EMBEDDED_4_ACTUAL_ARTIFACTS_PLUS12NEGATIVES_AST_GREEN=True'
