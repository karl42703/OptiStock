using System.Collections.ObjectModel;
using FIFO_Inventory_Management_System.Models;
using FIFO_Inventory_Management_System.Services;

namespace FIFO_Inventory_Management_System.Views;

public partial class ReceiveStockPage : ContentPage
{
    private DatabaseService _dbService;
    private ObservableCollection<DatabaseService.IncomingItemRequest> _pendingItems;

    // NEW: Cache the items so the search is instant and doesn't spam the database
    private List<Item> _allExistingItems = new List<Item>();

    public ReceiveStockPage()
    {
        InitializeComponent();
        _dbService = new DatabaseService();
        _pendingItems = new ObservableCollection<DatabaseService.IncomingItemRequest>();

        PendingItemsList.ItemsSource = _pendingItems;
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
        _pendingItems.Clear();
        try
        {
            _allExistingItems = _dbService.GetAllUniqueItems();
        }
        catch
        {
            _allExistingItems = new List<Item>();
        }
    }

    // --- NEW: THE SMART SEARCH ENGINE ---
    private void OnItemNameTextChanged(object sender, TextChangedEventArgs e)
    {
        string keyword = e.NewTextValue?.ToLower() ?? "";

        if (string.IsNullOrWhiteSpace(keyword))
        {
            SuggestionsFrame.IsVisible = false;
            return;
        }

        // Find matches in our cached list
        var matches = _allExistingItems
                        .Where(i => i.Name.ToLower().Contains(keyword))
                        .ToList();

        // Show the dropdown if matches exist (and they haven't exactly typed the full name yet)
        if (matches.Count > 0 && matches.FirstOrDefault()?.Name.ToLower() != keyword)
        {
            SuggestionsList.ItemsSource = matches;
            SuggestionsFrame.IsVisible = true;
        }
        else
        {
            SuggestionsFrame.IsVisible = false;
        }
    }

    // --- NEW: AUTO-FILL THE FIELDS ---
    private void OnSuggestionTapped(object sender, TappedEventArgs e)
    {
        if (e.Parameter is Item selectedItem)
        {
            // Auto-fill the corresponding fields!
            NameEntry.Text = selectedItem.Name;
            ItemCodeEntry.Text = selectedItem.Item_Code;
            UnitEntry.Text = selectedItem.Unit;

            // Hide the dropdown menu
            SuggestionsFrame.IsVisible = false;

            // Move focus down to the Quantity field to speed up their workflow
            QuantityEntry.Focus();
        }
    }

    // --- EXISTING METHODS ---
    private async void OnAddItemClicked(object sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ItemCodeEntry.Text) ||
            string.IsNullOrWhiteSpace(NameEntry.Text) ||
            string.IsNullOrWhiteSpace(UnitEntry.Text) ||
            string.IsNullOrWhiteSpace(QuantityEntry.Text) ||
            string.IsNullOrWhiteSpace(PurchasePriceEntry.Text) || // Checked
            string.IsNullOrWhiteSpace(RetailPriceEntry.Text))   // Checked
        {
            await DisplayAlert("Error", "Please fill out all Item details (Code, Name, Unit, Qty, Purch Price, Retail Price).", "OK");
            return;
        }

        var newItem = new DatabaseService.IncomingItemRequest
        {
            Item_Code = ItemCodeEntry.Text,
            Name = NameEntry.Text,
            Unit = UnitEntry.Text,
            Quantity = int.Parse(QuantityEntry.Text),
            Unit_Price = double.Parse(PurchasePriceEntry.Text), // Assigned
            Retail_Price = double.Parse(RetailPriceEntry.Text)  // Assigned
        };

        _pendingItems.Add(newItem);

        // Clear the mini-form
        ItemCodeEntry.Text = "";
        NameEntry.Text = "";
        UnitEntry.Text = "";
        QuantityEntry.Text = "";
        PurchasePriceEntry.Text = ""; // Cleared
        RetailPriceEntry.Text = "";   // Cleared
        SuggestionsFrame.IsVisible = false;

        ItemCodeEntry.Focus();
    }

    private async void OnSavePOClicked(object sender, EventArgs e)
    {
        if (_pendingItems.Count == 0)
        {
            await DisplayAlert("Error", "Please add at least one item to the PO.", "OK");
            return;
        }

        if (string.IsNullOrWhiteSpace(PoNumberEntry.Text))
        {
            await DisplayAlert("Error", "Please enter a PO Number.", "OK");
            return;
        }

        try
        {
            var newPO = new Purchase_Order
            {
                PO_Number = PoNumberEntry.Text,
                Date_Received = $"{DateReceivedPicker.Date:yyyy-MM-dd}",
                Supplier_Name = SupplierEntry.Text
            };

            var lineItemsToSave = _pendingItems.ToList();

            _dbService.ProcessIncomingStock(newPO, lineItemsToSave);

            await DisplayAlert("Success", "Incoming stock has been saved!", "OK");

            // Refresh the cache immediately so new items appear in the search next time!
            _allExistingItems = _dbService.GetAllUniqueItems();

            PoNumberEntry.Text = "";
            SupplierEntry.Text = "";
            _pendingItems.Clear();
        }
        catch (Exception ex)
        {
            await DisplayAlert("Database Error", ex.Message, "OK");
        }
    }

    private void OnRemovePendingItemClicked(object sender, EventArgs e)
    {
        var button = sender as Button;
        var itemToRemove = button.CommandParameter as DatabaseService.IncomingItemRequest;

        if (itemToRemove != null)
        {
            _pendingItems.Remove(itemToRemove);
        }
    }
}
