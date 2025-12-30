# Implementing IBitstreamReader
The `IBitstreamReader` interface provides access to the bit-stream.

There are only two methods in this interface: `ReadBit()` and `ReadBitAsync()`.

`ReadBit()` simply reads the next bit in the bit-stream and returns a boolean representing that bit, while `ReadBitAsync` is the async
version.

Both should throw `EndOfStreamException` when attempting to read out of bounds of the bit-stream.

General view:
```cs
public interface IBitstreamReader
{
    bool ReadBit();
    Task<bool> ReadBitAsync();
}
```
