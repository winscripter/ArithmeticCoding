# Implementing IH263PscFifo
This interface provides access to the Picture Start Code First-in First-out reading/writing in H.263.

General view:
```cs
public interface IH263PscFifo
{
    bool GetBitFromFifo();
    Task<bool> GetBitFromFifoAsync();
    void WriteBitToFifo(bool bit);
    Task WriteBitToFifoAsync(bool bit);
    bool SupportsAsyncRead { get; }
    bool SupportsAsyncWrite { get; }
}
```

The methods `GetBitFromFifo()` and `GetBitFromFifoAsync()` simply return a single bit from the PSC FIFO, with the latter being the asynchronous
version. Methods `WriteBitToFifo` and `WriteBitToFifoAsync` are the opposite of the previous two - this time, they write a single bit to the
PSC FIFO, with, again, the latter being asynchronous. Finally, the two properties - `SupportsAsyncRead` and `SupportsAsyncWrite` - are feature
indicators that tell the decoder/encoder if the asynchronous implementations are supported (GetBitFromFifoAsync/WriteBitToFifoAsync).
