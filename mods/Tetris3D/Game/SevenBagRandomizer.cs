namespace Game {
    public sealed class SevenBagRandomizer {
        public readonly List<PieceType> m_bag = [];
        public readonly System.Random m_random;

        public SevenBagRandomizer(int? seed = null) {
            m_random = seed.HasValue ? new System.Random(seed.Value) : new System.Random();
            Refill();
        }

        public PieceType Next() {
            if (m_bag.Count == 0) {
                Refill();
            }
            PieceType type = m_bag[m_bag.Count - 1];
            m_bag.RemoveAt(m_bag.Count - 1);
            return type;
        }

        public void Refill() {
            m_bag.Clear();
            m_bag.Add(PieceType.I);
            m_bag.Add(PieceType.O);
            m_bag.Add(PieceType.T);
            m_bag.Add(PieceType.L);
            m_bag.Add(PieceType.J);
            m_bag.Add(PieceType.S);
            m_bag.Add(PieceType.Z);
            for (int i = m_bag.Count - 1; i > 0; i--) {
                int j = m_random.Next(i + 1);
                (m_bag[i], m_bag[j]) = (m_bag[j], m_bag[i]);
            }
        }
    }
}
