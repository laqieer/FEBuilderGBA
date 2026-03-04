using System.Collections.Generic;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Services
{
    /// <summary>
    /// Interface for ViewModels that can report their loaded data for verification.
    /// Used by --data-verify mode to cross-check ViewModel fields against raw ROM bytes.
    /// </summary>
    public interface IDataVerifiable
    {
        /// <summary>Number of items in the list for this editor.</summary>
        int GetListCount();

        /// <summary>
        /// Returns a dictionary of field name → hex value string for the currently loaded item.
        /// Example: { "NameId" → "0x0001", "ClassId" → "0x01" }
        /// </summary>
        Dictionary<string, string> GetDataReport();

        /// <summary>
        /// Returns a dictionary of raw ROM read description → hex value string.
        /// Example: { "u16@0x00" → "0x0001", "u8@0x04" → "0x01" }
        /// </summary>
        Dictionary<string, string> GetRawRomReport();
    }

    /// <summary>
    /// Interface for Views that expose their ViewModel for data verification.
    /// </summary>
    public interface IDataVerifiableView
    {
        ViewModelBase? DataViewModel { get; }
    }
}
