using System.Collections.ObjectModel;
using FIFO_Inventory_Management_System.Services;
using FIFO_Inventory_Management_System.Models;

namespace FIFO_Inventory_Management_System.Views;

public partial class IssueStockPage : ContentPage
{
    private DatabaseService _dbService;
    private List<Item> _allItemsMasterList = new();
    private Item _selectedItem;

    // We updated the helper class to hold the Unit string
    private ObservableCollection<PendingRequestItem> _pendingRequests;

    public IssueStockPage()
    {
        InitializeComponent();
        _dbService = new DatabaseService();
        _pendingRequests = new ObservableCollection<PendingRequestItem>();

        PendingRequestsList.ItemsSource = _pendingRequests;
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
        ResetAfterBranchChange();
    }

    private void ResetAfterBranchChange()
    {
        _pendingRequests.Clear();
        SearchEntry.Text = "";
        QuantityEntry.Text = "";
        SearchResultsList.IsVisible = false;
        UnitLabel.IsVisible = false;
        AvailableStockLabel.IsVisible = false;
        _selectedItem = null;
        _allItemsMasterList = _dbService.GetAllItems();
    }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        string keyword = e.NewTextValue;

        if (string.IsNullOrWhiteSpace(keyword))
        {
            SearchResultsList.IsVisible = false;

            // Hide the extra UI elements if the search box is cleared
            UnitLabel.IsVisible = false;
            AvailableStockLabel.IsVisible = false;
            _selectedItem = null;
            return;
        }

        var filteredList = _allItemsMasterList
            .Where(i => i.Name.ToLower().Contains(keyword.ToLower()))
            .ToList();

        if (filteredList.Count > 0)
        {
            SearchResultsList.ItemsSource = filteredList;
            SearchResultsList.IsVisible = true;
        }
        else
        {
            SearchResultsList.IsVisible = false;
        }
    }

    private void OnItemSelected(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is Item selectedItem)
        {
            _selectedItem = selectedItem;
            SearchEntry.Text = _selectedItem.Name;
            SearchResultsList.IsVisible = false;
            SearchResultsList.SelectedItem = null;

            // Show the specific Unit next to the quantity box
            UnitLabel.Text = _selectedItem.Unit;
            UnitLabel.IsVisible = true;

            // Query the database to see exactly how much is available right now
            var inventorySummary = _dbService.GetCurrentInventory();
            var stockData = inventorySummary.FirstOrDefault(i => i.Item_Code == _selectedItem.Item_Code);
            int availableQty = stockData != null ? stockData.TotalQuantity : 0;

            AvailableStockLabel.Text = $"Available Stock: {availableQty} {_selectedItem.Unit}";
            AvailableStockLabel.IsVisible = true;

            QuantityEntry.Focus();
        }
    }

    private async void OnAddItemClicked(object sender, EventArgs e)
    {
        if (_selectedItem == null || string.IsNullOrWhiteSpace(QuantityEntry.Text) || !int.TryParse(QuantityEntry.Text, out int qty) || qty <= 0)
        {
            await DisplayAlert("Error", "Please select an item and enter a valid quantity.", "OK");
            return;
        }

        var existingItem = _pendingRequests.FirstOrDefault(p => p.Item_Code == _selectedItem.Item_Code);
        int totalTargetQty = existingItem != null ? existingItem.Quantity + qty : qty;

        try
        {
            // 1. Run the Preview to get the Supplier/Price Breakdown
            var breakdown = _dbService.PreviewFifoWithdrawal(_selectedItem.Item_Code, totalTargetQty);

            // 2. Format the beautiful text for the UI
            double totalCost = breakdown.Sum(b => b.Quantity_Taken * b.Unit_Price);
            var remarksList = breakdown.Select(x => $"{x.Quantity_Taken} from {x.Supplier_Name} (@₱{x.Unit_Price:N2})");
            string breakdownText = string.Join("\n", remarksList); // Uses a new line for multiple suppliers

            if (existingItem != null)
            {
                existingItem.Quantity = totalTargetQty;
                existingItem.SupplierBreakdown = breakdownText;
                existingItem.TotalPrice = totalCost;

                // Force the UI list to refresh
                int index = _pendingRequests.IndexOf(existingItem);
                _pendingRequests[index] = existingItem;
            }
            else
            {
                _pendingRequests.Add(new PendingRequestItem
                {
                    Item_Code = _selectedItem.Item_Code,
                    Name = _selectedItem.Name,
                    Unit = _selectedItem.Unit,
                    Quantity = totalTargetQty,
                    SupplierBreakdown = breakdownText,
                    TotalPrice = totalCost
                });
            }

            // Reset form
            SearchEntry.Text = "";
            QuantityEntry.Text = "";
            UnitLabel.IsVisible = false;
            AvailableStockLabel.IsVisible = false;
            _selectedItem = null;
        }
        catch (Exception ex)
        {
            await DisplayAlert("Stock Error", ex.Message, "OK");
        }
    }

    // NEW: The Delete Button Logic
    private void OnRemoveItemClicked(object sender, EventArgs e)
    {
        var button = (Button)sender;
        var itemToRemove = (PendingRequestItem)button.CommandParameter;

        if (itemToRemove != null)
        {
            _pendingRequests.Remove(itemToRemove);
        }
    }

    // UPDATED: The Helper Class (Put this at the very bottom of the file)
    public class PendingRequestItem
    {
        public string Item_Code { get; set; }
        public string Name { get; set; }
        public string Unit { get; set; }
        public int Quantity { get; set; }
        public string SupplierBreakdown { get; set; }
        public double TotalPrice { get; set; }
        public string FormattedTotalPrice => $"Total: ₱{TotalPrice:N2}";
    }

    private async void OnConfirmClicked(object sender, EventArgs e)
    {
        if (_pendingRequests.Count == 0)
        {
            await DisplayAlert("Error", "Please add at least one item to withdraw.", "OK");
            return;
        }

        try
        {
            var newIssuance = new Models.Issuance
            {
                RIS_Number = RisNumberEntry.Text,
                Office_Name = OfficeEntry.Text,
                Date_Issued = ((DateTime)DateIssuedPicker.Date).ToString("yyyy-MM-dd")
            };

            var databaseRequests = new List<DatabaseService.TransactionRequest>();
            foreach (var req in _pendingRequests)
            {
                databaseRequests.Add(new DatabaseService.TransactionRequest
                {
                    Item_Code = req.Item_Code,
                    Quantity = req.Quantity
                });
            }

            // 1. Save to database AND catch the detailed breakdown
            var detailedBreakdown = _dbService.ProcessIssuance(newIssuance, databaseRequests);

            // 2. Ask the user if they want to print the form
            bool wantsToPrint = await DisplayAlert(
                "Success",
                "RIS Batch created and inventory deducted! Do you want to fill out and print the official form?",
                "Yes, Print Form",
                "No, Close");

            // 3. Open the Print Preview Page if they clicked Yes (Now passing the detailed breakdown!)
            //if (wantsToPrint)
           // {
           //     await Navigation.PushModalAsync(new RisPrintPreviewPage(newIssuance, detailedBreakdown));
          //  }

            // 4. Clear the screen
            RisNumberEntry.Text = "";
            OfficeEntry.Text = "";
            _pendingRequests.Clear();
        }
        catch (Exception ex)
        {
            await DisplayAlert("Transaction Error", ex.Message, "OK");
        }
    }

    // Updated helper class to include Unit
    
}