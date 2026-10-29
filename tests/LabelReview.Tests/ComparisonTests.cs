using System.Text.Json;
using LabelReview;
using Xunit;

namespace LabelReview.Tests;

public class ComparisonTests
{
    internal static LabelDocument Label() => new(Comparison.Fields.Select(field =>
        new LabelField(field.Key, "Example", "Example", "read")).ToArray());

    internal static LabelDocument With(LabelDocument label, string key, string value, string state = "read") =>
        new(label.Fields.Select(field => field.Key == key ? field with
        { Value = value, Evidence = state == "absent" ? "" : "Printed evidence", State = state } : field).ToArray());

    [Fact]
    public void IdenticalLabelsHaveNoFindings() => Assert.All(Comparison.Compare(Label(), Label()), row => Assert.Equal("unchanged", row.Status));

    [Theory]
    [InlineData("Milk powder,\n cocoa", "milk powder, cocoa", "unchanged")]
    [InlineData("55 mg", "85 mg", "changed")]
    [InlineData("50 mg", "50 mcg", "changed")]
    [InlineData("Milk, soy", "Soy, milk", "changed")]
    [InlineData("Gluten-free", "Gluten free", "changed")]
    [InlineData("0.5 g", "5 g", "changed")]
    public void NormalizationOnlyIgnoresCaseAndLayout(string old, string current, string expected)
    {
        var rows = Comparison.Compare(With(Label(), "ingredients", old), With(Label(), "ingredients", current));
        Assert.Equal(expected, rows.Single(row => row.Key == "ingredients").Status);
    }

    [Theory]
    [InlineData("absent", "read", "added")]
    [InlineData("read", "absent", "removed")]
    [InlineData("unclear", "read", "review")]
    [InlineData("read", "unclear", "review")]
    [InlineData("unclear", "unclear", "review")]
    public void StateControlsFindingType(string oldState, string newState, string expected)
    {
        var before = With(Label(), "allergens", oldState == "absent" ? "" : "Milk", oldState);
        var after = With(Label(), "allergens", newState == "absent" ? "" : "Milk", newState);
        Assert.Equal(expected, Comparison.Compare(before, after).Single(row => row.Key == "allergens").Status);
    }

    [Fact]
    public void VisualChangesRemainDistinctFromTextChanges()
    {
        var rows = Comparison.Compare(Label(), With(Label(), "visualMarks", "New badge"));
        Assert.Equal("visual", rows.Single(row => row.Key == "visualMarks").Kind);
    }

    [Fact]
    public void MissingAndDuplicateFieldsAreRejected()
    {
        var label = Label();
        Assert.Throws<InvalidDataException>(() => Comparison.Validate(new LabelDocument(label.Fields[..^1])));
        label.Fields[0] = label.Fields[1];
        Assert.Throws<InvalidDataException>(() => Comparison.Validate(label));
    }

    [Theory]
    [InlineData("absent", "Milk", "")]
    [InlineData("absent", "", "Evidence")]
    [InlineData("read", "", "Evidence")]
    [InlineData("read", "Milk", "")]
    [InlineData("unclear", "", "")]
    [InlineData("guessed", "Milk", "Evidence")]
    public void InvalidEvidenceStatesAreRejected(string state, string value, string evidence)
    {
        var label = Label();
        label.Fields[0] = label.Fields[0] with { State = state, Value = value, Evidence = evidence };
        Assert.Throws<InvalidDataException>(() => Comparison.Validate(label));
    }

    [Fact]
    public void NullOversizedAndUnknownFieldsAreRejected()
    {
        var label = Label();
        label.Fields[0] = label.Fields[0] with { Value = new string('x', 3001) };
        Assert.Throws<InvalidDataException>(() => Comparison.Validate(label));
        label.Fields[0] = label.Fields[0] with { Key = "hidden", Value = "Example" };
        Assert.Throws<InvalidDataException>(() => Comparison.Validate(label));
        label.Fields[0] = null!;
        Assert.Throws<InvalidDataException>(() => Comparison.Validate(label));
    }

    [Fact]
    public void UnknownJsonPropertiesAndMissingFieldsAreRejected()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<LabelDocument>("{}", Comparison.Json));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<LabelDocument>("{\"fields\":[],\"extra\":true}", Comparison.Json));
    }

    [Fact]
    public void ReportOrderIsStableEvenWhenProviderReordersFields()
    {
        var reversed = new LabelDocument(Label().Fields.Reverse().ToArray());
        Assert.Equal(Comparison.Fields.Select(field => field.Key), Comparison.Compare(Label(), reversed).Select(row => row.Key));
    }
}
