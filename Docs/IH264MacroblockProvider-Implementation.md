# Implementing IH264MacroblockProvider
When dealing with CABAC data, it's important to acknowledge that CABAC relies on traits of other macroblocks
in order to encode data. It is not possible to write a CABAC decoder without relying on other macroblocks -
that's just not a thing.

Since getting other macroblocks and their traits would probably lean more into a universal H.264 decoder than
just the CABAC decoder, we provide an `IH264MacroblockProvider` interface so that you can write your
custom implementation of getting macroblocks and their information that adapts to your codebase and API
implementation.

Implement `IH264MacroblockProvider` and pass it to the constructor of the `H264CabacReader` class. This
document describes how to implement that interface.

Here's the general view of this interface:

```cs
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
    void DeriveNeighboring8x8LumaBlocksWithChromaArrayType3(
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
```

Tip: 
In H.264, all macroblocks in the current slice are stored in an array in memory at decoding time, that is left-to-right top-to-bottom. A macroblock
address is like the index of a macroblock in that array.

All clauses described in this document refer to those in the [Rec. ITU-T H.264 (V15) (08/2024) specification](https://www.itu.int/rec/T-REC-H.264-202408-I/en).

### TryGetMacroblock
This takes a macroblock address and attempts to return the macroblock in memory with that address.

- If a macroblock with address `address` exists, the return value is `true` and `descriptorOfMacroblock` is assigned a macroblock with address `address`.
- If a macroblock with address `address` does not exist, the return value is `false` and `descriptorOfMacroblock` is assigned with the C# `default` keyword.

> [!NOTE]
> `TryGetMacroblock` **MUST** also account for addresses of macroblocks being parsed. For instance, when a macroblock has just began parsing,
> it should already be considered a macroblock with a valid address, even if the rest of the syntax elements didn't parse yet, in which case,
> they're assigned default values.

### DeriveNeighboringMacroblocks
This method should implement Clause 6.4.9 in the H.264 spec.

> [!NOTE]
> Although that clause also specifies derivation processes for C and D neighboring macroblocks, only macroblocks A and B are required. The rest
> can be discarded.

The returned macroblocks are stored in `H264CabacMacroblockWithAvailability`, which contains both the actual macroblock, and its availability status. If the
availability status is `false`, the macroblock descriptor inside `H264CabacMacroblockWithAvailability` should be equal to the C# `default` keyword.

### CurrMbAddr
This property defines the address of the current macroblock that's actively being parsed right now.

You'll see its actual values being assigned in Clause 7.3.4 in the H.264 spec.

### CabacInitIdc
It is just the value of the `cabac_init_idc` syntax element of the last `slice_header` (clause 7.3.3) that was parsed.

### PpsConstrainedIntraPredFlag
It is just the value of the `constrained_intra_pred_flag` syntax element of the Picture Parameter Set (PPS) whose
`pic_parameter_set_id` syntax element is the same as the last `slice_header`'s `pic_parameter_set_id` syntax element.

### CurrentNalUnitType
It is just the value of the `nal_unit_type` syntax element of the last `nal_unit` (clause 7.3.1) that was parsed.

### MbPartPredMode
This function should implement the `MbPartPredMode` function as specified in clause 7.4.5 in the H.264 spec.

Note that the return value of the `MbPartPredMode` function and the way it works actually depends on the slice type. It is
all described in the H.264 spec and that function should produce results for all 5 slice types (I, P, B, SI, SP).

### DeriveNeighboringPartitions
This function should implement clause 6.4.11.7 in the H.264 spec.

The resulting `H264CabacMacroblockWithAvailabilityAndPartitionIndices` structs contain three properties: a `H264CabacMacroblockWithAvailability`,
as well as `mbPartIdx` and `subMbPartIdx`.

### SubMbPredMode
This function should implement the `SubMbPredMode` function as specified in clause 7.4.5.2 in the H.264 spec.

Note that the return value of the `SubMbPredMode` function and the way it works actually depends on the slice type. It is
all described in the H.264 spec and that function should produce results for all 5 slice types (I, P, B, SI, SP).

### DeriveNeighboring4x4LumaBlocks
This function should implement the derivation of neighboring 4x4 luma blocks as specified in clause 6.4.11.4 in the H.264 spec.

### DeriveNeighboring4x4ChromaBlocks
This function should implement the derivation of neighboring 4x4 chroma blocks as specified in clause 6.4.11.5 in the H.264 spec.

### DeriveNeighboring8x8LumaBlocks
This function should implement the derivation of neighboring 8x8 luma blocks as specified in clause 6.4.11.2 in the H.264 spec.

### DeriveNeighboring8x8ChromaBlocksWithChromaArrayType3
This function should implement the derivation of neighboring 8x8 chroma blocks with ChromaArrayType being set equal to 3
as specified in clause 6.4.11.3 in the H.264 spec.
