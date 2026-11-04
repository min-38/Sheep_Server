namespace SheepServer.Rounds;

public record Round(RoundPhase Phase, RoundPhase? Next, double NextAt);