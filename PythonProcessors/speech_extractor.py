import os
import sys
import re
import argparse
import traceback
import warnings
from typing import Callable

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

VAD_MODEL_PATH = "models/silero_vad.jit"

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

# =====================================================================
# ВЫНЕСЕНО ЗА ПРЕДЕЛЫ КЛАССА (как в оригинальном utils.py от Silero)
# =====================================================================
@torch.no_grad()
def get_speech_timestamps(audio: torch.Tensor,
                          model,
                          threshold: float = 0.5,
                          sampling_rate: int = 16000,
                          min_speech_duration_ms: int = 250,
                          max_speech_duration_s: float = float('inf'),
                          min_silence_duration_ms: int = 100,
                          speech_pad_ms: int = 30,
                          return_seconds: bool = False,
                          time_resolution: int = 1,
                          visualize_probs: bool = False,
                          progress_tracking_callback: Callable[[float], None] = None,
                          neg_threshold: float = None,
                          window_size_samples: int = 512,
                          min_silence_at_max_speech: int = 98,
                          use_max_poss_sil_at_max_speech: bool = True):

    if not torch.is_tensor(audio):
        try:
            audio = torch.Tensor(audio)
        except:
            raise TypeError("Audio cannot be casted to tensor. Cast it manually")

    if len(audio.shape) > 1:
        for i in range(len(audio.shape)):
            audio = audio.squeeze(0)
        if len(audio.shape) > 1:
            raise ValueError("More than one dimension in audio. Are you trying to process audio with 2 channels?")

    if sampling_rate > 16000 and (sampling_rate % 16000 == 0):
        step = sampling_rate // 16000
        sampling_rate = 16000
        audio = audio[::step]
        warnings.warn('Sampling rate is a multiply of 16000, casting to 16000 manually!')
    else:
        step = 1

    if sampling_rate not in [8000, 16000]:
        raise ValueError("Currently silero VAD models support 8000 and 16000 (or multiply of 16000) sample rates")

    window_size_samples = 512 if sampling_rate == 16000 else 256

    model.reset_states()
    min_speech_samples = sampling_rate * min_speech_duration_ms / 1000
    speech_pad_samples = sampling_rate * speech_pad_ms / 1000
    max_speech_samples = sampling_rate * max_speech_duration_s - window_size_samples - 2 * speech_pad_samples
    min_silence_samples = sampling_rate * min_silence_duration_ms / 1000
    min_silence_samples_at_max_speech = sampling_rate * min_silence_at_max_speech / 1000

    audio_length_samples = len(audio)

    speech_probs = []
    for current_start_sample in range(0, audio_length_samples, window_size_samples):
        chunk = audio[current_start_sample: current_start_sample + window_size_samples]
        if len(chunk) < window_size_samples:
            chunk = torch.nn.functional.pad(chunk, (0, int(window_size_samples - len(chunk))))
        speech_prob = model(chunk, sampling_rate).item()
        speech_probs.append(speech_prob)
        
        progress = current_start_sample + window_size_samples
        if progress > audio_length_samples:
            progress = audio_length_samples
        progress_percent = (progress / audio_length_samples) * 100
        if progress_tracking_callback:
            progress_tracking_callback(progress_percent)

    triggered = False
    speeches = []
    current_speech = {}

    if neg_threshold is None:
        neg_threshold = max(threshold - 0.15, 0.01)
    temp_end = 0 
    prev_end = next_start = 0 
    possible_ends = []

    for i, speech_prob in enumerate(speech_probs):
        cur_sample = window_size_samples * i

        if (speech_prob >= threshold) and temp_end:
            sil_dur = cur_sample - temp_end
            if sil_dur > min_silence_samples_at_max_speech:
                possible_ends.append((temp_end, sil_dur))
            temp_end = 0
            if next_start < prev_end:
                next_start = cur_sample

        if (speech_prob >= threshold) and not triggered:
            triggered = True
            current_speech['start'] = cur_sample
            continue

        if triggered and (cur_sample - current_speech['start'] > max_speech_samples):
            if use_max_poss_sil_at_max_speech and possible_ends:
                prev_end, dur = max(possible_ends, key=lambda x: x[1]) 
                current_speech['end'] = prev_end
                speeches.append(current_speech)
                current_speech = {}
                next_start = prev_end + dur

                if next_start < prev_end + cur_sample: 
                    current_speech['start'] = next_start
                else:
                    triggered = False
                prev_end = next_start = temp_end = 0
                possible_ends = []
            else:
                if prev_end:
                    current_speech['end'] = prev_end
                    speeches.append(current_speech)
                    current_speech = {}
                    if next_start < prev_end:
                        triggered = False
                    else:
                        current_speech['start'] = next_start
                    prev_end = next_start = temp_end = 0
                    possible_ends = []
                else:
                    current_speech['end'] = cur_sample
                    speeches.append(current_speech)
                    current_speech = {}
                    prev_end = next_start = temp_end = 0
                    triggered = False
                    possible_ends = []
                    continue

        if (speech_prob < neg_threshold) and triggered:
            if not temp_end:
                temp_end = cur_sample
            sil_dur_now = cur_sample - temp_end

            if not use_max_poss_sil_at_max_speech and sil_dur_now > min_silence_samples_at_max_speech:
                prev_end = temp_end

            if sil_dur_now < min_silence_samples:
                continue
            else:
                current_speech['end'] = temp_end
                if (current_speech['end'] - current_speech['start']) > min_speech_samples:
                    speeches.append(current_speech)
                current_speech = {}
                prev_end = next_start = temp_end = 0
                triggered = False
                possible_ends = []
                continue

    if current_speech and (audio_length_samples - current_speech['start']) > min_speech_samples:
        current_speech['end'] = audio_length_samples
        speeches.append(current_speech)

    for i, speech in enumerate(speeches):
        if i == 0:
            speech['start'] = int(max(0, speech['start'] - speech_pad_samples))
        if i != len(speeches) - 1:
            silence_duration = speeches[i+1]['start'] - speech['end']
            if silence_duration < 2 * speech_pad_samples:
                speech['end'] += int(silence_duration // 2)
                speeches[i+1]['start'] = int(max(0, speeches[i+1]['start'] - silence_duration // 2))
            else:
                speech['end'] = int(min(audio_length_samples, speech['end'] + speech_pad_samples))
                speeches[i+1]['start'] = int(max(0, speeches[i+1]['start'] - speech_pad_samples))
        else:
            speech['end'] = int(min(audio_length_samples, speech['end'] + speech_pad_samples))

    if return_seconds:
        audio_length_seconds = audio_length_samples / sampling_rate
        for speech_dict in speeches:
            speech_dict['start'] = max(round(speech_dict['start'] / sampling_rate, time_resolution), 0)
            speech_dict['end'] = min(round(speech_dict['end'] / sampling_rate, time_resolution), audio_length_seconds)
    elif step > 1:
        for speech_dict in speeches:
            speech_dict['start'] *= step
            speech_dict['end'] *= step

    # make_visualization удалена, так как не используется и требует matplotlib
    return speeches


# =====================================================================
# КЛАСС ОБРАБОТЧИКА
# =====================================================================
class AudioSplitter:
    def __init__(self, max_duration_sec=DEFAULT_MAX_DURATION):
        print(f"Загрузка модели Silero VAD из {VAD_MODEL_PATH}...", flush=True)
        if not os.path.exists(VAD_MODEL_PATH):
            raise FileNotFoundError(f"Файл модели не найден по пути: {VAD_MODEL_PATH}")
            
        self.model = torch.jit.load(VAD_MODEL_PATH)
        self.model.eval()
        
        # Ссылка на глобальную функцию
        self.utils = [get_speech_timestamps] 
        self.max_duration = max(int(max_duration_sec), 5)

    def read_and_prepare_audio(self, file_path):
        waveform, sample_rate = torchaudio.load(file_path, backend="soundfile")

        if waveform.dim() == 2:
            waveform = waveform.mean(dim=0)

        if sample_rate != 16000:
            waveform = torchaudio.transforms.Resample(sample_rate, 16000)(waveform)
            sample_rate = 16000

        return waveform, sample_rate

    def detect_speech(self, waveform, sample_rate):
        get_speech_timestamps_func = self.utils[0]
        return get_speech_timestamps_func(
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
    name = unicodedata.normalize('NFKD', name)
    replacements = {
        '\u2013': '-', '\u2014': '-', '\u2212': '-', '\uFF0E': '.', '\u00A0': ' ',
    }
    for bad, good in replacements.items():
        name = name.replace(bad, good)
    name = ''.join(c for c in name if ord(c) < 128)
    return name.strip()

def validate_meta_filename_or_fail(name: str, out_dir: str):
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
    waveform, sample_rate, segments, meta_start_seconds, out_dir, thread_index, splitter, session_id=None
):
    os.makedirs(out_dir, exist_ok=True)

    for start_sample, end_sample in segments:
        try:
            seg_wave = waveform[start_sample:end_sample].unsqueeze(0)
            start_sec = meta_start_seconds + start_sample / sample_rate
            duration = (end_sample - start_sample) / sample_rate

            if session_id:
                out_name = f"{thread_index}_{session_id}_{seconds_to_hhmmss_ms(start_sec)}_{duration:.3f}.wav"
            else:
                out_name = f"{thread_index}_{seconds_to_hhmmss_ms(start_sec)}_{duration:.3f}.wav"

            splitter.save_wav(os.path.join(out_dir, out_name), seg_wave, sample_rate)
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

    save_segments(waveform, sample_rate, final_segments, meta_start_seconds, segments_dir, thread_index, splitter, session_id)
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