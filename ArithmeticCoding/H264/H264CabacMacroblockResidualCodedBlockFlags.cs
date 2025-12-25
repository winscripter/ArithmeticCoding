using ArithmeticCoding.Shared;

namespace ArithmeticCoding.H264
{
    /// <summary>
    ///   Includes Coded Block Flags for residuals. For example, if <see cref="Intra16x16DCLevel"/>
    ///   is <see langword="true"/>, the <c>coded_block_flag</c> for Intra16x16DCLevel transform coefficient
    ///   level block is equal to 1.
    /// </summary>
    public unsafe struct H264CabacMacroblockResidualCodedBlockFlags
    {
        /// <summary>
        ///   If true, there is a residual in the macroblock; otherwise, false
        ///   if the macroblock is residualless.
        /// </summary>
        public bool IsResidualPresent;

        public bool Intra16x16DCLevel;
        public bool CbIntra16x16DCLevel;
        public bool CrIntra16x16DCLevel;

        public SixteenStackedBits LumaLevel4x4;
        public SixteenStackedBits LumaLevel8x8;

        public bool HasChromaLevels;
        public fixed bool ChromaDCLevel[2];

        /// <summary>
        ///   1 if ChromaArrayType == 3, essentially.
        /// </summary>
        public bool HasCbCrLevels;

        public SixteenStackedBits CrLevel4x4;
        public SixteenStackedBits CrLevel8x8;

        public SixteenStackedBits CbLevel4x4;
        public SixteenStackedBits CbLevel8x8;
    }
}
