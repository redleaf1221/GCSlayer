using CliFx;
using GCSlayer.Commands;

await new CliApplicationBuilder()
    .SetExecutableName("GCSlayer")
    .AddCommand<RecoverCommand>()
    .AddCommand<DrmCryptoCommand>()
    .AddCommand<InitCommand>()
    .Build()
    .RunAsync();
