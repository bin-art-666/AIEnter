using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AIEnter;

internal sealed partial class SettingsWindow : PanelWindow
{
    readonly AiOptionsStore options;
    readonly ComboBox provider = new() { Header = "默认 AI 服务", HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly TextBox customName = new() { Header = "自定义服务名称", MaxLength = 40 };
    readonly TextBox customUrl = new() { Header = "自定义网址", PlaceholderText = "https://…", MaxLength = 2048 };
    readonly StackPanel custom = new() { Spacing = 12 };
    readonly ToggleSwitch floatingMode = new() { Header = "窗口模式", OnContent = "悬浮面板", OffContent = "独立窗口" };
    readonly ToggleSwitch pinned = new() { Header = "固定 / 置顶", OnContent = "保持展开并置顶", OffContent = "悬浮自动收起 / 独立正常层级" };
    readonly ToggleSwitch followCorners = new() { Header = "网页圆角跟随界面", OnContent = "跟随界面圆角", OffContent = "使用自定义圆角" };
    readonly Slider radius = Slider("网页圆角", 0, 40);
    readonly Slider backplateRadius = Slider("背板圆角", 12, 40);
    readonly Slider buttonRadius = Slider("按钮圆角", 0, 20);
    readonly Slider buttonSize = Slider("按钮高度", 28, 44);
    readonly Slider padding = Slider("窗口内边距", 0, 16);
    readonly Slider taskbarSize = Slider("任务栏图标大小", 16, 36);
    readonly ToggleSwitch glow = new() { Header = "对话打开时显示光晕", OnContent = "开启", OffContent = "关闭" };
    readonly Slider glowRange = Slider("光晕范围", 0, 12);
    readonly Slider glowIntensity = Slider("光晕强度（%）", 0, 100);
    readonly TextBlock status = PanelTheme.Hint("外观调整实时预览，保存后记住；关闭撤销未保存的预览。");
    bool syncing = true, resetPanelSize;
    protected override bool DismissOnOutside => false;
    protected override bool Frameless => false;
    static Slider Slider(string header, int min, int max) => new()
    { Header = header, Minimum = min, Maximum = max, StepFrequency = 1, TickFrequency = 1 };
    static TextBlock Section(string title) => new() { Text = title, FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 0) };

    internal SettingsWindow(AiOptionsStore options, Action quit)
    {
        this.options = options;
        Title = "AIEnter · 设置";
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AIEnter.ico"));
        var presenter = (OverlappedPresenter)AppWindow.Presenter;
        presenter.SetBorderAndTitleBar(true, true); presenter.IsMaximizable = false; presenter.IsMinimizable = false;
        SystemBackdrop = PanelTheme.Backdrop();
        var layout = new Grid { Padding = new Thickness(18), RowSpacing = 12 };
        layout.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var fields = new StackPanel { Spacing = 16 };
        fields.Children.Add(Section("任务栏图标"));
        fields.Children.Add(taskbarSize); fields.Children.Add(glow); fields.Children.Add(glowRange); fields.Children.Add(glowIntensity);
        fields.Children.Add(PanelTheme.Hint("图标和光晕以任务栏可用高度为上限，预留空间随大小调整。只有对话展开时显示光晕，设置窗口不会点亮。"));
        fields.Children.Add(Section("AI 与窗口"));
        for (int index = 0; index < AiPolicy.Providers.Length; index++)
            provider.Items.Add(new ComboBoxItem { Content = AiPolicy.Names[index], Tag = AiPolicy.Providers[index] });
        provider.SelectionChanged += (_, _) => RefreshFields(); fields.Children.Add(provider);
        custom.Children.Add(customName); custom.Children.Add(customUrl); fields.Children.Add(custom);
        fields.Children.Add(Section("顶栏 AI 按钮排序"));
        fields.Children.Add(PanelTheme.Hint("按从左到右排列；点击上移、下移调整，保存后生效。未配置的自定义 AI 暂不显示，配置后使用这里的位置。"));
        fields.Children.Add(providerOrderRows);
        fields.Children.Add(floatingMode); fields.Children.Add(pinned);
        fields.Children.Add(Section("窗口外观"));
        fields.Children.Add(backplateRadius); fields.Children.Add(buttonRadius); fields.Children.Add(buttonSize); fields.Children.Add(padding);
        InitializeButtonColorSettings(fields);
        fields.Children.Add(followCorners); fields.Children.Add(radius);
        followCorners.Toggled += (_, _) => { RefreshFields(); Preview(); };
        radius.ValueChanged += (_, _) => Preview();

        foreach (var slider in new[] { backplateRadius, buttonRadius, buttonSize, padding, taskbarSize, glowRange, glowIntensity })
            slider.ValueChanged += (_, _) => Preview();
        taskbarSize.ValueChanged += (_, _) => UpdateTaskbarSizeLabel();
        glow.Toggled += (_, _) => { RefreshFields(); Preview(); };
        var defaults = new Button { Content = "恢复外观默认值" };
        defaults.Click += (_, _) =>
        {
            syncing = true; LoadAppearance(new());
            followCorners.IsOn = UiDefaults.FollowGlobalCorners; radius.Value = UiDefaults.WebCornerRadius;
            resetPanelSize = true; syncing = false; RefreshFields(); Preview();
            status.Text = "已预览默认外观；保存后恢复面板默认尺寸。";
        };
        fields.Children.Add(defaults);
        var scroll = new ScrollViewer { Content = fields, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        layout.Children.Add(scroll);
        var bottom = new StackPanel { Spacing = 8 };
        bottom.Children.Add(status);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var save = new Button { Content = "保存设置" }; save.Click += (_, _) => Save();
        var close = new Button { Content = "关闭" }; close.Click += (_, _) => Hide();
        var exit = new Button { Content = "退出 AIEnter" }; exit.Click += (_, _) => quit();
        actions.Children.Add(save); actions.Children.Add(close); actions.Children.Add(exit); bottom.Children.Add(actions);
        Grid.SetRow(bottom, 1); layout.Children.Add(bottom);
        InitializePanel(layout);
        var display = Native.Monitors().First(m => m.Primary); var work = display.Info.Work;
        int width = Math.Min(work.Right - work.Left, (int)(500 * display.Dpi / 96d));
        int height = Math.Min(work.Bottom - work.Top, (int)(760 * display.Dpi / 96d));
        AppWindow.MoveAndResize(new(work.Left + (work.Right - work.Left - width) / 2,
            work.Top + (work.Bottom - work.Top - height) / 2, width, height));
    }
    internal void Open()
    {
        if (!IsOpen)
        {
            syncing = true; resetPanelSize = false;
            var value = options.Data;
            LoadProviderOrder(value);
            provider.SelectedIndex = Array.IndexOf(AiPolicy.Providers, value.Provider);
            customName.Text = value.CustomName; customUrl.Text = value.CustomUrl;
            floatingMode.IsOn = value.FloatingPanel; pinned.IsOn = value.Pinned;
            followCorners.IsOn = value.FollowGlobalCorners; radius.Value = value.WebCornerRadius;
            LoadAppearance(value.Appearance ?? new());
            status.Text = options.Warning ?? "外观调整实时预览，保存后记住；关闭撤销未保存的预览。";
            syncing = false; RefreshFields();
        }
        ShowPanel();
    }
    void LoadAppearance(UiAppearance value)
    {
        LoadButtonColors(value);
        backplateRadius.Value = value.BackplateRadius;
        buttonRadius.Value = value.ButtonRadius; buttonSize.Value = value.ButtonSize;
        padding.Value = value.WindowPadding; taskbarSize.Value = value.TaskbarIconSize; UpdateTaskbarSizeLabel();
        glow.IsOn = value.GlowEnabled; glowRange.Value = value.GlowRange; glowIntensity.Value = value.GlowIntensity;
    }
    void UpdateTaskbarSizeLabel() => taskbarSize.Header = $"任务栏图标大小：{(int)taskbarSize.Value} DIP（16–36）";
    UiAppearance ReadAppearance() => new((int)buttonRadius.Value, (int)buttonSize.Value, (int)padding.Value,
        (int)taskbarSize.Value, glow.IsOn, (int)glowRange.Value, (int)glowIntensity.Value, (int)backplateRadius.Value, normalButtonColor, selectedButtonColor);
    void Preview()
    {
        if (syncing) return;
        App.PreviewAppearance(ReadAppearance());
        App.Ai.PreviewCorners(followCorners.IsOn, (int)Math.Round(radius.Value));
        status.Text = "正在预览；保存后记住，关闭撤销未保存的外观。";
    }
    protected override void OnHiding()
    {
        App.PreviewAppearance(null);
        App.Ai.PreviewCorners(options.Data.FollowGlobalCorners, options.Data.WebCornerRadius);
    }
    void RefreshFields()
    {
        custom.Visibility = provider.SelectedIndex == 4 ? Visibility.Visible : Visibility.Collapsed;
        radius.IsEnabled = !followCorners.IsOn; glowRange.IsEnabled = glowIntensity.IsEnabled = glow.IsOn;
    }
    void Save()
    {
        try
        {
            if (provider.SelectedItem is not ComboBoxItem item) throw new InvalidDataException("请选择 AI 服务。");
            options.Save(options.Data with { Provider = (string)item.Tag, CustomName = customName.Text.Trim(),
                CustomUrl = customUrl.Text.Trim(), ProviderOrder = providerOrder.ToArray(), Pinned = pinned.IsOn, FloatingPanel = floatingMode.IsOn,
                FollowGlobalCorners = followCorners.IsOn, WebCornerRadius = (int)Math.Round(radius.Value), Appearance = ReadAppearance(),
                DockWidth = resetPanelSize ? UiDefaults.DockWidth : options.Data.DockWidth,
                DockHeight = resetPanelSize ? UiDefaults.DockHeight : options.Data.DockHeight,
                DockTopOffset = resetPanelSize ? 0 : options.Data.DockTopOffset });
            resetPanelSize = false; App.PreviewAppearance(null); status.Text = "设置已保存。";
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        { status.Text = "保存失败：" + error.Message; }
    }
}
