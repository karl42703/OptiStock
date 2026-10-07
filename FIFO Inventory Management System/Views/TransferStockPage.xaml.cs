using System.Collections.ObjectModel;
using FIFO_Inventory_Management_System.Models;
using FIFO_Inventory_Management_System.Services;

namespace FIFO_Inventory_Management_System.Views;

public partial class TransferStockPage : ContentPage
{
    private DatabaseService _dbService = new();
    private List<Item> _items = new();
    private Item? _selected;
    private readonly ObservableCollection<PendingLine> _pending = new();

    public TransferStockPage()
    {
        InitializeComponent();
        PendingList.ItemsSource = _pending;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (!SessionService.Has(AppPermission.TransferStock))
        {
            DisplayAlert("Not allowed", "Your role cannot transfer stock.", "OK");
            return;
        }

        DatabaseService.ActiveBranchChanged += OnBranchChanged;
        SystemCatalog.BranchListChanged += OnBranchChanged;
        Reload();
    }

    protected override void OnDisappearing()
    {
        DatabaseService.ActiveBranchChanged -= OnBranchChanged;
        SystemCatalog.BranchListChanged -= OnBranchChanged;
        base.OnDisappearing();
    }

    private void OnBranchChanged(object? sender, EventArgs e) =>
        MainThread.BeginInvokeOnMainThread(Reload);

    private void Reload()
    {
        _dbService = new DatabaseService();
        _pending.Clear();
        _selected = null;
        _items = _dbService.GetAllItems();
        DestinationPicker.ItemsSource = BranchNames.All
            .Where(b => !string.Equals(b, DatabaseService.ActiveBranchName, StringComparison.Ordinal))
            .ToList();
    }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        string keyword = e.NewTextValue?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(keyword))
        {
            SearchResultsList.IsVisible = false;
            return;
        }

        var matches = _items.Where(i =>
            (i.Name ?? "").Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
            (i.Item_Code ?? "").Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
            (i.Barcode ?? "").Contains(keyword, StringComparison.OrdinalIgnoreCase)).ToList();

        var exactBarcode = _dbService.FindItemByBarcode(keyword);
        if (exactBarcode is not null && matches.All(m => m.Item_Code != exactBarcode.Item_Code))
            matches.Insert(0, exactBarcode);

        SearchResultsList.ItemsSource = matches;
        SearchResultsList.IsVisible = matches.Count > 0;
    }

    private void OnItemSelected(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not Item item)
            return;

        _selected = item;
        SearchEntry.Text = item.Name;
        SearchResultsList.IsVisible = false;
        var stock = _dbService.GetCurrentInventory().FirstOrDefault(i => i.Item_Code == item.Item_Code);
        AvailableStockLabel.Text = $"Available: {stock?.TotalQuantity ?? 0}";
        AvailableStockLabel.IsVisible = true;
    }

    private async void OnAddItemClicked(object sender, EventArgs e)
    {
        if (_selected is null || !int.TryParse(QuantityEntry.Text, out int qty) || qty <= 0)
        {
            await DisplayAlert("Error", "Select an item and enter a valid quantity.", "OK");
            return;
        }

        try
        {
            int already = _pending.Where(p => p.Item_Code == _selected.Item_Code).Sum(p => p.Quantity);
            _dbService.PreviewFifoWithdrawal(_selected.Item_Code, already + qty);
            var existing = _pending.FirstOrDefault(p => p.Item_Code == _selected.Item_Code);
            if (existing is not null)
                existing.Quantity += qty;
            else
                _pending.Add(new PendingLine { Item_Code = _selected.Item_Code, Name = _selected.Name, Quantity = qty });

            QuantityEntry.Text = "";
            SearchEntry.Text = "";
            _selected = null;
            PendingList.ItemsSource = null;
            PendingList.ItemsSource = _pending;
        }
        catch (Exception ex)
        {
            await DisplayAlert("Stock Error", ex.Message, "OK");
        }
    }

    private void OnRemoveClicked(object sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: PendingLine line })
            _pending.Remove(line);
    }

    private async void OnConfirmClicked(object sender, EventArgs e)
    {
        if (DestinationPicker.SelectedItem is not string dest)
        {
            await DisplayAlert("Required", "Select a destination branch.", "OK");
            return;
        }

        if (string.IsNullOrWhiteSpace(TransferNumberEntry.Text) || _pending.Count == 0)
        {
            await DisplayAlert("Required", "Enter a transfer number and at least one item.", "OK");
            return;
        }

        try
        {
            var lines = _pending.Select(p => new DatabaseService.TransactionRequest
            {
                Item_Code = p.Item_Code,
                Quantity = p.Quantity
            }).ToList();

            DatabaseService.ProcessStockTransfer(
                DatabaseService.ActiveBranchName,
                dest,
                TransferNumberEntry.Text.Trim(),
                TransferDatePicker.Date ?? DateTime.Today,
                lines,
                NotesEntry.Text ?? "");

            await DisplayAlert("Transferred", $"Stock moved to {dest} using FIFO costs.", "OK");
            TransferNumberEntry.Text = "";
            NotesEntry.Text = "";
            _pending.Clear();
            Reload();
        }
        catch (Exception ex)
        {
            await DisplayAlert("Transfer failed", ex.Message, "OK");
        }
    }

    private class PendingLine
    {
        public string Item_Code { get; set; } = "";
        public string Name { get; set; } = "";
        public int Quantity { get; set; }
    }
}
