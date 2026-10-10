# Runtime accessibility audit

This is a bounded inventory for [issue #2205](https://github.com/laqieer/FEBuilderGBA/issues/2205), not certification of every editor, third-party widget, or platform. AutomationIds alone do not establish accessible names, roles, values, or operation.

## Verified Windows desktop surfaces

The October 10, 2026 audit used the production Avalonia desktop application, a disposable FE8U ROM copy, and process-scoped UI Automation descendants. Other running applications were not operated. No editor Write action was used; saving the unchanged disposable ROM preserved its SHA-256.

| Surface | Runtime finding and result |
| --- | --- |
| Production Yes/No dialog | Previously verified with UIA Invoke: No returns false, Yes returns true. Both buttons expose names, Button roles and enabled/onscreen states. A top-level window title is not a reliable descendant-discovery strategy. |
| Main editor filter | The Edit name was empty. It now follows the translated Filter label; ValuePattern and keyboard focus are available. |
| Shared address-list search | The Edit name was empty. It now follows the translated search watermark; ValuePattern and keyboard focus are available. Verified in Move Cost and Classes. |
| Shared address-list rows | ListItems announced `FEBuilderGBA.Avalonia.Controls.AddressListItem` rather than their displayed entries. They now announce names such as `01 Lord`; UIA SelectionItem selects the corresponding class. |
| Move Cost type | The ComboBox name was empty. It now follows the translated Cost Type label; selection/value/expand-collapse patterns and keyboard focus remain available. |
| Move Cost terrain inputs | All 65 template TextBoxes had empty names. The numeric control and its actual editable template descendant now reference the same live terrain label, including its terrain index. A valid class exposes enabled Edit peers, writable ValuePattern and values; setting the existing value back through UIA succeeds without writing ROM data. |
| Content-repository setup URLs | The three Edit names were empty. They now announce their repository labels; writable ValuePattern and keyboard focus remain available. Repository initialization was not invoked. |

Accessible labels follow the displayed localized text rather than maintaining a second, potentially stale English name. Focused headless tests exercise automation-peer names, translated/dynamic label changes, realized list containers, and the numeric template descendants.

## Critical commands

File/Edit menu descendants expose translated MenuItem names, focusability and enabled states. Open, Save, Save As and Undo were operated by focusing the owned menu item through UIA and posting native Enter key messages to the same owned application. This is application keyboard-routing evidence, **not** a physical foreground-keyboard test.

- **Open:** opens the native picker. Cancel using that picker's WindowPattern Close; do not select an element by AutomationId alone, because native file pickers reuse IDs across list items and buttons.
- **Save:** on the unchanged disposable ROM, displays the production “ROM saved” notification; its OK button supports UIA Invoke. File content remains unchanged.
- **Save As:** opens the native save picker, then closes it without saving.
- **Undo:** is enabled after loading a ROM; the no-edit invocation completes without modifying the copied file. Undoing a ROM edit was deliberately not exercised in this audit.
- **Redo:** is explicitly unsupported; Ctrl+Y reports this in the existing main-window keyboard handler. There is no Redo command to certify.

Avalonia's menu peers in this Windows run expose neither Invoke nor ExpandCollapse for these MenuItems. Keyboard routing remains usable; this patch does not replace third-party menu peers. The native Save As Cancel surface appeared as a Pane without Invoke, while WindowPattern Close provided cancellation.

## Remaining inventory and platform limits

The issue remains open. The ten additional unnamed Classes numeric template inputs confirmed in the native audit were acceptance failures, not non-goals. The follow-up fixes all **46 Classes numeric inputs and their template Edits**, plus **8 pointer/data TextBoxes**, using the existing translated/version-specific field captions. Identity, base stats, stat caps, growth, promotion, weapon ranks, terrain pointers and simulation level are explicitly paired in semantic peer regressions. Numeric templates are recreated in tests to ensure their label references survive retemplating without altering values. These follow-up results are **headless**, awaiting exact-head native verification; the original screenshot does not prove them.

The shared `EditorTopBar` filter and main read-only decompilation build output also had empty production peer names in headless tests. The filter now follows its live caption, with a writable Value provider that retains filter synchronization. Output announces its translated purpose rather than compiler diagnostics, while keeping a read-only Value provider.

### Offline inventory (not native certification)

The static October 10 follow-up enumerated **371 AXAML files**: 2,048 NumericUpDowns, 475 TextBoxes, 147 ComboBoxes, 1,506 Buttons, 138 CheckBoxes, 99 ListBoxes, 39 MenuItems, 21 TabControls and one Slider. These are source instances, not realized runtime peers; generated fields/templates add descendants. Static presence, content and AutomationIds do not certify accessible names, roles or operation.

| Surface | Static inventory / outstanding runtime work |
| --- | --- |
| Classes detail inputs | 46 numerics + 8 text inputs: explicit current caption, Edit-role/value and replacement-template headless coverage; native values/focus/enabled states and version-specific visibility still to refresh. |
| Classes commands | 16 Buttons with visible Content; includes seven pointer Jump buttons with repeated names, description navigation, Write, growth/export commands. Their context and keyboard/Invoke behavior require native validation; no ROM Write was exercised. |
| Classes flags/export options | Four BitFlagPanels generate 32 CheckBoxes, with version-specific Content; eight additional CheckBoxes have Content. Toggle states and native operation remain to audit. |
| Main host | 36 MenuItems, 233 Buttons and two TextBoxes. Critical commands have the earlier native evidence above; build-output purpose has new headless coverage. Other launchers/menu states require representative runtime verification. |
| Shared EditorTopBar | One optional filter TextBox and one Reload Button, plus noneditable metadata labels. Filter name/value has new headless coverage; native optional-filter/reload flow remains. |
| Shared AddressListControl | One search TextBox, three Buttons, one ListBox, three context MenuItems; generated ListBoxItems have the earlier native evidence. Context commands and edge selection/disabled states remain to audit. |
| Shared BitFlagPanel | Eight generated CheckBoxes per instance; labels follow bit Content. Cross-editor version-specific names and toggle behavior remain to audit. |

No confirmed Classes input-name defect is intentionally left unfixed. The remaining gates above are concrete incomplete validation/inventory, not assertions that unaffected controls are accessible. Further confirmed distinct failures require fixes and focused regression coverage.

The rest of the Classes detail controls and other editors, dialogs, third-party templates, and disabled/selection/validation states remain unaudited beyond the surfaces above. Physical foreground keyboard navigation was unavailable to the automation process (`GetForegroundWindow` returned zero); native UIA focus/value/selection/invoke and application keyboard messages are reported separately, not presented as an end-to-end screen-reader pass.

Native evidence is limited to Windows with an active RDP session and a responsive owned application. Headless automation-peer regressions are not native accessibility-tree evidence for Linux/AT-SPI, macOS, Android, iOS or Browser. Those platforms still need their own runtime audits. The live affected-editor image and detailed test results are recorded in the linked issue/PR, not stored as repository binaries.
