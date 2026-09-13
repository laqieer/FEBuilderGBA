function Get-WindowsBindingFileName([string]$Path) {
    return $Path.Substring($Path.LastIndexOf('\')+1)
}

function Confirm-PinnedRuntimeBinding {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$SupportPath,
        [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$HostPath,
        [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$ExpectedIdentity,
        [Parameter(Mandatory)][ValidateRange(1,268435456)][long]$ExpectedBytes,
        [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{64}$')][string]$ExpectedSha256,
        [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$ActualPath,
        [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$ActualIdentity,
        [Parameter(Mandatory)][scriptblock]$ReadMetadata
    )
    foreach ($path in @($SupportPath,$HostPath)) {
        if ($path -cnotmatch '^[A-Za-z]:\\' -or $path.Substring(3) -cmatch '[<>:"/|?*\x00-\x1f]') {
            throw 'Runtime binding requires canonical local Windows paths.'
        }
        foreach ($part in $path.Substring(3).Split('\')) {
            if (!$part -or $part -ceq '.' -or $part -ceq '..' -or $part.EndsWith('.') -or $part.EndsWith(' ')) {
                throw 'Runtime binding requires canonical path components.'
            }
        }
    }
    if ($SupportPath -ceq $HostPath -or
        (Get-WindowsBindingFileName $SupportPath) -cne (Get-WindowsBindingFileName $HostPath)) {
        throw 'Runtime binding requires distinct corresponding support and host files.'
    }
    if ($ActualPath -cne $SupportPath -and $ActualPath -cne $HostPath) {
        throw "Runtime binding rejected location: $ActualPath"
    }
    if ($ActualIdentity -cne $ExpectedIdentity) { throw "Runtime binding identity mismatch: $ActualPath" }
    $metadata=& $ReadMetadata $ActualPath
    if ($null -eq $metadata -or $metadata.bytes -isnot [long] -or
        $metadata.bytes -ne $ExpectedBytes -or $metadata.sha256 -isnot [string] -or
        $metadata.sha256 -cne $ExpectedSha256) { throw "Runtime binding file pin mismatch: $ActualPath" }
    [pscustomobject]@{
        allowedLocation=if ($ActualPath -ceq $SupportPath) {'support'} else {'pinned-pshome'}
        expectedPath=$SupportPath; expectedHostPath=$HostPath; expectedIdentity=$ExpectedIdentity
        expectedBytes=$ExpectedBytes; expectedSha256=$ExpectedSha256
        actualPath=$ActualPath; actualIdentity=$ActualIdentity
        actualBytes=$metadata.bytes; actualSha256=$metadata.sha256
    }
}
