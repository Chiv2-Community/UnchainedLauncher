using JetBrains.Annotations;
using LanguageExt;
using LanguageExt.Common;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Utilities;

namespace UnchainedLauncher.Core.Tests.Unit.Services.PakDir {
    [TestSubject(typeof(Core.Services.Mods.PakDir))]
    public class PakDirTest {
        public const string BaseTestDir = "TestDirectories";

        public static readonly IPakDir.MakeFileWriter StaticMkWriter = (string path) => {
            Stream fileStream = new MemoryStream();
            StreamWriter streamWriter = new StreamWriter(fileStream);
            var fileContents = $"dummy contents for '{path}'";
            streamWriter.Write(fileContents);
            return new FileWriter(path, fileStream, fileContents.Length);
        };

        /// <summary>
        /// this should not have multiple versions of the same mod. Make a different dataset for that
        /// </summary>
        private static readonly List<ReleaseCoordinates> DummyMods = new List<ReleaseCoordinates> {
            new("Unchained-Mods", "Mod1", "1.0.0"),
            new("Unchained-Mods", "Mod2", "1.0.0"),
            new("Chained-Mods", "Mod1", "1.0.0"),
            new("Chained-Mods", "Mod2", "1.0.0"),
        };

        // for theories
        public static List<object[]> DummyModsParam = DummyMods.Select(x => new object[] { x }).ToList();

        private static async Task InstallDummy(IPakDir pd, ReleaseCoordinates coords) {
            var requests = new List<ModInstallRequest> {
                new ModInstallRequest(coords, StaticMkWriter)
            };
            await foreach (var result in pd.InstallModSet(requests, Option<AccumulatedMemoryProgress>.None)) {
                result.IfLeft(e => throw e);
            }
        }

        private static void PrepareTestDir(string dir) {
            if (Directory.Exists(dir)) {
                Directory.Delete(dir, true);
            }
            Directory.CreateDirectory(dir);
        }

        private static void PrepareTestDirWithSig(string dir) {
            PrepareTestDir(dir);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "pakchunk0-WindowsNoEditor.sig"), "test sig file");
        }

        [Fact]
        public void DeleteOrphanedSigs() {
            var thisTestPath = Path.Join(BaseTestDir, System.Reflection.MethodBase.GetCurrentMethod()?.Name);
            PrepareTestDirWithSig(thisTestPath);

            var orphanedSigName = Path.Join(thisTestPath, "orphaned.sig");
            File.WriteAllText(orphanedSigName, "orphaned sig file");

            var pd = new Core.Services.Mods.PakDir(thisTestPath, Enumerable.Empty<ManagedPak>());
            Assert.True(File.Exists(orphanedSigName));
            pd.DeleteOrphanedSigs();
            Assert.False(File.Exists(orphanedSigName));
        }

        private void throwErrors(IEnumerable<Error> errors) {
            throw Error.Many(errors.ToSeq());
        }

        [Fact]
        public async Task CleanUpPakDir() {
            var thisTestPath = Path.Join(BaseTestDir, System.Reflection.MethodBase.GetCurrentMethod()?.Name);
            PrepareTestDirWithSig(thisTestPath);

            var pd = new Core.Services.Mods.PakDir(thisTestPath, Enumerable.Empty<ManagedPak>());
            new List<string> {
                "pakchunk0-WindowsNoEditor.pak",
                "file1.pak",
                "file2.pak",
                "file1.sig",
                "file2.sig",
                "file3.sig"
            }.Map(p => Path.Join(thisTestPath, p))
                .ToList().ForEach(p => File.WriteAllText(p, "mysterious contents"));

            var m1 = new ReleaseCoordinates("Unchained-Mods", "Mod1", "1.0.0");
            await InstallDummy(pd, m1);
            pd.Reset().IfLeft(throwErrors);
            Assert.Empty(
                Directory.EnumerateFiles(thisTestPath)
                    .Map(Path.GetFileName)
                    .Filter(p => p != "pakchunk0-WindowsNoEditor.pak")
                    .Filter(p => p != "pakchunk0-WindowsNoEditor.sig")
                );
        }

        [Fact]
        public async Task InstallModSet_SkipsExistingMods_DoesNotRedownload() {
            var thisTestPath = Path.Join(BaseTestDir, nameof(InstallModSet_SkipsExistingMods_DoesNotRedownload));
            PrepareTestDirWithSig(thisTestPath);

            var writerCallCount = 0;
            IPakDir.MakeFileWriter countingWriter = path => {
                writerCallCount++;
                return StaticMkWriter(path);
            };

            var pd = new Core.Services.Mods.PakDir(thisTestPath, Enumerable.Empty<ManagedPak>());
            var coords = new ReleaseCoordinates("Unchained-Mods", "Mod1", "1.0.0");

            // First install - should download
            var firstResults = await pd.InstallModSet(
                new[] { new ModInstallRequest(coords, countingWriter) },
                Option<AccumulatedMemoryProgress>.None
            ).ToListAsync();
            Assert.Equal(1, writerCallCount);
            Assert.Single(firstResults);
            firstResults[0].IfLeft(e => throw e);

            // Second install - same mod, should skip (no redownload)
            var secondResults = await pd.InstallModSet(
                new[] { new ModInstallRequest(coords, countingWriter) },
                Option<AccumulatedMemoryProgress>.None
            ).ToListAsync();
            Assert.Equal(1, writerCallCount); // Unchanged - Writer was not called again
            Assert.Single(secondResults);
            secondResults[0].IfLeft(e => throw e);
        }

        [Fact]
        public async Task InstallModSet_RelocatesExistingMods_WhenOrderChanges() {
            var thisTestPath = Path.Join(BaseTestDir, nameof(InstallModSet_RelocatesExistingMods_WhenOrderChanges));
            PrepareTestDirWithSig(thisTestPath);

            var pd = new Core.Services.Mods.PakDir(thisTestPath, Enumerable.Empty<ManagedPak>());
            var modA = new ReleaseCoordinates("Unchained-Mods", "ModA", "1.0.0");
            var modB = new ReleaseCoordinates("Chained-Mods", "ModB", "1.0.0");

            // Install [A, B] - creates qa__-__ModA.pak, qb__-__ModB.pak (lexicographic by index)
            var firstResults = await pd.InstallModSet(
                new[] {
                    new ModInstallRequest(modA, StaticMkWriter),
                    new ModInstallRequest(modB, StaticMkWriter)
                },
                Option<AccumulatedMemoryProgress>.None
            ).ToListAsync();
            firstResults.ForEach(r => r.IfLeft(e => throw e));

            var firstPakNames = pd.ManagedPaks.OrderBy(p => p.Priority).Select(p => p.PakFileName).ToList();
            Assert.Equal(2, firstPakNames.Count);

            // Install [B, A] - order reversed; should relocate (move) not redownload
            var writerCallCount = 0;
            IPakDir.MakeFileWriter countingWriter = path => {
                writerCallCount++;
                return StaticMkWriter(path);
            };

            var secondResults = await pd.InstallModSet(
                new[] {
                    new ModInstallRequest(modB, countingWriter),
                    new ModInstallRequest(modA, countingWriter)
                },
                Option<AccumulatedMemoryProgress>.None
            ).ToListAsync();
            secondResults.ForEach(r => r.IfLeft(e => throw e));

            // Writer should not have been called - we moved existing files, not downloaded
            Assert.Equal(0, writerCallCount);

            var secondPakNames = pd.ManagedPaks.OrderBy(p => p.Priority).Select(p => p.PakFileName).ToList();
            Assert.Equal(2, secondPakNames.Count);
            // B is now at index 0 (qa), A at index 1 (qb)
            Assert.Contains("ModB", secondPakNames[0]);
            Assert.Contains("ModA", secondPakNames[1]);
        }

        [Fact]
        public async Task Uninstall_DeletesFileAndSig() {
            var thisTestPath = Path.Join(BaseTestDir, nameof(Uninstall_DeletesFileAndSig));
            PrepareTestDirWithSig(thisTestPath);

            var pd = new Core.Services.Mods.PakDir(thisTestPath, Enumerable.Empty<ManagedPak>());
            var coords = new ReleaseCoordinates("Unchained-Mods", "Mod1", "1.0.0");

            // Install the mod first
            await InstallDummy(pd, coords);
            Assert.Single(pd.ManagedPaks);
            var installedPak = pd.ManagedPaks.First();
            var pakPath = Path.Join(thisTestPath, installedPak.PakFileName);
            var sigPath = Path.ChangeExtension(pakPath, ".sig");
            Assert.True(File.Exists(pakPath));
            Assert.True(File.Exists(sigPath));

            // Uninstall
            var result = pd.Uninstall(coords);
            result.IfLeft(e => throw e);

            // Verify file and sig are deleted
            // Note: ManagedPaks list is updated on next PakDir construction (SynchronizeWithDir)
            Assert.False(File.Exists(pakPath));
            Assert.False(File.Exists(sigPath));
        }

        [Fact]
        public async Task Uninstall_SynchronizeRemovesMissingPaks() {
            var thisTestPath = Path.Join(BaseTestDir, nameof(Uninstall_SynchronizeRemovesMissingPaks));
            PrepareTestDirWithSig(thisTestPath);

            var pd = new Core.Services.Mods.PakDir(thisTestPath, Enumerable.Empty<ManagedPak>());
            var coords = new ReleaseCoordinates("Unchained-Mods", "Mod1", "1.0.0");

            // Install the mod first
            await InstallDummy(pd, coords);
            var installedPak = pd.ManagedPaks.First();

            // Uninstall (deletes file but doesn't update in-memory list)
            pd.Uninstall(coords);

            // Creating a new PakDir with the old managed paks should synchronize and remove missing entries
            var pdAfterSync = new Core.Services.Mods.PakDir(thisTestPath, pd.ManagedPaks);
            Assert.Empty(pdAfterSync.ManagedPaks);
        }

        [Fact]
        public void Uninstall_NonExistentMod_Succeeds() {
            var thisTestPath = Path.Join(BaseTestDir, nameof(Uninstall_NonExistentMod_Succeeds));
            PrepareTestDirWithSig(thisTestPath);

            var pd = new Core.Services.Mods.PakDir(thisTestPath, Enumerable.Empty<ManagedPak>());
            var coords = new ReleaseCoordinates("Unchained-Mods", "NonExistent", "1.0.0");

            // Uninstalling a mod that doesn't exist should succeed (no-op)
            var result = pd.Uninstall(coords);
            Assert.True(result.IsRight);
        }

        [Fact]
        public async Task SignAll_SignsAllManagedPaks() {
            var thisTestPath = Path.Join(BaseTestDir, nameof(SignAll_SignsAllManagedPaks));
            PrepareTestDirWithSig(thisTestPath);

            var pd = new Core.Services.Mods.PakDir(thisTestPath, Enumerable.Empty<ManagedPak>());
            var mod1 = new ReleaseCoordinates("Unchained-Mods", "Mod1", "1.0.0");
            var mod2 = new ReleaseCoordinates("Unchained-Mods", "Mod2", "1.0.0");

            await InstallDummy(pd, mod1);
            await InstallDummy(pd, mod2);

            // Delete all sig files except base
            foreach (var pak in pd.ManagedPaks) {
                var sigPath = Path.ChangeExtension(Path.Join(thisTestPath, pak.PakFileName), ".sig");
                if (File.Exists(sigPath)) File.Delete(sigPath);
            }

            // Verify no sig files exist for mods
            foreach (var pak in pd.ManagedPaks) {
                var sigPath = Path.ChangeExtension(Path.Join(thisTestPath, pak.PakFileName), ".sig");
                Assert.False(File.Exists(sigPath));
            }

            // Sign all
            var result = pd.SignAll();
            result.IfLeft(throwErrors);

            // Verify all sig files now exist
            foreach (var pak in pd.ManagedPaks) {
                var sigPath = Path.ChangeExtension(Path.Join(thisTestPath, pak.PakFileName), ".sig");
                Assert.True(File.Exists(sigPath));
            }
        }

        [Fact]
        public async Task UnSignAll_RemovesAllSigFiles() {
            var thisTestPath = Path.Join(BaseTestDir, nameof(UnSignAll_RemovesAllSigFiles));
            PrepareTestDirWithSig(thisTestPath);

            var pd = new Core.Services.Mods.PakDir(thisTestPath, Enumerable.Empty<ManagedPak>());
            var mod1 = new ReleaseCoordinates("Unchained-Mods", "Mod1", "1.0.0");
            var mod2 = new ReleaseCoordinates("Unchained-Mods", "Mod2", "1.0.0");

            await InstallDummy(pd, mod1);
            await InstallDummy(pd, mod2);

            // Verify sig files exist
            foreach (var pak in pd.ManagedPaks) {
                var sigPath = Path.ChangeExtension(Path.Join(thisTestPath, pak.PakFileName), ".sig");
                Assert.True(File.Exists(sigPath));
            }

            // Unsign all
            var result = pd.UnSignAll();
            result.IfLeft(throwErrors);

            // Verify no sig files exist for mods
            foreach (var pak in pd.ManagedPaks) {
                var sigPath = Path.ChangeExtension(Path.Join(thisTestPath, pak.PakFileName), ".sig");
                Assert.False(File.Exists(sigPath));
            }
        }

        [Fact]
        public void Constructor_SynchronizesWithDir_RemovesMissingPaks() {
            var thisTestPath = Path.Join(BaseTestDir, nameof(Constructor_SynchronizesWithDir_RemovesMissingPaks));
            PrepareTestDirWithSig(thisTestPath);

            // Create managed paks list with entries that don't actually exist on disk
            var existingPakName = "qa__-__ExistingMod.pak";
            var missingPakName = "qb__-__MissingMod.pak";

            // Create only the existing pak file
            File.WriteAllText(Path.Join(thisTestPath, existingPakName), "existing mod contents");

            var managedPaks = new List<ManagedPak> {
                new ManagedPak(new ReleaseCoordinates("Org", "ExistingMod", "1.0.0"), existingPakName, 0),
                new ManagedPak(new ReleaseCoordinates("Org", "MissingMod", "1.0.0"), missingPakName, 1)
            };

            // Constructor should synchronize and remove the missing entry
            var pd = new Core.Services.Mods.PakDir(thisTestPath, managedPaks);

            // Only the existing pak should remain
            Assert.Single(pd.ManagedPaks);
            Assert.Equal("ExistingMod", pd.ManagedPaks.First().Coordinates.ModuleName);
        }

        [Fact]
        public void Constructor_PreservesAllPaks_WhenAllExist() {
            var thisTestPath = Path.Join(BaseTestDir, nameof(Constructor_PreservesAllPaks_WhenAllExist));
            PrepareTestDirWithSig(thisTestPath);

            var pak1Name = "qa__-__Mod1.pak";
            var pak2Name = "qb__-__Mod2.pak";

            // Create both pak files
            File.WriteAllText(Path.Join(thisTestPath, pak1Name), "mod1 contents");
            File.WriteAllText(Path.Join(thisTestPath, pak2Name), "mod2 contents");

            var managedPaks = new List<ManagedPak> {
                new ManagedPak(new ReleaseCoordinates("Org", "Mod1", "1.0.0"), pak1Name, 0),
                new ManagedPak(new ReleaseCoordinates("Org", "Mod2", "1.0.0"), pak2Name, 1)
            };

            var pd = new Core.Services.Mods.PakDir(thisTestPath, managedPaks);

            // Both paks should be preserved
            Assert.Equal(2, pd.ManagedPaks.Count());
        }

        [Fact]
        public async Task InstallModSet_EmptySet_ReturnsNoResults() {
            var thisTestPath = Path.Join(BaseTestDir, nameof(InstallModSet_EmptySet_ReturnsNoResults));
            PrepareTestDirWithSig(thisTestPath);

            var pd = new Core.Services.Mods.PakDir(thisTestPath, Enumerable.Empty<ManagedPak>());

            var results = await pd.InstallModSet(
                Enumerable.Empty<ModInstallRequest>(),
                Option<AccumulatedMemoryProgress>.None
            ).ToListAsync();

            Assert.Empty(results);
            Assert.Empty(pd.ManagedPaks);
        }

        [Fact]
        public async Task InstallModSet_DuplicateModuleNames_AddsOrgPrefix() {
            var thisTestPath = Path.Join(BaseTestDir, nameof(InstallModSet_DuplicateModuleNames_AddsOrgPrefix));
            PrepareTestDirWithSig(thisTestPath);

            var pd = new Core.Services.Mods.PakDir(thisTestPath, Enumerable.Empty<ManagedPak>());

            // Same module name, different organizations
            var mod1 = new ReleaseCoordinates("Unchained-Mods", "SharedName", "1.0.0");
            var mod2 = new ReleaseCoordinates("Chained-Mods", "SharedName", "1.0.0");

            var results = await pd.InstallModSet(
                new[] {
                    new ModInstallRequest(mod1, StaticMkWriter),
                    new ModInstallRequest(mod2, StaticMkWriter)
                },
                Option<AccumulatedMemoryProgress>.None
            ).ToListAsync();

            results.ForEach(r => r.IfLeft(e => throw e));

            // Both mods should be installed with org prefixes to differentiate
            Assert.Equal(2, pd.ManagedPaks.Count());
            var pakNames = pd.ManagedPaks.Select(p => p.PakFileName).ToList();

            // Verify both pak names contain the org prefix
            Assert.Contains(pakNames, n => n.Contains("Unchained-Mods_SharedName"));
            Assert.Contains(pakNames, n => n.Contains("Chained-Mods_SharedName"));
        }

        [Fact]
        public void DeleteOrphanedSigs_PreservesManagedSigs() {
            var thisTestPath = Path.Join(BaseTestDir, nameof(DeleteOrphanedSigs_PreservesManagedSigs));
            PrepareTestDirWithSig(thisTestPath);

            var pakName = "qa__-__TestMod.pak";
            var sigName = "qa__-__TestMod.sig";

            // Create pak and its sig file
            File.WriteAllText(Path.Join(thisTestPath, pakName), "mod contents");
            File.WriteAllText(Path.Join(thisTestPath, sigName), "sig contents");

            // Also create an orphaned sig
            var orphanedSigName = "orphaned.sig";
            File.WriteAllText(Path.Join(thisTestPath, orphanedSigName), "orphaned sig");

            var managedPaks = new List<ManagedPak> {
                new ManagedPak(new ReleaseCoordinates("Org", "TestMod", "1.0.0"), pakName, 0)
            };

            var pd = new Core.Services.Mods.PakDir(thisTestPath, managedPaks);
            pd.DeleteOrphanedSigs();

            // Managed sig should be preserved, orphaned sig should be deleted
            Assert.True(File.Exists(Path.Join(thisTestPath, sigName)));
            Assert.False(File.Exists(Path.Join(thisTestPath, orphanedSigName)));
        }

        [Fact]
        public void DeleteOrphanedSigs_PreservesBaseSig() {
            var thisTestPath = Path.Join(BaseTestDir, nameof(DeleteOrphanedSigs_PreservesBaseSig));
            PrepareTestDirWithSig(thisTestPath);

            // Create base pak file (sig is only preserved if corresponding pak exists)
            File.WriteAllText(Path.Join(thisTestPath, "pakchunk0-WindowsNoEditor.pak"), "base pak");

            // Create an orphaned sig
            var orphanedSigName = "orphaned.sig";
            File.WriteAllText(Path.Join(thisTestPath, orphanedSigName), "orphaned sig");

            var pd = new Core.Services.Mods.PakDir(thisTestPath, Enumerable.Empty<ManagedPak>());
            pd.DeleteOrphanedSigs();

            // Base sig should be preserved (because base pak exists)
            Assert.True(File.Exists(Path.Join(thisTestPath, "pakchunk0-WindowsNoEditor.sig")));
            Assert.False(File.Exists(Path.Join(thisTestPath, orphanedSigName)));
        }

        [Fact]
        public async Task InstallModSet_PakNameCollision_UsesTextualSuccessor() {
            var thisTestPath = Path.Join(BaseTestDir, nameof(InstallModSet_PakNameCollision_UsesTextualSuccessor));
            PrepareTestDirWithSig(thisTestPath);

            // Create an unmanaged pak file with a conflicting name
            // The lexicographic prefix will make the actual name qa__-__TestMod.pak
            File.WriteAllText(Path.Join(thisTestPath, "qa__-__TestMod.pak"), "unmanaged pak contents");

            var pd = new Core.Services.Mods.PakDir(thisTestPath, Enumerable.Empty<ManagedPak>());
            var coords = new ReleaseCoordinates("Org", "TestMod", "1.0.0");

            var results = await pd.InstallModSet(
                new[] { new ModInstallRequest(coords, StaticMkWriter) },
                Option<AccumulatedMemoryProgress>.None
            ).ToListAsync();

            results.ForEach(r => r.IfLeft(e => throw e));

            // The installed pak should have a different name due to collision avoidance
            Assert.Single(pd.ManagedPaks);
            var installedPakName = pd.ManagedPaks.First().PakFileName;

            // Should not be exactly the same as the conflicting file
            Assert.NotEqual("qa__-__TestMod.pak", installedPakName);
            // But should still contain the module name
            Assert.Contains("TestMod", installedPakName);
        }

        [Fact]
        public async Task SignAll_SkipsAlreadySignedFiles() {
            var thisTestPath = Path.Join(BaseTestDir, nameof(SignAll_SkipsAlreadySignedFiles));
            PrepareTestDirWithSig(thisTestPath);

            var pd = new Core.Services.Mods.PakDir(thisTestPath, Enumerable.Empty<ManagedPak>());
            var coords = new ReleaseCoordinates("Unchained-Mods", "Mod1", "1.0.0");

            await InstallDummy(pd, coords);

            var pak = pd.ManagedPaks.First();
            var sigPath = Path.ChangeExtension(Path.Join(thisTestPath, pak.PakFileName), ".sig");

            // Sig file should already exist from install
            Assert.True(File.Exists(sigPath));
            var originalContent = File.ReadAllText(sigPath);
            var originalWriteTime = File.GetLastWriteTimeUtc(sigPath);

            // Small delay to ensure write time would differ if file was modified
            await Task.Delay(100);

            // Sign all again - should skip because sig already exists
            var result = pd.SignAll();
            result.IfLeft(throwErrors);

            // File should not have been modified
            Assert.True(File.Exists(sigPath));
            Assert.Equal(originalContent, File.ReadAllText(sigPath));
        }

        [Fact]
        public void SignAll_FailsGracefully_WhenBaseSigMissing() {
            var thisTestPath = Path.Join(BaseTestDir, nameof(SignAll_FailsGracefully_WhenBaseSigMissing));
            PrepareTestDir(thisTestPath); // Note: no sig file created

            var pakName = "qa__-__TestMod.pak";
            File.WriteAllText(Path.Join(thisTestPath, pakName), "mod contents");

            var managedPaks = new List<ManagedPak> {
                new ManagedPak(new ReleaseCoordinates("Org", "TestMod", "1.0.0"), pakName, 0)
            };

            var pd = new Core.Services.Mods.PakDir(thisTestPath, managedPaks);

            // Delete the sig file for the pak if it exists
            var sigPath = Path.ChangeExtension(Path.Join(thisTestPath, pakName), ".sig");
            if (File.Exists(sigPath)) File.Delete(sigPath);

            // SignAll should return an error when base sig is missing
            var result = pd.SignAll();
            Assert.True(result.IsLeft);
        }

        [Fact]
        public async Task Reset_ClearsAllManagedPaks() {
            var thisTestPath = Path.Join(BaseTestDir, nameof(Reset_ClearsAllManagedPaks));
            PrepareTestDirWithSig(thisTestPath);

            var pd = new Core.Services.Mods.PakDir(thisTestPath, Enumerable.Empty<ManagedPak>());

            // Install multiple mods
            await InstallDummy(pd, new ReleaseCoordinates("Org1", "Mod1", "1.0.0"));
            await InstallDummy(pd, new ReleaseCoordinates("Org2", "Mod2", "1.0.0"));

            Assert.Equal(2, pd.ManagedPaks.Count());

            // Reset
            var result = pd.Reset();
            result.IfLeft(throwErrors);

            // All mod files should be deleted, only base files remain
            var remainingFiles = Directory.EnumerateFiles(thisTestPath).Select(Path.GetFileName).ToList();
            Assert.Contains("pakchunk0-WindowsNoEditor.sig", remainingFiles);
            Assert.DoesNotContain(remainingFiles, f => f != "pakchunk0-WindowsNoEditor.pak" && f != "pakchunk0-WindowsNoEditor.sig");
        }

        [Fact]
        public async Task InstallModSet_UpdatesPriority_WhenModAlreadyInstalled() {
            var thisTestPath = Path.Join(BaseTestDir, nameof(InstallModSet_UpdatesPriority_WhenModAlreadyInstalled));
            PrepareTestDirWithSig(thisTestPath);

            var pd = new Core.Services.Mods.PakDir(thisTestPath, Enumerable.Empty<ManagedPak>());
            var modA = new ReleaseCoordinates("Unchained-Mods", "ModA", "1.0.0");
            var modB = new ReleaseCoordinates("Chained-Mods", "ModB", "1.0.0");
            var modC = new ReleaseCoordinates("Other-Mods", "ModC", "1.0.0");

            // Install [A, B]
            var firstResults = await pd.InstallModSet(
                new[] {
                    new ModInstallRequest(modA, StaticMkWriter),
                    new ModInstallRequest(modB, StaticMkWriter)
                },
                Option<AccumulatedMemoryProgress>.None
            ).ToListAsync();
            firstResults.ForEach(r => r.IfLeft(e => throw e));

            // Verify initial priorities
            var pakA = pd.ManagedPaks.First(p => p.Coordinates.ModuleName == "ModA");
            var pakB = pd.ManagedPaks.First(p => p.Coordinates.ModuleName == "ModB");
            Assert.Equal(0, pakA.Priority);
            Assert.Equal(1, pakB.Priority);

            // Install [C, A, B] - A and B should update priorities
            var secondResults = await pd.InstallModSet(
                new[] {
                    new ModInstallRequest(modC, StaticMkWriter),
                    new ModInstallRequest(modA, StaticMkWriter),
                    new ModInstallRequest(modB, StaticMkWriter)
                },
                Option<AccumulatedMemoryProgress>.None
            ).ToListAsync();
            secondResults.ForEach(r => r.IfLeft(e => throw e));

            // Verify updated priorities
            var updatedPakC = pd.ManagedPaks.First(p => p.Coordinates.ModuleName == "ModC");
            var updatedPakA = pd.ManagedPaks.First(p => p.Coordinates.ModuleName == "ModA");
            var updatedPakB = pd.ManagedPaks.First(p => p.Coordinates.ModuleName == "ModB");
            Assert.Equal(0, updatedPakC.Priority);
            Assert.Equal(1, updatedPakA.Priority);
            Assert.Equal(2, updatedPakB.Priority);
        }

        [Theory]
        [MemberData(nameof(DummyModsParam))]
        public async Task InstallModSet_SingleMod_CreatesCorrectFileName(ReleaseCoordinates coords) {
            var thisTestPath = Path.Join(BaseTestDir, nameof(InstallModSet_SingleMod_CreatesCorrectFileName) + "_" + coords.ModuleName + "_" + coords.Org);
            PrepareTestDirWithSig(thisTestPath);

            var pd = new Core.Services.Mods.PakDir(thisTestPath, Enumerable.Empty<ManagedPak>());

            var results = await pd.InstallModSet(
                new[] { new ModInstallRequest(coords, StaticMkWriter) },
                Option<AccumulatedMemoryProgress>.None
            ).ToListAsync();

            results.ForEach(r => r.IfLeft(e => throw e));

            Assert.Single(pd.ManagedPaks);
            var installedPak = pd.ManagedPaks.First();

            // Pak file should exist on disk
            var pakPath = Path.Join(thisTestPath, installedPak.PakFileName);
            Assert.True(File.Exists(pakPath));

            // File name should contain module name and have .pak extension
            Assert.Contains(coords.ModuleName, installedPak.PakFileName);
            Assert.EndsWith(".pak", installedPak.PakFileName);
        }

        [Fact]
        public async Task InstallModSet_MoveAlsoMovesSigFile() {
            var thisTestPath = Path.Join(BaseTestDir, nameof(InstallModSet_MoveAlsoMovesSigFile));
            PrepareTestDirWithSig(thisTestPath);

            var pd = new Core.Services.Mods.PakDir(thisTestPath, Enumerable.Empty<ManagedPak>());
            var modA = new ReleaseCoordinates("Unchained-Mods", "ModA", "1.0.0");
            var modB = new ReleaseCoordinates("Chained-Mods", "ModB", "1.0.0");

            // Install [A, B]
            await pd.InstallModSet(
                new[] {
                    new ModInstallRequest(modA, StaticMkWriter),
                    new ModInstallRequest(modB, StaticMkWriter)
                },
                Option<AccumulatedMemoryProgress>.None
            ).ToListAsync();

            var originalPakA = pd.ManagedPaks.First(p => p.Coordinates.ModuleName == "ModA");
            var originalSigPathA = Path.ChangeExtension(Path.Join(thisTestPath, originalPakA.PakFileName), ".sig");
            Assert.True(File.Exists(originalSigPathA));

            // Install [B, A] - reversed order, triggers move
            await pd.InstallModSet(
                new[] {
                    new ModInstallRequest(modB, StaticMkWriter),
                    new ModInstallRequest(modA, StaticMkWriter)
                },
                Option<AccumulatedMemoryProgress>.None
            ).ToListAsync();

            var movedPakA = pd.ManagedPaks.First(p => p.Coordinates.ModuleName == "ModA");
            var newSigPathA = Path.ChangeExtension(Path.Join(thisTestPath, movedPakA.PakFileName), ".sig");

            // Old sig should not exist, new sig should exist
            Assert.False(File.Exists(originalSigPathA));
            Assert.True(File.Exists(newSigPathA));
        }

    }
}