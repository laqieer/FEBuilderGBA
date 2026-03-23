<#
.SYNOPSIS
    Adds AutomationProperties.AutomationId to all interactive controls in Avalonia .axaml files.

.DESCRIPTION
    Processes each .axaml file in the FEBuilderGBA.Avalonia project and adds
    AutomationProperties.AutomationId attributes to interactive controls following
    the naming convention: {EditorName}_{FieldName}_{ControlType}

    Exemptions:
    - Controls inside DataTemplate, ControlTemplate, TreeDataTemplate blocks
    - Internal children of BitFlagPanel.axaml, AddressListControl.axaml, GbaImageControl.axaml
    - Static TextBlock labels (not bound/dynamic)
    - App.axaml (application root)

.PARAMETER AvaloniaRoot
    Path to the FEBuilderGBA.Avalonia directory. Defaults to the project location.
#>
param(
    [string]$AvaloniaRoot = (Join-Path (Join-Path $PSScriptRoot "..") "FEBuilderGBA.Avalonia")
)

$ErrorActionPreference = "Stop"

# Resolve to absolute path
$AvaloniaRoot = (Resolve-Path $AvaloniaRoot).Path

# Files to skip entirely (internal reusable controls + App.axaml)
$skipFiles = @(
    "Controls\BitFlagPanel.axaml",
    "Controls\AddressListControl.axaml",
    "Controls\GbaImageControl.axaml",
    "App.axaml"
)

# Interactive control types that should get AutomationIds
$interactiveControls = @(
    'TextBox', 'NumericUpDown', 'ComboBox', 'Button',
    'ListBox', 'ListView', 'CheckBox', 'ToggleButton',
    'Expander', 'TabControl', 'TabItem', 'RadioButton',
    'Image', 'Slider', 'ToggleSwitch', 'MenuItem'
)

# Custom controls that should get AutomationIds when used as host elements
$customInteractiveControls = @(
    'controls:BitFlagPanel', 'controls:AddressListControl', 'controls:GbaImageControl'
)

# TextBlock is special - only tagged when it has a Name and is dynamic/bound
# (we handle it separately)

# Control type to suffix mapping
$controlSuffixMap = @{
    'TextBox'       = '_Input'
    'NumericUpDown' = '_Input'
    'ComboBox'      = '_Combo'
    'Button'        = '_Button'
    'ListBox'       = '_List'
    'ListView'      = '_List'
    'CheckBox'      = '_Check'
    'ToggleButton'  = '_Check'
    'RadioButton'   = '_Check'
    'Expander'      = '_Expander'
    'TabControl'    = '_TabControl'
    'TabItem'       = '_Tab'
    'Image'         = '_Image'
    'Slider'        = '_Input'
    'ToggleSwitch'  = '_Check'
    'MenuItem'      = '_Button'
    'ItemsControl'  = '_List'
    'controls:BitFlagPanel'       = '_Check'
    'controls:AddressListControl' = '_List'
    'controls:GbaImageControl'    = '_Image'
    'TextBlock'     = '_Label'
}

function Get-EditorName {
    param([string]$FilePath)
    $fileName = [System.IO.Path]::GetFileNameWithoutExtension($FilePath)
    # Strip View/Window suffix
    if ($fileName -match '^(.+)(View|Window)$') {
        return $Matches[1]
    }
    return $fileName
}

function Get-FieldName {
    param(
        [string]$NameAttr,      # value of Name= or x:Name=
        [string]$ControlType,   # e.g. Button, TextBox
        [string]$ClickHandler,  # value of Click= handler
        [string]$Content,       # value of Content= or Text= for buttons
        [string]$Header         # value of Header= for Expanders/TabItems
    )

    # Priority 1: From Name/x:Name - strip common suffixes
    if ($NameAttr) {
        $field = $NameAttr
        # Strip type suffixes
        $suffixes = @('Box','Button','Combo','List','Control','Panel','Label','Text','Image','Flags',
                      'Input','Check','Toggle','Expander','Tab','Scroller','Border','Display',
                      'Viewer', 'ListView')
        foreach ($s in $suffixes) {
            if ($field -ne $s -and $field.EndsWith($s)) {
                $field = $field.Substring(0, $field.Length - $s.Length)
                break
            }
        }
        # If stripping left nothing, use original
        if ([string]::IsNullOrWhiteSpace($field)) {
            $field = $NameAttr
        }
        return $field
    }

    # Priority 2: From Click handler - strip _Click suffix
    if ($ClickHandler) {
        $field = $ClickHandler -replace '_Click$', ''
        # Also strip On prefix if present
        $field = $field -replace '^On', ''
        return $field
    }

    # Priority 3: From Content/Text for buttons, Header for expanders/tabs
    if ($Header) {
        # Clean header text for use as identifier
        $field = $Header -replace '[^a-zA-Z0-9]', ''
        if ($field) { return $field }
    }
    if ($Content) {
        $field = $Content -replace '[^a-zA-Z0-9]', ''
        if ($field) { return $field }
    }

    return $null
}

function Get-ControlSuffix {
    param([string]$ControlType)
    if ($controlSuffixMap.ContainsKey($ControlType)) {
        return $controlSuffixMap[$ControlType]
    }
    return '_Control'
}

function Test-IsInsideTemplate {
    param(
        [string[]]$Lines,
        [int]$LineIndex
    )
    # Track template nesting by scanning from start to current line
    $templateDepth = 0
    for ($i = 0; $i -lt $LineIndex; $i++) {
        $line = $Lines[$i]
        # Opening template tags
        if ($line -match '<\s*(DataTemplate|ControlTemplate|TreeDataTemplate)[\s>]' -and $line -notmatch '/>') {
            $templateDepth++
        }
        # Also match property-style template openings
        if ($line -match '<\w+\.(ItemTemplate|ContentTemplate|HeaderTemplate|CellTemplate)[\s>]') {
            # The DataTemplate inside will handle depth, but mark we're entering a template property
        }
        # Closing template tags
        if ($line -match '</\s*(DataTemplate|ControlTemplate|TreeDataTemplate)\s*>') {
            $templateDepth--
            if ($templateDepth -lt 0) { $templateDepth = 0 }
        }
    }
    return $templateDepth -gt 0
}

function Test-IsNamedDynamicTextBlock {
    param([string]$Line, [string]$FullElement)
    # A TextBlock is "dynamic" if it has a Name and is NOT just a static label
    # We consider it dynamic if:
    # 1. It has a Name= or x:Name= attribute, AND
    # 2. It has a {Binding} in Text= OR no static Text= at all (implying programmatic setting)
    if ($FullElement -match '(?:^|\s)(?:Name|x:Name)\s*=\s*"([^"]+)"') {
        # Has a name - check if it's dynamic
        if ($FullElement -match 'Text\s*=\s*"\{Binding') {
            return $true
        }
        # If no Text= attribute at all, it's likely set programmatically
        if ($FullElement -notmatch '\bText\s*=\s*"[^{]') {
            return $true
        }
    }
    return $false
}

function Process-AxamlFile {
    param([string]$FilePath)

    $editorName = Get-EditorName -FilePath $FilePath
    $content = Get-Content -Path $FilePath -Raw -Encoding UTF8
    $lines = $content -split "`n"

    $modified = $false
    $counters = @{}  # Track sequential counters per control type
    $usedIds = @{}   # Track used IDs to avoid duplicates

    $result = New-Object System.Collections.ArrayList

    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]

        # Check if inside a template
        if (Test-IsInsideTemplate -Lines $lines -LineIndex $i) {
            [void]$result.Add($line)
            continue
        }

        # Check if line already has AutomationProperties.AutomationId
        if ($line -match 'AutomationProperties\.AutomationId\s*=') {
            [void]$result.Add($line)
            continue
        }

        # Build full element text for multi-line elements
        $fullElement = $line
        if ($line -match '^\s*<' -and $line -notmatch '/>' -and $line -notmatch '>\s*$' -and $line -notmatch '<!--') {
            # Might be a multi-line element - collect until we find > or />
            $j = $i + 1
            while ($j -lt $lines.Count -and $fullElement -notmatch '(?:/>|(?<!=)>)\s*$') {
                $fullElement += "`n" + $lines[$j]
                $j++
            }
        }

        # Try to match an interactive control on this line
        $controlType = $null
        $isCustomControl = $false

        # Check for custom controls first
        foreach ($cc in $customInteractiveControls) {
            $escapedCC = [regex]::Escape($cc)
            if ($line -match "^\s*<$escapedCC[\s/>]") {
                $controlType = $cc
                $isCustomControl = $true
                break
            }
        }

        # Check standard interactive controls
        if (-not $controlType) {
            foreach ($ic in $interactiveControls) {
                if ($line -match "^\s*<$ic[\s/>]") {
                    $controlType = $ic
                    break
                }
            }
        }

        # Special handling for TextBlock - only if named and dynamic
        if (-not $controlType -and $line -match '^\s*<TextBlock[\s/>]') {
            if (Test-IsNamedDynamicTextBlock -Line $line -FullElement $fullElement) {
                $controlType = 'TextBlock'
            }
        }

        # Also check for ItemsControl (used for WarningsList etc.)
        if (-not $controlType -and $line -match '^\s*<ItemsControl[\s/>]') {
            if ($fullElement -match '(?:^|\s)(?:Name|x:Name)\s*=\s*"([^"]+)"') {
                $controlType = 'ItemsControl'
            }
        }

        if ($controlType) {
            # Extract Name or x:Name
            $nameAttr = $null
            if ($fullElement -match '(?:^|\s)(?:Name|x:Name)\s*=\s*"([^"]+)"') {
                $nameAttr = $Matches[1]
            }

            # Extract Click handler
            $clickHandler = $null
            if ($fullElement -match '\bClick\s*=\s*"([^"]+)"') {
                $clickHandler = $Matches[1]
            }

            # Extract Content
            $contentAttr = $null
            if ($fullElement -match '\bContent\s*=\s*"([^"]+)"') {
                $contentAttr = $Matches[1]
            }

            # Extract Header
            $headerAttr = $null
            if ($fullElement -match '\bHeader\s*=\s*"([^"]+)"') {
                $headerAttr = $Matches[1]
            }

            # Get field name
            $fieldName = Get-FieldName -NameAttr $nameAttr -ControlType $controlType `
                -ClickHandler $clickHandler -Content $contentAttr -Header $headerAttr

            # If no field name could be derived, use sequential fallback
            if (-not $fieldName) {
                $baseType = ($controlType -replace 'controls:', '')
                if (-not $counters.ContainsKey($baseType)) {
                    $counters[$baseType] = 0
                }
                $counters[$baseType]++
                $fieldName = "$baseType$($counters[$baseType])"
            }

            # Build the automation ID
            $suffix = Get-ControlSuffix -ControlType $controlType
            $automationId = "${editorName}_${fieldName}${suffix}"

            # Ensure uniqueness — insert counter before suffix to preserve valid suffix
            if ($usedIds.ContainsKey($automationId)) {
                $usedIds[$automationId]++
                $counter = $usedIds[$automationId]
                # Insert counter before the suffix: "Editor_Field_Button" -> "Editor_Field2_Button"
                $automationId = "${editorName}_${fieldName}${counter}${suffix}"
            } else {
                $usedIds[$automationId] = 1
            }

            # Insert AutomationProperties.AutomationId attribute
            # Find the right place to insert - after the opening tag name and before the first attribute or >
            $attrToInsert = "AutomationProperties.AutomationId=""$automationId"""

            if ($line -match '^\s*<[^\s>]+\s') {
                # There are attributes on this line - insert after the tag name
                $newLine = $line -replace "^(\s*<[^\s>]+)\s", "`$1 $attrToInsert "
                if ($newLine -eq $line) {
                    # Fallback: insert before />  or > at end of line
                    if ($line -match '/>$') {
                        $newLine = $line -replace '\s*/>$', " $attrToInsert />"
                    } elseif ($line -match '>\s*$') {
                        $newLine = $line -replace '>\s*$', " $attrToInsert>"
                    } else {
                        $newLine = $line
                    }
                }
                [void]$result.Add($newLine)
                $modified = $true
            } elseif ($line -match '^\s*<[^\s>]+\s*/>') {
                # Self-closing with no attributes
                $newLine = $line -replace "^(\s*<[^\s>]+)\s*/>", "`$1 $attrToInsert />"
                [void]$result.Add($newLine)
                $modified = $true
            } elseif ($line -match '^\s*<[^\s>]+\s*>') {
                # Tag with no attributes but has closing >
                $newLine = $line -replace "^(\s*<[^\s>]+)\s*>", "`$1 $attrToInsert>"
                [void]$result.Add($newLine)
                $modified = $true
            } else {
                # Multi-line tag - tag name only on this line, attrs on next lines
                # Insert the attribute on the next line
                $newLine = $line
                [void]$result.Add($newLine)
                # Check if next line has attributes - insert before them
                if ($i + 1 -lt $lines.Count) {
                    $nextLine = $lines[$i + 1]
                    $indent = ""
                    if ($nextLine -match '^(\s+)') {
                        $indent = $Matches[1]
                    }
                    # Insert new attribute line
                    $attrLine = "${indent}${attrToInsert}"
                    [void]$result.Add($attrLine)
                    $modified = $true
                }
                continue
            }
        } else {
            [void]$result.Add($line)
        }
    }

    if ($modified) {
        $finalContent = $result -join "`n"
        [System.IO.File]::WriteAllText($FilePath, $finalContent, [System.Text.UTF8Encoding]::new($false))
        return $true
    }
    return $false
}

# Main execution
Write-Host "=== Adding AutomationProperties.AutomationId to Avalonia .axaml files ===" -ForegroundColor Cyan
Write-Host "Root: $AvaloniaRoot" -ForegroundColor Gray

$allAxamlFiles = Get-ChildItem -Path $AvaloniaRoot -Filter "*.axaml" -Recurse | Sort-Object FullName
$totalFiles = $allAxamlFiles.Count
$processedFiles = 0
$modifiedFiles = 0
$skippedFiles = 0

foreach ($file in $allAxamlFiles) {
    $relativePath = $file.FullName.Substring($AvaloniaRoot.Length + 1)

    # Check if file should be skipped
    $shouldSkip = $false
    foreach ($skip in $skipFiles) {
        if ($relativePath -eq $skip -or $relativePath -eq ($skip -replace '\\', '/')) {
            $shouldSkip = $true
            break
        }
    }

    if ($shouldSkip) {
        Write-Host "  SKIP: $relativePath" -ForegroundColor DarkGray
        $skippedFiles++
        continue
    }

    $processedFiles++
    try {
        $wasModified = Process-AxamlFile -FilePath $file.FullName
        if ($wasModified) {
            Write-Host "  MODIFIED: $relativePath" -ForegroundColor Green
            $modifiedFiles++
        } else {
            Write-Host "  NO CHANGE: $relativePath" -ForegroundColor DarkGray
        }
    } catch {
        Write-Host "  ERROR: $relativePath - $_" -ForegroundColor Red
    }
}

Write-Host ""
Write-Host "=== Summary ===" -ForegroundColor Cyan
Write-Host "  Total files:    $totalFiles"
Write-Host "  Skipped:        $skippedFiles"
Write-Host "  Processed:      $processedFiles"
Write-Host "  Modified:       $modifiedFiles"
Write-Host "  Unchanged:      $($processedFiles - $modifiedFiles)"
