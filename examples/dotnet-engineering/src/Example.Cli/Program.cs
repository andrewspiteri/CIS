using Example.Cli;

return await ReadingCommand.Create().Parse(args).InvokeAsync();
