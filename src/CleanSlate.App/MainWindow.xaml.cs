using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CleanSlate.App.ViewModels;
using CleanSlate.Uninstall;

namespace CleanSlate.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();
    private ForceRemovalPlan? _lastForcePlan;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        SoftwareList.ItemsSource = _viewModel.Software;
        ReviewList.ItemsSource = _viewModel.ReviewItems;
        L.SetCulture("zh-CN");
        if (LangBox is not null && LangBox.Items.Count >= 1) LangBox.SelectedIndex = 0;
        // Check nav AFTER InitializeComponent to avoid NRE in Checked handlers
        if (NavHome is not null) NavHome.IsChecked = true;
        ShowPage("Home");
        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.StatusText)) StatusText.Text = _viewModel.StatusText;
            if (e.PropertyName == nameof(MainViewModel.FilteredSoftware)) SoftwareList.ItemsSource = _viewModel.FilteredSoftware;
        };
    }

    private void ShowPage(string page)
    {
        // Null-safe: Checked events fire during InitializeComponent before named fields exist
        if (PageHome is not null) PageHome.Visibility = page == "Home" ? Visibility.Visible : Visibility.Collapsed;
        if (PageDeep is not null) PageDeep.Visibility = page == "Deep" ? Visibility.Visible : Visibility.Collapsed;
        if (PageTools is not null) PageTools.Visibility = page == "Tools" ? Visibility.Visible : Visibility.Collapsed;
        if (PageSettings is not null) PageSettings.Visibility = page == "Settings" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void NavHome_Checked(object sender, RoutedEventArgs e) => ShowPage("Home");
    private void NavDeep_Checked(object sender, RoutedEventArgs e) => ShowPage("Deep");
    private void NavTools_Checked(object sender, RoutedEventArgs e) => ShowPage("Tools");
    private void NavSettings_Checked(object sender, RoutedEventArgs e) => ShowPage("Settings");

    private void DarkToggle_Changed(object sender, RoutedEventArgs e)
    {
        var dark = DarkToggle?.IsChecked == true;
        var uri = dark ? new Uri("Themes/GlassNeu.Dark.xaml", UriKind.Relative) : new Uri("Themes/GlassNeu.xaml", UriKind.Relative);
        var dict = new ResourceDictionary { Source = uri };
        if (Resources.MergedDictionaries.Count == 0) Resources.MergedDictionaries.Add(dict);
        else Resources.MergedDictionaries[0] = dict;
        Background = (Brush)FindResource("BgCanvasBrush");
    }

    private void LangBox_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (LangBox?.SelectedItem is ComboBoxItem { Tag: string tag })
        {
            L.SetCulture(tag);
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _viewModel.SearchText = SearchBox.Text;
        SoftwareList.ItemsSource = _viewModel.FilteredSoftware;
    }

    private void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.LoadSoftwareData();
        SoftwareList.ItemsSource = _viewModel.FilteredSoftware;
    }

    private void BtnScan_Click(object sender, RoutedEventArgs e)
    {
        var selected = _viewModel.FilteredSoftware.FirstOrDefault(s => s.IsSelected)
                       ?? _viewModel.FilteredSoftware.FirstOrDefault();
        if (selected is null) { _viewModel.StatusText = "请先选择软件"; return; }
        _viewModel.ScanLeftoversFor(selected);
        if (NavDeep is not null) NavDeep.IsChecked = true;
        ShowPage("Deep");
        ResetPipeline();
        if (Step2 is not null) Step2.Foreground = (Brush)FindResource("AccentBrush");
        UpdateConfirmBar();
    }

    private void BtnOrphans_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.ScanOrphans();
    }

    private void BtnDeepAgent_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.LoadAgentRulesIfEmpty();
        _viewModel.NavigateAgentItemsToReview();
    }

    private void BtnUninstall_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is not SoftwareItemViewModel item) return;
        if (item.IsSystem)
        {
            MessageBox.Show("系统组件受保护，无法卸载。", "简卸", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var related = _viewModel.ListRelatedProcesses(item);
        var processMsg = related.Count == 0
            ? "未检测到相关运行中进程。"
            : "相关进程：\n" + string.Join("\n", related.Select(p => $"  [{p.Id}] {p.Name}")) + "\n\n卸载前关闭它们？\n是=关闭，否=保持运行，取消=中止。";
        var confirm = MessageBox.Show("卸载「" + item.DisplayName + "」？\n卸载后可自动扫描残留。\n\n" + processMsg, "卸载",
            related.Count == 0 ? MessageBoxButton.YesNo : MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (confirm == MessageBoxResult.Cancel || confirm != MessageBoxResult.Yes) return;
        var message = _viewModel.RunUninstallWithProcessConfirm(item, related.Count > 0 ? related : Array.Empty<RelatedProcess>());
        MessageBox.Show(message, "结果", MessageBoxButton.OK, MessageBoxImage.Information);
        if (item.Entry is not null)
        {
            _viewModel.ScanLeftoversFor(item);
            if (NavDeep is not null) NavDeep.IsChecked = true;
            ShowPage("Deep");
            ResetPipeline();
            UpdateConfirmBar();
        }
    }

    private void BtnDetail_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is SoftwareItemViewModel item)
        {
            MessageBox.Show(item.DisplayName + "\n" + item.Publisher + "\n" + item.InstallLocation, "详情",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void BtnClean_Click(object sender, RoutedEventArgs e)
    {
        var selected = _viewModel.ReviewItems.Count(r => r.IsSelected);
        if (selected == 0) { _viewModel.StatusText = "未选中清理项"; return; }
        var high = _viewModel.ReviewItems.Count(r => r.IsSelected && (r.RiskLevel == "High" || r.RiskLevel == "Protected"));
        var msg = "将清理 " + selected + " 项到隔离区（可恢复）。";
        if (high > 0) msg += "\n含 " + high + " 项高风险，请再次确认。";
        if (MessageBox.Show(msg, "确认清理", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            _viewModel.StatusText = "已取消";
            return;
        }
        if (Step2 is not null) Step2.Foreground = (Brush)FindResource("AccentBrush");
        var status = _viewModel.RunCleanup();
        ShowComplete("清理完成", status + "\n14 天内可在「设置 · 安全与恢复」一键恢复。");
    }

    private void BtnBatch_Click(object sender, RoutedEventArgs e)
    {
        _ = BatchAsync();
    }

    private async System.Threading.Tasks.Task BatchAsync()
    {
        var selected = _viewModel.Software.Count(s => s.IsSelected && !s.IsSystem);
        if (selected == 0) { _viewModel.StatusText = "请先勾选软件"; return; }
        if (MessageBox.Show("批量卸载 " + selected + " 个软件？", "批量卸载", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }
        BtnBatch.IsEnabled = false;
        try
        {
            var status = await _viewModel.RunBatchUninstallAsync();
            MessageBox.Show(status, "批量卸载", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        finally
        {
            BtnBatch.IsEnabled = true;
        }
    }

    private void BtnExport_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(_viewModel.ExportLastReport(), "报告", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void BtnUndoLog_Click(object sender, RoutedEventArgs e)
    {
        SafetyDetail.Text = _viewModel.LoadUndoJobs();
    }

    private void BtnRestore_Click(object sender, RoutedEventArgs e)
    {
        var status = _viewModel.RestoreLatestQuarantine();
        SafetyDetail.Text = status;
        MessageBox.Show(status, "恢复", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void BtnForcePlan_Click(object sender, RoutedEventArgs e)
    {
        var name = _viewModel.Software.FirstOrDefault(s => s.IsSelected)?.DisplayName
                   ?? _viewModel.Software.FirstOrDefault()?.DisplayName ?? "App";
        _lastForcePlan = _viewModel.PlanForceRemovalObject(name);
        MessageBox.Show(_viewModel.PlanForceRemoval(name), "强制卸载", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void BtnForceExecute_Click(object sender, RoutedEventArgs e)
    {
        if (_lastForcePlan is null || _lastForcePlan.Targets.Count == 0)
        {
            _viewModel.StatusText = "请先生成痕迹列表";
            return;
        }
        if (MessageBox.Show("强制清理 " + _lastForcePlan.Targets.Count + " 个痕迹？", "强制卸载", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }
        MessageBox.Show(_viewModel.ExecuteForceRemoval(_lastForcePlan), "结果", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void BtnUpdateRules_Click(object sender, RoutedEventArgs e)
    {
        SafetyDetail.Text = _viewModel.UpdateAgentRulesInteractive();
    }

    private async void BtnCdnUpdate_Click(object sender, RoutedEventArgs e)
    {
        BtnCdnUpdate.IsEnabled = false;
        try { SafetyDetail.Text = await _viewModel.UpdateRulesFromCdnAsync(); }
        finally { BtnCdnUpdate.IsEnabled = true; }
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0) return;
        if (NavTools is not null) NavTools.IsChecked = true;
        ShowPage("Tools");
        _lastForcePlan = _viewModel.PlanForceRemovalObject(paths[0]);
        MessageBox.Show(_viewModel.PlanForceRemoval(paths[0]), "拖入目标", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void BtnSnapBefore_Click(object sender, RoutedEventArgs e) =>
        MonitorDetail.Text = _viewModel.CaptureSnapshot("before");

    private void BtnSnapAfter_Click(object sender, RoutedEventArgs e) =>
        MonitorDetail.Text = _viewModel.CaptureSnapshot("after");

    private void BtnSnapDiff_Click(object sender, RoutedEventArgs e) =>
        MonitorDetail.Text = _viewModel.DiffSnapshots();

    private void BtnReloadExt_Click(object sender, RoutedEventArgs e)
    {
        ExtensionList.ItemsSource = _viewModel.LoadBrowserExtensions();
        _viewModel.StatusText = "扩展扫描完成";
    }

    private void BtnRelocate_Click(object sender, RoutedEventArgs e)
    {
        var src = RelocSrc.Text?.Trim();
        var dst = RelocDst.Text?.Trim();
        if (string.IsNullOrWhiteSpace(src) || string.IsNullOrWhiteSpace(dst))
        {
            RelocDetail.Text = "请填写源目录与目标目录";
            return;
        }
        RelocDetail.Text = _viewModel.Relocate(src, dst);
    }

    private void BtnRelocRollback_Click(object sender, RoutedEventArgs e)
    {
        var journal = RelocDst.Text?.Trim();
        if (string.IsNullOrWhiteSpace(journal)) { RelocDetail.Text = "将 journal 路径填入目标目录框"; return; }
        RelocDetail.Text = _viewModel.RollbackRelocation(journal);
    }

    private void ReviewItem_Checked(object sender, RoutedEventArgs e) => UpdateConfirmBar();

    private void UpdateConfirmBar()
    {
        if (ConfirmSummary is null) return;
        var n = _viewModel.ReviewItems.Count(r => r.IsSelected);
        var high = _viewModel.ReviewItems.Count(r => r.IsSelected && r.RiskLevel is "High" or "Protected");
        if (n == 0)
        {
            ConfirmSummary.Text = "未选中清理项。可勾选「低风险」项，或先扫描。";
        }
        else if (high > 0)
        {
            ConfirmSummary.Text = $"将清理 {n} 项（含 {high} 项高风险）· 全部进入隔离区";
        }
        else
        {
            ConfirmSummary.Text = $"将清理 {n} 项 · 全部进入隔离区，可随时恢复";
        }
    }

    private void BtnBackHome_Click(object sender, RoutedEventArgs e)
    {
        if (NavHome is not null) NavHome.IsChecked = true;
        ShowPage("Home");
    }

    private void ShowComplete(string title, string detail)
    {
        CompletePanel.Visibility = Visibility.Visible;
        DeepScroll.Visibility = Visibility.Collapsed;
        ConfirmBar.Visibility = Visibility.Collapsed;
        CompleteTitle.Text = title;
        CompleteDetail.Text = detail;
        if (Step1 is not null) Step1.Foreground = (Brush)FindResource("InkSecondaryBrush");
        if (Step2 is not null) Step2.Foreground = (Brush)FindResource("InkSecondaryBrush");
        if (Step3 is not null) Step3.Foreground = (Brush)FindResource("AccentBrush");
    }

    private void ResetPipeline()
    {
        CompletePanel.Visibility = Visibility.Collapsed;
        DeepScroll.Visibility = Visibility.Visible;
        ConfirmBar.Visibility = Visibility.Visible;
        if (Step1 is not null) Step1.Foreground = (Brush)FindResource("AccentBrush");
        if (Step2 is not null) Step2.Foreground = (Brush)FindResource("InkSecondaryBrush");
        if (Step3 is not null) Step3.Foreground = (Brush)FindResource("InkSecondaryBrush");
        UpdateConfirmBar();
    }

    private void BtnHealth_Click(object sender, RoutedEventArgs e)
    {
        BtnHealth.IsEnabled = false;
        try
        {
            _viewModel.RunHealthCheck();
            HealthSoft.Text = _viewModel.HealthSoftwareText;
            HealthAgent.Text = _viewModel.HealthAgentText;
            HealthSafe.Text = _viewModel.HealthSafeText;
            HealthSummaryText.Text = _viewModel.HealthSummary;
        }
        finally
        {
            BtnHealth.IsEnabled = true;
        }
    }

    private void BtnHealthClean_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.ReviewItems.Count == 0)
        {
            _viewModel.RunHealthCheck();
            HealthSoft.Text = _viewModel.HealthSoftwareText;
            HealthAgent.Text = _viewModel.HealthAgentText;
            HealthSafe.Text = _viewModel.HealthSafeText;
            HealthSummaryText.Text = _viewModel.HealthSummary;
        }

        // Jump to review with recommended pre-selected
        foreach (var item in _viewModel.ReviewItems)
        {
            item.IsSelected = item.DefaultSelected;
        }

        if (NavDeep is not null) NavDeep.IsChecked = true;
        ShowPage("Deep");
        ResetPipeline();
        UpdateConfirmBar();
        _viewModel.StatusText = "已根据体检结果载入推荐清理项";
    }

    private void BtnSelectRecommended_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _viewModel.ReviewItems)
        {
            item.IsSelected = item.DefaultSelected || item.RiskLevel == "Low";
        }
        UpdateConfirmBar();
        _viewModel.StatusText = "已选中推荐项";
    }

    private void BtnSelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _viewModel.ReviewItems)
        {
            if (item.RiskLevel == "Protected")
            {
                item.IsSelected = false;
                continue;
            }
            item.IsSelected = true;
        }
        UpdateConfirmBar();
        _viewModel.StatusText = "已全选（系统保护项除外）";
    }
}
