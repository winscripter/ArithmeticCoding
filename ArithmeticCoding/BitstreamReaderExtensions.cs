using ArithmeticCoding.Shared;

namespace ArithmeticCoding;

/// <summary>
///   Extensions for <see cref="IBitstreamReader"/>.
/// </summary>
public static class BitstreamReaderExtensions
{
    /// <summary>
    ///   Extension methods.
    /// </summary>
    /// <param name="bitstreamReader"></param>
    extension(IBitstreamReader bitstreamReader)
    {
        /// <summary>
        ///   Reads the next <paramref name="bits"/> bits.
        /// </summary>
        /// <param name="bits">The bits to read.</param>
        /// <returns>Next <paramref name="bits"/> bits.</returns>
        public int ReadBits(int bits)
        {
            int n = 0;

            for (int i = 0; i < bits; i++)
                n = (n << 1) | bitstreamReader.ReadBit().AsInt32();

            return n;
        }

        /// <summary>
        ///   Reads the next <paramref name="bits"/> bits.
        /// </summary>
        /// <param name="bits">The bits to read.</param>
        /// <returns>Next <paramref name="bits"/> bits.</returns>
        public async Task<int> ReadBitsAsync(int bits)
        {
            int n = 0;

            for (int i = 0; i < bits; i++)
                n = (n << 1) | (await bitstreamReader.ReadBitAsync()).AsInt32();

            return n;
        }
    }
}
