using Core.Run;

namespace Core.Abstractions.Persistence;

/// <summary>
/// Unidade atômica de durabilidade: o estado e a descrição da transição que o
/// produziu nunca são gravados separadamente.
/// </summary>
public sealed record RunCheckpoint(
    RunState State,
    RunJournalEntry JournalEntry);
