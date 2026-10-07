using FIFO_Inventory_Management_System.Models;
using SQLite;

namespace FIFO_Inventory_Management_System.Services;

/// <summary>
/// Shared catalog (users, branches, audit, transfers) stored separately from per-branch inventory files.
/// </summary>
public static class SystemCatalog
{
    private static readonly object Gate = new();
    private static SQLiteConnection? _db;

    public static event EventHandler? BranchListChanged;

    public const string DefaultAdminUsername = "admin";
    public const string DefaultAdminPassword = "admin123";

    public static string GetSystemDatabasePath() =>
        Path.Combine(DatabaseService.GetDatabaseDirectory(), "OptiStock_System.db3");

    private static SQLiteConnection Db
    {
        get
        {
            if (_db is not null)
                return _db;

            lock (Gate)
            {
                if (_db is not null)
                    return _db;

                Directory.CreateDirectory(DatabaseService.GetDatabaseDirectory());
                _db = new SQLiteConnection(GetSystemDatabasePath());
                _db.CreateTable<BranchRecord>();
                _db.CreateTable<UserAccount>();
                _db.CreateTable<AuditLogEntry>();
                _db.CreateTable<StockTransferRecord>();
                SeedDefaultAdmin();
                return _db;
            }
        }
    }

    private static void SeedDefaultAdmin()
    {
        if (Db.Table<UserAccount>().Any())
            return;

        Db.Insert(new UserAccount
        {
            Username = DefaultAdminUsername,
            PasswordHash = PasswordHasher.Hash(DefaultAdminPassword),
            DisplayName = "Administrator",
            Role = UserRoles.Admin,
            IsActive = 1,
            CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
        });
    }

    public static IReadOnlyList<string> GetBranchNames()
    {
        lock (Gate)
        {
            return Db.Table<BranchRecord>()
                .Where(b => b.IsActive == 1)
                .OrderBy(b => b.Name)
                .Select(b => b.Name)
                .ToList();
        }
    }

    public static BranchRecord? FindBranch(string name)
    {
        lock (Gate)
        {
            return Db.Table<BranchRecord>()
                .FirstOrDefault(b => b.Name == name && b.IsActive == 1);
        }
    }

    public static BranchRecord AddBranch(string name)
    {
        name = name.Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Branch name is required.");

        lock (Gate)
        {
            if (Db.Table<BranchRecord>().Any(b => b.Name.ToLower() == name.ToLower() && b.IsActive == 1))
                throw new InvalidOperationException($"Branch '{name}' already exists.");

            var record = new BranchRecord
            {
                Name = name,
                CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                IsActive = 1
            };
            Db.Insert(record);
        }

        DatabaseService.EnsureBranchDatabaseReady(name);
        Log("Add branch", name, $"Created branch '{name}'.");
        BranchListChanged?.Invoke(null, EventArgs.Empty);
        return FindBranch(name)!;
    }

    public static void RenameBranch(string oldName, string newName)
    {
        newName = newName?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(newName))
            throw new ArgumentException("Branch name cannot be empty.");

        lock (Gate)
        {
            var branch = Db.Table<BranchRecord>().FirstOrDefault(b => b.Name == oldName && b.IsActive == 1);
            if (branch == null)
                throw new InvalidOperationException($"Branch '{oldName}' not found.");

            if (Db.Table<BranchRecord>().Any(b => b.Name.ToLower() == newName.ToLower() && b.IsActive == 1 && b.Id != branch.Id))
                throw new InvalidOperationException($"Branch '{newName}' already exists.");

            branch.Name = newName;
            Db.Update(branch);
        }

        Log("Rename branch", newName, $"Renamed branch '{oldName}' to '{newName}'.");
        BranchListChanged?.Invoke(null, EventArgs.Empty);
    }

    public static void DeleteBranch(string name)
    {
        lock (Gate)
        {
            var branch = Db.Table<BranchRecord>().FirstOrDefault(b => b.Name == name && b.IsActive == 1);
            if (branch == null)
                throw new InvalidOperationException($"Branch '{name}' not found.");

            branch.IsActive = 0; // Soft delete
            Db.Update(branch);
        }

        Log("Delete branch", name, $"Deleted branch '{name}'.");
        BranchListChanged?.Invoke(null, EventArgs.Empty);
    }


    public static void RegisterBranchIfMissing(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return;

        lock (Gate)
        {
            if (Db.Table<BranchRecord>().Any(b => b.Name == name && b.IsActive == 1))
                return;
        }

        try
        {
            AddBranch(name);
        }
        catch (InvalidOperationException)
        {
            // Already exists after a race; ignore.
        }
    }

    public static UserAccount? Authenticate(string username, string password)
    {
        lock (Gate)
        {
            var cleanUsername = username.Trim().ToLower();
            var user = Db.Table<UserAccount>()
                .FirstOrDefault(u => u.Username.ToLower() == cleanUsername);

            if (user is null || user.IsActive == 0)
                return null;
            if (!PasswordHasher.Verify(password, user.PasswordHash))
                return null;
            return user;
        }
    }

    public static List<UserAccount> GetUsers()
    {
        lock (Gate)
        {
            return Db.Table<UserAccount>().OrderBy(u => u.Username).ToList();
        }
    }

    public static UserAccount CreateUser(string username, string password, string displayName, string role, string homeBranch, string allowedBranchesCsv)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            throw new ArgumentException("Username and password are required.");
        if (!UserRoles.All.Contains(role))
            throw new ArgumentException("Invalid role.");

        username = username.Trim();

        lock (Gate)
        {
            if (Db.Table<UserAccount>().Any(u => u.Username.ToLower() == username.ToLower()))
                throw new InvalidOperationException("That username is already taken.");

            var user = new UserAccount
            {
                Username = username,
                PasswordHash = PasswordHasher.Hash(password),
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? username : displayName.Trim(),
                Role = role,
                IsActive = 1,
                HomeBranch = homeBranch?.Trim() ?? "",
                AllowedBranchesCsv = allowedBranchesCsv?.Trim() ?? "",
                CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            };
            Db.Insert(user);
            Log("Create user", DatabaseService.ActiveBranchName, $"Created user '{username}' with role {role}.");
            return user;
        }
    }

    public static void UpdateUser(UserAccount user, string? newPassword)
    {
        lock (Gate)
        {
            var existing = Db.Find<UserAccount>(user.Id)
                ?? throw new InvalidOperationException("User not found.");

            if (existing.Username.Equals(SystemCatalog.DefaultAdminUsername, StringComparison.OrdinalIgnoreCase)
                && user.IsActive == 0)
                throw new InvalidOperationException("The default admin account cannot be deactivated.");

            existing.DisplayName = user.DisplayName;
            existing.Role = user.Role;
            existing.IsActive = user.IsActive;
            existing.HomeBranch = user.HomeBranch;
            existing.AllowedBranchesCsv = user.AllowedBranchesCsv;
            if (!string.IsNullOrWhiteSpace(newPassword))
                existing.PasswordHash = PasswordHasher.Hash(newPassword);

            Db.Update(existing);
            Log("Update user", DatabaseService.ActiveBranchName, $"Updated user '{existing.Username}'.");
        }
    }

    public static void Log(string action, string branchName, string details)
    {
        lock (Gate)
        {
            Db.Insert(new AuditLogEntry
            {
                Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                Username = SessionService.Username,
                Role = SessionService.Role,
                Action = action,
                BranchName = branchName ?? "",
                Details = details ?? ""
            });
        }
    }

    public static List<AuditLogEntry> GetAuditLogs(int limit = 500)
    {
        lock (Gate)
        {
            return Db.Table<AuditLogEntry>()
                .OrderByDescending(a => a.Id)
                .Take(limit)
                .ToList();
        }
    }

    public static void SaveTransfer(StockTransferRecord record)
    {
        lock (Gate)
        {
            Db.Insert(record);
        }
    }

    public static List<StockTransferRecord> GetTransfers()
    {
        lock (Gate)
        {
            return Db.Table<StockTransferRecord>()
                .OrderByDescending(t => t.Id)
                .ToList();
        }
    }
}
