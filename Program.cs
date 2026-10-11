namespace Heat5Music;

static class Program
{
    public static Rectangle? ScreenArea { get; private set; }

    [STAThread]
    static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        ScreenArea = ReadScreen(args);
        Application.Run(new MainForm());
    }

    static Rectangle? ReadScreen(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (!string.Equals(args[i], "--screen", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var parts = args[i + 1].Split(',');
            if (parts.Length == 4
                && int.TryParse(parts[0], out var left)
                && int.TryParse(parts[1], out var top)
                && int.TryParse(parts[2], out var width)
                && int.TryParse(parts[3], out var height)
                && width > 0
                && height > 0)
            {
                return new Rectangle(left, top, width, height);
            }
        }

        return null;
    }
}
