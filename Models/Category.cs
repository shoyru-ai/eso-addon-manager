namespace EsoAddons.Models;

/// <summary>An ESOUI addon category (bucket).</summary>
public class Category
{
    public string Id { get; init; } = "";       // "" = All
    public string Title { get; init; } = "";
    public int Count { get; init; }
    public string Display => Id.Length == 0 ? Title : (Count > 0 ? $"{Title}  ({Count})" : Title);
}
