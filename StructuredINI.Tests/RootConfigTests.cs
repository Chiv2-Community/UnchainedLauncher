namespace StructuredINI.Tests;

public sealed class RootConfigTests {
    [INISection("Demo")]
    private sealed record Selection(string[] Names);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ModelSerializationRequiresAnExplicitRootMode(bool isConfigRoot) {
        var parser = new StructuredINIParser();
        var values = new[] { "Courtyard", "Wardenglade", "Courtyard", "Bazaar", "Courtyard" };
        var text = parser.Serialize(new Selection(values), isConfigRoot);
        Assert.Equal(values, parser.Deserialize<Selection>(text).Names);
        Assert.Equal(isConfigRoot, parser.ReadEntries(text).All(entry => entry.Key == "Names"));
        var empty = parser.Serialize(new Selection([]), isConfigRoot);
        Assert.Equal(isConfigRoot ? [] : new[] { "!Names" }, parser.ReadEntries(empty).Select(entry => entry.Key));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void WriterResolvesCommandsOnlyForRootsAndPreservesOrder(bool isConfigRoot) {
        var path = Path.GetTempFileName();
        try {
            File.WriteAllText(path, "; keep\n[Other]\n+Untouched=old\n[Demo]\nNames=stale\n");
            var writer = new StructuredINIWriter();
            writer.BufferSection("Demo", new[] {
                KeyValuePair.Create("!Names", "ClearArray"),
                KeyValuePair.Create("+Names", "A"), KeyValuePair.Create("+Names", "A"),
                KeyValuePair.Create(".Names", "A"), KeyValuePair.Create("+Names", "B"),
                KeyValuePair.Create("-Names", "A"), KeyValuePair.Create(".Names", "A"),
                KeyValuePair.Create("!Empty", "ClearArray")
            });
            Assert.True(writer.WriteOut(path, isConfigRoot));
            var text = File.ReadAllText(path);
            Assert.Contains("; keep", text);
            Assert.Equal(new[] { "A", "B", "A" }, new StructuredINIParser().Deserialize<Selection>(text).Names);
            var entries = new StructuredINIParser().ReadEntries(text);
            if (isConfigRoot) {
                Assert.All(entries, entry => Assert.DoesNotContain(entry.Key[0], "!+.-"));
                Assert.DoesNotContain(entries, entry => entry.Name == "Empty");
                Assert.Contains("Untouched=old", text);
            } else {
                Assert.Contains("!Names=ClearArray", text);
                Assert.Contains("-Names=A", text);
                Assert.Contains("!Empty=ClearArray", text);
            }
            Assert.True(writer.WriteOut(path, isConfigRoot));
            Assert.Equal(text, File.ReadAllText(path));
        } finally { File.Delete(path); }
    }
}
