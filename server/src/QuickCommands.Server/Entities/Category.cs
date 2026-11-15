namespace QuickCommands.Server.Entities;

public sealed class Category
{
    public int Id { get; set; }
    public string Slug { get; set; } = "";
    public string NameDe { get; set; } = "";
    public string NameEn { get; set; } = "";
    public int SortOrder { get; set; }
}
