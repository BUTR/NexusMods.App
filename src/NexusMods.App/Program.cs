﻿using System.CommandLine.Builder;
using System.CommandLine.Parsing;
using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NexusMods.App;
using NexusMods.App.UI;
using NexusMods.CLI;
using NLog.Extensions.Logging;
using NLog.Targets;
using static NexusMods.App.UI.Startup;


public class Program
{
    [STAThread]
    public static async Task<int> Main(string[] args)
    {
        var host = BuildHost();

        if (args.Length > 0)
        {
            var service = host.Services.GetRequiredService<CommandLineConfigurator>();
            var root = service.MakeRoot();

            var builder = new CommandLineBuilder(root)
                .UseDefaults()
                .Build();

            return await builder.InvokeAsync(args);
        }

        Startup.Main(host.Services, args);
        return 0;
    }

    private static IHost BuildHost()
    {
        var host = Host.CreateDefaultBuilder(Environment.GetCommandLineArgs())
            .ConfigureLogging(AddLogging)
            .ConfigureServices((_, services) => { services.AddApp(); }).Build();
        return host;
    }


    static void AddLogging(ILoggingBuilder loggingBuilder)
    {
        var config = new NLog.Config.LoggingConfiguration();

        var fileTarget = new FileTarget("file")
        {
            FileName = "logs/nexusmods.app.current.log",
            ArchiveFileName = "logs/nexusmods.app.{##}.log",
            ArchiveOldFileOnStartup = true,
            MaxArchiveFiles = 10,
            Layout = "${processtime} [${level:uppercase=true}] (${logger}) ${message:withexception=true}",
            Header = "############ Nexus Mods App log file - ${longdate} ############"
        };

        var consoleTarget = new ConsoleTarget("console")
        {
            Layout = "${processtime} [${level:uppercase=true}] ${message:withexception=true}",
        };

        config.AddRuleForAllLevels(fileTarget);
        config.AddRuleForAllLevels(consoleTarget);

        loggingBuilder.ClearProviders();
        loggingBuilder.SetMinimumLevel(LogLevel.Information);
        loggingBuilder.AddNLog(config);
    }

    private static AppBuilder BuildAvaloniaApp()
    {
        var host = BuildHost();
        return Startup.BuildAvaloniaApp(host.Services);
    }
}
