namespace StructuredINI.Tests {
    public class INIFileTests {
        [INISection("SectionA")]
        public record SectionA(int Foo = 1);

        [INISection("SectionB")]
        public record SectionB(string Bar = "Default");

        [INIFile]
        public record MyFile(SectionA A, SectionB B);

        [INISection("Same")]
        public record SameA(int Foo = 1);

        [INISection("Same")]
        public record SameB(int Bar = 2);

        [INIFile]
        public record BadFile(SameA A, SameB B);

        [Fact]
        public void Reader_CanRead_INIFile_Record() {
            var tempPath = Path.Combine(Path.GetTempPath(), $"structuredini_inifile_{Guid.NewGuid():N}.ini");
            try {
                File.WriteAllText(tempPath, @"[SectionA]
Foo=42

[SectionB]
Bar=Hello
");

                var reader = new StructuredINIReader();
                Assert.True(reader.Load(tempPath));

                Assert.True(reader.TryRead<MyFile>(out var file));
                Assert.Equal(42, file.A.Foo);
                Assert.Equal("Hello", file.B.Bar);
            }
            finally {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }

        [Fact]
        public void Writer_BufferWrite_INIFile_WritesAllSections_AndPreservesUnknownKeys() {
            var path = Path.Combine(Path.GetTempPath(), $"structuredini_inifile_writer_{Guid.NewGuid():N}.ini");
            try {
                File.WriteAllText(path, @"[SectionA]
Foo=1
Extra=KeepMe

[SectionB]
Bar=Old
");

                var writer = new StructuredINIWriter();
                Assert.True(writer.BufferWrite(new MyFile(new SectionA(99), new SectionB("New"))));
                Assert.True(writer.WriteOut(path));

                var content = File.ReadAllText(path);
                Assert.Contains("[SectionA]", content);
                Assert.Contains("Foo=99", content);
                Assert.Contains("Extra=KeepMe", content);
                Assert.Contains("[SectionB]", content);
                Assert.Contains("Bar=New", content);
                Assert.DoesNotContain("Bar=Old", content);
            }
            finally {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Fact]
        public void INIFile_WithDuplicateSectionNames_IsRejected() {
            var path = Path.Combine(Path.GetTempPath(), $"structuredini_inifile_dupe_{Guid.NewGuid():N}.ini");
            try {
                File.WriteAllText(path, @"[Same]
Foo=1
");

                var reader = new StructuredINIReader();
                Assert.True(reader.Load(path));
                Assert.False(reader.TryRead<BadFile>(out _));

                var writer = new StructuredINIWriter();
                Assert.False(writer.BufferWrite(new BadFile(new SameA(1), new SameB(2))));
            }
            finally {
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }
}
