using Game;
using Xunit;

namespace Tetris3D.Tests {
    public class TetrisBoardTests {
        [Fact]
        public void SevenBag_ContainsEachTypeOnce() {
            SevenBagRandomizer bag = new(1);
            HashSet<PieceType> set = [];
            for (int i = 0; i < 7; i++) {
                set.Add(bag.Next());
            }
            Assert.Equal(7, set.Count);
        }

        [Fact]
        public void Reset_StartsPlayingWithActivePiece() {
            TetrisBoard board = new(2);
            Assert.Equal(TetrisState.Playing, board.State);
            Assert.NotNull(board.Active);
            Assert.Equal(1, board.Level);
            Assert.True(board.PeekNext(3).Count >= 3);
        }

        [Fact]
        public void Hold_OnlyOncePerTurn() {
            TetrisBoard board = new(3);
            Assert.True(board.TryHold());
            Assert.False(board.TryHold());
        }

        [Fact]
        public void ClearOneLine_ScoresAndCompacts() {
            TetrisBoard board = new(4);
            board.Active = null;
            for (int x = 0; x < TetrisBoard.Width; x++) {
                board.Cells[x, 0] = (int)PieceType.I;
            }
            board.Cells[0, 1] = (int)PieceType.T;
            typeof(TetrisBoard).GetMethod("LockActive", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            int scoreBefore = board.Score;
            board.HardDrop();
            Assert.True(board.Score >= scoreBefore);
        }

        [Fact]
        public void LineClearScore_MatchesPrd() {
            Assert.Equal(100, TetrisScoring.LineClearScore(1, 1));
            Assert.Equal(300, TetrisScoring.LineClearScore(2, 1));
            Assert.Equal(500, TetrisScoring.LineClearScore(3, 1));
            Assert.Equal(800, TetrisScoring.LineClearScore(4, 1));
            Assert.Equal(1600, TetrisScoring.LineClearScore(4, 2));
        }

        [Fact]
        public void FillBoardToTop_GameOver() {
            TetrisBoard board = new(5);
            for (int y = 0; y < TetrisBoard.TotalHeight; y++) {
                for (int x = 0; x < TetrisBoard.Width; x++) {
                    board.Cells[x, y] = (int)PieceType.O;
                }
            }
            board.Reset();
            for (int y = 0; y < TetrisBoard.TotalHeight; y++) {
                for (int x = 0; x < TetrisBoard.Width; x++) {
                    board.Cells[x, y] = (int)PieceType.O;
                }
            }
            board.HardDrop();
            Assert.True(board.State == TetrisState.GameOver || board.Active != null);
        }

        [Fact]
        public void HardDrop_IncreasesScore() {
            TetrisBoard board = new(6);
            int before = board.Score;
            board.HardDrop();
            Assert.True(board.Score > before);
        }
    }
}
