
using FIFO_Inventory_Management_System.Models;
using FIFO_Inventory_Management_System.Services;

namespace FIFO_Inventory_Management_System.Views;

public partial class TransactionLogsPage : ContentPage
{
    private DatabaseService _dbService;

    // Master lists so we don't hit the database every time we filter
    private List<DatabaseService.TransactionLogHeader> _masterPoLogs;
    private List<DatabaseService.TransactionLogHeader> _masterRisLogs;

    private string _currentTab = "PO";

    public TransactionLogsPage()
    {
        InitializeComponent();
        _dbService = new DatabaseService();
        SortPicker.SelectedIndex = 0; // Default to Newest First
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
        LoadData();
    }

    private void LoadData()
    {
        _masterPoLogs = _dbService.GetPurchaseOrderLogs();
        _masterRisLogs = _dbService.GetIssuanceLogs();
        
        bool canViewAudit = SessionService.Has(AppPermission.ViewAudit);
        TabAuditBtn.IsVisible = canViewAudit;

        if (canViewAudit)
            AuditList.ItemsSource = SystemCatalog.GetAuditLogs();
        else if (_currentTab == "AUDIT")
        {
            // If they are on Audit tab but no longer have permission, switch to PO
            OnTabClicked(TabPoBtn, EventArgs.Empty);
            return; // OnTabClicked will call RefreshUI
        }

        RefreshUI();
    }

    // --- TAB SWITCHING ---
    private void OnTabClicked(object sender, EventArgs e)
    {
        var button = sender as Button;
        _currentTab = button.CommandParameter.ToString();

        TabPoBtn.BackgroundColor = Colors.LightGray;
        TabPoBtn.TextColor = Colors.Black;
        TabRisBtn.BackgroundColor = Colors.LightGray;
        TabRisBtn.TextColor = Colors.Black;
        TabAuditBtn.BackgroundColor = Colors.LightGray;
        TabAuditBtn.TextColor = Colors.Black;

        if (_currentTab == "PO")
        {
            TabPoBtn.BackgroundColor = Color.FromArgb("#005A9E");
            TabPoBtn.TextColor = Colors.White;
            FilterLabel.Text = "Filter Supplier";
        }
        else if (_currentTab == "RIS")
        {
            TabRisBtn.BackgroundColor = Color.FromArgb("#005A9E");
            TabRisBtn.TextColor = Colors.White;
            FilterLabel.Text = "Filter Office";
        }
        else
        {
            TabAuditBtn.BackgroundColor = Color.FromArgb("#005A9E");
            TabAuditBtn.TextColor = Colors.White;
            FilterLabel.Text = "Filter user";
        }

        RefreshUI();
    }

    // --- REFRESH, SORT, AND FILTER ---
    private void RefreshUI()
    {
        bool audit = _currentTab == "AUDIT";
        LogsList.IsVisible = !audit;
        AuditList.IsVisible = audit;
        if (audit)
        {
            if (!SessionService.Has(AppPermission.ViewAudit))
            {
                AuditList.ItemsSource = new List<AuditLogEntry>();
                return;
            }
            AuditList.ItemsSource = SystemCatalog.GetAuditLogs();
            return;
        }

        var activeList = _currentTab == "PO" ? _masterPoLogs : _masterRisLogs;

        if (activeList == null) return;

        // 1. Populate the Entity Picker Dropdown dynamically
        PopulateEntityPicker(activeList);

        // 2. Apply Office/Supplier Filter
        string selectedEntity = EntityPicker.SelectedItem as string;
        if (!string.IsNullOrEmpty(selectedEntity) && selectedEntity != "All")
        {
            activeList = activeList.Where(x => x.Entity_Name == selectedEntity).ToList();
        }

        // 3. Apply Date Sort
        string sortOrder = SortPicker.SelectedItem as string;
        if (sortOrder == "Oldest First")
            activeList = activeList.OrderBy(x => x.Date).ToList();
        else // Newest First
            activeList = activeList.OrderByDescending(x => x.Date).ToList();

        // 4. Push to screen
        LogsList.ItemsSource = activeList;
    }

    private void PopulateEntityPicker(List<DatabaseService.TransactionLogHeader> list)
    {
        // Save what they had selected so it doesn't reset randomly
        string previousSelection = EntityPicker.SelectedItem as string;

        var distinctEntities = list.Select(x => x.Entity_Name).Distinct().OrderBy(x => x).ToList();
        distinctEntities.Insert(0, "All"); // Always offer an 'All' option

        // Temporarily detach the event so we don't cause an infinite loop
        EntityPicker.SelectedIndexChanged -= OnFilterChanged;

        EntityPicker.ItemsSource = distinctEntities;

        if (distinctEntities.Contains(previousSelection))
            EntityPicker.SelectedItem = previousSelection;
        else
            EntityPicker.SelectedIndex = 0;

        EntityPicker.SelectedIndexChanged += OnFilterChanged;
    }

    private void OnFilterChanged(object sender, EventArgs e)
    {
        RefreshUI();
    }

    // --- EXPANDABLE ROW LOGIC ---
    private void OnRowTapped(object sender, TappedEventArgs e)
    {
        var clickedHeader = e.Parameter as DatabaseService.TransactionLogHeader;
        if (clickedHeader != null)
        {
            // Toggle the boolean. The INotifyPropertyChanged in our class auto-updates the UI!
            clickedHeader.IsExpanded = !clickedHeader.IsExpanded;
        }
    }
}