using System.CommandLine;
using Project27.Cli;

return CliRoot.Build().Parse(args).Invoke();
