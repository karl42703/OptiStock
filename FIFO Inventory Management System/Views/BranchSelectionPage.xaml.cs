using FIFO_Inventory_Management_System.Services;

namespace FIFO_Inventory_Management_System.Views;

public partial class BranchSelectionPage : ContentPage
{
    public BranchSelectionPage()
    {
        InitializeComponent();
        var branches = SessionService.VisibleBranches().ToList();
        BranchPicker.ItemsSource = branches;

        string home = SessionService.CurrentUser?.HomeBranch ?? "";
        if (!string.IsNullOrWhiteSpace(home) && branches.Contains(home))
            BranchPicker.SelectedItem = home;
        else if (branches.Count == 1)
            BranchPicker.SelectedItem = branches[0];
    }

    private async void OnEnterSystemClicked(object sender, EventArgs e)
    {
        if (BranchPicker.SelectedItem is not string selectedBranch)
        {
            await DisplayAlertAsync("Selection Required", "Please select a branch to continue.", "OK");
            return;
        }

        if (!SessionService.CanAccessBranch(selectedBranch))
        {
            await DisplayAlertAsync("Not allowed", "Your account cannot open that branch.", "OK");
            return;
        }

        DatabaseService.SetActiveBranch(selectedBranch);
        SystemCatalog.Log("Select branch", selectedBranch, $"Opened branch {selectedBranch} after login.");
        AppNavigator.Show(new AppShell());
    }
}
