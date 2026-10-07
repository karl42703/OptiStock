using FIFO_Inventory_Management_System.Services;
using Microsoft.Maui.Storage;
using System.Linq; // Ensures your filtering works

namespace FIFO_Inventory_Management_System.Views;

public partial class CurrentInventoryPage : ContentPage
{
    private DatabaseService _dbService;

    // UPDATED: Now uses the new 'InventoryItem' model that holds the expandable batches
    private List<DatabaseService.InventoryItem> _masterInventoryList;

    public CurrentInventoryPage()
    {
        InitializeComponent();
        _dbService = new DatabaseService();

        // Set the dropdown to "All Items" by default when the app opens
        FilterPicker.SelectedIndex = 0;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        DatabaseService.ActiveBranchChanged += OnActiveBranchChanged;
        RefreshForCurrentBranch();
    }

    protected override void OnDisappearing()
    {
        DatabaseService.ActiveBranchChanged -= OnActiveBranchChanged;
        base.OnDisappearing();
    }

    private void OnActiveBranchChanged(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(RefreshForCurrentBranch);
    }

    private void RefreshForCurrentBranch()
    {
        _dbService = new DatabaseService();
        LoadInventoryData();
    }

    private void OnRefreshClicked(object sender, EventArgs e)
    {
        LoadInventoryData();
    }

    private void LoadInventoryData()
    {
        try
        {
            // 1. Ask the database for the data ONE time
            _masterInventoryList = _dbService.GetCurrentInventory();

            // 2. Apply whatever filter is currently selected in the dropdown
            ApplyFilter();
        }
        catch (Exception ex)
        {
            DisplayAlert("Database Error", "Could not load inventory: " + ex.Message, "OK");
        }
    }

    // This triggers whenever the user clicks a different option in the Picker
    private void OnFilterChanged(object sender, EventArgs e)
    {
        ApplyFilter();
    }
    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyFilter();
    }
    // --- THE FILTER LOGIC ---
    private void ApplyFilter()
    {
        if (_masterInventoryList == null) return;

        // 1. Grab the current states of both filters
        string selectedStatus = FilterPicker.SelectedItem as string;
        string searchPO = POSearchBar.Text?.ToLower() ?? "";

        // 2. Start with the full master list
        var filteredList = _masterInventoryList.AsEnumerable();

        // 3. Apply the Status Filter
        if (selectedStatus == "In Stock")
        {
            filteredList = filteredList.Where(i => i.TotalQuantity > 0);
        }
        else if (selectedStatus == "Out of Stock")
        {
            filteredList = filteredList.Where(i => i.TotalQuantity == 0);
        }

        // 4. Apply the PO Search Filter (The Magic Part)
        if (!string.IsNullOrWhiteSpace(searchPO))
        {
            // Only keep items where AT LEAST ONE of their batches matches the PO number they typed
            filteredList = filteredList.Where(item =>
                item.Batches.Any(batch => batch.PONumber.ToLower().Contains(searchPO))
            );
        }

        // 5. Push the perfectly filtered list to the screen
        InventoryList.ItemsSource = filteredList.ToList();
    }

    // --- NEW: THE EXPAND/COLLAPSE ENGINE ---
    private void OnInventoryRowTapped(object sender, TappedEventArgs e)
    {
        // Check which specific row the user tapped
        if (e.Parameter is DatabaseService.InventoryItem tappedItem)
        {
            // If it's closed, open it. If it's open, close it!
            tappedItem.IsExpanded = !tappedItem.IsExpanded;
        }
    }
}