using CliFx;
using GCSlayer.Commands;

await new CliApplicationBuilder()
    .SetExecutableName("GCSlayer")
    .AddCommand<RecoverCommand>()
    .AddCommand<DrmCryptoCommand>()
    .AddCommand<InitCommand>()
    .AddCommand<Z1Command>()
    .AddCommand<FixupCommand>()
    .AddCommand<VerifyCommand>()
    .Build()
    .RunAsync();
