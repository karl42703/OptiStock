using System.Diagnostics;
using System.Text;

namespace FIFO_Inventory_Management_System.Services;

/// <summary>
/// Builds a simple HTML document and opens it in the default browser so the user can print (Ctrl+P).
/// </summary>
public static class PrintService
{
    public static string OpenHtml(string title, string bodyHtml)
    {
        string fileName = $"OptiStock_Print_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.html";
        string path = Path.Combine(Path.GetTempPath(), fileName);

        string html = $$"""
            <!DOCTYPE html>
            <html>
            <head>
              <meta charset="utf-8"/>
              <title>{{System.Net.WebUtility.HtmlEncode(title)}}</title>
              <style>
                body { font-family: Segoe UI, Arial, sans-serif; margin: 24px; color: #111; }
                h1 { font-size: 20px; margin-bottom: 4px; }
                .meta { color: #555; margin-bottom: 16px; }
                table { border-collapse: collapse; width: 100%; }
                th, td { border: 1px solid #ccc; padding: 6px 8px; font-size: 13px; text-align: left; }
                th { background: #f2f2f2; }
                @media print { .no-print { display: none; } }
              </style>
            </head>
            <body>
              <p class="no-print">Use your browser Print dialog (Ctrl+P). This file is a printable snapshot.</p>
              <h1>{{System.Net.WebUtility.HtmlEncode(title)}}</h1>
              <div class="meta">
                Branch: {{System.Net.WebUtility.HtmlEncode(DatabaseService.ActiveBranchName)}} ·
                Printed by: {{System.Net.WebUtility.HtmlEncode(SessionService.DisplayName)}} ·
                {{DateTime.Now:MMMM dd, yyyy HH:mm}}
              </div>
              {{bodyHtml}}
            </body>
            </html>
            """;

        File.WriteAllText(path, html, Encoding.UTF8);
        Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        return path;
    }

    public static string Table(IEnumerable<string> headers, IEnumerable<IEnumerable<string>> rows)
    {
        var sb = new StringBuilder();
        sb.Append("<table><thead><tr>");
        foreach (var h in headers)
            sb.Append("<th>").Append(System.Net.WebUtility.HtmlEncode(h)).Append("</th>");
        sb.Append("</tr></thead><tbody>");
        foreach (var row in rows)
        {
            sb.Append("<tr>");
            foreach (var cell in row)
                sb.Append("<td>").Append(System.Net.WebUtility.HtmlEncode(cell ?? "")).Append("</td>");
            sb.Append("</tr>");
        }
        sb.Append("</tbody></table>");
        return sb.ToString();
    }
}
