using System.Reflection;
using FgScanner.Cli;

if (args is ["--version"])
{
    Console.WriteLine(Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0");
    return 0;
}

return await CliRunner.RunAsync(args);
