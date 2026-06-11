namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>Text reference add dialog ViewModel.</summary>
    public class TextRefAddDialogViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _targetText = "";
        string _comment = "";
        int _refId;
        string _dialogResult = "";
        string _originalComment = "";

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        /// <summary>The target text to add a reference for (read-only display).</summary>
        public string TargetText { get => _targetText; set => SetField(ref _targetText, value); }
        /// <summary>User comment for the text reference.</summary>
        public string Comment { get => _comment; set => SetField(ref _comment, value); }
        /// <summary>Text ID being referenced.</summary>
        public int RefId { get => _refId; set => SetField(ref _refId, value); }
        public string DialogResult { get => _dialogResult; set => SetField(ref _dialogResult, value); }

        /// <summary>
        /// True when opened via <see cref="Init"/> for the Text Editor References-tab
        /// flow — the Text ID is fixed to the selected text and the input is locked.
        /// </summary>
        public bool IsTextIdLocked { get; private set; }

        public void Initialize()
        {
            IsLoaded = true;
        }

        /// <summary>
        /// Pre-fill the dialog for the Text Editor References-tab "Add Reference"
        /// flow (#1028 Slice A). Mirrors WinForms <c>TextRefAddDialogForm.Init</c>:
        /// the Text ID is fixed to the selected text, and the comment box is
        /// seeded with the existing reference comment. The original comment is
        /// remembered so <see cref="GetComment"/> can replicate the WF "new blank
        /// entry → store a single space" convention.
        /// </summary>
        public void Init(uint textid, string existingComment)
        {
            RefId = (int)textid;
            IsTextIdLocked = true;
            _originalComment = existingComment ?? "";
            Comment = _originalComment;
        }

        /// <summary>
        /// Resolve the comment to persist, replicating WinForms
        /// <c>TextRefAddDialogForm.GetComment</c>: an empty comment on a NEW entry
        /// (no prior comment) becomes a single space <c>" "</c> so the reference is
        /// kept but blank; an empty comment that clears an EXISTING entry is passed
        /// through as <c>""</c> so <c>ITextIDCache.Update</c> removes it.
        /// </summary>
        public string GetComment()
        {
            string comment = Comment ?? "";
            if (comment == "" && _originalComment == "")
            {
                // New entry with a blank comment: keep it (WF stores a single space).
                comment = " ";
            }
            return comment;
        }
    }
}
