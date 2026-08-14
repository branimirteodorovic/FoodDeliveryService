namespace FoodDeliveryService.LoadTest.Seeder;

/// <summary>
/// Console output. Seeding is a several-minute operation whose slowest step (waiting for the
/// outbox) looks identical to a hang, so it narrates what it is doing and how long it has been
/// doing it — a silent tool here gets killed at the four-minute mark and blamed for the failure.
/// </summary>
internal static class Log
{
    private static readonly Lock Gate = new();

    public static void Step(string message) => Write(ConsoleColor.Cyan, "»", message);

    public static void Info(string message) => Write(null, " ", message);

    public static void Warn(string message) => Write(ConsoleColor.Yellow, "!", message);

    public static void Error(string message) => Write(ConsoleColor.Red, "x", message, toStandardError: true);

    public static void Done(string message) => Write(ConsoleColor.Green, "✓", message);

    private static void Write(ConsoleColor? color, string marker, string message, bool toStandardError = false)
    {
        lock (Gate)
        {
            TextWriter writer = toStandardError ? Console.Error : Console.Out;

            if (color.HasValue)
            {
                Console.ForegroundColor = color.Value;
            }

            writer.WriteLine($"[{DateTime.Now:HH:mm:ss}] {marker} {message}");

            if (color.HasValue)
            {
                Console.ResetColor();
            }
        }
    }
}
