using FIFO_Inventory_Management_System.Views;

namespace FIFO_Inventory_Management_System
{
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();
            MainPage = new LoginPage();
        }
    }
}