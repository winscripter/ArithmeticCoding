namespace ArithmeticCoding.H264
{
    public interface IH264MacroblockProvider
    {
        bool TryGetMacroblock(int address, out H264CabacMacroblockDescriptor descriptorOfMacroblock);
        void DeriveNeighboringMacroblocks(int address, out H264CabacMacroblockWithAvailability a, out H264CabacMacroblockWithAvailability b);
        int CurrMbAddr { get; set; }
        int CabacInitIdc { get; }
        bool PpsConstrainedIntraPredFlag { get; }
        int CurrentNalUnitType { get; }
        H264MacroblockPredictionMode MbPartPredMode(in H264CabacMacroblockDescriptor descriptor, int mbPartIdx);
        void DeriveNeighboringPartitions(int mbPartIdx, int currSubMbType, int subMbPartIdx,
            out H264CabacMacroblockWithAvailabilityAndPartitionIndices a,
            out H264CabacMacroblockWithAvailabilityAndPartitionIndices b,
            out H264CabacMacroblockWithAvailabilityAndPartitionIndices c,
            out H264CabacMacroblockWithAvailabilityAndPartitionIndices d);
        H264MacroblockPredictionMode SubMbPredMode(int address, int subMbType);
        void DeriveNeighboring4x4LumaBlocks(
            int address,
            out H264CabacAddressAndBlockIndices a,
            out H264CabacAddressAndBlockIndices b);
        void DeriveNeighboring4x4ChromaBlocks(
            int address,
            int blkIdx,
            out H264CabacAddressAndBlockIndices a,
            out H264CabacAddressAndBlockIndices b);
        void DeriveNeighboring8x8LumaBlocks(
            int address,
            int blkIdx,
            out H264CabacAddressAndBlockIndices a,
            out H264CabacAddressAndBlockIndices b);
        void DeriveNeighboring8x8ChromaBlocksWithChromaArrayType3(
            int address,
            int blkIdx,
            out H264CabacAddressAndBlockIndices a,
            out H264CabacAddressAndBlockIndices b);
    }
}
