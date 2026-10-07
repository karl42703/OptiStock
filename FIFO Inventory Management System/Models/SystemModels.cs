using SQLite;

namespace FIFO_Inventory_Management_System.Models;

public static class UserRoles
{
    public const string Admin = "Admin";
    public const string Manager = "Manager";
    public const string Staff = "Staff";
    public const string Viewer = "Viewer";

    public static readonly string[] All = [Admin, Manager, Staff, Viewer];
}

public static class AppPermission
{
    public const string ManageUsers = "ManageUsers";
    public const string ManageBranches = "ManageBranches";
    public const string ReceiveStock = "ReceiveStock";
    public const string IssueStock = "IssueStock";
    public const string TransferStock = "TransferStock";
    public const string ViewReports = "ViewReports";
    public const string WipeDatabase = "WipeDatabase";
    public const string ImportExport = "ImportExport";
    public const string ViewAudit = "ViewAudit";
    public const string Print = "Print";
}

public class BranchRecord
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Unique]
    public string Name { get; set; } = "";

    public string CreatedAt { get; set; } = "";
    public int IsActive { get; set; } = 1;
}

public class UserAccount
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Unique]
    public string Username { get; set; } = "";

    public string PasswordHash { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Role { get; set; } = UserRoles.Staff;
    public int IsActive { get; set; } = 1;
    public string HomeBranch { get; set; } = "";
    public string AllowedBranchesCsv { get; set; } = "";
    public string CreatedAt { get; set; } = "";
}

public class AuditLogEntry
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    public string Timestamp { get; set; } = "";
    public string Username { get; set; } = "";
    public string Role { get; set; } = "";
    public string Action { get; set; } = "";
    public string BranchName { get; set; } = "";
    public string Details { get; set; } = "";
}

public class StockTransferRecord
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    public string TransferNumber { get; set; } = "";
    public string FromBranch { get; set; } = "";
    public string ToBranch { get; set; } = "";
    public string Date { get; set; } = "";
    public string CreatedBy { get; set; } = "";
    public string Notes { get; set; } = "";
    public int LineCount { get; set; }
    public int TotalQuantity { get; set; }
    public double TotalCost { get; set; }
}
