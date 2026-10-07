using FIFO_Inventory_Management_System.Models;
using FIFO_Inventory_Management_System.Services;

namespace FIFO_Inventory_Management_System.Views;

public partial class ReportsPage : ContentPage
{
    private DatabaseService _db = new();
    private List<DatabaseService.InventoryItem> _valuation = new();
    private List<DatabaseService.SalesReportRow> _sales = new();
    private List<DatabaseService.InventoryItem> _low = new();
    private List<StockTransferRecord> _transfers = new();

    public ReportsPage()
    {
        InitializeComponent();
        FromDate.Date = DateTime.Today.AddDays(-30);
        ToDate.Date = DateTime.Today;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        DatabaseService.ActiveBranchChanged += OnBranchChanged;
        RunReports();
    }

    protected override void OnDisappearing()
    {
        DatabaseService.ActiveBranchChanged -= OnBranchChanged;
        base.OnDisappearing();
    }

    private void OnBranchChanged(object? sender, EventArgs e) =>
        MainThread.BeginInvokeOnMainThread(RunReports);

    private void OnRunClicked(object sender, EventArgs e) => RunReports();

    private void RunReports()
    {
        if (!SessionService.Has(AppPermission.ViewReports))
        {
            SummaryLabel.Text = "Your role cannot view reports.";
            return;
        }

        _db = new DatabaseService();
        int threshold = 5;
        int.TryParse(ThresholdEntry.Text, out threshold);

        _valuation = _db.GetCurrentInventory();
        _sales = _db.GetSalesReport(FromDate.Date ?? DateTime.Today, ToDate.Date ?? DateTime.Today);
        _low = _db.GetLowStock(threshold);
        _transfers = SystemCatalog.GetTransfers()
            .Where(t => t.FromBranch == DatabaseService.ActiveBranchName
                     || t.ToBranch == DatabaseService.ActiveBranchName)
            .ToList();

        ValuationList.ItemsSource = _valuation.Select(i =>
            $"{i.Item_Code}  {i.Name}  qty {i.TotalQuantity}  {i.FormattedTotalValue}").ToList();
        SalesList.ItemsSource = _sales.Select(s =>
            $"{s.Date}  {s.Document}  {s.Item}  x{s.Quantity}  profit ₱{s.Profit:N2}").ToList();
        LowStockList.ItemsSource = _low.Select(i =>
            $"{i.Name}  ({i.Item_Code})  qty {i.TotalQuantity}").ToList();
        TransferList.ItemsSource = _transfers.Select(t =>
            $"{t.Date}  {t.TransferNumber}  {t.FromBranch} → {t.ToBranch}  qty {t.TotalQuantity}  ₱{t.TotalCost:N2}").ToList();

        double onHand = _valuation.Sum(i => i.TotalValue);
        double profit = _sales.Sum(s => s.Profit);
        SummaryLabel.Text =
            $"On-hand value ₱{onHand:N2} · Range profit ₱{profit:N2} · { _sales.Count} sale line(s) · {_low.Count} low-stock item(s).";
    }

    private async void OnPrintClicked(object sender, EventArgs e)
    {
        if (!SessionService.Has(AppPermission.Print))
        {
            await DisplayAlert("Not allowed", "Your role cannot print.", "OK");
            return;
        }

        RunReports();
        string body =
            "<h2>Valuation</h2>" +
            PrintService.Table(
                ["Code", "Name", "Qty", "Value"],
                _valuation.Select(i => (IEnumerable<string>)[i.Item_Code, i.Name, i.TotalQuantity.ToString(), i.TotalValue.ToString("N2")])) +
            "<h2>Sales</h2>" +
            PrintService.Table(
                ["Date", "Doc", "Item", "Qty", "Profit"],
                _sales.Select(s => (IEnumerable<string>)[s.Date, s.Document, s.Item, s.Quantity.ToString(), s.Profit.ToString("N2")])) +
            "<h2>Low stock</h2>" +
            PrintService.Table(
                ["Code", "Name", "Qty"],
                _low.Select(i => (IEnumerable<string>)[i.Item_Code, i.Name, i.TotalQuantity.ToString()]));

        PrintService.OpenHtml($"OptiStock reports — {DatabaseService.ActiveBranchName}", body);
        SystemCatalog.Log("Print report", DatabaseService.ActiveBranchName, "Opened printable HTML reports.");
    }
}
