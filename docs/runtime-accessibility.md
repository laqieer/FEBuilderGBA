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

The issue remains open. Known additional gaps observed in the Classes editor are the unnamed `PART_TextBox` descendants of `ClassEditor_NameId_Input`, `ClassEditor_DescId_Input`, `ClassEditor_ClassNumber_Input`, `ClassEditor_PromotionLevel_Input`, `ClassEditor_WaitIcon_Input`, `ClassEditor_WalkSpeed_Input`, `ClassEditor_PortraitId_Input`, `ClassEditor_SortOrder_Input`, `ClassEditor_BaseHp_Input`, and `ClassEditor_BaseStr_Input`. These require a separate focused investigation of shared ID/numeric controls and their field labels; fixing shared list-row names does not fix those inputs.

The rest of the Classes detail controls and other editors, dialogs, third-party templates, and disabled/selection/validation states remain unaudited beyond the surfaces above. Physical foreground keyboard navigation was unavailable to the automation process (`GetForegroundWindow` returned zero); native UIA focus/value/selection/invoke and application keyboard messages are reported separately, not presented as an end-to-end screen-reader pass.

Native evidence is limited to Windows with an active RDP session and a responsive owned application. Headless automation-peer regressions are not native accessibility-tree evidence for Linux/AT-SPI, macOS, Android, iOS or Browser. Those platforms still need their own runtime audits. The live affected-editor image and detailed test results are recorded in the linked issue/PR, not stored as repository binaries.
