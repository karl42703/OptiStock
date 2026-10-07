using FIFO_Inventory_Management_System.Services;

namespace FIFO_Inventory_Management_System;

/// <summary>Live branch list from the system catalog (not a hardcoded array).</summary>
public static class BranchNames
{
    public static IReadOnlyList<string> All => SystemCatalog.GetBranchNames();
}
