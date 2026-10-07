using FIFO_Inventory_Management_System.Models;

namespace FIFO_Inventory_Management_System.Services;

public static class SessionService
{
    public static UserAccount? CurrentUser { get; private set; }

    public static string Username => CurrentUser?.Username ?? "";
    public static string DisplayName => CurrentUser?.DisplayName ?? CurrentUser?.Username ?? "Guest";
    public static string Role => CurrentUser?.Role ?? "";
    public static bool IsSignedIn => CurrentUser is not null;

    public static void SignIn(UserAccount user) => CurrentUser = user;

    public static void SignOut() => CurrentUser = null;

    public static bool Has(string permission)
    {
        if (CurrentUser is null || CurrentUser.IsActive == 0)
            return false;

        return CurrentUser.Role switch
        {
            UserRoles.Admin => true,
            UserRoles.Manager => permission is
                AppPermission.ReceiveStock or
                AppPermission.IssueStock or
                AppPermission.TransferStock or
                AppPermission.ViewReports or
                AppPermission.ImportExport or
                AppPermission.ViewAudit or
                AppPermission.ManageBranches or
                AppPermission.Print,
            UserRoles.Staff => permission is
                AppPermission.IssueStock or
                AppPermission.ReceiveStock or
                AppPermission.ViewReports or
                AppPermission.Print,
            UserRoles.Viewer => permission is
                AppPermission.ViewReports or
                AppPermission.Print,
            _ => false
        };
    }

    public static bool CanAccessBranch(string branchName)
    {
        if (CurrentUser is null)
            return false;
        if (string.Equals(CurrentUser.Role, UserRoles.Admin, StringComparison.Ordinal) ||
            string.Equals(CurrentUser.Role, UserRoles.Manager, StringComparison.Ordinal))
            return true;

        if (string.IsNullOrWhiteSpace(CurrentUser.AllowedBranchesCsv))
            return true;

        return CurrentUser.AllowedBranchesCsv
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(b => string.Equals(b, branchName, StringComparison.OrdinalIgnoreCase));
    }

    public static IReadOnlyList<string> VisibleBranches()
    {
        return BranchNames.All.Where(CanAccessBranch).ToList();
    }
}
