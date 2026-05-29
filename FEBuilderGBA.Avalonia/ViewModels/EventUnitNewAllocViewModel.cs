namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// ViewModel for the EventUnit New(Alloc) modal count-picker (#776).
    /// Holds the chosen allocation count (Min=1, Max=50, Value=1 — WF
    /// <c>EventUnitNewAllocForm</c> parity). The picker returns the count via
    /// <c>ShowDialog&lt;uint?&gt;</c>; the actual block allocation is done by
    /// <see cref="FEBuilderGBA.MapEventUnitCore.AllocNewUnitList"/>.
    /// </summary>
    public class EventUnitNewAllocViewModel : ViewModelBase
    {
        uint _allocCount = 1;
        bool _isLoaded;

        /// <summary>Chosen number of unit rows to allocate (default 1).</summary>
        public uint AllocCount { get => _allocCount; set => SetField(ref _allocCount, value); }

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
    }
}
