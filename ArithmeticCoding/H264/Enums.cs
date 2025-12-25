namespace ArithmeticCoding.H264;

/// <summary>
///   Represents the H.264 slice type. The slice type can be applied
///   to either the entire slice or as macroblock's prediction mode.
/// </summary>
public enum H264CabacSliceType
{
    /// <summary>
    ///   The Intra slice.
    /// </summary>
    I,

    /// <summary>
    ///   The Switching Intra slice.
    /// </summary>
    SI,

    /// <summary>
    ///   The P slice.
    /// </summary>
    P,

    /// <summary>
    ///   The switching P slice.
    /// </summary>
    SP,

    /// <summary>
    ///   The B slice.
    /// </summary>
    B
}

/// <summary>
///   If MBAFF is enabled, this specifies the MBAFF mode.
/// </summary>
public enum H264MacroblockMbaffCoding
{
    /// <summary>
    ///   This macroblock is an MBAFF frame macroblock.
    /// </summary>
    Frame = 0,

    /// <summary>
    ///   This macroblock is an MBAFF field macroblock.
    /// </summary>
    Field = 1,

    /// <summary>
    ///   MBAFF is disabled.
    /// </summary>
    Neither = 2
}

/// <summary>
///   Represents the kind of prediction to use for this macroblock
///   (think Intra, Inter, etc).
/// </summary>
public enum H264MacroblockPredictionCoding
{
    /// <summary>
    ///   I/SI frame
    /// </summary>
    Intra,

    /// <summary>
    ///   P/SP/B frame
    /// </summary>
    Inter,

    /// <summary>
    ///   I frame with PCM macroblocks
    /// </summary>
    Pcm,

    /// <summary>
    ///   Something else
    /// </summary>
    Other
}

/// <summary>
///   MbPartPredMode for H.264 macroblocks
/// </summary>
public enum H264MacroblockPredictionMode
{
    /// <summary>
    ///   The macroblock is divided into 16 4x4 macroblocks.
    /// </summary>
    Intra4x4,

    /// <summary>
    ///   The macroblock is divided into 4 8x8 macroblocks.
    /// </summary>
    Intra8x8,

    /// <summary>
    ///   The entire macroblock is a single 16x16 block.
    /// </summary>
    Intra16x16,

    /// <summary>
    ///   The entire macroblock is B-predicted.
    /// </summary>
    BiPred,
    
    /// <summary>
    ///   Direct prediction.
    /// </summary>
    Direct,

    /// <summary>
    ///   Uses List 0.
    /// </summary>
    L0,

    /// <summary>
    ///   Uses List 1.
    /// </summary>
    L1,

    /// <summary>
    ///   Unknown, not specified by this enum, or "na" (Not An).
    /// </summary>
    InvalidOrNotAn
}

/// <summary>
///   The type of the residual block being parsed. The values
///   map directly to the variable ctxBlockCat.
/// </summary>
public enum H264ResidualBlockKind
{
    /// <summary>
    ///   Specifies that the current kind of residual transform coefficient block
    ///   being parsed is Intra16x16DCLevel, the DC coefficient levels for
    ///   Intra 16x16 Luma prediction.
    /// </summary>
    Intra16x16DCLevel,

    /// <summary>
    ///   Specifies that the current kind of residual transform coefficient block
    ///   being parsed is Intra16x16ACLevel, the AC coefficient levels for
    ///   Intra 16x16 Luma prediction.
    /// </summary>
    Intra16x16ACLevel,

    /// <summary>
    ///   Specifies that the current kind of residual transform coefficient block
    ///   being parsed is LumaLevel4x4, the coefficient levels for
    ///   Intra 4x4 Luma prediction.
    /// </summary>
    LumaLevel4x4,

    /// <summary>
    ///   Specifies that the current kind of residual transform coefficient block
    ///   being parsed is ChromaDCLevel, the DC coefficient levels for
    ///   Chroma prediction.
    /// </summary>
    ChromaDCLevel,

    /// <summary>
    ///   Specifies that the current kind of residual transform coefficient block
    ///   being parsed is ChromaACLevel, the AC coefficient levels for
    ///   Chroma prediction.
    /// </summary>
    ChromaACLevel,

    /// <summary>
    ///   Specifies that the current kind of residual transform coefficient block
    ///   being parsed is LumaLevel8x8, the coefficient levels for
    ///   Intra 8x8 Luma prediction.
    /// </summary>
    LumaLevel8x8,

    /// <summary>
    ///   Specifies that the current kind of residual transform coefficient block
    ///   being parsed is CbIntra16x16DCLevel, the DC coefficient levels for
    ///   Intra 16x16 Cb prediction.
    /// </summary>
    CbIntra16x16DCLevel,

    /// <summary>
    ///   Specifies that the current kind of residual transform coefficient block
    ///   being parsed is CbIntra16x16ACLevel, the AC coefficient levels for
    ///   Intra 16x16 Cb prediction.
    /// </summary>
    CbIntra16x16ACLevel,

    /// <summary>
    ///   Specifies that the current kind of residual transform coefficient block
    ///   being parsed is CbLevel4x4, the coefficient levels for
    ///   Intra 4x4 Cb prediction.
    /// </summary>
    CbLevel4x4,

    /// <summary>
    ///   Specifies that the current kind of residual transform coefficient block
    ///   being parsed is CbLevel8x8, the coefficient levels for
    ///   Intra 8x8 Cb prediction.
    /// </summary>
    CbLevel8x8,

    /// <summary>
    ///   Specifies that the current kind of residual transform coefficient block
    ///   being parsed is CrIntra16x16DCLevel, the DC coefficient levels for
    ///   Intra 16x16 Cr prediction.
    /// </summary>
    CrIntra16x16DCLevel,

    /// <summary>
    ///   Specifies that the current kind of residual transform coefficient block
    ///   being parsed is CrIntra16x16DCLevel, the AC coefficient levels for
    ///   Intra 16x16 Cr prediction.
    /// </summary>
    CrIntra16x16ACLevel,

    /// <summary>
    ///   Specifies that the current kind of residual transform coefficient block
    ///   being parsed is CrLevel4x4, the coefficient levels for
    ///   Intra 4x4 Cr prediction.
    /// </summary>
    CrLevel4x4,

    /// <summary>
    ///   Specifies that the current kind of residual transform coefficient block
    ///   being parsed is CrLevel8x8, the coefficient levels for
    ///   Intra 8x8 Cr prediction.
    /// </summary>
    CrLevel8x8,
}

/// <summary>
///   Represents the H.264 macroblock type. This does not cover every single type of
///   macroblocks in H.264, just those that are required for the H.264 CABAC decoder
///   to function. If the Macroblock type does not match any of those, consider
///   using the value <see cref="Other" />.
/// </summary>
public enum H264MacroblockType
{
    /// <summary>
    ///   B_Direct_16x16 type.
    /// </summary>
    B_Direct_16x16,

    /// <summary>
    ///   B_Skip type.
    /// </summary>
    B_Skip,
    
    /// <summary>
    ///   P_8x8 type.
    /// </summary>
    P_8x8,

    /// <summary>
    ///   B_8x8 type.
    /// </summary>
    B_8x8,

    /// <summary>
    ///   P_Skip type.
    /// </summary>
    P_Skip,

    /// <summary>
    ///   I_PCM type.
    /// </summary>
    I_PCM,

    /// <summary>
    ///   SI type.
    /// </summary>
    SI,

    /// <summary>
    ///   I_NxN type.
    /// </summary>
    I_NxN,

    /// <summary>
    ///   Any other type that is not specified by this enum.
    /// </summary>
    Other
}
