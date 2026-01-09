namespace StructuredINI.Tests {
    public class WriterTests {
        [INISection("SectionA")]
        public record SectionA(int Foo);

        [INISection("SectionB")]
        public record SectionB(string Bar);

        [INISection("SectionC")]
        public record SectionC(int Baz);

        [Fact]
        public void BufferWrite_And_WriteOut_WritesMultipleSections() {
            var writer = new StructuredINIWriter();

            Assert.True(writer.BufferWrite(new SectionA(42)));
            Assert.True(writer.BufferWrite(new SectionB("Hello")));

            var path = Path.Combine(Path.GetTempPath(), $"structuredini-writer-{Guid.NewGuid():N}.ini");
            try {
                Assert.True(writer.WriteOut(path));

                var content = File.ReadAllText(path);
                Assert.Contains("[SectionA]", content);
                Assert.Contains("Foo=42", content);
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
            Assert.False(writer.WriteOut(path));
        }

        [Fact]
        public void BufferWrite_OverwritesSameSectionName() {
            var writer = new StructuredINIWriter();
            Assert.True(writer.BufferWrite(new SectionA(1)));
            Assert.True(writer.BufferWrite(new SectionA(2)));

            var path = Path.Combine(Path.GetTempPath(), $"structuredini-writer-{Guid.NewGuid():N}.ini");
            try {
                Assert.True(writer.WriteOut(path));
                var content = File.ReadAllText(path);
                Assert.DoesNotContain("Foo=1", content);
                Assert.Contains("Foo=2", content);
            }
            finally {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Fact]
        public void WriteOut_PatchesExistingFile_PreservesUnknownKeysInSameSection() {
            var path = Path.Combine(Path.GetTempPath(), $"structuredini-writer-{Guid.NewGuid():N}.ini");
            try {
                File.WriteAllText(path, @"[SectionA]
Foo=1
Extra=KeepMe
");

                var writer = new StructuredINIWriter();
                Assert.True(writer.BufferWrite(new SectionA(2)));
                Assert.True(writer.WriteOut(path));

                var content = File.ReadAllText(path);
                Assert.Contains("[SectionA]", content);
                Assert.Contains("Foo=2", content);
                Assert.DoesNotContain("Foo=1", content);
                Assert.Contains("Extra=KeepMe", content);
            }
            finally {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Fact]
        public void WriteOut_PatchesExistingFile_PreservesOtherSections() {
            var path = Path.Combine(Path.GetTempPath(), $"structuredini-writer-{Guid.NewGuid():N}.ini");
            try {
                File.WriteAllText(path, @"[SectionA]
Foo=1

[SectionC]
Baz=99
");

                var writer = new StructuredINIWriter();
                Assert.True(writer.BufferWrite(new SectionA(2)));
                Assert.True(writer.WriteOut(path));

                var content = File.ReadAllText(path);
                Assert.Contains("[SectionA]", content);
                Assert.Contains("Foo=2", content);
                Assert.Contains("[SectionC]", content);
                Assert.Contains("Baz=99", content);
            }
            finally {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Fact]
        public void WriteOut_PatchesExistingFile_AppendsMissingSections() {
            var path = Path.Combine(Path.GetTempPath(), $"structuredini-writer-{Guid.NewGuid():N}.ini");
            try {
                File.WriteAllText(path, @"[SectionC]
Baz=99
");

                var writer = new StructuredINIWriter();
                Assert.True(writer.BufferWrite(new SectionA(42)));
                Assert.True(writer.WriteOut(path));

                var content = File.ReadAllText(path);
                Assert.Contains("[SectionC]", content);
                Assert.Contains("Baz=99", content);
                Assert.Contains("[SectionA]", content);
                Assert.Contains("Foo=42", content);

                Assert.True(content.IndexOf("[SectionC]", StringComparison.Ordinal) < content.IndexOf("[SectionA]", StringComparison.Ordinal));
            }
            finally {
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }
}