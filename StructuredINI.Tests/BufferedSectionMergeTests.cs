namespace StructuredINI.Tests;

public sealed class BufferedSectionMergeTests {
    [Fact]
    public void MergeReplacesSelectedKeysButBufferSectionStillReplacesTheSection() {
        var path = Path.Combine(Path.GetTempPath(), "ini-merge-" + Guid.NewGuid().ToString("N") + ".ini");
        try {
            var writer = new StructuredINIWriter();
            writer.BufferSection("Section", [new("Default", "yes"), new(".Array", "old"), new("Scalar", "old")]);
            writer.MergeSection("section", [new("!Array", "ClearArray"), new(".Array", "A"), new(".Array", "A"), new("Scalar", "new")]);
            Assert.True(writer.WriteOut(path));
            var text = File.ReadAllText(path);
            Assert.Contains("Default=yes", text);
            Assert.DoesNotContain("old", text);
            Assert.Contains("Scalar=new", text);
            Assert.Equal(2, text.Split('\n').Count(line => line.TrimEnd('\r') == ".Array=A"));
            File.Delete(path);
            writer.BufferSection("SECTION", [new("Only", "replacement")]);
            Assert.True(writer.WriteOut(path));
            text = File.ReadAllText(path);
            Assert.Contains("Only=replacement", text);
            Assert.DoesNotContain("Default", text);
            Assert.DoesNotContain("Array", text);
        }
        finally { File.Delete(path); }
    }
}
