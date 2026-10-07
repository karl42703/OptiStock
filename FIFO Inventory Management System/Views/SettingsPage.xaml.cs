using FIFO_Inventory_Management_System.Models;
using FIFO_Inventory_Management_System.Services;
using FIFO_Inventory_Management_System;

namespace FIFO_Inventory_Management_System.Views;

public partial class SettingsPage : ContentPage
{
    private static readonly FilePickerFileType ZipBackupFileType = new(new Dictionary<DevicePlatform, IEnumerable<string>>
    {
        { DevicePlatform.WinUI, new[] { ".zip" } }
    });

    private DatabaseService _dbService;

    public SettingsPage()
    {
        InitializeComponent();
        _dbService = new DatabaseService();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        DatabaseService.ActiveBranchChanged += OnActiveBranchChanged;
        _dbService = new DatabaseService();
        NewRolePicker.ItemsSource = UserRoles.All;
        NewRolePicker.SelectedItem = UserRoles.Staff;
        RefreshLists();
        ApplyPermissionVisibility();
    }

    protected override void OnDisappearing()
    {
        DatabaseService.ActiveBranchChanged -= OnActiveBranchChanged;
        base.OnDisappearing();
    }

    private void OnActiveBranchChanged(object? sender, EventArgs e)
    {
        _dbService = new DatabaseService();
        RefreshLists();
    }

    private void ApplyPermissionVisibility()
    {
        bool canManageBranches = SessionService.Has(AppPermission.ManageBranches);
        bool canManageUsers = SessionService.Has(AppPermission.ManageUsers);
        bool canManageData = SessionService.Has(AppPermission.ImportExport) || SessionService.Has(AppPermission.WipeDatabase);

        BranchSection.IsVisible = canManageBranches;
        UsersSection.IsVisible = canManageUsers;
        ExportAllBranchesBtn.IsVisible = SessionService.Has(AppPermission.ImportExport);
        ImportAllBranchesBtn.IsVisible = SessionService.Has(AppPermission.ImportExport);
        ExportBtn.IsVisible = SessionService.Has(AppPermission.ImportExport);

        BranchesTabButton.IsVisible = canManageBranches;
        UsersTabButton.IsVisible = canManageUsers;
        DataManagementTabButton.IsVisible = canManageData;

        if (canManageBranches)
        {
            OnBranchesClicked(this, EventArgs.Empty);
        }
        else if (canManageUsers)
        {
            OnUsersClicked(this, EventArgs.Empty);
        }
        else if (canManageData)
        {
            OnDataManagementClicked(this, EventArgs.Empty);
        }
        else
        {
            BranchesTabContent.IsVisible = false;
            UsersTabContent.IsVisible = false;
            DataManagementLayout.IsVisible = false;
        }
    }

    private void RefreshLists()
    {
        BranchList.ItemsSource = BranchNames.All.ToList(); // Changed to pure strings for easier binding
        UsersList.ItemsSource = SystemCatalog.GetUsers();
        if (Application.Current?.Windows.FirstOrDefault()?.Page is AppShell shell)
            shell.RefreshHeader();
    }

    private async void OnAddBranchClicked(object sender, EventArgs e)
    {
        if (!SessionService.Has(AppPermission.ManageBranches))
        {
            await DisplayAlert("Not allowed", "Your role cannot add branches.", "OK");
            return;
        }

        try
        {
            string name = NewBranchEntry.Text?.Trim() ?? "";
            SystemCatalog.AddBranch(name);
            if (string.IsNullOrWhiteSpace(DatabaseService.ActiveBranchName))
                DatabaseService.SetActiveBranch(name);
            NewBranchEntry.Text = "";
            RefreshLists();
            await DisplayAlert("Branch added", $"'{name}' is ready. It will appear on the post-login branch picker.", "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlert("Could not add branch", ex.Message, "OK");
        }
    }

    private async void OnEditBranchClicked(object sender, EventArgs e)
    {
        if (!SessionService.Has(AppPermission.ManageBranches)) return;
        if (sender is not Button { CommandParameter: string branchName }) return;

        string? newName = await DisplayPromptAsync("Edit Branch", "Enter new branch name:", initialValue: branchName);
        if (string.IsNullOrWhiteSpace(newName) || newName == branchName) return;

        try
        {
            SystemCatalog.RenameBranch(branchName, newName);
            if (DatabaseService.ActiveBranchName == branchName)
                DatabaseService.SetActiveBranch(newName);
            
            RefreshLists();
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", ex.Message, "OK");
        }
    }

    private async void OnDeleteBranchClicked(object sender, EventArgs e)
    {
        if (!SessionService.Has(AppPermission.ManageBranches)) return;
        if (sender is not Button { CommandParameter: string branchName }) return;

        bool confirm = await DisplayAlert("Confirm Delete", $"Are you sure you want to delete branch '{branchName}'?", "Yes, Delete", "Cancel");
        if (!confirm) return;

        try
        {
            SystemCatalog.DeleteBranch(branchName);
            
            if (DatabaseService.ActiveBranchName == branchName)
            {
                var nextBranch = BranchNames.All.FirstOrDefault();
                DatabaseService.SetActiveBranch(nextBranch ?? "");
            }

            RefreshLists();
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", ex.Message, "OK");
        }
    }

    private async void OnCreateUserClicked(object sender, EventArgs e)
    {
        if (!SessionService.Has(AppPermission.ManageUsers))
        {
            await DisplayAlert("Not allowed", "Your role cannot manage users.", "OK");
            return;
        }

        try
        {
            SystemCatalog.CreateUser(
                NewUsernameEntry.Text ?? "",
                NewPasswordEntry.Text ?? "",
                NewDisplayNameEntry.Text ?? "",
                NewRolePicker.SelectedItem as string ?? UserRoles.Staff,
                NewHomeBranchEntry.Text ?? "",
                NewAllowedBranchesEntry.Text ?? "");
            NewUsernameEntry.Text = "";
            NewPasswordEntry.Text = "";
            NewDisplayNameEntry.Text = "";
            RefreshLists();
            await DisplayAlert("User created", "They can sign in on the login page.", "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlert("Could not create user", ex.Message, "OK");
        }
    }

    private async void OnResetPasswordClicked(object sender, EventArgs e)
    {
        if (!SessionService.Has(AppPermission.ManageUsers))
            return;
        if (sender is not Button { CommandParameter: UserAccount user })
            return;

        string? pw = await DisplayPromptAsync("Reset password", $"New password for {user.Username}:", "Save", "Cancel");
        if (string.IsNullOrWhiteSpace(pw))
            return;

        SystemCatalog.UpdateUser(user, pw);
        await DisplayAlert("Updated", "Password was reset.", "OK");
    }

    private async void OnToggleUserClicked(object sender, EventArgs e)
    {
        if (!SessionService.Has(AppPermission.ManageUsers))
            return;
        if (sender is not Button { CommandParameter: UserAccount user })
            return;

        user.IsActive = user.IsActive == 1 ? 0 : 1;
        try
        {
            SystemCatalog.UpdateUser(user, null);
            RefreshLists();
        }
        catch (Exception ex)
        {
            await DisplayAlert("Update failed", ex.Message, "OK");
        }
    }

    private async void OnExportDatabaseClicked(object sender, EventArgs e)
    {
        if (!SessionService.Has(AppPermission.ImportExport))
        {
            await DisplayAlert("Not allowed", "Your role cannot export data.", "OK");
            return;
        }

        await RunWithLoadingOverlay("Exporting Excel file...", async () =>
        {
            string savedFileName = await Task.Run(() => _dbService.ExportFullDatabaseBackup());
            await DisplayAlert("Backup Successful",
                $"The current branch has been exported to your Desktop as:\n\n{savedFileName}",
                "OK");
        });
    }

    private async void OnExportAllBranchesClicked(object sender, EventArgs e)
    {
        await RunWithLoadingOverlay("Packing all branch databases...", async () =>
        {
            var result = await Task.Run(DatabaseService.ExportAllBranches);
            string branchList = string.Join("\n", result.ExportedBranches.Select(b => $"  • {b}"));
            await DisplayAlert("Export Successful",
                $"{result.BranchesExported} branch database(s) saved to your Desktop as:\n\n{result.FileName}\n\nIncluded:\n{branchList}\n\nCopy this .zip file to another device and use Import to restore all branches.",
                "OK");
        });
    }

    private async void OnImportAllBranchesClicked(object sender, EventArgs e)
    {
        var pickResult = await FilePicker.Default.PickAsync(new PickOptions
        {
            PickerTitle = "Select branch backup (.zip)",
            FileTypes = ZipBackupFileType
        });

        if (pickResult == null)
            return;

        bool isConfirmed = await DisplayAlert(
            "Import Branch Databases",
            "This will replace existing branch databases on this device with the data from the selected backup file. Continue?",
            "Import",
            "Cancel");

        if (!isConfirmed)
            return;

        await RunWithLoadingOverlay("Restoring branch databases...", async () =>
        {
            var result = await Task.Run(() => DatabaseService.ImportAllBranches(pickResult.FullPath));

            _dbService = new DatabaseService();
            DatabaseService.NotifyDataChanged();

            string branchList = string.Join("\n", result.ImportedBranches.Select(b => $"  • {b}"));
            string missingNote = result.MissingFiles.Count > 0
                ? $"\n\nNote: {result.MissingFiles.Count} file(s) listed in the manifest were missing from the package."
                : "";

            await DisplayAlert("Import Successful",
                $"{result.BranchesImported} branch database(s) restored:\n{branchList}{missingNote}\n\nAll open screens will reload with the imported data.",
                "OK");
        });
    }

    private async void OnResetDatabaseClicked(object sender, EventArgs e)
    {
        if (!SessionService.Has(AppPermission.WipeDatabase))
        {
            await DisplayAlert("Not allowed", "Only an Admin can wipe a branch database.", "OK");
            return;
        }

        bool isConfirmed = await DisplayAlert(
            "CRITICAL WARNING",
            "Are you absolutely sure you want to delete ALL inventory data for the current branch? This cannot be undone.",
            "Yes, WIPE Current Branch",
            "Cancel");

        if (isConfirmed)
        {
            try
            {
                _dbService.ClearAllData();
                DatabaseService.NotifyDataChanged();
                await DisplayAlert("System Reset", "The current branch database has been completely wiped.", "OK");
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", ex.Message, "OK");
            }
        }
    }

    private async Task RunWithLoadingOverlay(string loadingMessage, Func<Task> action)
    {
        try
        {
            LoadingTitleLabel.Text = loadingMessage;
            LoadingLabel.Text = "Please wait, do not close the app.";
            LoadingOverlay.IsVisible = true;
            ExportBtn.IsEnabled = false;
            ExportAllBranchesBtn.IsEnabled = false;
            ImportAllBranchesBtn.IsEnabled = false;

            await action();
        }
        catch (Exception ex)
        {
            await DisplayAlert("Operation Failed", ex.Message, "OK");
        }
        finally
        {
            LoadingOverlay.IsVisible = false;
            ExportBtn.IsEnabled = true;
            ExportAllBranchesBtn.IsEnabled = true;
            ImportAllBranchesBtn.IsEnabled = true;
        }
    }
    private async void OnBranchesClicked(object sender, EventArgs e)
    {
        BranchesTabContent.IsVisible = true;
        UsersTabContent.IsVisible = false;
        DataManagementLayout.IsVisible = false;

        BranchesTabButton.BackgroundColor = Colors.Blue;
        UsersTabButton.BackgroundColor = Colors.Transparent;
        DataManagementTabButton.BackgroundColor = Colors.Transparent;
    }
    private async void OnUsersClicked(object sender, EventArgs e)
    {
        BranchesTabContent.IsVisible = false;
        UsersTabContent.IsVisible = true;
        DataManagementLayout.IsVisible = false;

        BranchesTabButton.BackgroundColor = Colors.Transparent;
        UsersTabButton.BackgroundColor = Colors.Blue;
        DataManagementTabButton.BackgroundColor = Colors.Transparent;
    }
    private async void OnDataManagementClicked(object sender, EventArgs e)
    {
        BranchesTabContent.IsVisible = false;
        UsersTabContent.IsVisible = false;
        DataManagementLayout.IsVisible = true;

        BranchesTabButton.BackgroundColor = Colors.Transparent;
        UsersTabButton.BackgroundColor = Colors.Transparent;
        DataManagementTabButton.BackgroundColor = Colors.Blue;
    }
}
