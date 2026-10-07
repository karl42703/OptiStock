using ClosedXML.Excel;
using FIFO_Inventory_Management_System.Models;
using SQLite;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;

namespace FIFO_Inventory_Management_System.Services
{
    
    public class DatabaseService
    {
        public static string ActiveBranchName { get; set; } = "";

        /// <summary>Fired when the active branch changes (header picker or login).</summary>
        public static event EventHandler? ActiveBranchChanged;

        public static void SetActiveBranch(string name)
        {
            name ??= "";
            if (string.Equals(ActiveBranchName, name, StringComparison.Ordinal))
                return;
            ActiveBranchName = name;
            ActiveBranchChanged?.Invoke(null, EventArgs.Empty);
        }

        /// <summary>Notifies listeners to reload data without changing the active branch.</summary>
        public static void NotifyDataChanged()
        {
            ActiveBranchChanged?.Invoke(null, EventArgs.Empty);
        }

        public const string BackupManifestFileName = "manifest.json";
        public const string BackupPackagePrefix = "FIFO_Inventory_AllBranches_";

        public static string SanitizeBranchName(string branchName) =>
            branchName.Replace(" ", "_").Replace(".", "").Replace("-", "");

        public static string GetLegacyDatabaseFileName(string branchName) =>
            $"Inventory_{SanitizeBranchName(branchName)}.db3";

        public static string GetDatabaseFileName(string branchName)
        {
            var branch = SystemCatalog.FindBranch(branchName);
            if (branch is not null)
                return $"Inventory_B{branch.Id:D2}_{SanitizeBranchName(branchName)}.db3";

            return GetLegacyDatabaseFileName(branchName);
        }

        public static string GetDatabaseDirectory() =>
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        public static string GetDatabasePath(string branchName) =>
            Path.Combine(GetDatabaseDirectory(), GetDatabaseFileName(branchName));

        /// <summary>
        /// Ensures each branch uses its own database file and migrates legacy filenames once.
        /// </summary>
        public static void EnsureBranchDatabaseReady(string branchName)
        {
            string dbDir = GetDatabaseDirectory();
            Directory.CreateDirectory(dbDir);

            string targetPath = GetDatabasePath(branchName);
            if (File.Exists(targetPath))
                return;

            string legacyPath = Path.Combine(dbDir, GetLegacyDatabaseFileName(branchName));
            if (File.Exists(legacyPath))
            {
                File.Move(legacyPath, targetPath);
                return;
            }

            foreach (var otherBranch in BranchNames.All)
            {
                if (string.Equals(otherBranch, branchName, StringComparison.Ordinal))
                    continue;

                if (!string.Equals(
                        GetLegacyDatabaseFileName(otherBranch),
                        GetLegacyDatabaseFileName(branchName),
                        StringComparison.OrdinalIgnoreCase))
                    continue;

                string otherLegacyPath = Path.Combine(dbDir, GetLegacyDatabaseFileName(otherBranch));
                if (File.Exists(otherLegacyPath))
                    throw new InvalidOperationException(
                        $"Database filename collision detected between '{branchName}' and '{otherBranch}'. Contact support.");
            }
        }

        public class BranchBackupEntry
        {
            public string BranchName { get; set; } = "";
            public string DatabaseFileName { get; set; } = "";
        }

        public class BranchBackupManifest
        {
            public string ExportDate { get; set; } = "";
            public string AppVersion { get; set; } = "1.0";
            public List<BranchBackupEntry> Branches { get; set; } = new();
        }

        public class BranchExportResult
        {
            public string FileName { get; set; } = "";
            public string FullPath { get; set; } = "";
            public int BranchesExported { get; set; }
            public List<string> ExportedBranches { get; set; } = new();
        }

        public class BranchImportResult
        {
            public int BranchesImported { get; set; }
            public List<string> ImportedBranches { get; set; } = new();
            public List<string> MissingFiles { get; set; } = new();
        }

        private SQLiteConnection _db;

        public string BranchName { get; }

        private class RawPoData
        {
            public string Item_Code { get; set; }
            public string Name { get; set; }
            public string Unit { get; set; }
            public int Quantity { get; set; }
            public double Unit_Price { get; set; }
            public double Retail_Price { get; set; }
        }

        private class RawRisData
        {
            public string Item_Code { get; set; }
            public string Name { get; set; }
            public string Unit { get; set; }
            public int Quantity_Moved { get; set; }
            public double Unit_Price { get; set; }
            public double Retail_Price { get; set; }
            public string Supplier_Name { get; set; }
        }

            private class RawBatchData
        {
            public string Item_Code { get; set; }
            public string Name { get; set; }
            public string Unit { get; set; }
            public string Barcode { get; set; }
            public int Remaining_Qty { get; set; }
            public double Unit_Price { get; set; }
            public string Supplier_Name { get; set; }
            public string PO_Number { get; set; }
        }

        public DatabaseService(string? branchName = null)
        {
            BranchName = string.IsNullOrWhiteSpace(branchName) ? ActiveBranchName : branchName;

            if (string.IsNullOrWhiteSpace(BranchName))
            {
                _db = new SQLiteConnection(":memory:");
            }
            else
            {
                EnsureBranchDatabaseReady(BranchName);
                _db = new SQLiteConnection(GetDatabasePath(BranchName));
            }

            _db.Execute("PRAGMA foreign_keys = ON;");
            _db.CreateTable<Item>();
            _db.CreateTable<Purchase_Order>();
            _db.CreateTable<PO_Line_Item>();
            _db.CreateTable<Issuance>();
            _db.CreateTable<Transaction>();
            EnsureSchema();
        }

        private class SqliteColumnInfo
        {
            public string name { get; set; } = "";
        }

        private void EnsureSchema()
        {
            EnsureColumn("Item", "Barcode", "TEXT");
            EnsureColumn("Purchase_Order", "Created_By", "TEXT");
            EnsureColumn("Issuance", "Created_By", "TEXT");
            EnsureColumn("\"Transaction\"", "Created_By", "TEXT");
        }

        private void EnsureColumn(string table, string column, string type)
        {
            var cols = _db.Query<SqliteColumnInfo>($"PRAGMA table_info({table})");
            if (cols.Any(c => string.Equals(c.name, column, StringComparison.OrdinalIgnoreCase)))
                return;
            _db.Execute($"ALTER TABLE {table} ADD COLUMN {column} {type}");
        }

        // --- THE FIFO LOGIC ---
        public class TransactionRequest
        {
            public string Item_Code { get; set; } = "";
            public int Quantity { get; set; }
        }

        // UPDATED: Now returns a list of detailed withdrawals for the Excel Sheet
        public List<FifoWithdrawalDetail> ProcessIssuance(Issuance header, List<TransactionRequest> lines)
        {
            if (lines == null || lines.Count == 0)
                throw new ArgumentException("At least one line item is required.");

            var detailedWithdrawals = new List<FifoWithdrawalDetail>();

            _db.BeginTransaction();

            try
            {
                if (string.IsNullOrWhiteSpace(header.Created_By))
                    header.Created_By = SessionService.Username;

                _db.Insert(header);

                foreach (var line in lines)
                {
                    if (line.Quantity <= 0)
                        throw new Exception($"Invalid quantity for item {line.Item_Code}.");

                    // 1. Get the Master Item details (for the Name and Unit)
                    var itemMaster = _db.Find<Item>(line.Item_Code);
                    string itemName = itemMaster?.Name ?? "Unknown Item";
                    string itemUnit = itemMaster?.Unit ?? "";

                    // 2. Fetch the available batches, perfectly ordered by the OLDEST PO Date first
                    var query = @"
                        SELECT line.* FROM PO_Line_Item line
                        INNER JOIN Purchase_Order po ON line.PO_Number = po.PO_Number
                        WHERE line.Item_Code = ? AND line.Remaining_Qty > 0
                        ORDER BY po.Date_Received ASC";

                    var availableBatches = _db.Query<PO_Line_Item>(query, line.Item_Code);

                    // 3. Verify we have enough total stock
                    int totalAvailable = availableBatches.Sum(b => b.Remaining_Qty);
                    if (line.Quantity > totalAvailable)
                        throw new Exception($"Insufficient stock for item {line.Item_Code}.");

                    int remainingNeeded = line.Quantity;

                    // 4. Run the FIFO Deduction Algorithm
                    foreach (var batch in availableBatches)
                    {
                        if (remainingNeeded <= 0) break; // Done with this item!

                        int takeAmount = Math.Min(batch.Remaining_Qty, remainingNeeded);

                        // Deduct from batch
                        batch.Remaining_Qty -= takeAmount;
                        _db.Update(batch);

                        // Log to your Transaction table
                        var trans = new Transaction
                        {
                            RIS_Number = header.RIS_Number,
                            Item_Code = line.Item_Code,
                            Batch_ID_Affected = batch.Batch_ID,
                            Quantity_Moved = takeAmount,
                            Date_Logged = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                            Created_By = SessionService.Username
                        };
                        _db.Insert(trans);

                        // Fetch the Supplier Name from the PO table
                        var po = _db.Find<Purchase_Order>(batch.PO_Number);
                        string supplierName = po?.Supplier_Name ?? "Unknown Supplier";

                        // Add to our detailed Print breakdown
                        detailedWithdrawals.Add(new FifoWithdrawalDetail
                        {
                            Item_Code = line.Item_Code,
                            Name = itemName,
                            Unit = itemUnit,
                            Quantity_Taken = takeAmount,
                            Unit_Price = batch.Unit_Price,
                            Supplier_Name = supplierName,
                            PO_Number = batch.PO_Number
                        });

                        remainingNeeded -= takeAmount;
                    }
                }

                _db.Commit();

                SystemCatalog.Log(
                    "Issue stock",
                    BranchName,
                    $"{header.RIS_Number} issued {lines.Sum(l => l.Quantity)} unit(s) to {header.Office_Name}.");

                return detailedWithdrawals;
            }
            catch (Exception ex)
            {
                _db.Rollback();
                throw new Exception("Failed to process issuance: " + ex.Message);
            }
        }

        public List<FifoWithdrawalDetail> PreviewFifoWithdrawal(string itemCode, int quantityNeeded)
        {
            var query = @"
                SELECT line.* FROM PO_Line_Item line
                INNER JOIN Purchase_Order po ON line.PO_Number = po.PO_Number
                WHERE line.Item_Code = ? AND line.Remaining_Qty > 0
                ORDER BY po.Date_Received ASC";

            var availableBatches = _db.Query<PO_Line_Item>(query, itemCode);

            int totalAvailable = availableBatches.Sum(b => b.Remaining_Qty);
            if (quantityNeeded > totalAvailable)
                throw new Exception($"Only {totalAvailable} units available in stock right now.");

            var previewDetails = new List<FifoWithdrawalDetail>();
            int remainingNeeded = quantityNeeded;

            foreach (var batch in availableBatches)
            {
                if (remainingNeeded <= 0) break;

                int takeAmount = Math.Min(batch.Remaining_Qty, remainingNeeded);
                var po = _db.Find<Purchase_Order>(batch.PO_Number);

                previewDetails.Add(new FifoWithdrawalDetail
                {
                    Item_Code = itemCode,
                    Quantity_Taken = takeAmount,
                    Unit_Price = batch.Unit_Price,
                    Supplier_Name = po?.Supplier_Name ?? "Unknown Supplier"
                });

                remainingNeeded -= takeAmount;
            }

            return previewDetails;
        }

        // --- INCOMING STOCK LOGIC ---
        public void ProcessIncomingStock(Purchase_Order newPO, List<IncomingItemRequest> incomingItems)
        {
            if (string.IsNullOrWhiteSpace(newPO.Created_By))
                newPO.Created_By = SessionService.Username;

            _db.BeginTransaction();

            try
            {
                _db.Insert(newPO);

                foreach (var req in incomingItems)
                {
                    var existingItem = _db.Find<Item>(req.Item_Code);

                    if (existingItem == null)
                    {
                        var newItem = new Item
                        {
                            Item_Code = req.Item_Code,
                            Name = req.Name,
                            Unit = req.Unit,
                            Barcode = req.Barcode
                        };
                        _db.Insert(newItem);
                    }
                    else if (!string.IsNullOrWhiteSpace(req.Barcode) && existingItem.Barcode != req.Barcode)
                    {
                        existingItem.Barcode = req.Barcode;
                        _db.Update(existingItem);
                    }

                    var batch = new PO_Line_Item
                    {
                        PO_Number = newPO.PO_Number,
                        Item_Code = req.Item_Code,
                        Original_Qty = req.Quantity,
                        Remaining_Qty = req.Quantity,
                        Unit_Price = req.Unit_Price,
                        Retail_Price = req.Retail_Price
                    };

                    _db.Insert(batch);
                }

                _db.Commit();

                SystemCatalog.Log(
                    "Receive stock",
                    BranchName,
                    $"{newPO.PO_Number} received {incomingItems.Sum(i => i.Quantity)} unit(s) from {newPO.Supplier_Name}.");
            }
            catch (Exception ex)
            {
                _db.Rollback();
                throw new Exception("Failed to save Purchase Order: " + ex.Message);
            }
        }

        // Retrieves all items for the search dropdown
        public List<Item> GetAllItems()
        {
            var query = @"
                SELECT MIN(Item_Code) AS Item_Code, Name, Unit, MAX(Barcode) AS Barcode
                FROM Item 
                GROUP BY Name, Unit 
                ORDER BY Name ASC";

            return _db.Query<Item>(query);
        }

        public class StockBreakdown
        {
            public string SupplierName { get; set; }
            public string PONumber { get; set; }
            public int RemainingQuantity { get; set; }
            public double UnitPrice { get; set; }

            // Calculates the value of just this specific remaining batch
            public double TotalBatchValue => RemainingQuantity * UnitPrice;
            public string FormattedBatchValue => $"PHP {TotalBatchValue:N2}";
        }

        public class InventoryItem : System.ComponentModel.INotifyPropertyChanged
        {
            public string Item_Code { get; set; }
            public string Name { get; set; }
            public string Unit { get; set; }
            public string Barcode { get; set; }
            public int TotalQuantity { get; set; }
            public double TotalValue { get; set; }
            public string FormattedTotalValue => $"Total Value: PHP {TotalValue:N2}";

            // NEW: Holds the breakdown of the specific POs
            public List<StockBreakdown> Batches { get; set; }

            // NEW: Controls the UI Expand/Collapse
            private bool _isExpanded;
            public bool IsExpanded
            {
                get => _isExpanded;
                set
                {
                    _isExpanded = value;
                    PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsExpanded)));
                }
            }
            public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        }

        // --- DASHBOARD LOGIC ---
        public List<InventoryItem> GetCurrentInventory()
        {
            // Only fetch batches where Remaining_Qty is greater than 0
            var query = @"
                SELECT line.Item_Code, i.Name, i.Unit, i.Barcode, line.Remaining_Qty, line.Unit_Price, po.Supplier_Name, po.PO_Number
                FROM PO_Line_Item line
                INNER JOIN Item i ON line.Item_Code = i.Item_Code
                INNER JOIN Purchase_Order po ON line.PO_Number = po.PO_Number
                WHERE line.Remaining_Qty > 0";

            var rawBatches = _db.Query<RawBatchData>(query);

            // Group the raw batches by the actual Item
            var groupedItems = rawBatches.GroupBy(x => new { x.Item_Code, x.Name, x.Unit, x.Barcode }).ToList();

            var inventoryList = new List<InventoryItem>();

            foreach (var group in groupedItems)
            {
                // Map the raw data into our clean Breakdown list
                var batchList = group.Select(b => new StockBreakdown
                {
                    SupplierName = b.Supplier_Name,
                    PONumber = b.PO_Number,
                    RemainingQuantity = b.Remaining_Qty,
                    UnitPrice = b.Unit_Price
                }).ToList();

                inventoryList.Add(new InventoryItem
                {
                    Item_Code = group.Key.Item_Code,
                    Name = group.Key.Name,
                    Unit = group.Key.Unit,
                    Barcode = group.Key.Barcode,
                    TotalQuantity = batchList.Sum(b => b.RemainingQuantity),
                    TotalValue = batchList.Sum(b => b.TotalBatchValue),
                    Batches = batchList,
                    IsExpanded = false // Default to collapsed
                });
            }

            return inventoryList.OrderBy(x => x.Name).ToList();
        }

        // --- FETCH ALL UNIQUE ITEMS FOR AUTOCOMPLETE ---
        public List<Item> GetAllUniqueItems()
        {
            // Pulls everything from the master Item table
            return _db.Table<Item>().ToList();
        }

        // --- DATABASE BACKUP EXPORT ---
        public string ExportFullDatabaseBackup()
        {
            string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            string fileName = $"FIFO_Inventory_System_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.xlsx";
            string fullPath = Path.Combine(desktopPath, fileName);

            using (var workbook = new XLWorkbook())
            {
                // InsertTable automatically reads the database columns and formats them as a beautiful Excel table!
                workbook.Worksheets.Add("Items").Cell(1, 1).InsertTable(_db.Table<Item>().ToList(), "ItemsTable");
                workbook.Worksheets.Add("Purchase_Orders").Cell(1, 1).InsertTable(_db.Table<Purchase_Order>().ToList(), "POTable");
                workbook.Worksheets.Add("PO_Line_Items").Cell(1, 1).InsertTable(_db.Table<PO_Line_Item>().ToList(), "POLineTable");
                workbook.Worksheets.Add("Issuances").Cell(1, 1).InsertTable(_db.Table<Issuance>().ToList(), "IssuanceTable");
                workbook.Worksheets.Add("Transactions").Cell(1, 1).InsertTable(_db.Table<Transaction>().ToList(), "TransactionTable");

                // Auto-stretch columns so text isn't squished
                foreach (var sheet in workbook.Worksheets)
                {
                    sheet.Columns().AdjustToContents();
                }

                workbook.SaveAs(fullPath);
            }

            return fileName; // Return the filename so the UI knows it worked
        }

        /// <summary>
        /// Packages every branch SQLite database into a single .zip file for use on another device.
        /// </summary>
        public static BranchExportResult ExportAllBranches()
        {
            string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            string fileName = $"{BackupPackagePrefix}{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.zip";
            string fullPath = Path.Combine(desktopPath, fileName);
            string dbDir = GetDatabaseDirectory();
            string tempDir = Path.Combine(Path.GetTempPath(), $"fifo-export-{Guid.NewGuid():N}");

            var manifest = new BranchBackupManifest
            {
                ExportDate = DateTime.UtcNow.ToString("o"),
                AppVersion = "1.0"
            };
            var exportedBranches = new List<string>();
            var copiedFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                Directory.CreateDirectory(tempDir);

                foreach (var branchName in BranchNames.All)
                {
                    string dbPath = GetDatabasePath(branchName);
                    if (!File.Exists(dbPath))
                        continue;

                    string dbFileName = GetDatabaseFileName(branchName);
                    File.Copy(dbPath, Path.Combine(tempDir, dbFileName), overwrite: true);
                    manifest.Branches.Add(new BranchBackupEntry
                    {
                        BranchName = branchName,
                        DatabaseFileName = dbFileName
                    });
                    exportedBranches.Add(branchName);
                    copiedFileNames.Add(dbFileName);
                }

                foreach (var file in Directory.GetFiles(dbDir, "Inventory_*.db3"))
                {
                    string dbFileName = Path.GetFileName(file);
                    if (copiedFileNames.Contains(dbFileName))
                        continue;

                    File.Copy(file, Path.Combine(tempDir, dbFileName), overwrite: true);
                    manifest.Branches.Add(new BranchBackupEntry
                    {
                        BranchName = dbFileName,
                        DatabaseFileName = dbFileName
                    });
                    exportedBranches.Add(dbFileName);
                    copiedFileNames.Add(dbFileName);
                }

                if (manifest.Branches.Count == 0)
                    throw new InvalidOperationException("No branch databases found on this device. Add inventory data to at least one branch before exporting.");

                string manifestJson = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(Path.Combine(tempDir, BackupManifestFileName), manifestJson);

                if (File.Exists(fullPath))
                    File.Delete(fullPath);

                ZipFile.CreateFromDirectory(tempDir, fullPath);

                return new BranchExportResult
                {
                    FileName = fileName,
                    FullPath = fullPath,
                    BranchesExported = exportedBranches.Count,
                    ExportedBranches = exportedBranches
                };
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, recursive: true);
            }
        }

        /// <summary>
        /// Restores branch SQLite databases from a portable .zip package created by ExportAllBranches.
        /// </summary>
        public static BranchImportResult ImportAllBranches(string zipFilePath)
        {
            if (string.IsNullOrWhiteSpace(zipFilePath) || !File.Exists(zipFilePath))
                throw new FileNotFoundException("Backup file not found.");

            string tempDir = Path.Combine(Path.GetTempPath(), $"fifo-import-{Guid.NewGuid():N}");
            string dbDir = GetDatabaseDirectory();
            var importedBranches = new List<string>();
            var missingFiles = new List<string>();

            try
            {
                Directory.CreateDirectory(tempDir);
                Directory.CreateDirectory(dbDir);
                ZipFile.ExtractToDirectory(zipFilePath, tempDir);

                var manifest = ReadBackupManifest(tempDir);
                var entries = manifest?.Branches ?? DiscoverDatabaseEntries(tempDir);

                if (entries.Count == 0)
                    throw new InvalidOperationException("The selected file does not contain any branch databases.");

                foreach (var entry in entries)
                {
                    string sourcePath = Path.Combine(tempDir, entry.DatabaseFileName);
                    if (!File.Exists(sourcePath))
                    {
                        missingFiles.Add(entry.DatabaseFileName);
                        continue;
                    }

                    string destPath = Path.Combine(dbDir, entry.DatabaseFileName);
                    File.Copy(sourcePath, destPath, overwrite: true);
                    importedBranches.Add(entry.BranchName);
                    if (!string.IsNullOrWhiteSpace(entry.BranchName)
                        && !entry.BranchName.StartsWith("Inventory_", StringComparison.OrdinalIgnoreCase))
                    {
                        SystemCatalog.RegisterBranchIfMissing(entry.BranchName);
                    }
                }

                if (importedBranches.Count == 0)
                    throw new InvalidOperationException("Could not restore any branch databases from the selected file.");

                return new BranchImportResult
                {
                    BranchesImported = importedBranches.Count,
                    ImportedBranches = importedBranches,
                    MissingFiles = missingFiles
                };
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, recursive: true);
            }
        }

        private static BranchBackupManifest? ReadBackupManifest(string extractedFolder)
        {
            string manifestPath = Path.Combine(extractedFolder, BackupManifestFileName);
            if (!File.Exists(manifestPath))
                return null;

            return JsonSerializer.Deserialize<BranchBackupManifest>(File.ReadAllText(manifestPath));
        }

        private static List<BranchBackupEntry> DiscoverDatabaseEntries(string extractedFolder)
        {
            return Directory.GetFiles(extractedFolder, "Inventory_*.db3")
                .Select(file => new BranchBackupEntry
                {
                    BranchName = Path.GetFileNameWithoutExtension(file),
                    DatabaseFileName = Path.GetFileName(file)
                })
                .ToList();
        }

        public class IncomingItemRequest
        {
            public string Item_Code { get; set; }
            public string Name { get; set; }
            public string Unit { get; set; }
            public string Barcode { get; set; }
            public int Quantity { get; set; }
            public double Unit_Price { get; set; }
            public double Retail_Price { get; set; }
        }

        public class InventorySummary
        {
            public string Item_Code { get; set; }
            public string Name { get; set; }
            public string Unit { get; set; }
            public int TotalQuantity { get; set; }
            public double TotalValue { get; set; }
        }

        // --- LOG MODELS ---
        public class LogDetail
        {
            public string Item_Code { get; set; }
            public string Name { get; set; }
            public string Unit { get; set; }
            public int Quantity { get; set; }

            public double Price { get; set; }
            public double Retail_Price { get; set; }

            public string BreakdownText { get; set; }

            public double TotalRowPrice { get; set; }
            public double TotalRowRetailPrice { get; set; }
            public double TotalRowProfit => TotalRowRetailPrice - TotalRowPrice;
            public bool HasRetailPrice => TotalRowRetailPrice > 0;

            public string FormattedRowPrice => $"Purch: ₱{TotalRowPrice:N2}";
            public string FormattedRowRetailPrice => $"Retail: ₱{TotalRowRetailPrice:N2}";
            public string FormattedRowProfit => $"Profit: ₱{TotalRowProfit:N2}";
        }

        public class TransactionLogHeader : System.ComponentModel.INotifyPropertyChanged
        {
            public string Document_Number { get; set; }
            public string Entity_Name { get; set; }
            public string Date { get; set; }
            public string CreatedBy { get; set; }
            public string Summary { get; set; }
            public List<LogDetail> Details { get; set; }

            // Document Totals
            public double DocumentTotal { get; set; }
            public double DocumentRetailTotal { get; set; }
            public double DocumentProfit => DocumentRetailTotal - DocumentTotal;
            public bool HasDocumentProfit => DocumentRetailTotal > 0;
            public bool ShowRisProfit => !IsPOType && HasDocumentProfit;

            public string FormattedDocumentTotal => $"TOTAL: ₱{DocumentTotal:N2}";
            public string FormattedDocumentPurchaseTotal => $"Total Purchase Price of {Document_Number}: ₱{DocumentTotal:N2}";
            public string FormattedDocumentRetailTotal => $"Total Retail Price of {Document_Number}: ₱{DocumentRetailTotal:N2}";
            public string FormattedDocumentProfit => IsPOType
                ? $"Potential Profit of {Document_Number}: ₱{DocumentProfit:N2}"
                : $"Profit from {Document_Number}: ₱{DocumentProfit:N2}";

            public bool ShowDocumentTotal { get; set; }
            public bool IsPOType { get; set; } // <-- NEW: Tells the UI to show the Retail Totals

            public string SupplierTotalsText { get; set; }
            public bool HasSupplierTotals => !string.IsNullOrWhiteSpace(SupplierTotalsText);

            private bool _isExpanded;
            public bool IsExpanded
            {
                get => _isExpanded;
                set
                {
                    _isExpanded = value;
                    PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsExpanded)));
                }
            }
            public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        }

        // --- FETCH PO LOGS ---
        public List<TransactionLogHeader> GetPurchaseOrderLogs()
        {
            var pos = _db.Table<Purchase_Order>().ToList();
            var logs = new List<TransactionLogHeader>();

            foreach (var po in pos)
            {
                // UPDATED SQL: Now grabs the Retail_Price too!
                var query = @"
                    SELECT p.Item_Code, i.Name, i.Unit, p.Original_Qty AS Quantity, p.Unit_Price, p.Retail_Price
                    FROM PO_Line_Item p
                    INNER JOIN Item i ON p.Item_Code = i.Item_Code
                    WHERE p.PO_Number = ?";

                var rawDetails = _db.Query<RawPoData>(query, po.PO_Number);
                var formattedDetails = new List<LogDetail>();

                double totalPurch = 0;
                double totalRetail = 0;

                foreach (var r in rawDetails)
                {
                    // Calculate Running Grand Totals
                    totalPurch += (r.Quantity * r.Unit_Price);
                    totalRetail += (r.Quantity * r.Retail_Price);

                    formattedDetails.Add(new LogDetail
                    {
                        Item_Code = r.Item_Code,
                        Name = r.Name,
                        Unit = r.Unit,
                        Quantity = r.Quantity,
                        Price = r.Unit_Price,
                        Retail_Price = r.Retail_Price,

                        // Shows both prices perfectly stacked in the "Price/pc" column
                        BreakdownText = $"Purch: @₱{r.Unit_Price:N2}\nRetail: @₱{r.Retail_Price:N2}",

                        TotalRowPrice = r.Quantity * r.Unit_Price,
                        TotalRowRetailPrice = r.Quantity * r.Retail_Price
                    });
                }

                string formattedDate = DateTime.TryParse(po.Date_Received, out DateTime poDate)
                                       ? poDate.ToString("MMMM dd, yyyy")
                                       : po.Date_Received;

                logs.Add(new TransactionLogHeader
                {
                    Document_Number = po.PO_Number,
                    Entity_Name = po.Supplier_Name,
                    Date = formattedDate,
                    CreatedBy = po.Created_By,
                    Summary = string.IsNullOrWhiteSpace(po.Created_By)
                        ? $"{formattedDetails.Count} items received"
                        : $"{formattedDetails.Count} items received · by {po.Created_By}",
                    Details = formattedDetails,

                    DocumentTotal = totalPurch,        // Assign Grand Purchase Total
                    DocumentRetailTotal = totalRetail, // Assign Grand Retail Total

                    IsPOType = true,          // ACTIVATES the new UI blocks!
                    ShowDocumentTotal = true,
                    SupplierTotalsText = "",
                    IsExpanded = false
                });
            }
            return logs;
        }

        // --- FETCH RIS LOGS ---
        public List<TransactionLogHeader> GetIssuanceLogs()
        {
            var issuances = _db.Table<Issuance>().ToList();
            var logs = new List<TransactionLogHeader>();

            foreach (var iss in issuances)
            {
                var query = @"
                    SELECT t.Item_Code, i.Name, i.Unit, t.Quantity_Moved, p.Unit_Price, p.Retail_Price, po.Supplier_Name
                    FROM ""Transaction"" t
                    INNER JOIN Item i ON t.Item_Code = i.Item_Code
                    INNER JOIN PO_Line_Item p ON t.Batch_ID_Affected = p.Batch_ID
                    INNER JOIN Purchase_Order po ON p.PO_Number = po.PO_Number
                    WHERE t.RIS_Number = ?";

                var rawDetails = _db.Query<RawRisData>(query, iss.RIS_Number);
                var formattedDetails = new List<LogDetail>();

                // Group the batches back together by Item Code
                var groupedItems = rawDetails.GroupBy(x => new { x.Item_Code, x.Name, x.Unit }).ToList();

                foreach (var group in groupedItems)
                {
                    int totalQty = group.Sum(x => x.Quantity_Moved);
                    double totalRowPrice = group.Sum(x => x.Quantity_Moved * x.Unit_Price);
                    double totalRowRetail = group.Sum(x => x.Quantity_Moved * x.Retail_Price);

                    var remarksList = group.Select(x =>
                    {
                        if (x.Retail_Price > 0)
                            return $"{x.Quantity_Moved} from {x.Supplier_Name} (cost ₱{x.Unit_Price:N2}, retail ₱{x.Retail_Price:N2})";
                        return $"{x.Quantity_Moved} from {x.Supplier_Name} (@₱{x.Unit_Price:N2})";
                    });
                    string breakdown = string.Join("\n", remarksList);

                    formattedDetails.Add(new LogDetail
                    {
                        Item_Code = group.Key.Item_Code,
                        Name = group.Key.Name,
                        Unit = group.Key.Unit,
                        Quantity = totalQty,
                        Retail_Price = totalQty > 0 ? totalRowRetail / totalQty : 0,
                        BreakdownText = breakdown,
                        TotalRowPrice = totalRowPrice,
                        TotalRowRetailPrice = totalRowRetail
                    });
                }

                double docTotal = formattedDetails.Sum(d => d.TotalRowPrice);
                double docRetail = formattedDetails.Sum(d => d.TotalRowRetailPrice);

                // Group perfectly by Supplier, Unit, and Price to match the requested format
                var batchGroups = rawDetails.GroupBy(x => new { x.Supplier_Name, x.Unit, x.Unit_Price });

                var supplierTotalsList = batchGroups.Select(g =>
                {
                    int qty = g.Sum(x => x.Quantity_Moved);
                    double total = qty * g.Key.Unit_Price;
                    // Formats as: "ABC Hardware: 20 gallons (@PHP 400.00) = PHP 8000.00"
                    return $"{g.Key.Supplier_Name}: {qty} {g.Key.Unit} (@PHP {g.Key.Unit_Price:N2}) = PHP {total:N2}";
                });

                string supplierBreakdown = string.Join("\n", supplierTotalsList);

                // FORMAT THE DATE HERE
                string formattedDate = DateTime.TryParse(iss.Date_Issued, out DateTime issDate)
                                       ? issDate.ToString("MMMM dd, yyyy")
                                       : iss.Date_Issued;

                logs.Add(new TransactionLogHeader
                {
                    Document_Number = iss.RIS_Number,
                    Entity_Name = iss.Office_Name,
                    Date = formattedDate, // <--- Using the beautifully formatted Date
                    CreatedBy = iss.Created_By,
                    Summary = string.IsNullOrWhiteSpace(iss.Created_By)
                        ? $"{formattedDetails.Count} items issued"
                        : $"{formattedDetails.Count} items issued · by {iss.Created_By}",
                    Details = formattedDetails,
                    DocumentTotal = docTotal,
                    DocumentRetailTotal = docRetail,
                    SupplierTotalsText = supplierBreakdown,

                    // THIS IS THE MISSING MAGIC LINE! 
                    // It forces the RIS tab to show the totals we hid on the PO tab.
                    ShowDocumentTotal = true,

                    IsExpanded = false
                });
            }
            return logs;
        }

        // THIS IS NOW PROPERLY INSIDE THE DATABASESERVICE CLASS!
        public Item? GetItem(string itemCode) => _db.Find<Item>(itemCode);

        public Item? FindItemByBarcode(string barcode)
        {
            if (string.IsNullOrWhiteSpace(barcode))
                return null;
            return _db.Table<Item>().FirstOrDefault(i => i.Barcode == barcode);
        }

        public double GetLatestRetailPrice(string itemCode)
        {
            var line = _db.Table<PO_Line_Item>()
                .Where(l => l.Item_Code == itemCode)
                .OrderByDescending(l => l.Batch_ID)
                .FirstOrDefault();
            return line?.Retail_Price ?? 0;
        }

        public static List<FifoWithdrawalDetail> ProcessStockTransfer(
            string fromBranch,
            string toBranch,
            string transferNumber,
            DateTime date,
            List<TransactionRequest> lines,
            string notes)
        {
            if (string.IsNullOrWhiteSpace(fromBranch) || string.IsNullOrWhiteSpace(toBranch))
                throw new ArgumentException("Source and destination branches are required.");
            if (string.Equals(fromBranch, toBranch, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Choose a different destination branch.");
            if (string.IsNullOrWhiteSpace(transferNumber))
                throw new ArgumentException("Transfer number is required.");
            if (lines == null || lines.Count == 0)
                throw new ArgumentException("Add at least one item to transfer.");

            var source = new DatabaseService(fromBranch);
            var dest = new DatabaseService(toBranch);

            var issuance = new Issuance
            {
                RIS_Number = $"TRF-OUT-{transferNumber}",
                Office_Name = $"Transfer to {toBranch}",
                Date_Issued = date.ToString("yyyy-MM-dd"),
                Created_By = SessionService.Username
            };

            var details = source.ProcessIssuance(issuance, lines);

            var incoming = details.Select(d => new IncomingItemRequest
            {
                Item_Code = d.Item_Code,
                Name = d.Name,
                Unit = d.Unit,
                Barcode = source.GetItem(d.Item_Code)?.Barcode,
                Quantity = d.Quantity_Taken,
                Unit_Price = d.Unit_Price,
                Retail_Price = dest.GetLatestRetailPrice(d.Item_Code)
            }).ToList();

            var po = new Purchase_Order
            {
                PO_Number = $"TRF-IN-{transferNumber}",
                Date_Received = date.ToString("yyyy-MM-dd"),
                Supplier_Name = $"Transfer from {fromBranch}",
                Created_By = SessionService.Username
            };

            dest.ProcessIncomingStock(po, incoming);

            SystemCatalog.SaveTransfer(new Models.StockTransferRecord
            {
                TransferNumber = transferNumber,
                FromBranch = fromBranch,
                ToBranch = toBranch,
                Date = date.ToString("yyyy-MM-dd"),
                CreatedBy = SessionService.Username,
                Notes = notes ?? "",
                LineCount = details.Count,
                TotalQuantity = details.Sum(d => d.Quantity_Taken),
                TotalCost = details.Sum(d => d.Quantity_Taken * d.Unit_Price)
            });

            SystemCatalog.Log(
                "Stock transfer",
                fromBranch,
                $"{transferNumber}: {fromBranch} → {toBranch}, {details.Sum(d => d.Quantity_Taken)} unit(s). {notes}");

            NotifyDataChanged();
            return details;
        }

        public class SalesReportRow
        {
            public string Date { get; set; } = "";
            public string Document { get; set; } = "";
            public string Customer { get; set; } = "";
            public string Item { get; set; } = "";
            public int Quantity { get; set; }
            public double Cost { get; set; }
            public double Retail { get; set; }
            public double Profit => Retail - Cost;
        }

        public List<SalesReportRow> GetSalesReport(DateTime from, DateTime to)
        {
            var logs = GetIssuanceLogs();
            var rows = new List<SalesReportRow>();
            foreach (var log in logs)
            {
                if (!DateTime.TryParse(log.Date, out var when))
                    continue;
                if (when.Date < from.Date || when.Date > to.Date)
                    continue;
                foreach (var d in log.Details)
                {
                    rows.Add(new SalesReportRow
                    {
                        Date = log.Date,
                        Document = log.Document_Number,
                        Customer = log.Entity_Name,
                        Item = d.Name,
                        Quantity = d.Quantity,
                        Cost = d.TotalRowPrice,
                        Retail = d.TotalRowRetailPrice
                    });
                }
            }
            return rows;
        }

        public List<InventoryItem> GetLowStock(int threshold)
        {
            return GetCurrentInventory().Where(i => i.TotalQuantity <= threshold).ToList();
        }

        public void ClearAllData()
        {
            try
            {
                // Delete all rows from every table
                _db.DeleteAll<Transaction>();
                _db.DeleteAll<PO_Line_Item>();
                _db.DeleteAll<Purchase_Order>();
                _db.DeleteAll<Issuance>();
                _db.DeleteAll<Item>();

                // Reset the SQLite auto-increment counters back to 0
                _db.Execute("DELETE FROM sqlite_sequence");
                SystemCatalog.Log("Wipe database", BranchName, "Wiped all inventory tables for this branch.");
            }
            catch (Exception ex)
            {
                throw new Exception("Failed to clear database: " + ex.Message);
            }
        }
    } // <-- This is the brace that closes the DatabaseService class!

    // The Helper Class used to pass detailed supplier data to the Excel Print Page
    public class FifoWithdrawalDetail
    {
        public string Item_Code { get; set; }
        public string Name { get; set; }
        public string Unit { get; set; }
        public int Quantity_Taken { get; set; }
        public double Unit_Price { get; set; }
        public string Supplier_Name { get; set; }
        public string PO_Number { get; set; }
    }

} // <-- This brace closes the Namespace