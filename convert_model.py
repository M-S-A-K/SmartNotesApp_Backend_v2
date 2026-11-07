#!/usr/bin/env python3
"""Download ubaidngt/whisper-small-ur-en (Hugging Face) and convert it to ggml for Whisper.net.

Run from the backend project folder:   python3 convert_model.py
Result:                                models/ggml-small-ur-en.bin   (about 480 MB)
Needs: Python 3.10+, git, internet, ~5 GB free disk, ~4 GB RAM. Takes about 5-15 minutes.
"""
import shutil
import subprocess
import sys
from pathlib import Path

MODEL_ID = "ubaidngt/whisper-small-ur-en"   # the fine-tuned Urdu + English model
BASE_ID = "openai/whisper-small"            # only used to fill in tokenizer files if the model repo lacks them
ROOT = Path(__file__).resolve().parent
WORK = ROOT / "work_ggml"
OUT = ROOT / "models" / "ggml-small-ur-en.bin"


def run(cmd):
    print(">", " ".join(str(c) for c in cmd), flush=True)
    subprocess.check_call([str(c) for c in cmd])


def main():
    if OUT.exists() and OUT.stat().st_size > 400_000_000:
        print(f"Already done: {OUT} ({OUT.stat().st_size / 1e6:.1f} MB)")
        return

    WORK.mkdir(exist_ok=True)
    OUT.parent.mkdir(exist_ok=True)

    # 1. Python packages used by the converter
    run([sys.executable, "-m", "pip", "install", "-q", "torch", "transformers", "numpy", "huggingface_hub", "sentencepiece"])
    from huggingface_hub import hf_hub_download, snapshot_download

    # 2. Download the Hugging Face model (ignore tensorboard event runs which can cause size mismatch errors)
    hf_dir = WORK / "hf_model"
    print(f"Downloading {MODEL_ID} ...", flush=True)
    snapshot_download(MODEL_ID, local_dir=str(hf_dir), ignore_patterns=["runs/*"])

    # 3. The converter needs these tokenizer files. Take them from the base model if missing.
    tokenizer_files = [
        "vocab.json",
        "added_tokens.json",
        "merges.txt",
        "normalizer.json",
        "tokenizer.json",
        "tokenizer_config.json",
        "special_tokens_map.json",
    ]
    for name in tokenizer_files:
        if not (hf_dir / name).exists():
            print(f"{name} missing in {MODEL_ID}, taking it from {BASE_ID}", flush=True)
            try:
                p = hf_hub_download(BASE_ID, name)
                shutil.copy(p, hf_dir / name)
            except Exception as e:
                print(f"  could not get {name}: {e}", flush=True)

    # 4. Get the two repos the converter needs (whisper.cpp = converter, openai/whisper = mel filters)
    wcpp = WORK / "whisper.cpp"
    oai = WORK / "whisper"
    if not wcpp.exists():
        run(["git", "clone", "--depth", "1", "https://github.com/ggml-org/whisper.cpp", wcpp])
    if not oai.exists():
        run(["git", "clone", "--depth", "1", "https://github.com/openai/whisper", oai])

    # 5. Convert (writes WORK/ggml-model.bin)
    run([sys.executable, wcpp / "models" / "convert-h5-to-ggml.py", hf_dir, oai, WORK])
    shutil.move(str(WORK / "ggml-model.bin"), str(OUT))

    # 6. Check the result
    magic = OUT.read_bytes()[:4]
    size = OUT.stat().st_size
    print(f"Output: {OUT}  ({size / 1e6:.0f} MB, header {magic!r})")
    if magic != b"lmgg" or size < 400_000_000:
        sys.exit("Conversion looks wrong (expected header b'lmgg' and size > 400 MB).")

    shutil.rmtree(WORK, ignore_errors=True)   # remove the temporary files
    print("Done. In VoiceService point modelPath to models/ggml-small-ur-en.bin")


if __name__ == "__main__":
    main()
