using System.Globalization;
using System.Windows.Controls;

namespace UnchainedLauncher.GUI.Views {
    /// <summary>
    /// Interaction logic for ServerInfoForm.xaml
    /// </summary>
    public partial class ServerInfoForm : UserControl {
        public ServerInfoForm() {
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
}
