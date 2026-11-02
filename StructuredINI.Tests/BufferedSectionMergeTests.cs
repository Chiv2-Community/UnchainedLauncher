namespace StructuredINI.Tests;

public sealed class BufferedSectionMergeTests {
    [INISection("Section")]
    private sealed record Selection(string[] Maps, int Index);

    [Fact]
    public void TypedMergeUsesExistingSerializationAndPreservesPendingNeighborFields() {
        var root = OperatingSystem.IsLinux() ? "/tmp/opencode" : Path.GetTempPath();
        var path = Path.Combine(root, "ini-typed-merge-" + Guid.NewGuid().ToString("N") + ".ini");
        try {
            var writer = new StructuredINIWriter();
            writer.BufferSection("Section", [new("Neighbor", "keep"), new(".Maps", "old")]);
            Assert.True(writer.BufferWrite(new Selection(["A", "A", "B"], 1), merge: true));
            Assert.True(writer.WriteOut(path, isConfigRoot: false));
            var text = File.ReadAllText(path);
            Assert.Contains("Neighbor=keep", text);
            Assert.Equal(new[] { "!Maps=ClearArray", "+Maps=A", ".Maps=A", "+Maps=B" },
                text.Split('\n').Select(line => line.TrimEnd('\r')).Where(line => line.Contains("Maps=")));
            var parsed = new StructuredINIParser().Deserialize<Selection>(text);
            Assert.Equal(new[] { "A", "A", "B" }, parsed.Maps);
            Assert.Equal(1, parsed.Index);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void MergeReplacesSelectedKeysButBufferSectionStillReplacesTheSection() {
        var path = Path.Combine(Path.GetTempPath(), "ini-merge-" + Guid.NewGuid().ToString("N") + ".ini");
        try {
            var writer = new StructuredINIWriter();
            writer.BufferSection("Section", [new("Default", "yes"), new(".Array", "old"), new("Scalar", "old")]);
            writer.MergeSection("section", [new("!Array", "ClearArray"), new(".Array", "A"), new(".Array", "A"), new("Scalar", "new")]);
            Assert.True(writer.WriteOut(path, isConfigRoot: false));
            var text = File.ReadAllText(path);
            Assert.Contains("Default=yes", text);
            Assert.DoesNotContain("old", text);
            Assert.Contains("Scalar=new", text);
            Assert.Equal(2, text.Split('\n').Count(line => line.TrimEnd('\r') == ".Array=A"));
            File.Delete(path);
            writer.BufferSection("SECTION", [new("Only", "replacement")]);
            Assert.True(writer.WriteOut(path, isConfigRoot: false));
            text = File.ReadAllText(path);
            Assert.Contains("Only=replacement", text);
            Assert.DoesNotContain("Default", text);
            Assert.DoesNotContain("Array", text);
        }
        finally { File.Delete(path); }
    }
}
