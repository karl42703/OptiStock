using System;
using System.Collections.Generic;
using System.Text;
using SQLite;

namespace FIFO_Inventory_Management_System.Models
{
  
    public class Item
    {
        [PrimaryKey]
        public string Item_Code { get; set; }
        public string Name { get; set; }
        public string Unit { get; set; }
        public string Barcode { get; set; }
    }

    public class Purchase_Order
    {
        [PrimaryKey]
        public string PO_Number { get; set; }
        public string Date_Received { get; set; }
        public string Supplier_Name { get; set; }
        public string Created_By { get; set; }
    }

    public class PO_Line_Item
    {
        [PrimaryKey, AutoIncrement]
        public int Batch_ID { get; set; }
        public string PO_Number { get; set; } // Foreign Key
        public string Item_Code { get; set; } // Foreign Key
        public double Unit_Price { get; set; }
        public double Retail_Price { get; set; }
        public int Original_Qty { get; set; }
        public int Remaining_Qty { get; set; }
    }

    public class Issuance
    {
        [PrimaryKey]
        public string RIS_Number { get; set; }
        public string Date_Issued { get; set; }
        public string Office_Name { get; set; }
        public string Created_By { get; set; }
    }

    public class Transaction
    {
        [PrimaryKey, AutoIncrement]
        public int Transaction_ID { get; set; }
        public string RIS_Number { get; set; } // Foreign Key
        public string Item_Code { get; set; } // Foreign Key
        public int Batch_ID_Affected { get; set; } // Foreign Key
        public int Quantity_Moved { get; set; }
        public string Date_Logged { get; set; }
        public string Created_By { get; set; }
    }
}
