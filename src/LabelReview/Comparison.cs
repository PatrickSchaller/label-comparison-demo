using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace LabelReview;

public record FieldDefinition(string Key, string Title, string Category);
public record LabelField(string Key, string Value, string Evidence, string State);
public record LabelDocument(LabelField[] Fields);
public record Extraction(LabelDocument Label, string ResponseId, string Model);
public record SourceDocument(string Name, string Sha256, string ResponseId, string Model);
public record ComparisonRow(string Key, string Title, string Category, string Kind,
    string Status, LabelField Before, LabelField After);
public record ComparisonReport(string Mode, string Disclosure, DateTimeOffset CreatedAt,
    SourceDocument Before, SourceDocument After, ComparisonRow[] Rows);

public static partial class Comparison
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        MaxDepth = 16
    };

    public static readonly FieldDefinition[] Fields =
    [
        new("productName", "Product name", "Product identity"),
        new("netContents", "Net contents", "Product identity"),
        new("servingSize", "Serving size", "Nutrition facts"),
        new("servingsPerContainer", "Servings per container", "Nutrition facts"),
        new("factsPanel", "Facts panel rows", "Nutrition facts"),
        new("ingredients", "Ingredients (in printed order)", "Ingredients & allergens"),
        new("allergens", "Allergen statement", "Ingredients & allergens"),
        new("claims", "Claims & marketing text", "Claims & directions"),
        new("directions", "Directions", "Claims & directions"),
        new("warnings", "Warnings", "Warnings & storage"),
        new("storage", "Storage", "Warnings & storage"),
        new("manufacturer", "Manufacturer & disclaimers", "Other label content"),
        new("visualMarks", "Badges, barcode & artwork", "Visual elements"),
        new("otherText", "Other printed text", "Other label content")
    ];

    public static void Validate(LabelDocument? label)
    {
        if (label?.Fields is null || label.Fields.Length != Fields.Length)
            throw new InvalidDataException("Extraction must account for every expected field.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in label.Fields)
        {
            if (field is null || !Fields.Any(f => f.Key == field.Key) || !seen.Add(field.Key)
                || field.Value is null || field.Evidence is null
                || field.Value.Length > 3000 || field.Evidence.Length > 3000
                || field.State is not ("read" or "absent" or "unclear"))
                throw new InvalidDataException("Extraction contains an invalid or duplicate field.");
            if ((field.State == "absent" && (field.Value != "" || field.Evidence != ""))
                || (field.State == "read" && (string.IsNullOrWhiteSpace(field.Value) || string.IsNullOrWhiteSpace(field.Evidence)))
                || (field.State == "unclear" && string.IsNullOrWhiteSpace(field.Evidence)))
                throw new InvalidDataException("Extraction evidence does not match its reading state.");
        }
    }

    public static ComparisonRow[] Compare(LabelDocument before, LabelDocument after)
    {
        Validate(before);
        Validate(after);
        var oldFields = before.Fields.ToDictionary(f => f.Key);
        var newFields = after.Fields.ToDictionary(f => f.Key);
        return Fields.Select(definition =>
        {
            var old = oldFields[definition.Key];
            var current = newFields[definition.Key];
            var status = old.State == "unclear" || current.State == "unclear" ? "review"
                : Normalize(old.Value) == Normalize(current.Value) ? "unchanged"
                : old.State == "absent" ? "added"
                : current.State == "absent" ? "removed" : "changed";
            return new ComparisonRow(definition.Key, definition.Title, definition.Category,
                status == "review" ? "caution" : definition.Key == "visualMarks" ? "visual" : "textual",
                status, old, current);
        }).ToArray();
    }

    // Only layout whitespace and letter case are ignored; numbers, units and ingredient order remain significant.
    public static string Normalize(string text) => Whitespace().Replace(text.Trim(), " ").ToUpperInvariant();
    public static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Whitespace();
}
