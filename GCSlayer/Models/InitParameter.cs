namespace GCSlayer.Models;

public record InitParameter {
    public required string IdePath { get; init; }
    
    public string IdeScriptPath => Path.Combine(IdePath, @"kdsrpg\kdsrpg_IDE\kdsrpgEditor\bin\dist\index.js");

    public string CoreTemplatePath => Path.Combine(IdePath, @"kdsrpg\coreTemplate\GameCreator2D");
}
