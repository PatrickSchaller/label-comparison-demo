namespace LabelReview;

public record LabelInput(string Name, string MediaType, byte[] Bytes)
{
    public const int MaxBytes = 6 * 1024 * 1024;

    public static LabelInput Create(string name, byte[] bytes)
    {
        if (bytes.Length == 0 || bytes.Length > MaxBytes)
            throw new InvalidDataException("Each label must be between 1 byte and 6 MiB.");
        var extension = Path.GetExtension(name).ToLowerInvariant();
        var mediaType = extension switch
        {
            ".png" when bytes.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) => "image/png",
            ".jpg" or ".jpeg" when bytes.AsSpan().StartsWith(new byte[] { 255, 216, 255 }) => "image/jpeg",
            ".pdf" when bytes.AsSpan().StartsWith("%PDF-"u8) => "application/pdf",
            _ => throw new InvalidDataException("Use a PNG, JPEG or PDF with matching file contents.")
        };
        // Never forward a user-supplied path or filename to the provider.
        return new LabelInput("label" + extension, mediaType, bytes);
    }

    public object ToApiContent()
    {
        var data = $"data:{MediaType};base64,{Convert.ToBase64String(Bytes)}";
        return MediaType == "application/pdf"
            ? new { type = "input_file", filename = Name, file_data = data }
            : (object)new { type = "input_image", image_url = data, detail = "high" };
    }
}
