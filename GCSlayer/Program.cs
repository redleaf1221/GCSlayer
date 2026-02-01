using CliFx;
using CliFx.Attributes;
using CliFx.Infrastructure;
using GCSlayer;
using GCSlayer.Commands;
using GCSlayer.Models;
using GCSlayer.Services;

await new CliApplicationBuilder()
    .SetExecutableName("GCSlayer")
    .AddCommand<RecoverCommand>()
    .AddCommand<DrmCryptoCommand>()
    .AddCommand<InitCommand>()
    .Build()
    .RunAsync();
