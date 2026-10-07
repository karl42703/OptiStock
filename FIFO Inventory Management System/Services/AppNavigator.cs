namespace FIFO_Inventory_Management_System.Services;

public static class AppNavigator
{
    public static void Show(Page page)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (Application.Current is not null)
                Application.Current.MainPage = page;
        });
    }
}
