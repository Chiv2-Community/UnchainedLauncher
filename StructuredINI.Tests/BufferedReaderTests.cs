using System;
using System.IO;
using Xunit;

namespace StructuredINI.Tests
{
    public class StructuredINIReaderTests
    {
        [INISection("SectionA")]
        public record SectionA(int Foo, string Bar);

        [INISection("SectionB")]
        public record SectionB(double Baz);

        [Fact]
        public void LoadAndRead_MultipleSections()
        {
            var tempPath = Path.Combine(Path.GetTempPath(), $"structuredini_bufferedreader_{Guid.NewGuid():N}.ini");
            try
            {
                File.WriteAllText(tempPath, @"[SectionA]
Foo=42
Bar=Hello

[SectionB]
Baz=3.14
");

                var reader = new StructuredINIReader();
                Assert.True(reader.Load(tempPath));

                Assert.True(reader.TryRead<SectionA>(out var a));
                Assert.Equal(42, a.Foo);
                Assert.Equal("Hello", a.Bar);

                Assert.True(reader.TryRead<SectionB>(out var b));
                Assert.Equal(3.14, b.Baz);
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }

        [Fact]
        public void TryRead_MissingSection_ReturnsFalse()
        {
            var tempPath = Path.Combine(Path.GetTempPath(), $"structuredini_bufferedreader_{Guid.NewGuid():N}.ini");
            try
            {
                File.WriteAllText(tempPath, @"[SectionA]
Foo=1
Bar=X
");

                var reader = new StructuredINIReader();
                Assert.True(reader.Load(tempPath));

                Assert.False(reader.TryRead<SectionB>(out _));
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }

        [Fact]
        public void DuplicateSections_LastOneWins()
        {
            var tempPath = Path.Combine(Path.GetTempPath(), $"structuredini_bufferedreader_{Guid.NewGuid():N}.ini");
            try
            {
                File.WriteAllText(tempPath, @"[SectionA]
Foo=1
Bar=First

[SectionA]
Foo=2
Bar=Second
");

                var reader = new StructuredINIReader();
                Assert.True(reader.Load(tempPath));

                var a = reader.Read<SectionA>();
                Assert.Equal(2, a.Foo);
                Assert.Equal("Second", a.Bar);
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }
    }
}
