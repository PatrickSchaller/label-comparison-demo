using LabelReview;
using Xunit;

namespace LabelReview.Tests;

public class LabelInputTests
{
    [Fact]
    public void SafeFilenameAndContentTypeComeFromValidatedExtension()
    {
        var input = LabelInput.Create("customer-secret.PDF", "%PDF-1.7"u8.ToArray());
        Assert.Equal("label.pdf", input.Name);
        Assert.Equal("application/pdf", input.MediaType);
    }

    [Fact]
    public void EmptyOversizedAndMismatchedUploadsAreRejected()
    {
        Assert.Throws<InvalidDataException>(() => LabelInput.Create("a.png", []));
        Assert.Throws<InvalidDataException>(() => LabelInput.Create("a.png", new byte[LabelInput.MaxBytes + 1]));
        Assert.Throws<InvalidDataException>(() => LabelInput.Create("a.jpg", "%PDF-1.7"u8.ToArray()));
        Assert.Throws<InvalidDataException>(() => LabelInput.Create("a.svg", "<svg/>"u8.ToArray()));
    }

    [Fact]
    public void ByteHashIsStableAndContentSensitive()
    {
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", Comparison.Hash("abc"u8.ToArray()));
        Assert.NotEqual(Comparison.Hash("abc"u8.ToArray()), Comparison.Hash("abd"u8.ToArray()));
    }
}
