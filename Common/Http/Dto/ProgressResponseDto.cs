using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Common.Http.Dto
{
    public class ProgressResponseDto
    {
        public int EarliestExtractSegmentStart { get; set; }
        public int InputFileDuration { get; set; }
        public int LatestTranscriptionEnd { get; set; }

        /// <summary>
        /// Прогресс экстракции [0.0 - 1.0].
        /// </summary>
        public double ExtractProgress =>
            InputFileDuration > 0
                ? Math.Min(1.0, (double)EarliestExtractSegmentStart / InputFileDuration)
                : 0.0;

        /// <summary>
        /// Прогресс транскрипции [0.0 - 1.0].
        /// </summary>
        public double TranscribeProgress =>
            InputFileDuration > 0
                ? Math.Min(1.0, (double)LatestTranscriptionEnd / InputFileDuration)
                : 0.0;
    }
}
