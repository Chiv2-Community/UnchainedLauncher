using Xunit;

namespace UnchainedLauncher.Core.Tests.Scanner;

// The portable harness does not compile the WPF file that defines this collection.
[CollectionDefinition("Home launch UI", DisableParallelization = true)]
public sealed class HashLogCollection;
