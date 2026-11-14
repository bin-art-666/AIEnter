using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace AIEnter;

internal sealed partial class SettingsWindow
{
    readonly StackPanel providerOrderRows = new() { Spacing = 6 };
    readonly List<string> providerOrder = [];
    void LoadProviderOrder(AiOptions value)
    {
        providerOrder.Clear(); providerOrder.AddRange(AiPolicy.OrderedProviders(value));
        RenderProviderOrder();
    }
    void MoveProvider(string id, int direction)
    {
        int from = providerOrder.IndexOf(id), to = from + direction;
        if (from < 0 || to < 0 || to >= providerOrder.Count) return;
        (providerOrder[from], providerOrder[to]) = (providerOrder[to], providerOrder[from]);
        RenderProviderOrder();
        var row = (Grid)providerOrderRows.Children[to];
        ((Button)row.Children[direction < 0 ? 1 : 2]).Focus(FocusState.Programmatic);
        status.Text = "AI 按钮顺序已调整，点击保存设置后生效。";
    }
    void RenderProviderOrder()
    {
        providerOrderRows.Children.Clear();
        for (int index = 0; index < providerOrder.Count; index++)
        {
            string id = providerOrder[index];
            string name = id == "custom" ? (string.IsNullOrWhiteSpace(customName.Text) ? "自定义 AI" : customName.Text) : AiPolicy.Names[Array.IndexOf(AiPolicy.Providers, id)];
            var row = new Grid { ColumnSpacing = 6 };
            row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            row.Children.Add(new TextBlock { Text = $"{index + 1}. {name}", VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            var up = new Button { Content = "上移", IsEnabled = index > 0 };
            var down = new Button { Content = "下移", IsEnabled = index < providerOrder.Count - 1 };
            AutomationProperties.SetName(up, $"上移 {name}"); AutomationProperties.SetName(down, $"下移 {name}");
            up.Click += (_, _) => MoveProvider(id, -1); down.Click += (_, _) => MoveProvider(id, 1);
            Grid.SetColumn(up, 1); Grid.SetColumn(down, 2);
            row.Children.Add(up); row.Children.Add(down); providerOrderRows.Children.Add(row);
        }
    }
}
