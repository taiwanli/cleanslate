using System.Windows;
using CleanSlate.Safety;

namespace CleanSlate.App;

public partial class DisclaimerWindow : Window
{
    public DisclaimerWindow()
    {
        InitializeComponent();
        DisclaimerBody.Text = Disclaimer.ChineseText;
        ChkAgree.Checked += (_, _) => BtnAgree.IsEnabled = true;
        ChkAgree.Unchecked += (_, _) => BtnAgree.IsEnabled = false;
    }

    private void BtnAgree_Click(object sender, RoutedEventArgs e)
    {
        if (ChkAgree.IsChecked != true)
        {
            return;
        }

        Disclaimer.MarkAccepted(Disclaimer.DefaultSettingsPath());
        DialogResult = true;
        Close();
    }

    private void BtnDecline_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
