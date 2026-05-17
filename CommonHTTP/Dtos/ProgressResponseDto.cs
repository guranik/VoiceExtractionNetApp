namespace Common.Http.Dtos
{
    public class ProgressResponseDto
    {
        public int EarliestExtractSegmentStart { get; set; }
        public int InputFileDuration { get; set; }
        public int LatestTranscriptionEnd { get; set; }
    }
}
