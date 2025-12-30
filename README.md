# Arithmetic Coding for .NET

<img align="right" src="https://github.com/winscripter/ArithmeticCoding/blob/master/media/Logo/logo.png?raw=true" width="128" height="128">

ArithmeticCoding is a tiny, high-performance, zero allocation library for .NET that simplifies implementation of arithmetically coded file
formats and codecs. It is **highly** optimized for performance. It implements arithmetic decoding/encoding for different file formats and codecs, as listed below.
- AV1 Symbol decoder
- Generic arithmetic decoder/encoder - a binary arithmetic coder with a fixed 32-bit state; this is primarily for learning purposes or experimentation
- H.263 SAC (Syntax-Based Arithmetic) decoder/encoder
- H.264 CABAC decoder
- More upcoming

[ArithmeticCoding on NuGet](https://www.nuget.org/packages/ArithmeticCoding)

It can do all of this in fully managed .NET - it does not rely on native dependencies, everything
is written in C# code. Zero P/Invoke.

Arithmetic coding can be a very complex task. If you ever tried to write a file format or codec in C#
or any other programming language, i.e. H.264, you know how much of a pain it can be to
implement arithmetic coding correctly and efficiently. This library aims to solve that problem by
providing a simple and efficient API for arithmetic coding operations. You'll need at least
.NET Standard 2.0 to use this library, which means it works on .NET Framework 4.6.1 or newer. Newest
.NET versions are of course supported as well, i.e. .NET 10.

# Getting started
To get started with ArithmeticCoding, you can install it via NuGet. Use the following command in the Package Manager Console:
```
dotnet add package ArithmeticCoding
```

Alternatively, if you use Visual Studio, you may do so by right-clicking on your project in the Solution Explorer,
selecting "Manage NuGet Packages...", and searching for "ArithmeticCoding". Choose the package and click to
Install it.

# H.264
Here is an example of using the H.264 CABAC decoder. You'll need to implement `IH264MacroblockProvider`; see
[Implementing IH264MacroblockProvider](https://github.com/winscripter/ArithmeticCoding/blob/master/Docs/IH264MacroblockProvider-Implementation.md).
You should also implement `IBitstreamReader`, see
[Implementing IBitstreamReader](https://github.com/winscripter/ArithmeticCoding/blob/master/Docs/IBitstreamReader-Implementation.md). The slice
type can be derived from the `slice_type` syntax element from the Slice Header (see clause 7.4.3 in the H.264 spec; table 7-6). The slice QP
is defined with the `QPY` variable, which is specified in around at the bottom of clause 7.4.5 in the H.264 spec. Finally, `codIOffset`
is read once during CABAC initialization right before reading the first syntax element, or, after all the `cabac_alignment_one_bit` bits.

```cs
using ArithmeticCoding;
using ArithmeticCoding.H264;

byte[] cabacData = { /* your CABAC encoded data */ };
IH264MacroblockProvider provider = new MyMacroblockProvider(); // Implement this interface to provide macroblock data

H264CabacSliceType sliceType = H264CabacSliceType.P; // Specify the slice type
int sliceQPy = 26; // Specify the slice QP
IBitstreamReader bitstreamReader = new MyBitstreamReader(new MemoryStream(cabacData)); // Implement this interface to read bits from the bitstream

int codIOffset = bitstreamReader.ReadBits(9); // Read the initial CABAC offset from the bitstream. (Do this right after reading all the necessary cabac_init bits)

H264CabacDecoder decoder = new H264CabacDecoder(sliceType, sliceQPy, provider, codIOffset, bitstreamReader);
```

I know... setup can be a bit involved, but once you have everything in place, using the decoder is straightforward.

Here is an example of reading the mb_skip_flag syntax element:
```cs
bool mbSkipFlag = decoder.DecodeSkipFlag();
```

That's... pretty much it! You can now use the decoder to read other syntax elements as needed.

Do note that some syntax elements require additional context prior to decoding. `mvd_lX` is a great example. You'll have to
provide mbPartIdx and subMbPartIdx to the decoder so it can determine the correct context model to use.

```cs
decoder.MacroblockPartitionIndex = 1; // Example (mbPartIdx)
decoder.SubMacroblockPartitionIndex = 2; // Example (subMbPartIdx)

int mvdL0 = decoder.DecodeMotionVectorDifferenceL0(); // Reading mvd_l0
```

XML documentation on `Decode...` methods explains which input parameters are required to set up prior to using a
decode method. If there aren't any, then you can just call the method directly.

Example with mvd_lX (requires mbPartIdx, subMbPartIdx):

![mvd_l0 XML Documentation](https://github.com/winscripter/ArithmeticCoding/blob/master/media/MvdXMLDocumentation.png?raw=true)

Example with mb_skip_flag (no prior setup required):

![mb_skip_flag XML Documentation](https://github.com/winscripter/ArithmeticCoding/blob/master/media/SkipFlagXMLDocumentation.png?raw=trueg)

# H.263
Import the namespace:
```cs
using ArithmeticCoding.H263;
```

H.263 uses what's known as a Syntax-Based Arithmetic (SAC) coder. It relies on cumulative
frequencies for decoding symbols. Before we get started, you'll need to implement the
`IH263PscFifo` interface which implements a Picture Start Code (PSC) First-in First-out (FIFO).
See [Implementing IH263PscFifo](https://github.com/winscripter/ArithmeticCoding/blob/master/Docs/IH263PscFifo-Implementation.md).

```cs
IH263PscFifo pscFifo = ...; // <-- Implement
```

Let's start with the decoder.
```cs
H263SyntaxBasedArithmeticDecoder sacDecoder = new();

int lumaCodedBlockPattern = sacDecoder.DecodeSymbol(
	H263SyntaxBasedArithmeticModels.CbpY, pscFifo);

// Async is also supported
int asyncLumaCodedBlockPattern = await sacDecoder.DecodeSymbolAsync(
	H263SyntaxBasedArithmeticModels.CbpY.ToArray(), pscFifo);
```

The encoder part is also very simple.

```cs
H263SyntaxBasedArithmeticEncoder sacEncoder = new();

const int index = 4; // example index
sacEncoder.EncodeSymbol(index, H263SyntaxBasedArithmeticModels.CbpY, pscFifo);

// Async is also supported
await sacEncoder.EncodeSymbolAsync(index, H263SyntaxBasedArithmeticModels.CbpY.ToArray(), pscFifo);
```

# AV1
Currently, ArithmeticCoding includes an AV1 Symbol Decoder. For comprehensive AV1 entropy coding support,
Symbol Encoding/CDF support may be introduced in future versions. For now, here's how to use the symbol
decoder.

`disable_cdf_update` is a syntax element. `sz` is the input parameter for the initialization process.

```cs
using ArithmeticCoding.Av1;

IBitstreamReader bitstreamReader = ...; // <-- Implement
const int sz = 10; // <-- Implement
const bool disable_cdf_update = false; // <-- Implement

var symbolDecoder = new Av1SymbolDecoder(
	bitstreamReader, sz, disable_cdf_update);
```

> [!WARNING]
> This will immediately read a few bits off of the bit-stream. This is necessary for proper initialization.

You can now use this to read AV1 bools, literals or symbols, as follows.

```cs
int boolean = symbolDecoder.ReadBooleanAsInt32();
int literal = symbolDecoder.ReadLiteral(5);
int customSymbol = symbolDecoder.ReadSymbol(provide CDF here);
```

# Powers
Use of this library makes manual .NET implementations (remember, C#, VB.NET and even F#)
of supported file formats and codecs that include arithmetically coded data significantly
easier and faster to implement.

The license is very permissive (MIT License), so you can use this library in both open-source
and commercial projects without worrying about licensing issues. (Note that some codecs,
i.e. H.264, are patented by the owners of such codecs and may require paying royalties
to use in commercial contexts, though most patents have expired and others are on the verge
of expiration.)

It is also greatly optimized for performance, so you can expect efficient arithmetic coding operations.

# Got any questions?
You're more than welcome to ask for help through the GitHub Issues page.

In GitHub Issues, you can report bugs, request features, or seek assistance with using the library.
