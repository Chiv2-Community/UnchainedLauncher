using System.Globalization;
using System.Net;
using System.Windows.Controls;

namespace UnchainedLauncher.GUI.Views.Servers {
    /// <summary>
    ///     Interaction logic for ServersTab.xaml
    /// </summary>
    public partial class ServersTab : UserControl {
        public ServersTab() {
            InitializeComponent();
        }
    }

    public class PortRangeValidationRule : ValidationRule {
        public override ValidationResult Validate(object value, CultureInfo cultureInfo) {
            int numVal;
            if (int.TryParse((string)value, out numVal)) {
                return numVal > 0 && numVal <= 65535
                    ? ValidationResult.ValidResult
                    : new ValidationResult(false, "Ports must be in range (0, 65535]");
            }

            return new ValidationResult(false, "Ports must be an integer in range (0, 65535]");
        }
    }

    public class IPAddressValidationRule : ValidationRule {
        public override ValidationResult Validate(object value, CultureInfo cultureInfo) {
            try {
                IPAddress.Parse((string)value);
                return ValidationResult.ValidResult;
            }
            catch {
                return new ValidationResult(false, "IP address is not valid");
            }
        }
    }
}