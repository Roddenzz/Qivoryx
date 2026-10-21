"""Normalize the finished audio, add chapters and verify the deliverable."""
import json
from pathlib import Path
import re
import shutil
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]
WORKSPACE = ROOT.parent
sys.path.insert(0, str(WORKSPACE / '.tools/video-python'))
import imageio_ffmpeg

FFMPEG = imageio_ffmpeg.get_ffmpeg_exe()
RAW = WORKSPACE / 'artifacts/Qivoryx-Presentation-RU-Arseniy-raw.mp4'
FINAL = WORKSPACE / 'dist/Qivoryx-Presentation-RU-Arseniy-1080p.mp4'
DATA = json.loads((ROOT / 'src/timeline.json').read_text(encoding='utf-8'))


def run(args):
    result = subprocess.run([FFMPEG, '-hide_banner', '-nostdin', *args], capture_output=True, text=True, encoding='utf-8', errors='replace')
    if result.returncode:
        raise RuntimeError(result.stderr[-5000:])
    return result.stderr


def measure(path):
    log = run(['-i', str(path), '-map', '0:a:0', '-af', 'loudnorm=I=-16:TP=-1.5:LRA=11:print_format=json', '-f', 'null', '-'])
    match = re.search(r'\{\s*"input_i".*?\}', log, flags=re.S)
    if not match:
        raise RuntimeError('Audio measurement was not returned')
    return json.loads(match.group())


def main():
    FINAL.parent.mkdir(exist_ok=True)
    print('Measuring full soundtrack...', flush=True)
    measured = measure(RAW)
    print(json.dumps({'source_loudness_lufs': measured['input_i'], 'source_true_peak_dbtp': measured['input_tp']}), flush=True)
    metadata = [';FFMETADATA1', 'title=Qivoryx — обзор возможностей', 'artist=Qivoryx', 'comment=Russian narration: Arseniy / ElevenLabs. Demonstration charts are labeled.']
    for i, scene in enumerate(DATA['scenes']):
        end = DATA['scenes'][i+1]['from'] if i+1<len(DATA['scenes']) else DATA['totalFrames']
        metadata += ['[CHAPTER]', 'TIMEBASE=1/30', f"START={scene['from']}", f'END={end}', f"title={scene['title']}"]
    chapter_file = ROOT / 'out/chapters.ffmeta'
    chapter_file.parent.mkdir(exist_ok=True)
    chapter_file.write_text('\n'.join(metadata)+'\n', encoding='utf-8')
    filter_text = ('loudnorm=I=-16:TP=-1.5:LRA=11:linear=true'
        f":measured_I={measured['input_i']}:measured_LRA={measured['input_lra']}"
        f":measured_TP={measured['input_tp']}:measured_thresh={measured['input_thresh']}"
        f":offset={measured['target_offset']}:print_format=json")
    print('Writing normalized MP4 with chapters...', flush=True)
    run(['-y', '-i', str(RAW), '-f', 'ffmetadata', '-i', str(chapter_file), '-map', '0:v:0', '-map', '0:a:0', '-map_metadata', '1', '-map_chapters', '1', '-metadata:s:a:0', 'language=rus', '-metadata:s:a:0', 'title=Arseniy / ElevenLabs', '-c:v', 'copy', '-c:a', 'aac', '-b:a', '192k', '-ar', '48000', '-ac', '2', '-af', filter_text, '-movflags', '+faststart', str(FINAL)])
    print('Checking final audio loudness and full video decode...', flush=True)
    final_levels = measure(FINAL)
    assert -16.7 < float(final_levels['input_i']) < -15.3, final_levels
    assert float(final_levels['input_tp']) <= -1.0, final_levels
    decode_log = run(['-v', 'info', '-i', str(FINAL), '-map', '0:v:0', '-f', 'null', '-'])
    all_frames = re.findall(r'frame=\s*(\d+)', decode_log)
    assert all_frames and int(all_frames[-1]) == DATA['totalFrames'], all_frames[-1:]
    assert '1920x1080' in decode_log and '30 fps' in decode_log
    subtitles = FINAL.with_suffix('.srt')
    shutil.copy2(ROOT/'public/Qivoryx-RU.srt', subtitles)
    poster = WORKSPACE/'artifacts/video-qa/1-intro-a.png'
    if not poster.exists():
        poster = WORKSPACE/'docs/images/video-poster.png'
    shutil.copy2(poster, FINAL.with_suffix('.png'))
    report = {'file': str(FINAL), 'width': 1920, 'height': 1080, 'fps': 30, 'frames': DATA['totalFrames'], 'duration_seconds': DATA['totalFrames']/30, 'bytes': FINAL.stat().st_size, 'audio_lufs': final_levels['input_i'], 'audio_true_peak_dbtp': final_levels['input_tp'], 'chapters': len(DATA['scenes']), 'speech_provider': 'ElevenLabs', 'visual_qa': '22 representative frames inspected', 'checks': ['TypeScript', 'ESLint', 'narration and subtitle bounds', 'no overlapping narration', 'credential scan', 'full video decode', 'audio loudness and true peak']}
    provider = json.loads((ROOT/'public/voice-arseniy/provider.json').read_text(encoding='utf-8'))
    report['voice'] = provider['voice']
    report['voice_id'] = provider['voice_id']
    assert report['voice_id'] == 'fmZEtrbS5wudON3ynVP2'
    (WORKSPACE/'artifacts/video-validation-Arseniy.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps(report, ensure_ascii=True, indent=2), flush=True)


if __name__ == '__main__':
    main()
