namespace ArithmeticCoding.H264
{
    public static class H264CabacMacroblockExtensions
    {
        /// <summary>
        ///   Extensions.
        /// </summary>
        /// <param name="descriptor">The descriptor</param>
        extension(H264CabacMacroblockDescriptor descriptor)
        {
            /// <summary>
            ///   Returns the CodedBlockPatternLuma variable.
            /// </summary>
            /// <returns>CBP for Luma</returns>
            public int GetLumaCbp() => descriptor.CodedBlockPattern % 16;

            /// <summary>
            ///   Returns the CodedBlockPatternChroma variable.
            /// </summary>
            /// <returns>CBP for chroma</returns>
            public int GetChromaCbp() => descriptor.CodedBlockPattern / 16;
        }
    }
}
