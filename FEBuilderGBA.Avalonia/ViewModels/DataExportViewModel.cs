using System.Collections.ObjectModel;
using System.Linq;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class DataExportViewModel : ViewModelBase
    {
        string _selectedTable = "";
        string _statusMessage = "";

        public ObservableCollection<string> TableNames { get; } = new();

        public string SelectedTable
        {
            get => _selectedTable;
            set => SetField(ref _selectedTable, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetField(ref _statusMessage, value);
        }

        public DataExportViewModel()
        {
            foreach (var name in StructExportCore.GetTableNames().OrderBy(n => n))
                TableNames.Add(name);

            if (TableNames.Count > 0)
                SelectedTable = TableNames[0];
        }
    }
}
