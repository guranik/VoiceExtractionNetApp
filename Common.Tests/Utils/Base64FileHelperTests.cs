using Common.Tcp.Utils;
using FluentAssertions;
using Xunit;

public class Base64FileHelperTests
{
    [Fact]
    public void ReadAndWriteBase64_ShouldPreserveFile()
    {
        var tempFile = Path.GetTempFileName();
        var outputFile = Path.GetTempFileName();

        try
        {
            File.WriteAllText(tempFile, "hello world");

            var base64 = Base64FileHelper.ReadFileAsBase64(tempFile);
            Base64FileHelper.WriteBase64ToFile(outputFile, base64);

            File.ReadAllText(outputFile).Should().Be("hello world");
        }
        finally
        {
            File.Delete(tempFile);
            File.Delete(outputFile);
        }
    }
}
