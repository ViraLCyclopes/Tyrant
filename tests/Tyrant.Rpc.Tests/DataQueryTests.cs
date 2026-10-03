using System.Text.Json;
using Tyrant.Core.Data;
using Tyrant.Rpc.Data;

namespace Tyrant.Rpc.Tests;

public class DataQueryTests
{
    private static DataTable Table(params (string Name, string Json)[] rows) =>
        DataTable.From(rows.Select(r => (r.Name, JsonDocument.Parse(r.Json).RootElement)));

    [Fact]
    public void Numbers_sort_by_value_and_text_case_insensitively()
    {
        Assert.True(DataQuery.CompareValues("9", "10") < 0);
        Assert.True(DataQuery.CompareValues("-1.5", "2") < 0);
        Assert.True(DataQuery.CompareValues("apple", "Banana") < 0);
        Assert.True(DataQuery.CompareValues("", "a") > 0); // empty values sort last
        Assert.Equal(0, DataQuery.CompareValues(null, ""));
    }

    [Fact]
    public void Every_filter_word_must_match_some_field()
    {
        var table = Table(("Stego", """{"diet":"Herbivore","era":"Jurassic"}"""), ("Rex", """{"diet":"Carnivore","era":"Cretaceous"}"""));

        Assert.Equal(["Stego"], DataQuery.Apply(table, "herb  JURA", null, false).Select(r => r.Name));
        Assert.Empty(DataQuery.Apply(table, "herb cretaceous", null, false));
        Assert.Equal(2, DataQuery.Apply(table, "  ", null, false).Count);
    }

    [Fact]
    public void Sorting_uses_the_column_and_direction()
    {
        var table = Table(("a", """{"cost":100}"""), ("b", """{"cost":9}"""), ("c", """{"cost":10}"""));

        Assert.Equal(["b", "c", "a"], DataQuery.Apply(table, null, "cost", false).Select(r => r.Name));
        Assert.Equal(["a", "c", "b"], DataQuery.Apply(table, null, "cost", true).Select(r => r.Name));
    }

    [Fact]
    public void Empty_values_sort_last_in_both_directions()
    {
        var table = Table(("a", """{"cost":5}"""), ("b", """{"cost":null}"""), ("c", """{"cost":7}"""));

        Assert.Equal(["a", "c", "b"], DataQuery.Apply(table, null, "cost", false).Select(r => r.Name));
        Assert.Equal(["c", "a", "b"], DataQuery.Apply(table, null, "cost", true).Select(r => r.Name));
    }

    [Fact]
    public void Numbers_come_before_text_and_non_finite_values_count_as_text()
    {
        Assert.True(DataQuery.CompareValues("9", "1a") < 0);
        Assert.True(DataQuery.CompareValues("10", "1a") < 0);
        Assert.True(DataQuery.CompareValues("NaN", "1") > 0);
        Assert.True(DataQuery.CompareValues("Infinity", "5") > 0);
        var mixed = Table(("x", """{"v":"1a"}"""), ("y", """{"v":"10"}"""), ("z", """{"v":"9"}"""));
        Assert.Equal(["z", "y", "x"], DataQuery.Apply(mixed, null, "v", false).Select(r => r.Name));
    }
}
