using FIFO_Inventory_Management_System.Services;
using FIFO_Inventory_Management_System.Views;
using FIFO_Inventory_Management_System.Models;

namespace FIFO_Inventory_Management_System
{
    public partial class AppShell : Shell
    {
        
       
        private bool _branchEventsEnabled;

        public AppShell()
        {
            InitializeComponent();
            if (Application.Current is not null)
                Application.Current.UserAppTheme = AppTheme.Dark;
            SetupHeader();
            SystemCatalog.BranchListChanged += OnBranchListChanged;
        }

        private void SetupHeader()
        {
            BranchPicker.SelectedIndexChanged += OnHeaderBranchPickerChanged;
            RefreshHeader();
        }

        private void OnBranchListChanged(object? sender, EventArgs e) =>
            MainThread.BeginInvokeOnMainThread(RefreshHeader);

        public void RefreshHeader()
        {
            _branchEventsEnabled = false;
            var branches = SessionService.VisibleBranches().ToList();
            BranchPicker.ItemsSource = branches;

            if (branches.Count == 0)
            {
                BranchPicker.SelectedItem = null;
                BranchPicker.IsEnabled = false;
                BranchPicker.Title = "Add a branch in Settings";
            }
            else
            {
                BranchPicker.IsEnabled = branches.Count > 1;
                if (branches.Contains(DatabaseService.ActiveBranchName))
                    BranchPicker.SelectedItem = DatabaseService.ActiveBranchName;
                else
                {
                    BranchPicker.SelectedItem = branches[0];
                    DatabaseService.SetActiveBranch(branches[0]);
                }
            }

            UserLabel.Text = SessionService.IsSignedIn
                ? $"{SessionService.DisplayName}  ({SessionService.Role})"
                : "";

            // Page Visibility Based on Permissions
            NavDashboard.IsVisible = true; 
            NavWithdraw.IsVisible = SessionService.Has(AppPermission.IssueStock);
            NavReceive.IsVisible = SessionService.Has(AppPermission.ReceiveStock);
            NavTransfer.IsVisible = SessionService.Has(AppPermission.TransferStock);
            NavReports.IsVisible = SessionService.Has(AppPermission.ViewReports);
            
            // Logs tab is visible to all roles
            NavLogs.IsVisible = true; 

            // Settings is only visible to Admin and Manager
            NavSettings.IsVisible = SessionService.Role == UserRoles.Admin || SessionService.Role == UserRoles.Manager;

            _branchEventsEnabled = true;
        }

        private void OnHeaderBranchPickerChanged(object? sender, EventArgs e)
        {
            if (!_branchEventsEnabled)
                return;
            if (BranchPicker.SelectedItem is not string branch)
                return;
            if (!SessionService.CanAccessBranch(branch))
                return;
            DatabaseService.SetActiveBranch(branch);
            SystemCatalog.Log("Switch branch", branch, $"Active branch set to {branch}.");
        }

        private async void OnLogoutClicked(object? sender, EventArgs e)
        {
            bool ok = await DisplayAlert("Sign out", "Return to the login screen?", "Sign out", "Cancel");
            if (!ok)
                return;

            SystemCatalog.Log("Logout", DatabaseService.ActiveBranchName, $"Signed out {SessionService.Username}.");
            SystemCatalog.BranchListChanged -= OnBranchListChanged;
            SessionService.SignOut();
            DatabaseService.SetActiveBranch("");
            AppNavigator.Show(new LoginPage());
        }
    }
}
