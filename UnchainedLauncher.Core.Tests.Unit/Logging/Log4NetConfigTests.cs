using System;
using FluentAssertions;
using log4net;
using log4net.Config;
using Xunit;

namespace UnchainedLauncher.Core.Tests.Unit.Logging {
    public class Log4NetConfigTests {
        [Fact]
        public void EmbeddedLog4NetConfig_CanCreateRollingFileAppender() {
            var repository = LogManager.CreateRepository(Guid.NewGuid().ToString());

            var assembly = typeof(UnchainedLauncher.GUI.App).Assembly;
            using var configStream = assembly.GetManifestResourceStream("UnchainedLauncher.GUI.Resources.log4net.config");
            configStream.Should().NotBeNull("the GUI embeds Resources/log4net.config");

            XmlConfigurator.Configure(repository, configStream!);

            var appenders = repository.GetAppenders();
            appenders.Should().ContainSingle(a => a.Name == "RollingFile");

            var rollingFileAppender = appenders.Should().ContainSingle(a => a.Name == "RollingFile").Subject;
            rollingFileAppender.Should().BeOfType<UnchainedLauncher.GUI.Logging.CWDRollingFileAppender>();
        }
    }
}
