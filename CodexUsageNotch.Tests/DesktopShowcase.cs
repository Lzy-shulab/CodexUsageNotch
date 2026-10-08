using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

// The surrounding client is drawn entirely from fictional, fixed content.
// Only the usage capsule comes from the application's real WPF visual tree.
internal static class DesktopShowcase
{
    private const int Width = 1624;
    private const int Height = 1090;
    private static readonly FontFamily Font = new("Segoe UI Variable Text, Microsoft YaHei UI");

    internal static void Export(Window notch, bool dark, string path)
    {
        var palette = dark
            ? new Palette("#18191B", "#1D1E20", "#202123", "#2C2D30", "#333438", "#ECEDEF", "#B5B7BC", "#999DA4")
            : new Palette("#F6F6F6", "#FCFCFC", "#FFFFFF", "#F0F0F0", "#E5E5E5", "#25292E", "#4B5057", "#82878E");
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            Fill(dc, palette.Frame, 0, 0, Width, Height);
            DrawTitleBar(dc, palette);
            DrawSidebar(dc, palette);
            DrawWorkspace(dc, palette);

            // Use the same 1280-DIP layout as the existing gallery, shown at
            // 125% to match the reference desktop's display density.
            const double scale = 1.25;
            var capsuleWidth = notch.ActualWidth * scale;
            var capsuleHeight = notch.ActualHeight * scale;
            var content = (Visual)notch.Content;
            var brush = new VisualBrush(content)
            {
                ViewboxUnits = BrushMappingMode.Absolute,
                Viewbox = new Rect(0, 0, notch.ActualWidth, notch.ActualHeight),
                Stretch = Stretch.Fill
            };
            dc.DrawRectangle(brush, null, new Rect((Width - capsuleWidth) / 2, 1, capsuleWidth, capsuleHeight));
        }

        var bitmap = new RenderTargetBitmap(Width, Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static void DrawTitleBar(DrawingContext dc, Palette p)
    {
        Icon(dc, "back", 14, 12, 20, p.Muted);
        Icon(dc, "forward", 58, 12, 20, p.Muted);
        Icon(dc, "panel", 99, 12, 20, p.Muted);
        Text(dc, "文件", 152, 8, 20, p.Muted);
        Text(dc, "编辑", 222, 8, 20, p.Muted);
        Text(dc, "视图", 292, 8, 20, p.Muted);
        Text(dc, "帮助", 362, 8, 20, p.Muted);
        Line(dc, 1475, 21, 1489, 21, p.Secondary, 1);
        dc.DrawRectangle(null, Pen(p.Secondary, 1), new Rect(1532, 15, 12, 12));
        Line(dc, 1589, 15, 1602, 28, p.Secondary, 1);
        Line(dc, 1602, 15, 1589, 28, p.Secondary, 1);
    }

    private static void DrawSidebar(DrawingContext dc, Palette p)
    {
        Fill(dc, p.Sidebar, 63, 54, 342, 1036, 18);
        Fill(dc, p.Sidebar, 382, 74, 23, 1016);
        Line(dc, 404.5, 55, 404.5, 1090, p.Border, 1);
        Fill(dc, p.Selection, 9, 65, 44, 44, 12);
        Icon(dc, "home", 20, 77, 23, p.Foreground);
        Icon(dc, "cards", 20, 131, 23, p.Muted);
        Icon(dc, "clock", 20, 185, 23, p.Muted);
        Icon(dc, "orbit", 20, 241, 23, p.Muted);
        Icon(dc, "more", 20, 295, 23, p.Muted);
        Line(dc, 16, 340, 47, 340, p.Border, 1);
        Icon(dc, "branch", 20, 361, 23, p.Muted);
        Icon(dc, "cube", 20, 416, 23, p.Muted);
        Icon(dc, "folder", 20, 472, 23, p.Muted);
        dc.DrawEllipse(Brush(p.Selection), null, new Point(31, 1061), 15, 15);
        CenterText(dc, "D", new Rect(16, 1046, 30, 30), 13, p.Secondary, true);

        Text(dc, "Codex", 84, 68, 26, p.Secondary, true);
        Icon(dc, "chevron", 171, 79, 15, p.Muted);
        Icon(dc, "bell", 323, 77, 18, p.Muted);
        Icon(dc, "search", 363, 77, 18, p.Muted);
        Icon(dc, "edit", 84, 128, 19, p.Secondary);
        Text(dc, "新聊天", 113, 122, 20, p.Secondary);
        Icon(dc, "bot", 84, 166, 20, p.Secondary);
        Text(dc, "你的 dot", 113, 160, 20, p.Secondary);
        Text(dc, "项目", 84, 213, 20, p.Muted);

        Fill(dc, p.Selection, 74, 255, 307, 39, 12);
        Project(dc, "Demo Workspace", 266, p);
        Chat(dc, "构建应用首页", 306, p, true);
        Chat(dc, "完善任务列表交互", 346, p);
        Chat(dc, "调整明暗主题", 386, p);
        Project(dc, "Sample Website", 444, p);
        Chat(dc, "编写页面组件", 484, p);
        Chat(dc, "优化响应式布局", 524, p);
        Project(dc, "Starter Kit", 582, p);
        Project(dc, "Playground", 624, p);
        Text(dc, "最近", 84, 699, 20, p.Muted);
        foreach (var (label, index) in new[] { "整理示例组件", "添加设置页面", "检查界面布局", "更新项目说明", "创建演示数据", "完善键盘操作" }.Select((label, index) => (label, index)))
        {
            Text(dc, label, 84, 743 + index * 40, 20, p.Secondary);
        }
        Fill(dc, p.Border, 394, 220, 7, 448, 4);
        Text(dc, "演示工作区", 84, 1042, 14, p.Muted);
    }

    private static void DrawWorkspace(DrawingContext dc, Palette p)
    {
        Fill(dc, p.Content, 405, 54, 1217, 1034, 18);
        Fill(dc, p.Content, 405, 72, 20, 1016);
        Icon(dc, "add-square", 1579, 79, 16, p.Muted);

        Icon(dc, "assistant", 983, 446, 58, p.Muted, 1.4);
        CenterText(dc, "你想让我们在 Demo Workspace 中构建什么？",
            new Rect(455, 536, 1117, 68), 38, p.Foreground);

        Fill(dc, p.Selection, 568, 898, 888, 82, 20);
        Icon(dc, "folder", 586, 914, 18, p.Foreground);
        Text(dc, "Demo Workspace", 612, 908, 18, p.Foreground);
        Icon(dc, "laptop", 808, 914, 19, p.Foreground);
        Text(dc, "此计算机", 837, 908, 18, p.Foreground);
        Icon(dc, "settings", 1422, 914, 18, p.Muted);

        // A quiet shadow at the same position as the reference composer.
        Fill(dc, p.Frame, 548, 949, 928, 122, 30);
        dc.DrawRoundedRectangle(Brush(p.Content), Pen(p.Border, 1), new Rect(551, 945, 922, 123), 28, 28);
        Text(dc, "描述一个任务，开始构建…", 568, 963, 20, p.Secondary);
        Icon(dc, "plus", 569, 1032, 19, p.Foreground);
        Icon(dc, "shield", 614, 1033, 17, "#F56425");
        Text(dc, "完全访问", 636, 1028, 19, "#F56425");
        Text(dc, "GPT-6.1 Sol", 1210, 1030, 18, p.Foreground);
        Text(dc, "极高", 1315, 1030, 18, p.Muted);
        Icon(dc, "chevron", 1353, 1035, 15, p.Muted);
        Icon(dc, "mic", 1389, 1030, 19, p.Foreground);
        dc.DrawEllipse(Brush(p.Foreground), null, new Point(1444, 1041), 18, 18);
        Icon(dc, "arrow-up", 1433, 1030, 22, p.Content);
    }

    private static void Project(DrawingContext dc, string name, double y, Palette p)
    {
        Icon(dc, "folder", 85, y, 19, p.Secondary);
        Text(dc, name, 114, y - 5, 20, p.Secondary);
    }

    private static void Chat(DrawingContext dc, string label, double y, Palette p, bool running = false)
    {
        Text(dc, label, 114, y - 5, 20, p.Secondary);
        if (running) Icon(dc, "clock", 350, y + 1, 15, p.Muted);
    }

    private static void Text(DrawingContext dc, string value, double x, double y, double size, string color, bool bold = false)
        => dc.DrawText(Format(value, size, color, bold), new Point(x, y));

    private static void CenterText(DrawingContext dc, string value, Rect rect, double size, string color, bool bold = false)
    {
        var text = Format(value, size, color, bold);
        dc.DrawText(text, new Point(rect.X + (rect.Width - text.Width) / 2, rect.Y + (rect.Height - text.Height) / 2));
    }

    private static FormattedText Format(string value, double size, string color, bool bold)
        => new(value, CultureInfo.GetCultureInfo("zh-CN"), FlowDirection.LeftToRight,
            new Typeface(Font, FontStyles.Normal, bold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal),
            size, Brush(color), 1);

    private static void Fill(DrawingContext dc, string color, double x, double y, double w, double h, double radius = 0)
        => dc.DrawRoundedRectangle(Brush(color), null, new Rect(x, y, w, h), radius, radius);

    private static void Line(DrawingContext dc, double x1, double y1, double x2, double y2, string color, double width)
        => dc.DrawLine(Pen(color, width), new Point(x1, y1), new Point(x2, y2));

    private static SolidColorBrush Brush(string value) => new((Color)ColorConverter.ConvertFromString(value));
    private static Pen Pen(string value, double width) => new(Brush(value), width)
    {
        StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round
    };

    private static void Icon(DrawingContext dc, string name, double x, double y, double size, string color, double stroke = 1.6)
    {
        var data = name switch
        {
            "back" => "M19,12 H5 M11,6 L5,12 11,18",
            "forward" => "M5,12 H19 M13,6 L19,12 13,18",
            "panel" => "M5,4 H19 Q21,4 21,6 V18 Q21,20 19,20 H5 Q3,20 3,18 V6 Q3,4 5,4 Z M10,4 V20",
            "home" => "M3,11 L12,3 21,11 M5,9 V21 H10 V15 H14 V21 H19 V9",
            "cards" => "M6,4 L18,3 Q20,3 20,5 L21,17 Q21,19 19,19 M4,7 L15,6 Q17,6 17,8 L18,20 Q18,22 16,22 L5,23 Q3,23 3,21 L2,9 Q2,7 4,7 Z",
            "clock" => "M22,12 A10,10 0 1 1 2,12 A10,10 0 1 1 22,12 M12,6 V12 L9,16",
            "orbit" => "M22,12 A10,10 0 1 1 2,12 A10,10 0 1 1 22,12 M8,7 C17,2 20,17 11,16 C3,15 9,8 16,11 M10,19 L7,16 8,12",
            "more" => "M4,12 H4.2 M12,12 H12.2 M20,12 H20.2",
            "branch" => "M6,6 V18 M18,6 V9 Q18,12 12,12 Q6,12 6,16 M8,4 A2,2 0 1 1 4,4 A2,2 0 1 1 8,4 M8,20 A2,2 0 1 1 4,20 A2,2 0 1 1 8,20 M20,4 A2,2 0 1 1 16,4 A2,2 0 1 1 20,4",
            "cube" => "M12,2 L22,8 V19 L12,24 2,19 V8 Z M2,8 L12,14 22,8 M12,14 V24 M12,2 V8",
            "folder" => "M3,6 Q3,4 5,4 H10 L13,7 H20 Q22,7 22,9 V19 Q22,21 20,21 H4 Q2,21 2,19 V8 Q2,6 3,6 Z M3,11 H21",
            "chevron" => "M6,9 L12,15 18,9",
            "bell" => "M5,17 H19 L17,14 V9 A5,5 0 0 0 7,9 V14 Z M10,21 H14",
            "search" => "M17,10 A7,7 0 1 1 3,10 A7,7 0 1 1 17,10 M15,15 L21,21",
            "edit" => "M10,4 H5 Q3,4 3,6 V19 Q3,21 5,21 H18 Q20,21 20,19 V14 M10,14 L11,10 19,2 23,6 15,14 Z",
            "bot" => "M5,8 H19 Q21,8 21,10 V19 Q21,21 19,21 H5 Q3,21 3,19 V10 Q3,8 5,8 Z M12,8 V3 M9,3 H15 M8,13 V15 M16,13 V15",
            "add-square" => "M5,3 H19 Q21,3 21,5 V19 Q21,21 19,21 H5 Q3,21 3,19 V5 Q3,3 5,3 Z M12,7 V17 M7,12 H17",
            "assistant" => "M5,7 C4,3 9,1 12,3 C15,0 20,3 20,7 C24,9 24,15 21,17 C22,22 17,25 13,22 C9,25 4,22 5,19 C0,17 0,11 5,7 Z M8,9 L11,13 8,17 M14,17 H18",
            "laptop" => "M5,3 H19 Q21,3 21,5 V16 H3 V5 Q3,3 5,3 Z M3,16 L1,21 H23 L21,16",
            "settings" => "M10,2 H14 L15,5 18,6 21,6 23,10 21,13 20,16 20,19 16,21 13,20 10,21 6,21 4,17 5,14 4,11 2,8 5,5 8,5 Z M16,12 A4,4 0 1 1 8,12 A4,4 0 1 1 16,12",
            "plus" => "M12,3 V21 M3,12 H21",
            "shield" => "M12,2 L21,6 V13 Q20,20 12,23 Q4,20 3,13 V6 Z M12,7 V13 M12,17 V17.2",
            "mic" => "M9,6 A3,3 0 0 1 15,6 V12 A3,3 0 0 1 9,12 Z M5,11 V12 A7,7 0 0 0 19,12 V11 M12,19 V23 M9,23 H15",
            "arrow-up" => "M12,20 V4 M5,11 L12,4 19,11",
            _ => throw new ArgumentException($"Unknown icon: {name}")
        };
        dc.PushTransform(new TranslateTransform(x, y));
        dc.PushTransform(new ScaleTransform(size / 24, size / 24));
        dc.DrawGeometry(null, Pen(color, stroke), Geometry.Parse(data));
        dc.Pop();
        dc.Pop();
    }

    private sealed record Palette(string Frame, string Sidebar, string Content, string Selection,
        string Border, string Foreground, string Secondary, string Muted);
}
