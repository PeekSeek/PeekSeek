using System;

namespace PeekSeekRaid.Core;

/// Who defends in round N, and whether the match is over yet.
///
/// The shape is Rainbow Six's: a block of rounds on one side, then the same block on
/// the other, so a team that built a base always gets to both defend it and attack
/// the base it has just been defending against. Regulation is 3 and 3, and level at
/// 3-3 goes to overtime, 2 and 2, over and over. A match ends the moment the rest of
/// the block cannot change who is ahead: 4-0 after four rounds is over, 4-1 after
/// five is over, and two rounds that change nothing are two rounds nobody wants to
/// play. Never before that, because half a block is not a fair swap.
///
/// Everything here is arithmetic on the round number and the score, so nothing has to
/// be carried between rounds and a reconnecting player can be told where they are from
/// three integers.
internal static class RoundOrder {
    internal const int RegulationHalf = 3;
    internal const int OvertimeHalf = 2;

    private const int Regulation = RegulationHalf * 2;
    private const int OvertimeBlock = OvertimeHalf * 2;

    /// Which team defends round `round` (1-based). Team 0 defends first.
    internal static int Defender(int round) {
        int index = round - 1;

        if (index < Regulation) return index < RegulationHalf ? 0 : 1;

        // Overtime alternates which team defends first in each block, so neither side
        // gets the same half of every extra block.
        //
        // The first block starts with team 1, because team 0 already defended first in
        // regulation and going straight back to them would hand one side the opening
        // defence of both. That is what the `+ 1` is: block 0 is team 1's to open.
        int overtimeIndex = index - Regulation;
        int block = overtimeIndex / OvertimeBlock;
        bool secondHalf = overtimeIndex % OvertimeBlock >= OvertimeHalf;

        return (block + (secondHalf ? 1 : 0) + 1) % 2;
    }

    internal static bool IsOvertime(int round) => round > Regulation;

    /// Whether round `round` is the last of its block, which in overtime is the only
    /// moment a score can end the match.
    internal static bool EndsABlock(int round) {
        if (round < Regulation) return false;
        if (round == Regulation) return true;

        return (round - Regulation) % OvertimeBlock == 0;
    }

    /// Rounds still to play in the block that round `round` belongs to, once it has
    /// been played.
    private static int LeftInBlock(int round) {
        if (!IsOvertime(round)) return Regulation - round;

        return (OvertimeBlock - (round - Regulation) % OvertimeBlock) % OvertimeBlock;
    }

    /// The winning team once round `round` has been scored, or -1 while the match is
    /// still going: whoever is ahead by more than the block has rounds left.
    internal static int Winner(int round, int teamZeroScore, int teamOneScore) {
        int lead = Math.Abs(teamZeroScore - teamOneScore);
        if (lead <= LeftInBlock(round)) return -1;

        return teamZeroScore > teamOneScore ? 0 : 1;
    }
}
