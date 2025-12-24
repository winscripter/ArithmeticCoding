using ArithmeticCoding.Shared;
using System.Runtime.CompilerServices;
using static ArithmeticCoding.H264.H264MacroblockType;
using static ArithmeticCoding.Shared.Maths;

namespace ArithmeticCoding.H264
{
    /// <summary>
    ///   H.264 CABAC decoder.
    /// </summary>
    public partial class H264CabacReader
    {
        /// <summary>
        ///   Maximum bins that ref_idx_lX's unary binarization can consume before
        ///   it counts as "infinite loop".
        /// </summary>
        private const int ReferenceIndexUnaryMaximumBins = 24;

        /// <summary>
        ///   Maps block kind factor indices (our custom optimization method where cases, i.e.,
        ///   5 &lt; ctxBlockCat &gt; 9 and MBAFF coding mode are transformed into indices to
        ///   directly use in lookups) into ctxIdxOffset for syntax elements significant_coeff_flag.
        /// </summary>
        private static ReadOnlySpan<int> SignificantCoeffFlagOffsetFromBlockFactors =>
        [
            // Reserved
            int.MinValue,

            // Non-MBAFF Frames, followed by Frame macroblocks (exactly same)
            105, 402, 484, 528, 660, 718, // <-- Non-MBAFF
            105, 402, 484, 528, 660, 718, // <-- Frame

            // Field macroblocks
            277, 436, 776, 820, 675, 733
        ];

        /// <summary>
        ///   Maps block kind factor indices (our custom optimization method where cases, i.e.,
        ///   5 &lt; ctxBlockCat &gt; 9 and MBAFF coding mode are transformed into indices to
        ///   directly use in lookups) into ctxIdxOffset for syntax elements last_significant_coeff_flag.
        /// </summary>
        private static ReadOnlySpan<int> LastSignificantCoeffFlagOffsetFromBlockFactors =>
        [
            // Reserved
            int.MinValue,

            // Non-MBAFF Frames, followed by Frame macroblocks (exactly same)
            166, 417, 572, 616, 690, 748, // <-- Non-MBAFF
            166, 417, 572, 616, 690, 748, // <-- Frame

            // Field macroblocks
            338, 451, 864, 908, 699, 757
        ];

        /// <summary>
        ///   Maps block kind factor indices (our custom optimization method where cases, i.e.,
        ///   5 &lt; ctxBlockCat &gt; 9 and MBAFF coding mode are transformed into indices to
        ///   directly use in lookups) into ctxIdxOffset for syntax elements coded_block_flag.
        /// </summary>
        private static ReadOnlySpan<int> CodedBlockFlagOffsetFromBlockFactors =>
        [
            // Reserved
            int.MinValue,

            // For coded_block_flag all cases are same for MBAFF, Frame and Field macroblocks
            // We'll just repeat them thrice.

            85, 1012, 460, 472, 1012, 1012,
            85, 1012, 460, 472, 1012, 1012,
            85, 1012, 460, 472, 1012, 1012,
        ];

        /// <summary>
        ///   Maps block kind factor indices (our custom optimization method where cases, i.e.,
        ///   5 &lt; ctxBlockCat &gt; 9 and MBAFF coding mode are transformed into indices to
        ///   directly use in lookups) into ctxIdxOffset for syntax elements coeff_abs_level_minus1.
        /// </summary>
        private static ReadOnlySpan<int> CoeffAbsLevelMinus1OffsetFromBlockFactors =>
        [
            // Reserved
            int.MinValue,

            // For coeff_abs_level_minus1 all cases are same for MBAFF, Frame and Field macroblocks
            // We'll just repeat them thrice.
            // Note: coeff_abs_level_minus1's suffix is Bypass-only. These offsets apply to the prefix only.

            227, 426, 952, 982, 708, 766,
            227, 426, 952, 982, 708, 766,
            227, 426, 952, 982, 708, 766,
        ];

        private const H264MacroblockType B_Direct_16x16 = H264MacroblockType.B_Direct_16x16;
        private const H264MacroblockType B_Skip = H264MacroblockType.B_Skip;
        private const H264MacroblockType B_8x8 = H264MacroblockType.B_8x8;
        private const H264MacroblockType P_8x8 = H264MacroblockType.P_8x8;

        /// <summary>
        ///   Decoding of List 0.
        /// </summary>
        private const bool L0 = false;

        /// <summary>
        ///   Decoding of List 1.
        /// </summary>
        private const bool L1 = true;

        /// <summary>
        ///   Maps binIdx to ctxIdxInc for MVD prefix decoding. Note: The first entry is a placeholder
        ///   for derivation based on other macroblocks.
        /// </summary>
        private static ReadOnlySpan<int> MvdPrefixCtxIdxIncMap => [int.MinValue, 3, 4, 5, 6, 6, 6];

        /// <summary>
        ///   ctxIdxInc for ref_idx based on bin positions.
        /// </summary>
        private static ReadOnlySpan<int> RefIdxIncrementalContexts => [-1, 4, 5, 5, 5, 5, 5];

        /// <summary>
        ///   The total number of context variables.
        /// </summary>
        private const int NumberOfContextVariables = 1024;

        /// <summary>
        ///   All context variables are stored here. They're preinitialized for performance.
        /// </summary>
        private readonly H264ContextVariable[] _contextVariables = new H264ContextVariable[NumberOfContextVariables];

        /// <summary>
        ///   The macroblock provider.
        /// </summary>
        private readonly IH264MacroblockProvider _macroblockProvider;

        /// <summary>
        ///   The bit-stream reader.
        /// </summary>
        private readonly IBitstreamReader _bitStreamReader;

        /// <summary>
        ///   The slice type.
        /// </summary>
        private readonly H264CabacSliceType _sliceType;

        /// <summary>
        ///   Internal arithmetic decoding engine register, see ArithmeticRange.
        /// </summary>
        private int codIRange = 510;

        /// <summary>
        ///   Internal arithmetic decoding engine register, see ArithmeticOffset.
        /// </summary>
        private int codIOffset = 0;

        /// <summary>
        ///   The variable mbPartIdx.
        /// </summary>
        private int _mbPartIdx = 0;

        /// <summary>
        ///   The variable subMbPartIdx.
        /// </summary>
        private int _subMbPartIdx = 0;

        /// <summary>
        ///   The type of the residual block.
        /// </summary>
        private H264ResidualBlockKind _residualBlockKind = 0;

        /// <summary>
        ///   Level list index.
        /// </summary>
        private int _levelListIdx = 0;

        /// <summary>
        ///   NumC8x8 variable.
        /// </summary>
        private int _numC8x8 = 0;

        /// <summary>
        ///   _numDecodAbsLevelGt1 variable.
        /// </summary>
        private int _numDecodAbsLevelGt1 = 0;

        /// <summary>
        ///   _numDecodAbsLevelEq1 variable.
        /// </summary>
        private int _numDecodAbsLevelEq1 = 0;

        public H264CabacReader(H264CabacSliceType slt, int qp, IH264MacroblockProvider macroblockProvider, int offset, IBitstreamReader bsr)
        {
            InitializeAllContextVariables(slt, qp);
            _macroblockProvider = macroblockProvider;
            codIOffset = offset;
            _bitStreamReader = bsr;
            _sliceType = slt;
        }

        /// <summary>
        ///   The range value. Defaults to 510.
        /// </summary>
        public int ArithmeticRange
        {
            get => codIRange;
            set => codIRange = value;
        }

        /// <summary>
        ///   The offset value.
        /// </summary>
        public int ArithmeticOffset
        {
            get => codIOffset;
            set => codIOffset = value;
        }

        /// <summary>
        ///   mbPartIdx property. Use this property to get and set the
        ///   variable mbPartIdx. The default value is 0. This variable
        ///   is needed for derivatives like mvd_lX.
        /// </summary>
        public int MacroblockPartitionIndex
        {
            get => _mbPartIdx;
            set => _mbPartIdx = value;
        }

        /// <summary>
        ///   subMbPartIdx property. Use this property to get and set the
        ///   variable subMbPartIdx. The default value is 0. This variable
        ///   is needed for derivatives like mvd_lX.
        /// </summary>
        public int SubMacroblockPartitionIndex
        {
            get => _subMbPartIdx;
            set => _subMbPartIdx = value;
        }

        /// <summary>
        ///   Type of the residual block being parsed at the moment.
        ///   Default: <see cref="H264ResidualBlockKind.Intra16x16DCLevel"/>.
        /// </summary>
        public H264ResidualBlockKind ResidualBlockKind
        {
            get => _residualBlockKind;
            set => _residualBlockKind = value;
        }

        /// <summary>
        ///   The level list index. This is the index of the list of
        ///   transform coefficient levels.
        /// </summary>
        public int LevelListIndex
        {
            get => _levelListIdx;
            set => _levelListIdx = value;
        }

        /// <summary>
        ///   The variable NumC8x8.
        /// </summary>
        public int NumC8x8
        {
            get => _numC8x8;
            set => _numC8x8 = value;
        }

        /// <summary>
        ///   <para>Number of coefficients in the current transform block that have
        ///   an absolute value > 1.</para>
        ///   <example>
        ///     Example pseudocode to derive this value:
        ///     <code>
        ///       int[] coefficients = ...; // The transform block coefficients
        ///       
        ///       int numAbsLevelGt1 = coefficients.Count(coef => Math.Abs(coef) &gt; 1);
        ///     </code>
        ///     <para>A more efficient way to do this would be to set numAbsLevelGt1 to 0, then,
        ///     after parsing each coefficient, increment it if the absolute value
        ///     is greater than 1, for example:</para>
        ///     <code>
        ///       int numAbsLevelGt1 = 0;
        ///       
        ///       for (int i = 0; i &lt; coefficients.Length; i++)
        ///       {
        ///           int coefficient = ParseNextCoefficient(numAbsLevelGt1); // Hypothetical method to parse the next coefficient
        ///           
        ///           if (coefficient > 1)
        ///               numAbsLevelGt1++;
        ///       }
        ///     </code>
        /// </example>
        /// </summary>
        public int NumDecodedAbsLevelGreaterThan1
        {
            get => _numDecodAbsLevelGt1;
            set => _numDecodAbsLevelGt1 = value;
        }

        /// <summary>
        ///   <para>Number of coefficients in the current transform block that have
        ///   an absolute value = 1.</para>
        ///   <example>
        ///     Example pseudocode to derive this value:
        ///     <code>
        ///       int[] coefficients = ...; // The transform block coefficients
        ///       
        ///       int numAbsLevelEq1 = coefficients.Count(coef => Math.Abs(coef) == 1);
        ///     </code>
        ///     <para>A more efficient way to do this would be to set numAbsLevelEq1 to 0, then,
        ///     after parsing each coefficient, increment it if the absolute value
        ///     is greater than 1, for example:</para>
        ///     <code>
        ///       int numAbsLevelEq1 = 0;
        ///       
        ///       for (int i = 0; i &lt; coefficients.Length; i++)
        ///       {
        ///           int coefficient = ParseNextCoefficient(numAbsLevelEq1); // Hypothetical method to parse the next coefficient
        ///           
        ///           if (coefficient == 1)
        ///               numAbsLevelEq1++;
        ///       }
        ///     </code>
        /// </example>
        /// </summary>
        public int NumDecodedAbsLevelEqualTo1
        {
            get => _numDecodAbsLevelEq1;
            set => _numDecodAbsLevelEq1 = value;
        }

        /// <summary>
        ///   Initializes all context variables.
        /// </summary>
        /// <param name="slt">The slice type.</param>
        /// <param name="qp">The variable SliceQPY.</param>
        private void InitializeAllContextVariables(H264CabacSliceType slt, int qp)
        {
            for (int i = 0; i < NumberOfContextVariables; i++)
            {
                var (m, n) = slt is H264CabacSliceType.I or H264CabacSliceType.SI
                    ? H264Tables.GetInitDataForIOrSISlice(i)
                    : H264Tables.GetInitData(i, _macroblockProvider.CabacInitIdc);

                int preCtxState = Clip3(1, 126, ((m * Clip3(0, 51, qp)) >> 4) + n);

                _contextVariables[i].PStateIndex = preCtxState <= 63 ? 63 - preCtxState : preCtxState - 64;
                _contextVariables[i].MpsValue = !(preCtxState <= 63);
            }
        }

        /// <summary>
        ///   Reads a CABAC decision using the specified context variable.
        /// </summary>
        /// <param name="cv">The context variable.</param>
        /// <returns>The decision bin.</returns>
        public bool ReadDecision(ref H264ContextVariable cv)
        {
            int qCode = (codIRange >> 6) & 3;
            int codIRangeLPS = H264Tables.GetRangeTabLps(cv.PStateIndex, qCode);

            codIRange -= codIRangeLPS;

            bool binVal;

            if (codIOffset >= codIRange)
            {
                binVal = !cv.MpsValue;
                codIOffset -= codIRange;
                codIRange -= codIRangeLPS;

                if (cv.PStateIndex == 0)
                {
                    cv.MpsValue = !cv.MpsValue;
                }

                cv.PStateIndex = H264Tables.LPSTransitioningTable[cv.PStateIndex];
            }
            else
            {
                binVal = cv.MpsValue;
                cv.PStateIndex = H264Tables.MPSTransitioningTable[cv.PStateIndex];
            }

            Renormalize();
            return binVal;
        }

        /// <summary>
        ///   Performs arithmetic renormalization.
        /// </summary>
        public void Renormalize()
        {
            while (codIRange < 256)
            {
                codIRange <<= 1;
                codIOffset <<= 1;
                codIOffset |= _bitStreamReader.ReadBit().AsInt32();
            }
        }

        /// <summary>
        ///   Decodes a bypass bin.
        /// </summary>
        /// <returns>The decoded bin.</returns>
        public bool ReadBypass()
        {
            codIOffset <<= 1;
            codIOffset |= _bitStreamReader.ReadBit().AsInt32();

            //
            // Value of the resulting bin
            //
            bool binVal;

            if (codIOffset >= codIRange)
            {
                binVal = true;
                codIOffset -= codIRange;
            }
            else
            {
                binVal = false;
            }

            return binVal;
        }

        /// <summary>
        ///   Reads a terminate bin.
        /// </summary>
        /// <returns>The termination bin.</returns>
        public bool ReadTerminate()
        {
            codIRange -= 2;
            if (codIOffset >= codIRange)
            {
                return true;
            }
            else
            {
                Renormalize();
                return false;
            }
        }

        /// <summary>
        ///   Read an UEGk's EGk part with Bypass coding.
        /// </summary>
        /// <param name="k">The count.</param>
        /// <returns>Exp-Golomb</returns>
        private int ReadEgkBypass(int k)
        {
            int x = 0;
            while (ReadBypass())
            {
                x += 1 << k++;
            }
            for (int i = k - 1; i >= 0; i--)
            {
                x |= (ReadBypass() ? 1 : 0) << i;
            }
            return x;
        }

        /// <summary>
        ///   <para>   <b>mb_skip_flag</b></para>
        ///   <para>   <b>Type:</b> FL, cMax=1</para>
        ///   <para>   <b>ctxIdxOffset:</b> 11, 24</para>
        ///   <para>   <b>Affix:</b> Prefix only</para>
        /// </summary>
        /// <returns>
        ///   Decoded value of the syntax element.
        /// </returns>
        public bool DecodeSkipFlag()
        {
            int ctxBase = _sliceType is H264CabacSliceType.P or H264CabacSliceType.SP ? 11 : 24;
            int ctxInc = GetIncrementalContextForSkipFlag();
            int ctxIdx = ctxBase + ctxInc;

            return ReadDecision(ref _contextVariables[ctxIdx]);
        }

        /// <summary>
        ///   <para>   <b>prev_intra_4x4_pred_mode_flag</b></para>
        ///   <para>   <b>Type:</b> FL, cMax=1</para>
        ///   <para>   <b>ctxIdxOffset:</b> 68</para>
        ///   <para>   <b>Affix:</b> Prefix only</para>
        /// </summary>
        /// <returns>
        ///   Decoded value of the syntax element.
        /// </returns>
        public bool DecodePrevIntra4x4PredModeFlag()
        {
            return PrevIntraNxNPredModeFlag();
        }

        /// <summary>
        ///   <para>   <b>prev_intra_8x8_pred_mode_flag</b></para>
        ///   <para>   <b>Type:</b> FL, cMax=1</para>
        ///   <para>   <b>ctxIdxOffset:</b> 68</para>
        ///   <para>   <b>Affix:</b> Prefix only</para>
        /// </summary>
        /// <returns>
        ///   Decoded value of the syntax element.
        /// </returns>
        public bool DecodePrevIntra8x8PredModeFlag()
        {
            return PrevIntraNxNPredModeFlag();
        }

        /// <summary>
        ///   <para>   <b>rem_intra_4x4_pred_mode_flag</b></para>
        ///   <para>   <b>Type:</b> FL, cMax=7</para>
        ///   <para>   <b>ctxIdxOffset:</b> 69</para>
        ///   <para>   <b>Affix:</b> Prefix only</para>
        /// </summary>
        /// <returns>
        ///   Decoded value of the syntax element.
        /// </returns>
        public int DecodeRemainingIntra4x4PredMode()
        {
            return RemainingIntraNxNPredMode();
        }

        /// <summary>
        ///   <para>   <b>rem_intra_8x8_pred_mode_flag</b></para>
        ///   <para>   <b>Type:</b> FL, cMax=7</para>
        ///   <para>   <b>ctxIdxOffset:</b> 69</para>
        ///   <para>   <b>Affix:</b> Prefix only</para>
        /// </summary>
        /// <returns>
        ///   Decoded value of the syntax element.
        /// </returns>
        public int DecodeRemainingIntra8x8PredMode()
        {
            return RemainingIntraNxNPredMode();
        }

        /// <summary>
        ///   <para>   <b>coeff_sign_flag</b></para>
        ///   <para>   <b>Type:</b> FL, cMax=1</para>
        ///   <para>   <b>ctxIdxOffset:</b> Missing (reads bypass bins instead of decisions)</para>
        ///   <para>   <b>Affix:</b> Prefix only</para>
        /// </summary>
        /// <returns>
        ///   Decoded value of the syntax element.
        /// </returns>
        public bool DecodeCoefficientSignFlag()
        {
            return ReadBypass();
        }

        /// <summary>
        ///   <para>   <b>end_of_slice_flag</b></para>
        ///   <para>   <b>Type:</b> FL, cMax=1</para>
        ///   <para>   <b>ctxIdxOffset:</b> 276</para>
        ///   <para>   <b>Affix:</b> Prefix only</para>
        /// </summary>
        /// <returns>
        ///   Decoded value of the syntax element.
        /// </returns>
        public bool DecodeEndOfSliceFlag()
        {
            return ReadTerminate();
        }

        /// <summary>
        ///   <para>   <b>transform_size_8x8_flag</b></para>
        ///   <para>   <b>Type:</b> FL, cMax=1</para>
        ///   <para>   <b>ctxIdxOffset:</b> 399</para>
        ///   <para>   <b>Affix:</b> Prefix only</para>
        /// </summary>
        /// <returns>
        ///   Decoded value of the syntax element.
        /// </returns>
        public bool DecodeTransformSize8x8Flag()
        {
            return ReadDecision(ref _contextVariables[399 + GetIncrementalContextForTransformSize8x8Flag()]);
        }

        /// <summary>
        ///   <para>   <b>mvd_l0</b></para>
        ///   <para>   <b>Type:</b> UEG3, signedValFlag=1, uCoff=9</para>
        ///   <para>   <b>ctxIdxOffset:</b> 40 (prefix), Bypass only (suffix)</para>
        ///   <para>   <b>Affix:</b> Prefix/suffix</para>
        /// </summary>
        /// <returns>
        ///   Decoded value of the syntax element.
        /// </returns>
        /// <remarks>
        ///   This method requires that the following properties are adjusted accordingly:
        ///   <list type="bullet">
        ///     <item>
        ///       <see cref="MacroblockPartitionIndex"/>
        ///     </item>
        ///     <item>
        ///       <see cref="SubMacroblockPartitionIndex"/>
        ///     </item>
        ///   </list>
        /// </remarks>
        public int DecodeMotionVectorDifferenceL0()
        {
            return DecodeMvdBinarization(GetIncrementalContextForMvd(L0, _mbPartIdx, _subMbPartIdx), L0);
        }

        /// <summary>
        ///   <para>   <b>mvd_l1</b></para>
        ///   <para>   <b>Type:</b> UEG3, signedValFlag=1, uCoff=9</para>
        ///   <para>   <b>ctxIdxOffset:</b> 47 (prefix), Bypass only (suffix)</para>
        ///   <para>   <b>Affix:</b> Prefix/suffix</para>
        /// </summary>
        /// <returns>
        ///   Decoded value of the syntax element.
        /// </returns>
        /// <remarks>
        ///   This method requires that the following properties are adjusted accordingly:
        ///   <list type="bullet">
        ///     <item>
        ///       <see cref="MacroblockPartitionIndex"/>
        ///     </item>
        ///     <item>
        ///       <see cref="SubMacroblockPartitionIndex"/>
        ///     </item>
        ///   </list>
        /// </remarks>
        public int DecodeMotionVectorDifferenceL1()
        {
            return DecodeMvdBinarization(GetIncrementalContextForMvd(L1, _mbPartIdx, _subMbPartIdx), L1);
        }

        /// <summary>
        ///   <para>   <b>significant_coeff_flag</b></para>
        ///   <para>   <b>Type:</b> FL, cMax=1</para>
        ///   <para>   <b>ctxIdxOffset:</b> Depends on ctxBlockCat</para>
        ///   <para>   <b>Affix:</b> Prefix only</para>
        /// </summary>
        /// <returns>
        ///   Decoded value of the syntax element.
        /// </returns>
        /// <remarks>
        ///   This method requires that the following properties are adjusted accordingly:
        ///   <list type="bullet">
        ///     <item>
        ///       <see cref="LevelListIndex"/>
        ///     </item>
        ///     <item>
        ///       <see cref="NumC8x8"/>
        ///     </item>
        ///     <item>
        ///       <see cref="ResidualBlockKind"/>
        ///     </item>
        ///   </list>
        /// </remarks>
        public bool DecodeSignificantCoefficientFlag()
        {
            int rbk = (int)_residualBlockKind; // Cache casted result as performance benefit

            int inc;

            if (rbk is not 3 and not 5 and not 9 and not 13)
            {
                inc = _levelListIdx;
            }
            else if (rbk == 3)
            {
                inc = Math.Min(_levelListIdx / _numC8x8, 2);
            }
            else
            {
                bool isCurrMbFrame = ForceGetMacroblockByAddress(_macroblockProvider.CurrMbAddr).MbaffCoding != H264MacroblockMbaffCoding.Field;
                ref LevelListIndex lli = ref LevelListIndicesTable[_levelListIdx];

                inc = isCurrMbFrame ? lli.SignificantCoeffFlagFrame : lli.SignificantCoeffFlagField;
            }

            int ctxIdx = inc + H264Tables.CtxBlockCatToOffsetForSignificantAndLastSignificantCoeffFlag[rbk] + GetSignificantCoefficientFlagOffset(rbk);

            return ReadDecision(ref _contextVariables[ctxIdx]);
        }

        /// <summary>
        ///   <para>   <b>last_significant_coeff_flag</b></para>
        ///   <para>   <b>Type:</b> FL, cMax=1</para>
        ///   <para>   <b>ctxIdxOffset:</b> Depends on ctxBlockCat</para>
        ///   <para>   <b>Affix:</b> Prefix only</para>
        /// </summary>
        /// <returns>
        ///   Decoded value of the syntax element.
        /// </returns>
        /// <remarks>
        ///   This method requires that the following properties are adjusted accordingly:
        ///   <list type="bullet">
        ///     <item>
        ///       <see cref="LevelListIndex"/>
        ///     </item>
        ///     <item>
        ///       <see cref="NumC8x8"/>
        ///     </item>
        ///     <item>
        ///       <see cref="ResidualBlockKind"/>
        ///     </item>
        ///   </list>
        /// </remarks>
        public bool DecodeLastSignificantCoefficientFlag()
        {
            int rbk = (int)_residualBlockKind; // Cache casted result as performance benefit

            var inc = rbk switch
            {
                not 3 and not 5 and not 9 and not 13 => _levelListIdx,
                3 => Math.Min(_levelListIdx / _numC8x8, 2),
                _ => LevelListIndicesTable[_levelListIdx].LastSignificantCoeffFlag,
            };

            int ctxIdx = inc + H264Tables.CtxBlockCatToOffsetForSignificantAndLastSignificantCoeffFlag[rbk] + GetLastSignificantCoefficientFlagOffset(rbk);

            return ReadDecision(ref _contextVariables[ctxIdx]);
        }

        /// <summary>
        ///   <para>   <b>coeff_abs_level_minus1</b></para>
        ///   <para>   <b>Type:</b> UEG0, signedValFlag=0, uCoff=14</para>
        ///   <para>   <b>ctxIdxOffset:</b> Prefix: Depends on ctxBlockCat Suffix: none (uses DecodeBypass)</para>
        ///   <para>   <b>Affix:</b> Prefix/suffix</para>
        /// </summary>
        /// <returns>
        ///   Decoded value of the syntax element.
        /// </returns>
        /// <remarks>
        ///   This method requires that the following properties are adjusted accordingly:
        ///   <list type="bullet">
        ///     <item>
        ///       <see cref="ResidualBlockKind"/>
        ///     </item>
        ///     <item>
        ///       <see cref="NumDecodedAbsLevelEqualTo1"/>
        ///     </item>
        ///     <item>
        ///       <see cref="NumDecodedAbsLevelGreaterThan1"/>
        ///     </item>
        ///   </list>
        /// </remarks>
        public int DecodeCoefficientAbsoluteLevelMinus1()
        {
            return DecodeCoeffAbsLevelMinus1Binarization();
        }

        /// <summary>
        ///   <para>   <b>coded_block_flag</b></para>
        ///   <para>   <b>Type:</b> FL, cMax=1</para>
        ///   <para>   <b>ctxIdxOffset:</b> Depends on ctxBlockCat</para>
        ///   <para>   <b>Affix:</b> Prefix only</para>
        /// </summary>
        /// <returns>
        ///   Decoded value of the syntax element.
        /// </returns>
        /// <remarks>
        ///   This method requires that the following properties are adjusted accordingly:
        ///   <list type="bullet">
        ///     <item>
        ///       <see cref="LevelListIndex"/>
        ///     </item>
        ///     <item>
        ///       <see cref="NumC8x8"/>
        ///     </item>
        ///     <item>
        ///       <see cref="ResidualBlockKind"/>
        ///     </item>
        ///   </list>
        /// </remarks>
        /// <param name="codedBlockFlagOptions">
        ///   Parameters for decoding coded_block_flag.
        /// </param>
        public bool DecodeCodedBlockFlag(in H264CodedBlockFlagOptions codedBlockFlagOptions)
        {
            int rbk = (int)_residualBlockKind; // Cache casted result as performance benefit

            int incremental = GetCodedBlockFlagCtxIdx(rbk, in codedBlockFlagOptions);
            int ctxOffset = GetCodedBlockFlagOffset(rbk);
            int blockCat = H264Tables.CtxBlockCatToOffsetForCodedBlockFlag[rbk];

            int ctxIdx = incremental + blockCat + ctxOffset;

            return ReadDecision(ref _contextVariables[ctxIdx]);
        }

        /// <summary>
        ///   <para>   <b>coded_block_flag</b></para>
        ///   <para>   <b>Type:</b> FL, cMax=1</para>
        ///   <para>   <b>ctxIdxOffset:</b> Depends on ctxBlockCat</para>
        ///   <para>   <b>Affix:</b> Prefix only</para>
        /// </summary>
        /// <returns>
        ///   Decoded value of the syntax element.
        /// </returns>
        /// <remarks>
        ///   This method requires that the following properties are adjusted accordingly:
        ///   <list type="bullet">
        ///     <item>
        ///       <see cref="LevelListIndex"/>
        ///     </item>
        ///     <item>
        ///       <see cref="NumC8x8"/>
        ///     </item>
        ///     <item>
        ///       <see cref="ResidualBlockKind"/>
        ///     </item>
        ///   </list>
        /// </remarks>
        /// <param name="codedBlockFlagOptions">
        ///   Parameters for decoding coded_block_flag.
        /// </param>
        public bool DecodeCodedBlockFlag(H264CodedBlockFlagOptions codedBlockFlagOptions) => DecodeCodedBlockFlag(in codedBlockFlagOptions);

        /// <summary>
        ///   <para>   <b>coded_block_pattern</b></para>
        ///   <para>   <b>Type:</b> CBP (FL+TU hybrid)</para>
        ///   <para>   <b>ctxIdxOffset:</b> 73 (Prefix), 77 (Suffix)</para>
        ///   <para>   <b>Affix:</b> Prefix AND suffix</para>
        /// </summary>
        /// <returns>
        ///   Decoded value of the syntax element.
        /// </returns>
        public int DecodeCodedBlockPattern()
        {
            int lumaCBP = 0;
            int chromaCBP = 0;

            BitString8 bs = new();
            int binIdx = 0;

            for (int i = 0; i < 4; i++)
            {
                bool bin = ReadDecision(ref _contextVariables[73 + GetIncrementalContextForCbp(73, binIdx, bs)]);
                bs.AppendBit(bin);
                binIdx++;

                lumaCBP <<= 1;
                lumaCBP |= bin.AsInt32();
            }

            bs = new();
            binIdx = 0;

            for (int i = 0; i < 2; i++)
            {
                bool bin = ReadDecision(ref _contextVariables[73 + GetIncrementalContextForCbp(73, binIdx, bs)]);
                bs.AppendBit(bin);
                binIdx++;

                chromaCBP <<= 1;
                chromaCBP |= bin.AsInt32();

                if (!bin)
                    break;
            }

            return (lumaCBP + 16) * chromaCBP;
        }

        /// <summary>
        ///   <para>   <b>mb_type</b></para>
        ///   <para>   <b>Type:</b> mb_type (dedicated binarization for mb_type; uses custom variable-length codes)</para>
        ///   <para>   <b>ctxIdxOffset:</b> Depends on slice type; depending on slice type, either prefix or prefix+suffix values are coded.</para>
        ///   <para>   <b>Affix:</b> Prefix OR prefix+suffix</para>
        /// </summary>
        /// <returns>
        ///   Decoded value of the syntax element, as well as the slice type for the macroblock itself.
        /// </returns>
        public (H264CabacSliceType sliceType, int mbTypeValue) DecodeMbType()
        {
            if (_sliceType == H264CabacSliceType.I)
            {
                return (H264CabacSliceType.I, ReadMbTypeInISlice());
            }
            else if (_sliceType is H264CabacSliceType.P or H264CabacSliceType.SP)
            {
                return ReadMbTypeInPSpSlice();

            }
            else if (_sliceType is H264CabacSliceType.B)
            {
                return ReadMbTypeInBSlice();
            }
            else if (_sliceType is H264CabacSliceType.SI)
            {
                // Structure of SI macroblocks:
                //   bin1: is this an SI macroblock?
                //     |-> TRUE => nothing continues; macroblock is SI
                //   |--> FALSE => I-slice macroblock data continues; macroblock is Intra

                bool isSISlice = ReadDecision(ref _contextVariables[GetIncrementalContextForMacroblockType(0)]);

                if (isSISlice)
                {
                    return (H264CabacSliceType.SI, 0);
                }
                else
                {
                    return (H264CabacSliceType.I, ReadMbTypeInISlice());
                }
            }

            throw new ArgumentException("Invalid kind of H.264 slice type was provided", nameof(_sliceType));
        }

        /// <summary>
        ///   <para>   <b>ref_idx_l0</b></para>
        ///   <para>   <b>Type:</b> U</para>
        ///   <para>   <b>ctxIdxOffset:</b> 54</para>
        ///   <para>   <b>Affix:</b> Prefix only</para>
        /// </summary>
        /// <returns>
        ///   Decoded value of the syntax element.
        /// </returns>
        /// <remarks>
        ///   This method requires that the following properties are adjusted accordingly:
        ///   <list type="bullet">
        ///     <item>
        ///       <see cref="MacroblockPartitionIndex"/>
        ///     </item>
        ///   </list>
        /// </remarks>
        public int DecodeReferenceIndexL0()
        {
            return RefIdx(false);
        }

        /// <summary>
        ///   <para>   <b>ref_idx_l1</b></para>
        ///   <para>   <b>Type:</b> U</para>
        ///   <para>   <b>ctxIdxOffset:</b> 54</para>
        ///   <para>   <b>Affix:</b> Prefix only</para>
        /// </summary>
        /// <returns>
        ///   Decoded value of the syntax element.
        /// </returns>
        /// <remarks>
        ///   This method requires that the following properties are adjusted accordingly:
        ///   <list type="bullet">
        ///     <item>
        ///       <see cref="MacroblockPartitionIndex"/>
        ///     </item>
        ///   </list>
        /// </remarks>
        public int DecodeReferenceIndexL1()
        {
            return RefIdx(true);
        }

        private int RefIdx(bool l1)
        {
            // Unary binarization, so larger reference indices consume more bins directly.

            // In case of incorrect or malformed CABAC data, unary binarization may result
            // in an infinite loop. It probably won't keep allocating memory, but it will
            // hang the thread, so let's add a limiter to prevent that.

            int binIdx = 0;
            bool lastBin;
            int result = 0;
            do
            {
                if (binIdx == ReferenceIndexUnaryMaximumBins - 1)
                    throw new InvalidOperationException("Too many bins read for U binarization");

                int incrementalCtx;
                if (binIdx == 0)
                    incrementalCtx = GetIncrementalContextForReferenceIndices(l1);
                else
                    incrementalCtx = RefIdxIncrementalContexts[Maths.Clamp(binIdx, 0, 6)]; // Clamp will ensure binIdx maximum value is 6

                int ctxIdx = 54 + incrementalCtx;

                lastBin = ReadDecision(ref _contextVariables[ctxIdx]);

                if (lastBin)
                    result++;

                binIdx++;
            }
            while (lastBin);

            return result;
        }

        private int ReadMbTypeInISlice()
        {
            int binIdx = 0;

            BitString8 bins = new();
            int inc = GetIncrementalContextForMacroblockType(3);

            bool b0 = decision(inc + 3);
            if (!b0)
            {
                return 0;
            }
            else
            {
                bool b1 = ReadTerminate();
                if (b1)
                {
                    return 25;
                }
                else
                {
                    bool b2 = decision(3 + 3);
                    if (!b2)
                    {
                        bool b3 = decision(3 + 4);
                        if (!b3)
                        {
                            // Starting with 1
                            bool b4 = decision(3 + GetIncrementalContextForPreviousBins(bins, 3, binIdx));
                            bool b5 = decision(3 + GetIncrementalContextForPreviousBins(bins, 3, binIdx));

                            return 1 + ((b4.AsInt32() << 1) | b5.AsInt32());
                        }
                        else
                        {
                            // Starting with 5
                            bool b4 = decision(3 + GetIncrementalContextForPreviousBins(bins, 3, binIdx));
                            bool b5 = decision(3 + GetIncrementalContextForPreviousBins(bins, 3, binIdx));
                            bool b6 = decision(3 + 7);

                            return 5 + ((b4.AsInt32() << 2) | (b5.AsInt32() << 1) | b6.AsInt32());
                        }
                    }
                    else
                    {
                        bool b3 = decision(3 + 4);
                        if (!b3)
                        {
                            // Starting with 13
                            bool b4 = decision(3 + GetIncrementalContextForPreviousBins(bins, 3, binIdx));
                            bool b5 = decision(3 + GetIncrementalContextForPreviousBins(bins, 3, binIdx));

                            return 13 + ((b4.AsInt32() << 1) | b5.AsInt32());
                        }
                        else
                        {
                            // Starting with 17
                            bool b4 = decision(3 + GetIncrementalContextForPreviousBins(bins, 3, binIdx));
                            bool b5 = decision(3 + GetIncrementalContextForPreviousBins(bins, 3, binIdx));
                            bool b6 = decision(3 + 7);

                            return 17 + ((b4.AsInt32() << 2) | (b5.AsInt32() << 1) | b6.AsInt32());
                        }
                    }
                }
            }

            bool decision(int ctxIdx)
            {
                bool result = ReadDecision(ref _contextVariables[ctxIdx]);
                bins.AppendBit(result);
                binIdx++;
                return result;
            }
        }

        private (H264CabacSliceType sliceType, int mbTypeValue) ReadMbTypeInPSpSlice()
        {
            int binIdx = 0;

            BitString8 bins = new();

            bool b0 = decision(14);
            if (!b0)
            {
                bool b1 = decision(15);
                bool b2 = decision(14 + GetIncrementalContextForPreviousBins(bins, 14, binIdx));

                return (b1, b2) switch
                {
                    (false, false) => (_sliceType, 0),
                    (true, true) => (_sliceType, 1),
                    (true, false) => (_sliceType, 2),
                    (false, true) => (_sliceType, 3)
                };
            }
            else
            {
                // Suffix part, that's the same as in I slices

                // Just do some resetting first

                binIdx = 0;
                bins = new();

                // Alright, let's go.

                return (H264CabacSliceType.I, GetIntra());

                int GetIntra()
                {
                    bool b0 = decision(17);
                    if (!b0)
                    {
                        return 0;
                    }
                    else
                    {
                        bool b1 = ReadTerminate();
                        if (b1)
                        {
                            return 25;
                        }
                        else
                        {
                            bool b2 = decision(17 + 1);
                            if (!b2)
                            {
                                bool b3 = decision(17 + 2);
                                if (!b3)
                                {
                                    // Starting with 1
                                    bool b4 = decision(17 + GetIncrementalContextForPreviousBins(bins, 17, binIdx));
                                    bool b5 = decision(17 + 3);

                                    return 1 + ((b4.AsInt32() << 1) | b5.AsInt32());
                                }
                                else
                                {
                                    // Starting with 5
                                    bool b4 = decision(17 + GetIncrementalContextForPreviousBins(bins, 17, binIdx));
                                    bool b5 = decision(17 + 3);
                                    bool b6 = decision(17 + 3);

                                    return 5 + ((b4.AsInt32() << 2) | (b5.AsInt32() << 1) | b6.AsInt32());
                                }
                            }
                            else
                            {
                                bool b3 = decision(17 + 2);
                                if (!b3)
                                {
                                    // Starting with 13
                                    bool b4 = decision(17 + GetIncrementalContextForPreviousBins(bins, 17, binIdx));
                                    bool b5 = decision(17 + 3);

                                    return 13 + ((b4.AsInt32() << 1) | b5.AsInt32());
                                }
                                else
                                {
                                    // Starting with 17
                                    bool b4 = decision(17 + GetIncrementalContextForPreviousBins(bins, 17, binIdx));
                                    bool b5 = decision(17 + 3);
                                    bool b6 = decision(17 + 3);

                                    return 17 + ((b4.AsInt32() << 2) | (b5.AsInt32() << 1) | b6.AsInt32());
                                }
                            }
                        }
                    }
                }
            }

            bool decision(int ctxIdx)
            {
                bool result = ReadDecision(ref _contextVariables[ctxIdx]);
                bins.AppendBit(result);
                binIdx++;
                return result;
            }
        }

        private (H264CabacSliceType sliceType, int mbTypeValue) ReadMbTypeInBSlice()
        {
            int binIdx = 0;

            BitString8 bins = new();

            int b0 = decision(27 + GetIncrementalContextForMacroblockType(27));
            if (b0 == 0)
            {
                return (H264CabacSliceType.B, 0);
            }
            else
            {
                int b1 = decision(27 + 3);
                if (b1 == 0) return (H264CabacSliceType.B, decision(27 + GetIncrementalContextForPreviousBins(bins, 27, binIdx)) + 1);
                else
                {
                    int b2 = decision(27 + GetIncrementalContextForPreviousBins(bins, 27, binIdx)) + 1;
                    int b3 = decision(27 + 5);
                    int b4 = decision(27 + 5);
                    int b5 = decision(27 + 5);

                    if (b0 == 1 && b1 == 1 && b2 == 1 && b3 == 1 && b4 == 1 && b5 == 0) return (H264CabacSliceType.B, 11);

                    if (b2 == 1)
                    {
                        if (b3 == 1 && b5 == 1)
                        {
                            if (b4 == 1) return (H264CabacSliceType.B, 22);
                            else
                            {
                                // Special case: 1 1 1 1 0  1 
                                // In this context, 111101 is the prefix.
                                // Following it is the same mb_type binarization, except, it's I slice. It's
                                // the suffix value.
                                //
                                // Finally, the result is the suffix.
                                //
                                // Note: We can't just directly call the Islice reader.
                                // The suffix part, although returns Islice, derives
                                // based on different context indices.

                                binIdx = 0;
                                bins = new();

                                bool b0B = decisionB(32);
                                if (!b0B)
                                {
                                    return (H264CabacSliceType.I, 0);
                                }
                                else
                                {
                                    bool b1B = ReadTerminate();
                                    if (b1B)
                                    {
                                        return (H264CabacSliceType.I, 25);
                                    }
                                    else
                                    {
                                        bool b2B = decisionB(32 + 1);
                                        if (!b2B)
                                        {
                                            bool b3B = decisionB(32 + 2);
                                            if (!b3B)
                                            {
                                                // Starting with 1
                                                bool b4B = decisionB(32 + GetIncrementalContextForPreviousBins(bins, 32, binIdx));
                                                bool b5B = decisionB(32 + 3);

                                                return (H264CabacSliceType.I, 1 + ((b4B.AsInt32() << 1) | b5B.AsInt32()));
                                            }
                                            else
                                            {
                                                // Starting with 5
                                                bool b4B = decisionB(32 + GetIncrementalContextForPreviousBins(bins, 32, binIdx));
                                                bool b5B = decisionB(32 + 3);
                                                bool b6B = decisionB(32 + 3);

                                                return (H264CabacSliceType.I, 5 + ((b4B.AsInt32() << 2) | (b5B.AsInt32() << 1) | b6B.AsInt32()));
                                            }
                                        }
                                        else
                                        {
                                            bool b3B = decisionB(32 + 2);
                                            if (!b3B)
                                            {
                                                // Starting with 13
                                                bool b4B = decisionB(32 + GetIncrementalContextForPreviousBins(bins, 32, binIdx));
                                                bool b5B = decisionB(32 + 3);

                                                return (H264CabacSliceType.I, 13 + ((b4B.AsInt32() << 1) | b5B.AsInt32()));
                                            }
                                            else
                                            {
                                                // Starting with 17
                                                bool b4B = decisionB(32 + GetIncrementalContextForPreviousBins(bins, 32, binIdx));
                                                bool b5B = decisionB(32 + 3);
                                                bool b6B = decisionB(32 + 3);

                                                return (H264CabacSliceType.I, 17 + ((b4B.AsInt32() << 2) | (b5B.AsInt32() << 1) | b6B.AsInt32()));
                                            }
                                        }
                                    }
                                }
                            }
                        }

                        int b6 = decision(27 + 5);

                        return (H264CabacSliceType.B, 12 + ((b3 << 3) | (b4 << 2) | (b5 << 1) | b6));
                    }
                    else
                    {
                        return (H264CabacSliceType.B, 3 + ((b3 << 2) | (b4 << 1) | b5));
                    }
                }
            }

            int decision(int ctxIdx)
            {
                bool result = ReadDecision(ref _contextVariables[ctxIdx]);
                bins.AppendBit(result);
                binIdx++;
                return result.AsInt32();
            }

            bool decisionB(int ctxIdx)
            {
                return decision(ctxIdx).AsBoolean();
            }
        }

        private int GetIncrementalContextForMacroblockType(int ctxIdxOffset)
        {
            _macroblockProvider.DeriveNeighboringMacroblocks(
                _macroblockProvider.CurrMbAddr,
                out H264CabacMacroblockWithAvailability a,
                out H264CabacMacroblockWithAvailability b);

            return ((!a.Availability || ctxIdxOffset switch
                {
                    0 => a.Descriptor.ExactType == SI,
                    3 => a.Descriptor.ExactType == I_NxN,
                    27 => a.Descriptor.ExactType is B_Skip or B_Direct_16x16,
                    _ => false
                }) ? 0 : 1) + ((!b.Availability || ctxIdxOffset switch
                {
                    0 => b.Descriptor.ExactType == SI,
                    3 => b.Descriptor.ExactType == I_NxN,
                    27 => b.Descriptor.ExactType is B_Skip or B_Direct_16x16,
                    _ => false
                }) ? 0 : 1);
        }

        private static int GetIncrementalContextForPreviousBins(BitString8 /* don't prefix with 'in', see below why */ bitString, int ctxIdxOffset, int binIdx)
        {
            // Don't prefix with 'in'
            // because BitString8 is a struct, and it's only 2 bytes. But passing it by 'in' would
            // actually create an 8 byte pointer, which is less efficient. so it's more efficient
            // to just copy the 2 bytes.

            if (ctxIdxOffset == 3)
            {
                // Macroblock type for I slices

                if (binIdx == 4)
                {
                    // Fifth bin

                    return bitString[3] ? 5 : 6;
                }
                else /* 5 */
                {
                    // Sixth bin

                    return bitString[3] ? 6 : 7;
                }
            }
            else if (ctxIdxOffset == 14)
            {
                // Macroblock type in P/SP slices

                // NOTE: Only binIdx=2 is expected here... maybe we should add validation?

                return !bitString[1] ? 2 : 3;
            }
            else if (ctxIdxOffset == 17)
            {
                // Macroblock type in P/SP slices (Suffix part)

                // NOTE: Only binIdx=4 is expected here... maybe we should add validation?

                return bitString[3] ? 2 : 3;
            }
            else if (ctxIdxOffset == 27)
            {
                // Macroblock type in B slices

                // NOTE: Only binIdx=2 is expected here... maybe we should add validation?

                return bitString[1] ? 4 : 5;
            }
            else if (ctxIdxOffset == 32)
            {
                // Macroblock type in B slices (Suffix part)

                // NOTE: Only binIdx=4 is expected here... maybe we should add validation?

                return bitString[3] ? 2 : 3;
            }
            else if (ctxIdxOffset == 36)
            {
                // Sub macroblock type in B slices

                // NOTE: Only binIdx=2 is expected here... maybe we should add validation?

                return bitString[1] ? 2 : 3;
            }
            else
            {
                throw new InvalidOperationException($"ctxIdxOffset={ctxIdxOffset} is not valid");
            }
        }

        private unsafe int GetIncrementalContextForReferenceIndices(bool l1)
        {
            H264CabacMacroblockDescriptor currMB = ForceGetMacroblockByAddress(_macroblockProvider.CurrMbAddr);

            int* ref_idx_lX_ptr = l1 ? currMB.L1ReferenceIndices : currMB.L0ReferenceIndices;
            H264MacroblockPredictionMode Pred_LX = l1 ? H264MacroblockPredictionMode.L1 : H264MacroblockPredictionMode.L0;

            // ref_idx_lX_ptr wrapped in Span so that if we accidentally access an
            // out of bounds value it doesn't generate a SIGSEGV/stack buffer overrun
            Span<int> ref_idx_lX = new(ref_idx_lX_ptr, 16);

            _macroblockProvider.DeriveNeighboringPartitions(
                mbPartIdx: _mbPartIdx,
                currSubMbType: currMB.SubMbType[_mbPartIdx],
                subMbPartIdx: 0,
                out H264CabacMacroblockWithAvailabilityAndPartitionIndices a,
                out H264CabacMacroblockWithAvailabilityAndPartitionIndices b,
                out _,
                out _);

            bool currentMacroblockIsFrame = currMB.MbaffCoding == H264MacroblockMbaffCoding.Frame;

            bool refIdxZeroFlagA =
                (a.Availability.Descriptor.MbaffFrameFlag && currentMacroblockIsFrame && a.Availability.Descriptor.MbaffCoding == H264MacroblockMbaffCoding.Field
                ? ((ref_idx_lX[a.MbPartIdx] > 1) ? 0 : 1)
                : ((ref_idx_lX[a.MbPartIdx] > 0) ? 0 : 1)).AsBoolean();

            bool refIdxZeroFlagB =
                (b.Availability.Descriptor.MbaffFrameFlag && currentMacroblockIsFrame && b.Availability.Descriptor.MbaffCoding == H264MacroblockMbaffCoding.Field
                ? ((ref_idx_lX[b.MbPartIdx] > 1) ? 0 : 1)
                : ((ref_idx_lX[b.MbPartIdx] > 0) ? 0 : 1)).AsBoolean();

            int predModeEqualFlagA = GetPredModeEqualFlag(in a);
            int predModeEqualFlagB = GetPredModeEqualFlag(in b);

            int condTermFlagA = GetCondTermFlag(in a, predModeEqualFlagA, refIdxZeroFlagA.AsInt32());
            int condTermFlagB = GetCondTermFlag(in b, predModeEqualFlagB, refIdxZeroFlagB.AsInt32());

            return condTermFlagA + 2 * condTermFlagB;

            int GetPredModeEqualFlag(in H264CabacMacroblockWithAvailabilityAndPartitionIndices n)
            {
                if (n.Availability.Descriptor.ExactType is B_Direct_16x16 or B_Skip)
                    return 0;

                if (n.Availability.Descriptor.ExactType is P_8x8 or B_8x8)
                {
                    H264MacroblockPredictionMode subMbPredMode = _macroblockProvider.SubMbPredMode(n.Availability.Descriptor.Address, n.Availability.Descriptor.SubMbType[n.MbPartIdx]);

                    return (subMbPredMode != Pred_LX && subMbPredMode != H264MacroblockPredictionMode.BiPred) ? 0 : 1;
                }
                else
                {
                    H264MacroblockPredictionMode mbPartPredMode = _macroblockProvider.MbPartPredMode(in n.Availability.Descriptor, n.MbPartIdx);

                    return (mbPartPredMode != Pred_LX && mbPartPredMode != H264MacroblockPredictionMode.BiPred) ? 0 : 1;
                }
            }

            int GetCondTermFlag(in H264CabacMacroblockWithAvailabilityAndPartitionIndices n, int predModeEqualFlagN, int refIdxZeroFlagN)
            {
                if (!n.Availability.Availability ||
                    n.Availability.Descriptor.ExactType is P_Skip or B_Skip ||
                    n.Availability.Descriptor.PredictionCoding == H264MacroblockPredictionCoding.Intra ||
                    predModeEqualFlagN == 0 ||
                    refIdxZeroFlagN == 1)
                    return 0;

                return 1;
            }
        }

        private int GetIncrementalContextForCbp(int offset, int binIdx, BitString8 prevBins)
        {
            if (offset == 73)
            {
                _macroblockProvider.DeriveNeighboring8x8LumaBlocks(
                    _macroblockProvider.CurrMbAddr,
                    binIdx,
                    out H264CabacAddressAndBlockIndices a,
                    out H264CabacAddressAndBlockIndices b);

                return GetCondTermFlagN73(in a) + 2 * GetCondTermFlagN73(in b);

                int GetCondTermFlagN73(in H264CabacAddressAndBlockIndices n)
                {
                    // I know this is super messy so please just refer to the spec
                    // if you want to understand how it works. xD
                    //
                    // Rec. ITU-T H.264 2024/08 V15, page 287, clause 9.3.3.1.1.4

                    return (!n.Address.Availability ||
                        n.Address.Descriptor.ExactType == I_PCM ||
                        (n.Address.Descriptor.Address != _macroblockProvider.CurrMbAddr &&
                          n.Address.Descriptor.ExactType is not P_Skip and not B_Skip &&
                          (n.Address.Descriptor.GetLumaCbp() >> n.BlockIndex) != 0) ||
                        (n.Address.Descriptor.Address == _macroblockProvider.CurrMbAddr &&
                         prevBins.GetSafeBit(n.BlockIndex))) ? 0 : 1;
                }
            }
            else
            {
                _macroblockProvider.DeriveNeighboringMacroblocks(
                    _macroblockProvider.CurrMbAddr,
                    out H264CabacMacroblockWithAvailability a,
                    out H264CabacMacroblockWithAvailability b);

                return GetCondTermFlagN77(in a) + 2 * GetCondTermFlagN77(in b) + ((binIdx == 1) ? 4 : 0);

                int GetCondTermFlagN77(in H264CabacMacroblockWithAvailability n)
                {
                    if (n.Availability && n.Descriptor.ExactType is I_PCM)
                        return 1;

                    if (!n.Availability ||
                        n.Descriptor.ExactType is P_Skip or B_Skip ||
                        (binIdx == 0 && n.Descriptor.GetChromaCbp() == 0) ||
                        (binIdx == 1 && n.Descriptor.GetChromaCbp() == 2))
                    {
                        return 0;
                    }

                    return 1;
                }
            }
        }

        /// <summary>
        ///   Decodes binarization for mvd_lX.
        /// </summary>
        /// <param name="ctxIdxIncForMVD">The initial ctxIdxInc for mvd_lX.</param>
        /// <param name="listIndex">true = decoding mvd_l1; false = decoding mvd_l0</param>
        /// <returns>The resulting binarization for the MVD syntax element.</returns>
        private int DecodeMvdBinarization(int ctxIdxIncForMVD, bool listIndex)
        {
            const int uCoff = 9; // uCoff (Coff starts for Cutoff) for mvd_lX is always 9
            const bool signedValFlag = true; // mvd_lX is always signed
            const int k = 3; // k for mvd_lX is always 3

            int binIdx = 0;

            int prefix = 0;
            bool lastBin;
            do
            {
                lastBin = ReadDecision(ref _contextVariables[(listIndex ? 47 : 40) + GetMvdCtxIdxInc(binIdx++, ctxIdxIncForMVD)]);
                if (lastBin)
                    prefix++;
            }
            while (prefix < uCoff && lastBin);

            return signedValFlag ? Map(prefix + ReadEgkBypass(k)) : prefix + ReadEgkBypass(k);
        }

        /// <summary>
        ///   Decodes binarization for coeff_abs_level_minus1.
        /// </summary>
        /// <returns>The resulting binarization for the MVD syntax element.</returns>
        private int DecodeCoeffAbsLevelMinus1Binarization()
        {
            const int uCoff = 14; // uCoff (Coff starts for Cutoff) for coeff_abs_level_minus1 is always 9
            const bool signedValFlag = false; // coeff_abs_level_minus1 is indeed signed, but it's the coeff_sign_flag's job to provide the sign
            const int k = 0; // k for coeff_abs_level_minus1 is always 0

            int prefix = 0;
            int binIdx = 0;

            int ctxIdx = 0;

            bool lastBin;
            do
            {
                if (binIdx == 0)
                    ctxIdx = ((_numDecodAbsLevelGt1 != 0) ? 0 : Math.Min(4, 1 + _numDecodAbsLevelEq1));

                lastBin = ReadDecision(ref _contextVariables[ctxIdx]);
                binIdx++;

                if (lastBin)
                    prefix++;

                if (binIdx == 1)
                    ctxIdx = 5 + Math.Min(4 - (((int)_residualBlockKind == 3) ? 1 : 0), _numDecodAbsLevelGt1);
            }
            while (prefix < uCoff && lastBin);

            return signedValFlag ? Map(prefix + ReadEgkBypass(k)) : prefix + ReadEgkBypass(k);
        }

        /// <summary>
        ///   Reads either the syntax element rem_intra_4x4_pred_mode or rem_intra_8x8_pred_mode.
        ///   Parsing of both syntax elements directly invoke this method, and parsing process
        ///   for both syntax elements is identical, including context indices.
        /// </summary>
        /// <returns>
        ///   Value of either rem_intra_4x4_pred_mode or rem_intra_8x8_pred_mode, depending on
        ///   which syntax element is being parsed.
        /// </returns>
        private int RemainingIntraNxNPredMode()
        {
            ref H264ContextVariable contextVariableRef = ref _contextVariables[69];
            return (ReadDecision(ref contextVariableRef).AsInt32() * 4)
                + (ReadDecision(ref contextVariableRef).AsInt32() * 2)
                + ReadDecision(ref contextVariableRef).AsInt32();
        }

        /// <summary>
        ///   Computes the variable ctxIdxInc for transform_size_8x8_flag using neighboring macroblocks.
        /// </summary>
        /// <returns>The variable ctxIdxInc for the syntax element transform_size_8x8_flag.</returns>
        private int GetIncrementalContextForTransformSize8x8Flag()
        {
            _macroblockProvider.DeriveNeighboringMacroblocks(_macroblockProvider.CurrMbAddr, out var a, out var b);
            return ((!a.Availability || !a.Descriptor.TransformSize8x8Flag) ? 0 : 1) + ((!b.Availability || !b.Descriptor.TransformSize8x8Flag) ? 0 : 1);
        }

        /// <summary>
        ///   Computes the variable ctxIdxInc for mb_skip_flag using neighboring macroblocks.
        /// </summary>
        /// <returns>The variable ctxIdxInc for the syntax element mb_skip_flag.</returns>
        private int GetIncrementalContextForSkipFlag()
        {
            this._macroblockProvider.DeriveNeighboringMacroblocks(this._macroblockProvider.CurrMbAddr, out var a, out var b);
            return ((!a.Availability || a.Descriptor.SkipFlag) ? 0 : 1) + ((!b.Availability || b.Descriptor.SkipFlag) ? 0 : 1);
        }

        /// <summary>
        ///   Computes the variable ctxIdxInc for mvd_lX using neighboring macroblocks.
        /// </summary>
        /// <param name="l1Flag">true - decoding mvd_l1; false - decoding mvd_l0</param>
        /// <param name="mbPartIdx">The macroblock partitioning index.</param>
        /// <param name="subMbPartIdx">The sub-macroblock partitioning index.</param>
        /// <returns>The variable ctxIdxInc for the syntax element mvd_lX.</returns>
        private unsafe int GetIncrementalContextForMvd(bool l1Flag, int mbPartIdx = 0, int subMbPartIdx = 0)
        {
            // Unsafe because we're accessing a fixed-size buffer inside H264CabacMacroblockDescriptor.

            H264CabacMacroblockDescriptor currentMacroblock = ForceGetMacroblockByAddress(_macroblockProvider.CurrMbAddr);

            int ctxIdxOffset = l1Flag ? 47 : 40;
            H264MacroblockPredictionMode Pred_LX = l1Flag ? H264MacroblockPredictionMode.L1 : H264MacroblockPredictionMode.L0;
            ref H264CabacMvd mvd_lX = ref l1Flag ? ref currentMacroblock.MvdL1 : ref currentMacroblock.MvdL0;
            int compIdx = l1Flag.AsInt32();

            this._macroblockProvider.DeriveNeighboringPartitions(mbPartIdx, currentMacroblock.SubMbType[mbPartIdx], subMbPartIdx,
                out var a, out var b,
                out _, out _);

            int predModeEqualFlagA = GetPredModeEqualFlag(in a, in currentMacroblock);
            int predModeEqualFlagB = GetPredModeEqualFlag(in b, in currentMacroblock);

            int absMvdCompA = GetAbsMvdComp(in a, predModeEqualFlagA, in currentMacroblock);
            int absMvdCompB = GetAbsMvdComp(in b, predModeEqualFlagB, in currentMacroblock);

            if ((absMvdCompA > 32 || absMvdCompB > 32) || (absMvdCompA + absMvdCompB > 32))
                return 2;
            else if (absMvdCompA + absMvdCompB > 2)
                return 1;
            else
                return 0;

            int GetPredModeEqualFlag(in H264CabacMacroblockWithAvailabilityAndPartitionIndices mbX, in H264CabacMacroblockDescriptor thisMB)
            {
                if (mbX.Availability.Descriptor.ExactType is B_Direct_16x16 or B_Skip)
                    return 0;

                if (mbX.Availability.Descriptor.ExactType is P_8x8 or B_8x8)
                {
                    var subMbPredMode = _macroblockProvider.SubMbPredMode(_macroblockProvider.CurrMbAddr, mbX.Availability.Descriptor.SubMbType[mbX.MbPartIdx]);
                    return (subMbPredMode != Pred_LX && subMbPredMode != H264MacroblockPredictionMode.BiPred) ? 0 : 1;
                }

                var mbppm = _macroblockProvider.MbPartPredMode(in mbX.Availability.Descriptor, mbX.MbPartIdx);
                return (mbppm is not H264MacroblockPredictionMode.BiPred && mbppm != Pred_LX) ? 0 : 1;
            }

            int GetAbsMvdComp(in H264CabacMacroblockWithAvailabilityAndPartitionIndices mbX, int predModeEqualFlagN, in H264CabacMacroblockDescriptor thisMB)
            {
                if (!mbX.Availability.Availability ||
                    mbX.Availability.Descriptor.ExactType is H264MacroblockType.P_Skip or B_Skip ||
                    mbX.Availability.Descriptor.PredictionCoding is H264MacroblockPredictionCoding.Intra ||
                    predModeEqualFlagN == 0)
                    return 0;

                if (compIdx == 1 &&
                    mbX.Availability.Descriptor.MbaffFrameFlag &&
                    thisMB.MbaffCoding == H264MacroblockMbaffCoding.Frame &&
                    mbX.Availability.Descriptor.MbaffCoding == H264MacroblockMbaffCoding.Field)
                    return Math.Abs(thisMB.MvdL0[mbX.MbPartIdx, mbX.SubMbPartIdx, compIdx]) * 2;

                if (compIdx == 1 &&
                    mbX.Availability.Descriptor.MbaffFrameFlag &&
                    thisMB.MbaffCoding == H264MacroblockMbaffCoding.Field &&
                    mbX.Availability.Descriptor.MbaffCoding == H264MacroblockMbaffCoding.Frame)
                    return Math.Abs(thisMB.MvdL0[mbX.MbPartIdx, mbX.SubMbPartIdx, compIdx]) / 2;

                return Math.Abs(thisMB.MvdL0[mbX.MbPartIdx, mbX.SubMbPartIdx, compIdx]);
            }
        }

        /// <summary>
        ///   Computes and returns ctxIdxOffset for the syntax element significant_coeff_flag.
        /// </summary>
        /// <param name="residualBlockKind"><see cref="H264ResidualBlockKind"/>, casted into an <see cref="int"/>.</param>
        /// <returns>ctxIdxOffset.</returns>
        private int GetSignificantCoefficientFlagOffset(int residualBlockKind)
        {
            int factor = GetBlockKindFactor(residualBlockKind);
            return SignificantCoeffFlagOffsetFromBlockFactors[factor];
        }

        /// <summary>
        ///   Computes and returns ctxIdxOffset for the syntax element last_significant_coeff_flag.
        /// </summary>
        /// <param name="residualBlockKind"><see cref="H264ResidualBlockKind"/>, casted into an <see cref="int"/>.</param>
        /// <returns>ctxIdxOffset.</returns>
        private int GetLastSignificantCoefficientFlagOffset(int residualBlockKind)
        {
            int factor = GetBlockKindFactor(residualBlockKind);
            return LastSignificantCoeffFlagOffsetFromBlockFactors[factor];
        }

        /// <summary>
        ///   Computes and returns ctxIdxOffset for the syntax element coded_block_flag.
        /// </summary>
        /// <param name="residualBlockKind"><see cref="H264ResidualBlockKind"/>, casted into an <see cref="int"/>.</param>
        /// <returns>ctxIdxOffset.</returns>
        private int GetCodedBlockFlagOffset(int residualBlockKind)
        {
            int factor = GetBlockKindFactor(residualBlockKind);
            return CodedBlockFlagOffsetFromBlockFactors[factor];
        }

        /// <summary>
        ///   Computes and returns ctxIdxOffset for the syntax element coeff_abs_level_minus1.
        /// </summary>
        /// <param name="residualBlockKind"><see cref="H264ResidualBlockKind"/>, casted into an <see cref="int"/>.</param>
        /// <returns>ctxIdxOffset.</returns>
        private int GetCoeffAbsLevelMinus1Offset(int residualBlockKind)
        {
            int factor = GetBlockKindFactor(residualBlockKind);
            return CoeffAbsLevelMinus1OffsetFromBlockFactors[factor];
        }

        /// <summary>
        ///   Transforms the residual block kind into the block kind factor, which is a type of ArithmeticCoding's
        ///   custom optimization algorithm.
        /// </summary>
        /// <param name="residualBlockKind"><see cref="H264ResidualBlockKind"/>, casted into an <see cref="int"/>.</param>
        /// <returns>The block kind factor.</returns>
        private int GetBlockKindFactor(int residualBlockKind)
        {
            // The macroblock that we're parsing right now.
            H264CabacMacroblockDescriptor currentMacroblock = ForceGetMacroblockByAddress(_macroblockProvider.CurrMbAddr);

            bool isFrameMacroblock = currentMacroblock.MbaffCoding == H264MacroblockMbaffCoding.Frame;
            bool isFieldMacroblock = currentMacroblock.MbaffCoding == H264MacroblockMbaffCoding.Field;

            return GetBlockKindFactor(residualBlockKind, isFieldMacroblock, isFrameMacroblock);
        }

        /// <summary>
        ///   Transforms the residual block kind into the block kind factor, which is a type of ArithmeticCoding's
        ///   custom optimization algorithm.
        /// </summary>
        /// <param name="residualBlockKind"><see cref="H264ResidualBlockKind"/>, casted into an <see cref="int"/>.</param>
        /// <param name="isFieldMacroblock">Is this current macroblock an MBAFF field macroblock?</param>
        /// <param name="isFrameMacroblock">Is this current macroblock an MBAFF frame macroblock?</param>
        /// <returns>The block kind factor.</returns>
        private static int GetBlockKindFactor(int residualBlockKind, bool isFieldMacroblock, bool isFrameMacroblock)
        {
            // If 1, MBAFF coding is disabled. (Only true if this is neither a field nor frame macroblock.)
            bool nonMBAFF = !isFrameMacroblock && !isFieldMacroblock;

            int baseCase;

            if (residualBlockKind < 5) baseCase = 1;
            else if (residualBlockKind == 5) baseCase = 2;
            else if (residualBlockKind > 5 && residualBlockKind < 9) baseCase = 3;
            else if (residualBlockKind > 9 && residualBlockKind < 13) baseCase = 4;
            else if (residualBlockKind == 9) baseCase = 5;
            else if (residualBlockKind == 13) baseCase = 6;
            else baseCase = 0;

            if (nonMBAFF) return baseCase;
            else if (!isFieldMacroblock && isFrameMacroblock) return baseCase + 6;
            else if (!isFrameMacroblock && isFieldMacroblock) return baseCase + 12;
            else return 0;
        }

        /// <summary>
        ///   Ensures that the macroblock with address <paramref name="mbAddr"/> is always returned.
        /// </summary>
        /// <param name="mbAddr">The address of the macroblock to retrieve.</param>
        /// <returns>A macroblock with address <paramref name="mbAddr"/>. If unavailable, <see cref="InvalidOperationException"/> is thrown.</returns>
        /// <exception cref="InvalidOperationException"></exception>
        private H264CabacMacroblockDescriptor ForceGetMacroblockByAddress(int mbAddr)
        {
            if (!_macroblockProvider.TryGetMacroblock(mbAddr, out var mb))
            {
                throw new InvalidOperationException($"Macroblock at address {mbAddr} is not available.");
            }
            return mb;
        }

        /// <summary>
        ///   Reads the syntax elements prev_intra_4x4_pred_mode_flag OR prev_intra_8x8_pred_mode_flag.
        /// </summary>
        /// <returns>
        ///   The result of either of those syntax elements.
        /// </returns>
        private bool PrevIntraNxNPredModeFlag() => ReadDecision(ref _contextVariables[68]);

        /// <summary>
        ///   Accesses a context variable by its index.
        /// </summary>
        /// <param name="i"></param>
        /// <returns></returns>
        public H264ContextVariable this[int i]
        {
            get => _contextVariables[i];
            set => _contextVariables[i] = value;
        }

        private static int GetMvdCtxIdxInc(int binIdx, int ctxIdxIncForMVD)
        {
            // Thanks, GitHub Copilot! :-)

            if (binIdx < 1)
            {
                return ctxIdxIncForMVD;
            }
            else if (binIdx < MvdPrefixCtxIdxIncMap.Length)
            {
                return MvdPrefixCtxIdxIncMap[binIdx];
            }
            else
            {
                return 6;
            }
        }

        private int GetCodedBlockFlagCtxIdx(int ctxBlockCat, in H264CodedBlockFlagOptions cbfOptions)
        {
            H264CabacMacroblockDescriptor currMb = ForceGetMacroblockByAddress(_macroblockProvider.CurrMbAddr);

            H264CabacMacroblockWithAvailability mbAddrA;
            H264CabacMacroblockWithAvailability mbAddrB;

            bool? transBlockA;
            bool? transBlockB;

            DeriveTransBlocks(in cbfOptions);

            return GetCondTermFlag(transBlockA, mbAddrA) + 2 * GetCondTermFlag(transBlockB, mbAddrB);

            int GetCondTermFlag(bool? transBlockN, H264CabacMacroblockWithAvailability mbAddrN)
            {
                if (!mbAddrN.Availability && currMb.PredictionCoding == H264MacroblockPredictionCoding.Inter) return 0;
                if (mbAddrN.Availability && transBlockN == null && mbAddrN.Descriptor.ExactType != I_PCM) return 0;
                if (currMb.PredictionCoding == H264MacroblockPredictionCoding.Intra && _macroblockProvider.PpsConstrainedIntraPredFlag &&
                    mbAddrN.Availability && mbAddrN.Descriptor.PredictionCoding == H264MacroblockPredictionCoding.Inter &&
                    _macroblockProvider.CurrentNalUnitType is >= 2 and <= 4) return 0;

                if (!mbAddrN.Availability && currMb.PredictionCoding == H264MacroblockPredictionCoding.Intra) return 1;
                if (mbAddrN.Descriptor.ExactType == I_PCM) return 1;

                return transBlockN.GetValueOrDefault().AsInt32();
            }

            void DeriveTransBlocks(in H264CodedBlockFlagOptions options)
            {
                if (ctxBlockCat is 0 or 6 or 10)
                {
                    _macroblockProvider.DeriveNeighboringMacroblocks(
                        _macroblockProvider.CurrMbAddr,
                        out H264CabacMacroblockWithAvailability a,
                        out H264CabacMacroblockWithAvailability b);

                    mbAddrA = a;
                    mbAddrB = b;

                    transBlockA = GetTransBlock(a);
                    transBlockB = GetTransBlock(b);

                    bool? GetTransBlock(H264CabacMacroblockWithAvailability n)
                    {
                        if (!n.Availability) return null;
                        ref H264CabacMacroblockDescriptor mb = ref n.Descriptor;

                        if (!mb.Residual.IsResidualPresent) return null;

                        if (_macroblockProvider.MbPartPredMode(in mb, 0) == H264MacroblockPredictionMode.Intra16x16)
                        {
                            if (ctxBlockCat == 0) return mb.Residual.Intra16x16DCLevel;
                            else if (ctxBlockCat == 6) return mb.Residual.CbIntra16x16DCLevel;
                            else return mb.Residual.CrIntra16x16DCLevel;
                        }
                        else
                        {
                            return null;
                        }
                    }
                }
                else if (ctxBlockCat is 1 or 2)
                {
                    _macroblockProvider.DeriveNeighboring4x4LumaBlocks(
                        _macroblockProvider.CurrMbAddr,
                        out H264CabacAddressAndBlockIndices a,
                        out H264CabacAddressAndBlockIndices b);

                    mbAddrA = a.Address;
                    mbAddrB = b.Address;

                    transBlockA = GetTransBlock(a);
                    transBlockB = GetTransBlock(b);

                    bool? GetTransBlock(H264CabacAddressAndBlockIndices n)
                    {
                        if (!n.Address.Availability) return null;

                        var mb = n.Address.Descriptor;

                        if (!mb.Residual.IsResidualPresent) return null;

                        if (!(mb.ExactType == P_Skip || mb.ExactType == B_Skip || mb.ExactType == I_PCM) &&
                            ((mb.GetLumaCbp() >> (n.BlockIndex >> 2)) & 1) != 0 &&
                            !mb.TransformSize8x8Flag)
                            return mb.Residual.LumaLevel4x4[n.BlockIndex];
                        else if (!(mb.ExactType == P_Skip || mb.ExactType == B_Skip) &&
                            ((mb.GetLumaCbp() >> (n.BlockIndex >> 2)) & 1) != 0 &&
                            mb.TransformSize8x8Flag)
                            return mb.Residual.LumaLevel8x8[n.BlockIndex >> 2];
                        else
                            return null;
                    }
                }
                else if (ctxBlockCat == 3)
                {
                    _macroblockProvider.DeriveNeighboringMacroblocks(
                        _macroblockProvider.CurrMbAddr,
                        out H264CabacMacroblockWithAvailability a,
                        out H264CabacMacroblockWithAvailability b);

                    mbAddrA = a;
                    mbAddrB = b;

                    transBlockA = GetTransBlock(a, in options);
                    transBlockB = GetTransBlock(b, in options);

                    unsafe bool? GetTransBlock(H264CabacMacroblockWithAvailability n, in H264CodedBlockFlagOptions options)
                    {
                        if (!n.Availability) return null;

                        ref var mb = ref n.Descriptor;

                        if (!mb.Residual.IsResidualPresent) return null;

                        if (!(mb.ExactType == P_Skip || mb.ExactType == B_Skip || mb.ExactType == I_PCM) &&
                            (mb.GetChromaCbp() != 0))
                            return mb.Residual.ChromaDCLevel[options.ICbCr];

                        return null;
                    }
                }
                else if (ctxBlockCat == 4)
                {
                    _macroblockProvider.DeriveNeighboring4x4ChromaBlocks(
                        _macroblockProvider.CurrMbAddr,
                        options.Chroma4x4BlkIdx,
                        out H264CabacAddressAndBlockIndices a,
                        out H264CabacAddressAndBlockIndices b);

                    mbAddrA = a.Address;
                    mbAddrB = b.Address;

                    transBlockA = GetTransBlock(a, in options);
                    transBlockB = GetTransBlock(b, in options);

                    bool? GetTransBlock(H264CabacAddressAndBlockIndices n, in H264CodedBlockFlagOptions options)
                    {
                        if (!n.Address.Availability) return null;

                        var mb = n.Address.Descriptor;

                        if (!mb.Residual.IsResidualPresent) return null;

                        if (!(mb.ExactType == P_Skip || mb.ExactType == B_Skip || mb.ExactType == I_PCM) &&
                            (mb.GetChromaCbp() == 2))
                            return options.ICbCr > 0
                                ? mb.Residual.CrLevel4x4[n.BlockIndex]
                                : mb.Residual.CbLevel4x4[n.BlockIndex];

                        return null;
                    }
                }
                else if (ctxBlockCat == 5)
                {
                    _macroblockProvider.DeriveNeighboring8x8LumaBlocks(
                        _macroblockProvider.CurrMbAddr,
                        options.Luma8x8BlkIdx,
                        out H264CabacAddressAndBlockIndices a,
                        out H264CabacAddressAndBlockIndices b);

                    mbAddrA = a.Address;
                    mbAddrB = b.Address;

                    transBlockA = GetTransBlock(a);
                    transBlockB = GetTransBlock(b);

                    bool? GetTransBlock(H264CabacAddressAndBlockIndices n)
                    {
                        if (!n.Address.Availability) return null;

                        var mb = n.Address.Descriptor;

                        if (!mb.Residual.IsResidualPresent) return null;

                        if (!(mb.ExactType == P_Skip || mb.ExactType == B_Skip || mb.ExactType == I_PCM) &&
                            ((mb.GetChromaCbp() >> n.BlockIndex) & 1) != 0 &&
                            mb.TransformSize8x8Flag)
                            return mb.Residual.LumaLevel8x8[n.BlockIndex];

                        return null;
                    }
                }
                else if (ctxBlockCat is 7 or 8)
                {
                    _macroblockProvider.DeriveNeighboring4x4ChromaBlocks(
                        _macroblockProvider.CurrMbAddr,
                        options.Chroma4x4BlkIdx,
                        out H264CabacAddressAndBlockIndices addrA,
                        out H264CabacAddressAndBlockIndices addrB);

                    mbAddrA = addrA.Address;
                    mbAddrB = addrB.Address;

                    transBlockA = GetTransBlock(addrA);
                    transBlockB = GetTransBlock(addrB);

                    bool? GetTransBlock(H264CabacAddressAndBlockIndices n)
                    {
                        if (!n.Address.Availability) return null;

                        var mb = n.Address.Descriptor;

                        if (!mb.Residual.IsResidualPresent) return null;

                        if (!(mb.ExactType == P_Skip || mb.ExactType == B_Skip || mb.ExactType == I_PCM) &&
                            ((mb.GetChromaCbp() >> (n.BlockIndex >> 2)) & 1) != 0 &&
                            !mb.TransformSize8x8Flag)
                        {
                            if (!mb.Residual.HasChromaLevels) return null;

                            return mb.Residual.CbLevel4x4[n.BlockIndex];
                        }
                        else if (!(mb.ExactType == P_Skip || mb.ExactType == B_Skip) &&
                            ((mb.GetLumaCbp() >> (n.BlockIndex >> 2)) & 1) != 0 &&
                            mb.TransformSize8x8Flag)
                        {
                            if (!mb.Residual.HasChromaLevels) return null;

                            return mb.Residual.CbLevel8x8[n.BlockIndex >> 2];
                        }

                        return null;
                    }
                }
                else if (ctxBlockCat == 9)
                {
                    _macroblockProvider.DeriveNeighboring8x8LumaBlocksWithChromaArrayType3(
                        _macroblockProvider.CurrMbAddr,
                        options.Luma8x8BlkIdx,
                        out H264CabacAddressAndBlockIndices addrA,
                        out H264CabacAddressAndBlockIndices addrB);

                    mbAddrA = addrA.Address;
                    mbAddrB = addrB.Address;

                    transBlockA = GetTransBlock(addrA);
                    transBlockB = GetTransBlock(addrB);

                    bool? GetTransBlock(H264CabacAddressAndBlockIndices n)
                    {
                        if (!n.Address.Availability) return null;

                        var mb = n.Address.Descriptor;

                        if (!mb.Residual.IsResidualPresent) return null;

                        if (!(mb.ExactType == P_Skip || mb.ExactType == B_Skip || mb.ExactType == I_PCM) &&
                            ((mb.GetChromaCbp() >> n.BlockIndex) & 1) != 0 &&
                            mb.TransformSize8x8Flag)
                        {
                            if (!mb.Residual.HasChromaLevels) return null;

                            return mb.Residual.CbLevel8x8[n.BlockIndex];
                        }

                        return null;
                    }
                }
                else if (ctxBlockCat is 11 or 12)
                {
                    _macroblockProvider.DeriveNeighboring4x4ChromaBlocks(
                        _macroblockProvider.CurrMbAddr,
                        options.Cr4x4BlkIdx,
                        out H264CabacAddressAndBlockIndices addrA,
                        out H264CabacAddressAndBlockIndices addrB);

                    mbAddrA = addrA.Address;
                    mbAddrB = addrB.Address;

                    transBlockA = GetTransBlock(addrA, in options);
                    transBlockB = GetTransBlock(addrB, in options);

                    bool? GetTransBlock(H264CabacAddressAndBlockIndices n, in H264CodedBlockFlagOptions options)
                    {
                        if (!n.Address.Availability) return null;

                        var mb = n.Address.Descriptor;

                        if (!mb.Residual.IsResidualPresent) return null;

                        if (!(mb.ExactType == P_Skip || mb.ExactType == B_Skip || mb.ExactType == I_PCM) &&
                            ((mb.GetChromaCbp() >> (options.Cr4x4BlkIdx >> 2)) & 1) != 0 &&
                            !mb.TransformSize8x8Flag)
                        {
                            if (!mb.Residual.HasChromaLevels) return null;

                            return mb.Residual.CrLevel4x4[n.BlockIndex];
                        }
                        else if (!(mb.ExactType == P_Skip || mb.ExactType == B_Skip) &&
                            ((mb.GetLumaCbp() >> (options.Cr4x4BlkIdx >> 2)) & 1) != 0 &&
                            mb.TransformSize8x8Flag)
                        {
                            if (!mb.Residual.HasChromaLevels) return null;

                            return mb.Residual.CrLevel4x4[n.BlockIndex >> 2];
                        }

                        return null;
                    }
                }
                else // ctxBlockCat is 13
                {
                    _macroblockProvider.DeriveNeighboring8x8ChromaBlocksWithChromaArrayType3(
                        _macroblockProvider.CurrMbAddr,
                        options.Cb8x8BlkIdx,
                        out H264CabacAddressAndBlockIndices addrA,
                        out H264CabacAddressAndBlockIndices addrB);

                    mbAddrA = addrA.Address;
                    mbAddrB = addrB.Address;

                    transBlockA = GetTransBlock(addrA);
                    transBlockB = GetTransBlock(addrB);

                    bool? GetTransBlock(H264CabacAddressAndBlockIndices n)
                    {
                        if (!n.Address.Availability) return null;

                        var mb = n.Address.Descriptor;

                        if (!mb.Residual.IsResidualPresent) return null;

                        if (!(mb.ExactType == P_Skip || mb.ExactType == B_Skip || mb.ExactType == I_PCM) &&
                            ((mb.GetChromaCbp() >> n.BlockIndex) & 1) != 0 &&
                            mb.TransformSize8x8Flag)
                        {
                            if (!mb.Residual.HasCbCrLevels) return null;

                            return mb.Residual.CrLevel8x8[n.BlockIndex];
                        }

                        return null;
                    }
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int Map(int x) => (int)Math.Pow(-1, x + 1) * (int)Math.Ceiling(x / 2D);
    }
}
