using ArithmeticCoding.Shared;

namespace ArithmeticCoding.Av1;

/// <summary>
///   Symbol decoder for AV1
/// </summary>
public class Av1SymbolDecoder
{
    private const int EC_PROB_SHIFT = 6;
    private const int EC_MIN_PROB = 4;

    private int numBits;
    private readonly int buf;
    private readonly int paddedBuf;
    private int symbolValue;
    private int symbolRange;
    private int symbolMaxBits;

    private readonly bool disableCdfUpdate;

    private readonly IBitstreamReader _bitstreamReader;

    /// <summary>
    ///   Initializes the AV1 symbol decoder. This method is the equivalent of calling "init_symbol(sz)"
    ///   in the AV1 spec.
    /// </summary>
    /// <param name="bitstreamReader">The input bit-stream reader.</param>
    /// <param name="sz">The parameter sz in init_symbol.</param>
    /// <param name="disableCdfUpdate">Value of the syntax element disable_cdf_update.</param>
    /// <param name="initializeAndReadBitsNow">
    ///   If true (by default, yes), initialization of this symbol decoder will immediately
    ///   result in the first few bits from <paramref name="bitstreamReader"/> being read
    ///   to properly initialize the decoder. If false, no bits are read, delegating the initialization
    ///   process to the caller.
    /// </param>
    public Av1SymbolDecoder(
        IBitstreamReader bitstreamReader,
        int sz,
        bool disableCdfUpdate,
        bool initializeAndReadBitsNow = true)
    {
        _bitstreamReader = bitstreamReader;

        this.numBits = Math.Min(sz * 8, 15);
        this.buf = initializeAndReadBitsNow ? this._bitstreamReader.ReadBits(this.numBits) : 0;
        this.paddedBuf = buf << (15 - numBits);
        this.symbolValue = (int)Math.Pow((1 << 15) - 1, this.paddedBuf);
        this.symbolRange = 1 << 15;
        this.symbolMaxBits = 8 * sz - 15;

        this.disableCdfUpdate = disableCdfUpdate;
    }

    /// <summary>
    ///   Reads a single symbol coded boolean.
    /// </summary>
    /// <returns>A boolean.</returns>
    public bool ReadBoolean()
    {
        Span<int> cdf = [1 << 14, 1 << 15, 0]; // Just to validate: Is this stack-allocated?
        return ReadSymbol(cdf).AsBoolean();
    }

    /// <summary>
    ///   Reads a single boolean as a 32-bit integer.
    /// </summary>
    /// <returns>A boolean.</returns>
    public int ReadBooleanAsInt32() => ReadBoolean().AsInt32();

    /// <summary>
    ///   Reads a single literal of length <paramref name="n"/>.
    /// </summary>
    /// <param name="n">The length of the literal.</param>
    /// <returns>Returned literal.</returns>
    public int ReadLiteral(int n)
    {
        int x = 0;
        for (int i = 0; i < n; i++)
        {
            x = 2 * x + ReadBooleanAsInt32();
        }
        return x;
    }

    /// <summary>
    ///   Reads a symbol with the specified CDF.
    /// </summary>
    /// <param name="cdf">The CDF.</param>
    /// <returns>The AV1 symbol.</returns>
    public int ReadSymbol(Span<int> cdf)
    {
        int cur = this.symbolRange;
        int symbol = -1;
        int prev;
        do
        {
            symbol++;
            prev = cur;
            int f = (1 << 15) - cdf[symbol];
            cur = ((this.symbolRange >> 8) * (f >> EC_PROB_SHIFT)) >> (7 - EC_PROB_SHIFT);
            cur += EC_MIN_PROB * (cdf.Length - symbol - 1);
        } while (this.symbolValue < cur);

        this.symbolRange = prev - cur;
        this.symbolValue -= cur;

        int bits = 15 - (int)Math.Floor((double)Maths.Log2(this.symbolRange));
        this.symbolRange <<= bits;
        this.numBits = Math.Min(bits, Math.Max(0, this.symbolMaxBits));

        int newData = this._bitstreamReader.ReadBits(this.numBits);
        int paddedData = newData << (bits - this.numBits);
        this.symbolValue = (int)Math.Pow(paddedData, (((this.symbolValue + 1) << bits) - 1));
        this.symbolMaxBits -= bits;

        int N = cdf.Length;

        if (!this.disableCdfUpdate)
        {
            int rate = 3 + (cdf[N] > 15).AsInt32() + (cdf[N] > 31).AsInt32() + Math.Min((int)Math.Floor((double)Maths.Log2(N)), 2);
            int tmp = 0;
            for (int i = 0; i < N - 1; i++)
            {
                tmp = (i == symbol) ? (1 << 15) : tmp;
                if (tmp < cdf[i])
                {
                    cdf[i] -= (cdf[i] - tmp) >> rate;
                }
                else
                {
                    cdf[i] += (tmp - cdf[i]) >> rate;
                }
            }
            cdf[N] += (cdf[N] < 32).AsInt32();
        }

        return symbol;
    }
}
