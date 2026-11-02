namespace StructuredINI.Tests;

public sealed class DynamicEntryTests {
    [INISection("Demo")]
    private sealed record Selection(string[] Names, int Number = 0);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DynamicAndModelSerializationUseTheSameArrayRepresentation(bool empty) {
        var parser = new StructuredINIParser();
        var values = empty ? Array.Empty<string>() : new[] { "A", "A", "B" };
        var typed = parser.ReadEntries(parser.Serialize(new Selection(values), isConfigRoot: false));
        var dynamic = parser.SerializeEntries("Names", values, collection: true, isConfigRoot: false).ToArray();
        Assert.Equal(typed.Where(entry => entry.Name == "Names").Select(entry => KeyValuePair.Create(entry.Key, entry.Value)), dynamic);
        Assert.Equal(empty ? new[] { "!Names" } : new[] { "!Names", "+Names", ".Names", "+Names" }, dynamic.Select(entry => entry.Key));
        Assert.Equal("ClearArray", dynamic[0].Value);
        var document = "[Demo]\n" + string.Join("\n", dynamic.Select(entry => $"{entry.Key}={entry.Value}"));
        Assert.Equal(values, parser.Deserialize<Selection>(document).Names);
        var inherited = "[Demo]\n+Names=Inherited\n+Names=B\n";
        Assert.Equal(values, parser.Deserialize<Selection>(inherited + document).Names);
    }

    [Fact]
    public void UntypedReaderPreservesOperationsAndRepeatedUnknownFields() {
        var parser = new StructuredINIParser();
        const string text = "; comment\r[Demo]\r!Names=ClearArray\r.Names=A\r.Names=A\r[demo]\r+Names=B\r-Names=A\rUnknown=value=with=equals\rNumber=7";
        var entries = parser.ReadEntries(text, strict: true);
        Assert.Equal(new[] { "!Names", ".Names", ".Names", "+Names", "-Names", "Unknown", "Number" }, entries.Select(entry => entry.Key));
        Assert.Equal("value=with=equals", entries[5].Value);
        var typed = parser.Deserialize<Selection>(text);
        Assert.Equal(new[] { "A", "B" }, typed.Names);
        Assert.Equal(7, typed.Number);
    }

    [Theory]
    [InlineData('+', new[] { "A", "A", "B" })]
    [InlineData('.', new[] { "A", "A", "B", "A" })]
    [InlineData('-', new[] { "A", "B" })]
    [InlineData('!', new string[0])]
    public void ExplicitArrayOperationsPreserveInheritedValuesAndRequestedOperators(char operation, string[] expected) {
        var parser = new StructuredINIParser();
        var values = operation == '!' ? Array.Empty<string>() : new[] { "A" };
        var commands = parser.SerializeEntries("Names", values, collection: true, isConfigRoot: false, operation: operation).ToArray();
        Assert.Equal(operation + "Names", Assert.Single(commands).Key);
        Assert.Equal(operation != '!', StructuredINIParser.IsArrayDelta(commands));
        var text = "[Demo]\n+Names=A\n.Names=A\n+Names=B\n" + string.Join("\n", commands.Select(entry => $"{entry.Key}={entry.Value}"));
        Assert.Equal(expected, parser.Deserialize<Selection>(text).Names);
    }

    [Theory]
    [InlineData('+', false, false)]
    [InlineData('?', true, false)]
    [InlineData('!', true, true)]
    public void ExplicitOperationsRejectScalarsUnknownOperatorsAndNonemptyClears(char operation, bool collection, bool nonempty) {
        var values = nonempty ? new[] { "A" } : Array.Empty<string>();
        Assert.Throws<ArgumentException>(() => new StructuredINIParser().SerializeEntries("Names", values, collection, isConfigRoot: false, operation).ToArray());
    }

    [Theory]
    [InlineData("Key=value")]
    [InlineData("[Demo]\n=value")]
    [InlineData("[Demo]\n!=Clear")]
    [InlineData("[Demo]\nKey=bad\0value")]
    public void StrictReadingRejectsMalformedEntries(string text) {
        Assert.Throws<FormatException>(() => new StructuredINIParser().ReadEntries(text, strict: true));
    }
}
