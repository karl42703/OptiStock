using FIFO_Inventory_Management_System.Services;

namespace FIFO_Inventory_Management_System.Views;

public partial class LoginPage : ContentPage
{
    public LoginPage()
    {
        InitializeComponent();
        bool hasUsers = SystemCatalog.GetUsers().Count > 0;
        HintLabel.Text = hasUsers
            ? "Default first-run account is admin / admin123 until you change it in Settings."
            : "";
        UsernameEntry.Text = SystemCatalog.DefaultAdminUsername;
    }

    private async void OnLoginClicked(object sender, EventArgs e)
    {
        string username = UsernameEntry.Text?.Trim() ?? "";
        string password = PasswordEntry.Text ?? "";

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            await DisplayAlertAsync("Sign in", "Enter your username and password.", "OK");
            return;
        }

        var user = SystemCatalog.Authenticate(username, password);
        if (user is null)
        {
            SystemCatalog.Log("Failed login", "", $"Failed sign-in attempt for '{username}'.");
            await DisplayAlertAsync("Sign in failed", "Invalid username or password.", "OK");
            return;
        }

        SessionService.SignIn(user);
        SystemCatalog.Log("Login", "", $"Signed in as {user.Username} ({user.Role}).");

        var branches = SessionService.VisibleBranches();
        if (branches.Count == 0)
        {
            DatabaseService.SetActiveBranch("");
            AppNavigator.Show(new AppShell());
            return;
        }

        AppNavigator.Show(new BranchSelectionPage());
    }
}
