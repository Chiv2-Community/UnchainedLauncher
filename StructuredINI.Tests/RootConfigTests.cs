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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RootWriteMatchesMaterializedLayerBytes(bool existingFile) {
        var path = Path.GetTempFileName();
        try {
            const string original = "\n\n\n; keep\r\n[demo]\r\nNames=old\r\n\r\n\r\n[Other]\n+Untouched=a=b\n[DEMO]\n# keep too\n\n\n";
            var writer = new StructuredINIWriter();
            writer.BufferWrite(new Selection(["A", "A", "B"]));
            writer.BufferSection("Empty", new[] { KeyValuePair.Create("!Names", "ClearArray") });
            if (existingFile) File.WriteAllText(path, original);
            else File.Delete(path);
            Assert.True(writer.WriteOut(path, isConfigRoot: false));
            var expected = System.Text.Encoding.UTF8.GetBytes(StructuredINIWriter.SerializeDocument(File.ReadAllText(path), isConfigRoot: true));
            if (existingFile) File.WriteAllText(path, original);
            else File.Delete(path);
            Assert.True(writer.WriteOut(path, isConfigRoot: true));
            Assert.Equal(expected, File.ReadAllBytes(path));
            Assert.True(writer.WriteOut(path, isConfigRoot: true));
            Assert.Equal(expected, File.ReadAllBytes(path));
        } finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WriterRemovesLeadingBlanksAndUsesOneBlankBetweenSections(bool isConfigRoot) {
        const string original = "\n \r\n\n; preamble\n\n\n[A]\n; keep\n\n!Empty=ClearArray\n\n\n[Empty]\n!Items=ClearArray\n \n[B]\nX=1\n\n\n";
        var expected = "; preamble\n[A]\n; keep\n"
            + (isConfigRoot ? "" : "\n!Empty=ClearArray\n")
            + "\n[Empty]\n" + (isConfigRoot ? "" : "!Items=ClearArray\n")
            + "\n[B]\nX=2\n";
        expected = expected.Replace("\n", Environment.NewLine);
        var path = Path.GetTempFileName();
        try {
            File.WriteAllText(path, original);
            var writer = new StructuredINIWriter();
            writer.BufferSection("B", new[] { KeyValuePair.Create("X", "2") });
            Assert.True(writer.WriteOut(path, isConfigRoot));
            Assert.Equal(expected, File.ReadAllText(path));
            Assert.True(writer.WriteOut(path, isConfigRoot));
            Assert.Equal(expected, File.ReadAllText(path));
            if (isConfigRoot)
                Assert.Equal(expected.Replace("X=2", "X=1"), StructuredINIWriter.SerializeDocument(original, isConfigRoot: true));
        } finally { File.Delete(path); }
    }
}
