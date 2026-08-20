using System.Reflection;

var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

if (args is ["--version"] or ["-v"])
{
    Console.WriteLine(version);
    return 0;
}

Console.WriteLine($"FG Scanner CLI {version}");
Console.WriteLine("Commands arrive in phase 8 (scan, process, export, list-devices).");
return 0;
