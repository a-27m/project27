using System.CommandLine;
using Microsoft.Extensions.Configuration;
using Project27.Cli;

var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true)
    .Build();

CliConfig.Initialize(config);

return CliRoot.Build().Parse(args).Invoke();
