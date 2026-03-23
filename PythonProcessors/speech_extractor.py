import os
import sys
import re
import argparse
import traceback

try:
    import torch
    import torchaudio
except Exception as e:
    print(f"[FATAL] Required audio libraries not found: {e}", file=sys.stderr, flush=True)
    sys.exit(2)

try:
    sys.stdout.reconfigure(encoding="utf-8")
    sys.stderr.reconfigure(encoding="utf-8")
except Exception:
    pass


# Обновлённый паттерн: SessionId_время.wav
META_NAME_RE = re.compile(r"^[A-Za-z0-9]+_\d{2}-\d{2}-\d{2}\.\d{3}\.wav$")
MIN_SEGMENT_SEC = 0.5
DEFAULT_MAX_DURATION = 30


def parse_input_filename(filename: str):
    """Разделяет SessionId и основную часть имени файла с временной меткой."""
    name_without_ext = filename.rsplit('.', 1)[0]
    parts = name_without_ext.split('_', 1)
    if len(parts) != 2:
        return None, None
    session_id, time_part = parts
    return session_id, f"{time_part}.wav"


def hhmmss_to_seconds(hhmmss: str) -> float:
    if '.' in hhmmss:
        time_part, ms = hhmmss.rsplit('.', 1)
        h, m, s = map(int, time_part.split('-'))
        return h * 3600 + m * 60 + s + int(ms) / 1000.0
    h, m, s = map(int, hhmmss.split('-'))
    return h * 3600 + m * 60 + s


def seconds_to_hhmmss_ms(sec: float) -> str:
    if sec < 0:
        sec = 0.0
    total_ms = int(round(sec * 1000))
    h = total_ms // 3_600_000
    m = (total_ms % 3_600_000) // 60_000
    s = (total_ms % 60_000) // 1000
    ms = total_ms % 1000
    return f"{h:02d}-{m:02d}-{s:02d}.{ms:03d}"


class AudioSplitter:
    def __init__(self, max_duration_sec=DEFAULT_MAX_DURATION):
        print("Загрузка модели Silero VAD...", flush=True)
        self.model, self.utils = torch.hub.load(
            repo_or_dir='snakers4/silero-vad',
            model='silero_vad',
            force_reload=False,
            onnx=False
        )
        self.model.eval()
        self.max_duration = max(int(max_duration_sec), 5)

    def read_and_prepare_audio(self, file_path):
        waveform, sample_rate = torchaudio.load(file_path)

        if waveform.dim() == 2:
            waveform = waveform.mean(dim=0)

        if sample_rate != 16000:
            waveform = torchaudio.transforms.Resample(sample_rate, 16000)(waveform)
            sample_rate = 16000

        return waveform, sample_rate

    def detect_speech(self, waveform, sample_rate):
        get_speech_timestamps = self.utils[0]
        return get_speech_timestamps(
            waveform,
            self.model,
            sampling_rate=sample_rate,
            min_speech_duration_ms=500,
            min_silence_duration_ms=500,
            speech_pad_ms=200,
        )

    def split_long_segment(self, start_sample, end_sample, sample_rate):
        max_len = int(self.max_duration * sample_rate)

        if end_sample - start_sample <= max_len + int(0.5 * sample_rate):
            return [(start_sample, end_sample)]

        segments = []
        cur = start_sample
        while cur < end_sample:
            nxt = min(cur + max_len, end_sample)
            segments.append((cur, nxt))
            cur = nxt

        return segments

    @staticmethod
    def save_wav(path, data, sample_rate):
        os.makedirs(os.path.dirname(path), exist_ok=True)
        torchaudio.save(path, data, sample_rate)


def cleanup_output_dir(out_dir):
    try:
        if os.path.isdir(out_dir):
            for f in os.listdir(out_dir):
                try:
                    os.remove(os.path.join(out_dir, f))
                except Exception:
                    pass
    except Exception:
        pass

import unicodedata

def normalize_filename(name: str) -> str:
    # NFKD-нормализация разложит составные символы
    name = unicodedata.normalize('NFKD', name)
    # Замена «похожих» символов на ASCII-аналоги
    replacements = {
        '\u2013': '-',  # en-dash
        '\u2014': '-',  # em-dash
        '\u2212': '-',  # minus sign
        '\uFF0E': '.',  # full-width dot
        '\u00A0': ' ',  # non-breaking space
    }
    for bad, good in replacements.items():
        name = name.replace(bad, good)
    # Удаление остаточных не-ASCII символов (опционально)
    name = ''.join(c for c in name if ord(c) < 128)
    return name.strip()

def validate_meta_filename_or_fail(name: str, out_dir: str):
    print(f"[DEBUG] Raw name: {repr(name)}", file=sys.stderr, flush=True)

    if not META_NAME_RE.match(name):
        print(f"[FATAL] Некорректное имя мета-сегмента: {name}", file=sys.stderr, flush=True)
        cleanup_output_dir(out_dir)
        sys.exit(3)


def load_audio_or_fail(splitter, meta_path):
    try:
        return splitter.read_and_prepare_audio(meta_path)
    except Exception as e:
        raise RuntimeError(f"Ошибка чтения wav: {e}")


def run_vad_or_fail(splitter, waveform, sample_rate):
    try:
        return splitter.detect_speech(waveform, sample_rate)
    except Exception as e:
        raise RuntimeError(f"VAD failed: {e}")


def build_final_segments(timestamps, splitter, sample_rate):
    segments = []
    for seg in timestamps:
        segments.extend(
            splitter.split_long_segment(
                int(seg["start"]),
                int(seg["end"]),
                sample_rate
            )
        )
    return segments


def save_segments(
    waveform,
    sample_rate,
    segments,
    meta_start_seconds,
    out_dir,
    thread_index,
    splitter,
    session_id=None
):
    os.makedirs(out_dir, exist_ok=True)

    for start_sample, end_sample in segments:
        try:
            seg_wave = waveform[start_sample:end_sample].unsqueeze(0)
            start_sec = meta_start_seconds + start_sample / sample_rate
            duration = (end_sample - start_sample) / sample_rate

            if session_id:
                out_name = (
                    f"{thread_index}_"
                    f"{session_id}_"
                    f"{seconds_to_hhmmss_ms(start_sec)}_"
                    f"{duration:.3f}.wav"
                )
            else:
                out_name = (
                    f"{thread_index}_"
                    f"{seconds_to_hhmmss_ms(start_sec)}_"
                    f"{duration:.3f}.wav"
                )

            splitter.save_wav(
                os.path.join(out_dir, out_name),
                seg_wave,
                sample_rate
            )
        except Exception as e:
            print(f"[ERROR] Failed to save segment: {e}", file=sys.stderr, flush=True)


def delete_input_file(path):
    try:
        os.remove(path)
        print(f"[INFO] Deleted input file: {path}", flush=True)
    except Exception as e:
        print(f"[WARN] Не удалось удалить входной файл {path}: {e}", file=sys.stderr, flush=True)


def process_one(input_filename, meta_dir, segments_dir, thread_index, splitter):

    validate_meta_filename_or_fail(input_filename, segments_dir)

    session_id, meta_name = parse_input_filename(input_filename)
    
    meta_path = os.path.join(meta_dir, input_filename)
    if not os.path.exists(meta_path):
        print(f"[ERROR] Input file not found: {meta_path}", file=sys.stderr, flush=True)
        return

    time_part = meta_name.rsplit('.', 1)[0] if meta_name else os.path.splitext(input_filename)[0].split('_', 1)[-1]
    meta_start_seconds = hhmmss_to_seconds(time_part)

    waveform, sample_rate = load_audio_or_fail(splitter, meta_path)
    timestamps = run_vad_or_fail(splitter, waveform, sample_rate)
    final_segments = build_final_segments(timestamps, splitter, sample_rate)

    save_segments(
        waveform,
        sample_rate,
        final_segments,
        meta_start_seconds,
        segments_dir,
        thread_index,
        splitter,
        session_id
    )

    delete_input_file(meta_path)
    print("Split finished for: " + input_filename, flush=True)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("meta_dir")
    parser.add_argument("segments_dir")
    parser.add_argument("thread_index", type=int)
    parser.add_argument("--max-duration", type=int, default=DEFAULT_MAX_DURATION)
    args = parser.parse_args()

    splitter = AudioSplitter(args.max_duration)

    print(f"Working dir: {args.meta_dir}")
    print("READY", flush=True)

    while True:
        line = sys.stdin.readline()
        if not line:
            break

        fn = line.strip()
        if not fn:
            continue

        print(f"[INFO] Received task: {fn}", flush=True)

        try:
            process_one(fn, args.meta_dir, args.segments_dir, args.thread_index, splitter)
            print(f"DONE:{fn}", flush=True)

        except SystemExit:
            raise
        except Exception as e:
            print(f"[ERROR] {e}\n{traceback.format_exc()}", file=sys.stderr, flush=True)
            print(f"DONE:{fn}", flush=True)

    print("[INFO] split_audio.py exiting (stdin closed).", flush=True)


if __name__ == "__main__":
    main()