# OptiStock

A comprehensive, multi-branch Inventory Management System built with .NET MAUI, utilizing the First-In, First-Out (FIFO) methodology for accurate stock tracking. Flexible enough to handle various retail scenarios.

## 🚀 Features

* **Multi-Branch Support:** Manage inventory across multiple locations seamlessly.
* **Role-Based Access Control (RBAC):** Secure access with granular roles (Admin, Manager, Staff, Viewer).
* **FIFO Costing:** Accurate profit margin calculations using First-In, First-Out methodology.
* **Receive & Issue Stock:** Easily add new stock to inventory or issue stock out for sales.
* **Inter-Branch Transfers:** Move stock between different branches with detailed transfer records.
* **Real-time Inventory Tracking:** View current stock levels and values.
* **Comprehensive Reporting & Transaction Logs:** Track every movement of stock.
* **Audit Logging:** Keep track of user actions for accountability.
* **Offline First:** Built on top of a local SQLite database for fast, offline capabilities.
* **Database Management:** Built-in tools for database wiping, importing, and exporting.

## 🛠️ Tech Stack

* **Framework:** [.NET MAUI](https://dotnet.microsoft.com/en-us/apps/maui) (Multi-platform App UI)
* **Language:** C#
* **Database:** SQLite (Local)
* **UI Pattern:** XAML / Code-behind

## 📁 Project Structure

* `/Models/` - Data schemas and SQLite table definitions (UserRoles, BranchRecord, AuditLogEntry, etc.)
* `/Views/` - XAML pages for the application interface (ReceiveStock, IssueStock, CurrentInventory, Settings, etc.)
* `/Services/` - Core business logic, database initialization, and app navigation.

## ⚙️ Getting Started

### Prerequisites

* [Visual Studio 2022](https://visualstudio.microsoft.com/) (17.3 or later) with the **.NET Multi-platform App UI development** workload installed.
* .NET 8.0 SDK (or compatible version)

### Installation & Setup

1. **Clone the repository:**
   ```bash
   git clone https://github.com/yourusername/fifo-inventory-vape-store.git
   ```
2. **Open the Solution:**
   Open `FIFO Inventory Management System.slnx` or `.csproj` in Visual Studio.
3. **Restore Dependencies:**
   Visual Studio should automatically restore NuGet packages. If not, right-click the solution and select "Restore NuGet Packages".
4. **Run the App:**
   Select your target platform (Windows Machine, Android Emulator, etc.) and hit `F5` to run and debug.

## 🔐 Default Access

Upon first run, the database is initialized. You can log in with the default admin credentials (if configured in `SQLite.cs`) or create an initial admin user through the UI if prompted.


