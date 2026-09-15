function Invoke-WindowsLexicalBindingTests {
    $source=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'RuntimeBinding.ps1'))
    if($source.Contains('[IO.Path]::GetFileName(')) {
        throw 'Windows-form binding paths must not use host-OS filename semantics.'
    }
    foreach($leaf in @('WindowsBase.dll','UIAutomationClient.dll')) {
        if((Get-WindowsBindingFileName ('C:\Owned\support\'+$leaf)) -cne $leaf) { throw 'Windows lexical leaf mismatch.' }
    }
    return 2
}

function Invoke-PinnedRuntimeBindingTests {
    param([scriptblock]$Check = { param($spec) Confirm-PinnedRuntimeBinding @spec })
    $cases = @(
        @{ name='support'; change=@{}; allowed=$true; reads=1 },
        @{ name='pinned-pshome'; change=@{ActualPath='C:\PinnedPS\WindowsBase.dll'}; allowed=$true; reads=1 },
        @{ name='third-location'; change=@{ActualPath='C:\Other\WindowsBase.dll'}; reads=0 },
        @{ name='wrong-filename'; change=@{ActualPath='C:\PinnedPS\Other.dll'}; reads=0 },
        @{ name='path-alias'; change=@{ActualPath='C:\Owned\support\..\support\WindowsBase.dll'}; reads=0 },
        @{ name='path-case'; change=@{ActualPath='c:\Owned\support\WindowsBase.dll'}; reads=0 },
        @{ name='wrong-identity'; change=@{ActualIdentity='Other, Version=10.0.0.0'}; reads=0 },
        @{ name='empty-actual'; change=@{ActualPath=''}; reads=0 },
        @{ name='invalid-pin'; change=@{ExpectedSha256='invalid'}; reads=0 },
        @{ name='invalid-size'; change=@{ExpectedBytes=0L}; reads=0 },
        @{ name='same-allowed-paths'; change=@{HostPath='C:\Owned\support\WindowsBase.dll'}; reads=0 },
        @{ name='wrong-host-filename'; change=@{HostPath='C:\PinnedPS\Other.dll'}; reads=0 },
        @{ name='noncanonical-support'; change=@{SupportPath='C:\Owned\..\support\WindowsBase.dll'}; reads=0 },
        @{ name='network-host'; change=@{HostPath='\\server\share\WindowsBase.dll'}; reads=0 },
        @{ name='forward-slash-host'; change=@{HostPath='C:/PinnedPS/WindowsBase.dll'}; reads=0 },
        @{ name='wrong-bytes'; change=@{}; metadata=@{bytes=4L;sha256=('a'*64)}; reads=1 },
        @{ name='wrong-hash'; change=@{}; metadata=@{bytes=3L;sha256=('b'*64)}; reads=1 },
        @{ name='uppercase-hash'; change=@{}; metadata=@{bytes=3L;sha256=('A'*64)}; reads=1 },
        @{ name='missing-hash'; change=@{}; metadata=@{bytes=3L}; reads=1 },
        @{ name='untyped-size'; change=@{}; metadata=@{bytes='3';sha256=('a'*64)}; reads=1 },
        @{ name='missing-metadata'; change=@{}; metadata=$null; reads=1 },
        @{ name='reader-failure'; change=@{}; readerFails=$true; reads=1 }
    )
    foreach ($case in $cases) {
        $state = @{reads=0;metadata=@{bytes=3L;sha256=('a'*64)};readerFails=$false}
        if ($case.ContainsKey('metadata')) { $state.metadata=$case.metadata }
        if ($case.ContainsKey('readerFails')) { $state.readerFails=$case.readerFails }
        $spec = @{
            SupportPath='C:\Owned\support\WindowsBase.dll'; HostPath='C:\PinnedPS\WindowsBase.dll'
            ExpectedIdentity='WindowsBase, Version=10.0.0.0'; ActualIdentity='WindowsBase, Version=10.0.0.0'
            ExpectedBytes=3L; ExpectedSha256=('a'*64); ActualPath='C:\Owned\support\WindowsBase.dll'
            ReadMetadata={
                param($path)
                $state.reads++
                if ($state.readerFails) { throw 'Injected reader failure.' }
                $state.metadata
            }.GetNewClosure()
        }
        foreach ($key in $case.change.Keys) { $spec[$key]=$case.change[$key] }
        $problem=$null; $record=$null
        try { $record=& $Check $spec } catch { $problem=$_ }
        $allowed=$case.ContainsKey('allowed') -and $case.allowed
        if ($allowed -and $null -ne $problem) { throw "Binding case $($case.name) failed: $problem" }
        if (!$allowed -and $null -eq $problem) { throw "Binding case $($case.name) accepted invalid input." }
        if ($state.reads -ne $case.reads) { throw "Binding case $($case.name) performed unexpected metadata reads." }
        if ($allowed -and ($record.actualPath -cne $spec.ActualPath -or
            $record.actualSha256 -cne $spec.ExpectedSha256 -or $record.actualBytes -ne 3 -or
            $record.allowedLocation -cne $case.name)) { throw "Binding case $($case.name) returned an invalid record." }
    }
    $cases.Count
}
