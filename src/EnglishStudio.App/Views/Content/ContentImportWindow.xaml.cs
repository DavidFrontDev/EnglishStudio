using EnglishStudio.App.Shell;
using EnglishStudio.App.ViewModels.Content;

namespace EnglishStudio.App.Views.Content;

public partial class ContentImportWindow : ChromedWindow
{
    public ContentImportWindow()
    {
        InitializeComponent();

        // The «Готово» command asks the VM to close us — bridge that to Window.Close().
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is ContentImportViewModel oldVm)
                oldVm.CloseRequested -= OnCloseRequested;
            if (e.NewValue is ContentImportViewModel newVm)
                newVm.CloseRequested += OnCloseRequested;
        };
    }

    private void OnCloseRequested() => Close();
}
