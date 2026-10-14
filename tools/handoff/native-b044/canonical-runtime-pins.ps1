function AssertB044ArtifactRecord($artifact,$actual,$head,$worktree) {
 if($head -ne 'b044fb98b53148ebb91b20f67ff5d2eda905eb5c') {throw 'Canonical reviewed source head refused'}
 $names=@('FEBuilderGBA.Avalonia.exe','FEBuilderGBA.Avalonia.dll','FEBuilderGBA.Core.dll','FEBuilderGBA.SkiaSharp.dll')
 if($artifact.name -notin $names) {throw 'Canonical artifact name refused'}
 $expected=Join-Path $worktree ('FEBuilderGBA.Avalonia\bin\Release\net10.0\'+$artifact.name)
 if(![String]::Equals([IO.Path]::GetFullPath($artifact.absolute_path),[IO.Path]::GetFullPath($expected),[StringComparison]::OrdinalIgnoreCase) -or
    ![String]::Equals([IO.Path]::GetFullPath($actual.Path),[IO.Path]::GetFullPath($expected),[StringComparison]::OrdinalIgnoreCase)) {throw 'Canonical exact production artifact path refused'}
 if($actual.Length -ne $artifact.length -or ![String]::Equals($actual.Hash,$artifact.sha256,[StringComparison]::OrdinalIgnoreCase) -or
    ![String]::Equals($actual.FileVersion,$artifact.file_version,[StringComparison]::Ordinal) -or
    ![String]::Equals($actual.ProductVersion,$artifact.product_version,[StringComparison]::Ordinal)) {throw ('Canonical actual artifact mismatch: '+$artifact.name)}
 if($artifact.name -ne 'FEBuilderGBA.Core.dll' -and $actual.ProductVersion -ne ('1.0.0+'+$head)) {throw 'Canonical actual committed-head PE stamp refused'}
}

function GetCanonicalB044RuntimePins([string]$manifestPath,[string]$manifestHash) {
 if((Get-FileHash -LiteralPath $manifestPath).Hash -ne $manifestHash) {throw 'Canonical provenance changed between wrapper and embedded verifier'}
 $m=Get-Content -LiteralPath $manifestPath -Raw|ConvertFrom-Json
 if($m.head -ne 'b044fb98b53148ebb91b20f67ff5d2eda905eb5c' -or $m.tree -ne '3cf284362bb34180c4b82b397adbcbd05b82e245' -or
    $m.worktree -ne 'C:\Projects\laqieer\FEBuilderGBA\.worktrees\maintenance-2213-d096b75f' -or $m.artifacts.Count -ne 4 -or
    @($m.artifacts.name|Select-Object -Unique).Count -ne 4) {throw 'Canonical head/tree/worktree/four-artifact provenance refused'}
 foreach($artifact in $m.artifacts) {
  $file=Get-Item -LiteralPath $artifact.absolute_path
  $actual=[pscustomobject]@{Path=$file.FullName;Length=$file.Length;Hash=(Get-FileHash $file.FullName).Hash;FileVersion=$file.VersionInfo.FileVersion;ProductVersion=$file.VersionInfo.ProductVersion}
  AssertB044ArtifactRecord $artifact $actual $m.head $m.worktree
  Write-Host "CANONICAL_B044_ARTIFACT Name=$($artifact.name) SHA256=$($actual.Hash) ProductVersion=$($actual.ProductVersion)"
  @{Name=$artifact.name;Length=$artifact.length;SHA256=$artifact.sha256;FileVersion=$artifact.file_version;ProductVersion=$artifact.product_version}
 }
}
