namespace StructuredINI.Tests {
    public class WriterTests {
        [INISection("SectionA")]
        public record SectionA(int Foo);

        [INISection("SectionB")]
        public record SectionB(string Bar);

        [Fact]
        public void BufferWrite_ReplacesBufferedSectionsAndWritesMultipleSections() {
            var writer = new StructuredINIWriter();

            Assert.True(writer.BufferWrite(new SectionA(1)));
            Assert.True(writer.BufferWrite(new SectionB("Hello")));
            Assert.True(writer.BufferWrite(new SectionA(42)));

            var path = Path.Combine(Path.GetTempPath(), $"structuredini-writer-{Guid.NewGuid():N}.ini");
            try {
                Assert.True(writer.WriteOut(path, isConfigRoot: false));

                var content = File.ReadAllText(path);
                Assert.Contains("[SectionA]", content);
                Assert.Contains("Foo=42", content);
                Assert.DoesNotContain("Foo=1", content);
                Assert.Contains("[SectionB]", content);
                Assert.Contains("Bar=Hello", content);
            }
            finally {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Fact]
        public void WriteOut_ReturnsFalse_WhenNothingBuffered() {
            var writer = new StructuredINIWriter();
            var path = Path.Combine(Path.GetTempPath(), $"structuredini-writer-{Guid.NewGuid():N}.ini");
            Assert.False(writer.WriteOut(path, isConfigRoot: false));
        }

        [Fact]
        public void WriteOut_PatchesExistingKeysPreservesUnknownFieldsAndAppendsMissingSections() {
            var path = Path.Combine(Path.GetTempPath(), $"structuredini-writer-{Guid.NewGuid():N}.ini");
            try {
                File.WriteAllText(path, @"[SectionA]
Foo=1
Extra=KeepMe

[SectionC]
Baz=99
");

                var writer = new StructuredINIWriter();
                Assert.True(writer.BufferWrite(new SectionA(2)));
                Assert.True(writer.BufferWrite(new SectionB("Hello")));
                Assert.True(writer.WriteOut(path, isConfigRoot: false));

                var content = File.ReadAllText(path);
                Assert.Contains("[SectionA]", content);
                Assert.Contains("Foo=2", content);
                Assert.DoesNotContain("Foo=1", content);
                Assert.Contains("Extra=KeepMe", content);
                Assert.Contains("[SectionC]", content);
                Assert.Contains("Baz=99", content);
                Assert.Contains("[SectionB]", content);
                Assert.Contains("Bar=Hello", content);
                Assert.True(content.IndexOf("[SectionA]", StringComparison.Ordinal) < content.IndexOf("[SectionC]", StringComparison.Ordinal));
                Assert.True(content.IndexOf("[SectionC]", StringComparison.Ordinal) < content.IndexOf("[SectionB]", StringComparison.Ordinal));
            }
            finally {
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }
}
