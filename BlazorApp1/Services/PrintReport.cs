namespace BlazorApp1.Services;

public sealed class PrintReport
{
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public bool Landscape { get; set; }
    public List<PrintTable> Tables { get; set; } = [];
}

public sealed class PrintTable
{
    public string Title { get; set; } = "";
    public string Summary { get; set; } = "";
    public string[] Columns { get; set; } = [];
    public List<string?[]> Rows { get; set; } = [];
    public string?[]? Totals { get; set; }
}
