using System.Windows.Media;

namespace StockTool.ViewModels;

/// <summary>任务栏名称/价格灰色色阶选项。</summary>
public sealed class TaskbarGrayOption
{
    public TaskbarGrayOption(int level, string label, Color color)
    {
        Level = level;
        Label = label;
        Color = color;
        Brush = new SolidColorBrush(color);
        Brush.Freeze();
    }

    public int Level { get; }
    public string Label { get; }
    public Color Color { get; }
    public Brush Brush { get; }
}
