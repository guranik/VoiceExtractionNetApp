import os
import sys
import argparse
import traceback

try:
    import whisper
except Exception as e:
    print(f"[FATAL] whisper library not available: {e}", file=sys.stderr, flush=True)
    sys.exit(2)

try:
    sys.stdout.reconfigure(encoding="utf-8")
    sys.stderr.reconfigure(encoding="utf-8")
except Exception:
    pass

def safe_write_text(path: str, text: str):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8") as f:
        f.write(text)


def build_wav_path(segments_dir: str, filename: str) -> str:
    return os.path.join(segments_dir, filename)


def build_output_path(transcriptions_dir: str, thread_index: int, filename: str) -> str:
    base_name = os.path.splitext(filename)[0]
    out_name = f"{thread_index}_{base_name}.txt"
    return os.path.join(transcriptions_dir, out_name)

def load_model(model_name="small"):
    try:
        print(f"Loading Whisper model '{model_name}'...", flush=True)
        model_dir = r"C:\Users\timof\whisper_models"
        model = whisper.load_model(model_name, download_root=model_dir)
        print("Whisper loaded.", flush=True)
        return model
    except Exception as e:
        print(f"[FATAL] Failed to load Whisper model {model_name}: {e}",
              file=sys.stderr, flush=True)
        raise

def detect_language(model, audio):
    mel = whisper.log_mel_spectrogram(audio).to(model.device)
    _, probs = model.detect_language(mel)
    return max(probs, key=probs.get) if probs else "unknown"

def decode_audio(model, audio):
    mel = whisper.log_mel_spectrogram(audio).to(model.device)
    options = whisper.DecodingOptions(fp16=False)
    result = whisper.decode(model, mel, options)
    return result.text.strip()

def transcribe_file(model, file_path):
    try:
        print(f"[INFO] Transcribing {file_path}", flush=True)
        audio = whisper.load_audio(file_path)
        audio = whisper.pad_or_trim(audio)

        language = detect_language(model, audio)
        text = decode_audio(model, audio)

        return language, text

    except Exception as e:
        print(
            f"[ERROR] Transcription failed for {file_path}: {e}\n{traceback.format_exc()}",
            file=sys.stderr,
            flush=True
        )
        return None, ""

def process_task(model, segments_dir, transcriptions_dir, thread_index, filename):
    print(f"[INFO] Received task: {filename}", flush=True)

    wav_path = build_wav_path(segments_dir, filename)
    if not os.path.exists(wav_path):
        print(f"[ERROR] Input file not found: {wav_path}", file=sys.stderr, flush=True)
        print(f"DONE:{filename}", flush=True)
        return

    language, text = transcribe_file(model, wav_path)

    try:
        os.remove(wav_path)
    except Exception as e:
        print(f"[WARN] Failed to remove {wav_path}: {e}", file=sys.stderr, flush=True)

    out_path = build_output_path(
        transcriptions_dir,
        thread_index,
        filename
    )

    try:
        safe_write_text(out_path, text)
        print(f"[INFO] Saved transcription: {out_path}", flush=True)
    except Exception as e:
        print(f"[ERROR] Failed to write transcription {out_path}: {e}",
              file=sys.stderr, flush=True)

    print(f"DONE:{filename}", flush=True)


def stdin_task_loop(model, segments_dir, transcriptions_dir, thread_index):
    print("READY", flush=True)

    while True:
        try:
            line = sys.stdin.readline()
            if not line:
                break

            filename = line.strip()
            if not filename:
                continue

            process_task(
                model,
                segments_dir,
                transcriptions_dir,
                thread_index,
                filename
            )

        except KeyboardInterrupt:
            break
        except Exception as e:
            print(
                f"[ERROR] Main loop exception: {e}\n{traceback.format_exc()}",
                file=sys.stderr,
                flush=True
            )
            sys.exit(5)

    print("[INFO] transcribe_audio.py exiting (stdin closed).", flush=True)


def parse_args():
    parser = argparse.ArgumentParser()
    parser.add_argument("segments_dir", help="Input directory with segments (wav files)")
    parser.add_argument("transcriptions_dir", help="Output directory for transcription files")
    parser.add_argument("thread_index", type=int, help="Index of this python thread (prefix)")
    parser.add_argument("--model", default="tiny", help="Whisper model to use (default: small)")
    return parser.parse_args()


def main():
    args = parse_args()

    try:
        model = load_model(args.model)
    except Exception:
        print("[FATAL] Cannot initialize model, exiting.", file=sys.stderr, flush=True)
        sys.exit(3)

    stdin_task_loop(
        model=model,
        segments_dir=args.segments_dir,
        transcriptions_dir=args.transcriptions_dir,
        thread_index=args.thread_index
    )


if __name__ == "__main__":
    main()
